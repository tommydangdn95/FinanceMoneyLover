using Microsoft.Extensions.Logging;
using MoneyLoverAutoInput.Core.Automation;
using MoneyLoverAutoInput.Core.Configuration;
using MoneyLoverAutoInput.Core.Drafts;
using MoneyLoverAutoInput.Core.Models;
using MoneyLoverAutoInput.Core.State;

namespace MoneyLoverAutoInput.Core.Import;

public enum RowOutcome
{
    /// <summary>Đã lưu vào Money Lover.</summary>
    Done,
    /// <summary>Chạy thử: đi hết các bước nhưng không lưu.</summary>
    DryRunOk,
    Failed,
    /// <summary>Có thể đã lưu nhưng không xác nhận được (mất kết nối lúc bấm Save) — kiểm tra tay.</summary>
    Uncertain,
}

/// <summary>Tiến trình gửi về UI. <see cref="Outcome"/> != null khi một dòng vừa xong.</summary>
public sealed record ImportProgress(
    int Index,
    int Total,
    TransactionRow Row,
    string Step,
    RowOutcome? Outcome = null,
    string? Message = null,
    string? Screenshot = null);

public sealed class ImportSummary
{
    public int Done { get; set; }
    public int DryRunOk { get; set; }
    public int Failed { get; set; }
    public int Uncertain { get; set; }
    public int NotRun { get; set; }
    public bool Cancelled { get; set; }

    /// <summary>Lý do dừng sớm (mất kết nối...), null nếu chạy hết.</summary>
    public string? StoppedReason { get; set; }
}

/// <summary>
/// Vòng lặp nhập (SPEC 8.1 bước 5): mỗi dòng → về màn chính → điền → Save (hoặc thoát nếu chạy thử) → ghi state.
/// Lỗi: mất kết nối → dừng hết; lỗi UI → thử lại tối đa RetryPerRow; dữ liệu không có → Failed ngay.
/// Hủy chỉ có hiệu lực giữa các bước/dòng, KHÔNG hủy sau khi đã bấm Save.
/// </summary>
public sealed class ImportRunner
{
    private readonly StateStore _state;
    private readonly DraftStore _drafts;
    private readonly TimingOptions _timing;
    private readonly ILogger _logger;

    public ImportRunner(StateStore state, DraftStore drafts, TimingOptions timing, ILogger logger)
    {
        _state = state;
        _drafts = drafts;
        _timing = timing;
        _logger = logger;
    }

    public async Task<ImportSummary> RunAsync(IReadOnlyList<TransactionRow> rows, IMoneyLoverUi ui, bool dryRun,
        string? sourceFile, IProgress<ImportProgress> progress, CancellationToken ct)
    {
        var summary = new ImportSummary();

        for (var i = 0; i < rows.Count; i++)
        {
            if (ct.IsCancellationRequested)
            {
                summary.Cancelled = true;
                summary.NotRun = rows.Count - i;
                break;
            }

            var row = rows[i];
            var number = i + 1; // chép ra biến riêng: Progress gọi callback bất đồng bộ, i có thể đã tăng
            var stepReporter = new SyncProgress<string>(s => progress.Report(new ImportProgress(number, rows.Count, row, s)));
            var (outcome, stop) = await RunRowAsync(row, i, rows.Count, ui, dryRun, sourceFile, stepReporter, progress, ct)
                .ConfigureAwait(false);

            switch (outcome)
            {
                case RowOutcome.Done: summary.Done++; break;
                case RowOutcome.DryRunOk: summary.DryRunOk++; break;
                case RowOutcome.Failed: summary.Failed++; break;
                case RowOutcome.Uncertain: summary.Uncertain++; break;
            }

            if (stop)
            {
                // Dừng toàn bộ (mất kết nối hoặc bị hủy giữa dòng). Dòng hiện tại nếu chưa có kết quả thì tính là chưa chạy.
                summary.NotRun = rows.Count - i - (outcome is null ? 0 : 1);
                summary.Cancelled = ct.IsCancellationRequested;
                summary.StoppedReason ??= _lastStopReason;
                return summary;
            }

            if (i < rows.Count - 1 && _timing.DelayBetweenRowsMs > 0)
            {
                try { await Task.Delay(_timing.DelayBetweenRowsMs, ct).ConfigureAwait(false); }
                catch (OperationCanceledException) { /* kiểm tra ở đầu vòng sau */ }
            }
        }

        return summary;
    }

    private string? _lastStopReason;

    /// <summary>Kết quả của dòng (null = chưa có kết quả) và có phải dừng toàn bộ không.</summary>
    private async Task<(RowOutcome? Outcome, bool Stop)> RunRowAsync(TransactionRow row, int index, int total, IMoneyLoverUi ui, bool dryRun,
        string? sourceFile, IProgress<string> step, IProgress<ImportProgress> progress, CancellationToken ct)
    {
        void Finish(RowOutcome o, string? msg = null, string? shot = null) =>
            progress.Report(new ImportProgress(index + 1, total, row, "", o, msg, shot));

        var maxAttempts = 1 + Math.Max(0, _timing.RetryPerRow);
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            var saveTapped = false;
            try
            {
                await ui.EnsureHomeAsync(ct).ConfigureAwait(false);
                await ui.FillTransactionAsync(row, step, ct).ConfigureAwait(false);

                if (dryRun)
                {
                    step.Report("Chạy thử: thoát, KHÔNG lưu");
                    await ui.DiscardEntryAsync().ConfigureAwait(false);
                    _logger.LogInformation("[{RowId}] Chạy thử OK", row.Id);
                    Finish(RowOutcome.DryRunOk, "Đủ các bước — không lưu (chạy thử).");
                    return (RowOutcome.DryRunOk, false);
                }

                // Từ đây KHÔNG hủy nữa (SPEC 8.2 bước 9).
                step.Report("Lưu");
                saveTapped = true;
                await ui.SaveAsync().ConfigureAwait(false);

                _state.MarkDone(row.Id, sourceFile, row.SoTien, row.Nhom);
                _drafts.Remove(row.Id);
                _logger.LogInformation("[{RowId}] Đã nhập {Amount} {Category}", row.Id, row.SoTien, row.Nhom);
                Finish(RowOutcome.Done);
                return (RowOutcome.Done, false);
            }
            catch (OperationCanceledException)
            {
                // Dừng giữa các bước: thoát màn nhập dở, dòng giữ nguyên trạng thái chưa nhập.
                await TryDiscard(ui).ConfigureAwait(false);
                _lastStopReason = "Đã dừng theo yêu cầu.";
                return (null, true);
            }
            catch (ConnectionLostException ex)
            {
                _lastStopReason = ex.Message;
                if (saveTapped)
                {
                    const string msg = "Mất kết nối đúng lúc Lưu — có thể đã lưu. Kiểm tra trong Money Lover trước khi nhập lại.";
                    _state.MarkFailed(row.Id, sourceFile, msg, needsManualCheck: true);
                    _logger.LogWarning(ex, "[{RowId}] {Message}", row.Id, msg);
                    Finish(RowOutcome.Uncertain, msg);
                    return (RowOutcome.Uncertain, true);
                }

                // Chưa bấm Save → dòng này chưa nhập, giữ nguyên trạng thái để chạy lại sau.
                _logger.LogError(ex, "[{RowId}] Mất kết nối — dừng toàn bộ", row.Id);
                return (null, true);
            }
            catch (DataNotFoundException ex)
            {
                var shot = await ui.CaptureFailureAsync(row.Id).ConfigureAwait(false);
                await TryDiscard(ui).ConfigureAwait(false);
                _state.MarkFailed(row.Id, sourceFile, ex.Message, shot);
                _logger.LogWarning("[{RowId}] Lỗi dữ liệu: {Message}", row.Id, ex.Message);
                Finish(RowOutcome.Failed, ex.Message, shot);
                return (RowOutcome.Failed, false);
            }
            catch (UiStepException ex)
            {
                if (saveTapped)
                {
                    // Đã bấm Save nhưng không xác nhận được kết quả → không thử lại (tránh nhập trùng).
                    var shotSave = await ui.CaptureFailureAsync(row.Id).ConfigureAwait(false);
                    var msg = "Đã bấm Lưu nhưng không xác nhận được: " + ex.Message + " Kiểm tra tay trong Money Lover.";
                    _state.MarkFailed(row.Id, sourceFile, msg, shotSave, needsManualCheck: true);
                    _logger.LogWarning(ex, "[{RowId}] {Message}", row.Id, msg);
                    Finish(RowOutcome.Uncertain, msg, shotSave);
                    return (RowOutcome.Uncertain, false);
                }

                if (attempt < maxAttempts)
                {
                    _logger.LogWarning("[{RowId}] Lỗi lần {Attempt}: {Message} — thử lại", row.Id, attempt, ex.Message);
                    step.Report($"Lỗi: {ex.Message} — thử lại ({attempt}/{maxAttempts - 1})");
                    await TryDiscard(ui).ConfigureAwait(false);
                    continue;
                }

                var shot = await ui.CaptureFailureAsync(row.Id).ConfigureAwait(false);
                await TryDiscard(ui).ConfigureAwait(false);
                _state.MarkFailed(row.Id, sourceFile, ex.Message, shot);
                _logger.LogError(ex, "[{RowId}] Thất bại sau {Attempts} lần", row.Id, maxAttempts);
                Finish(RowOutcome.Failed, ex.Message, shot);
                return (RowOutcome.Failed, false);
            }
        }

        return (RowOutcome.Failed, false); // không tới đây
    }

    /// <summary>IProgress gọi thẳng (không post qua SynchronizationContext); progress ngoài cùng tự marshal về UI.</summary>
    private sealed class SyncProgress<T> : IProgress<T>
    {
        private readonly Action<T> _action;
        public SyncProgress(Action<T> action) => _action = action;
        public void Report(T value) => _action(value);
    }

    private async Task TryDiscard(IMoneyLoverUi ui)
    {
        try
        {
            await ui.DiscardEntryAsync().ConfigureAwait(false);
        }
        catch (AutomationException ex)
        {
            _logger.LogWarning(ex, "Không thoát được màn nhập dở");
        }
    }
}
