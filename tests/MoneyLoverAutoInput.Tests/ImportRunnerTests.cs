using Microsoft.Extensions.Logging.Abstractions;
using MoneyLoverAutoInput.Core.Automation;
using MoneyLoverAutoInput.Core.Configuration;
using MoneyLoverAutoInput.Core.Drafts;
using MoneyLoverAutoInput.Core.Import;
using MoneyLoverAutoInput.Core.Models;
using MoneyLoverAutoInput.Core.State;

namespace MoneyLoverAutoInput.Tests;

public sealed class ImportRunnerTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "mlai-run-" + Guid.NewGuid().ToString("N"));
    private readonly StateStore _state;
    private readonly DraftStore _drafts;

    public ImportRunnerTests()
    {
        _state = StateStore.Load(Path.Combine(_dir, "state.json"));
        _drafts = DraftStore.Load(Path.Combine(_dir, "drafts.json"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    /// <summary>Bản giả: mỗi dòng có thể bị cài sẵn một chuỗi lỗi cho các lần Fill/Save.</summary>
    private sealed class FakeUi : IMoneyLoverUi
    {
        public Dictionary<string, Queue<Exception>> FillErrors { get; } = new();
        public Dictionary<string, Exception> SaveErrors { get; } = new();
        public List<string> Saved { get; } = new();
        public int Discards { get; private set; }
        public int FillCalls { get; private set; }
        public Action<string>? OnFill { get; set; }

        public Task EnsureHomeAsync(CancellationToken ct) => Task.CompletedTask;

        public Task FillTransactionAsync(TransactionRow row, IProgress<string> step, CancellationToken ct)
        {
            FillCalls++;
            OnFill?.Invoke(row.Id);
            ct.ThrowIfCancellationRequested();
            step.Report("điền");
            if (FillErrors.TryGetValue(row.Id, out var q) && q.Count > 0) throw q.Dequeue();
            return Task.CompletedTask;
        }

        public Task SaveAsync()
        {
            var id = _current;
            if (id is not null && SaveErrors.TryGetValue(id, out var e)) throw e;
            if (id is not null) Saved.Add(id);
            return Task.CompletedTask;
        }

        private string? _current;
        public void Current(string id) => _current = id;

        public Task DiscardEntryAsync() { Discards++; return Task.CompletedTask; }
        public Task<string?> CaptureFailureAsync(string rowId) => Task.FromResult<string?>($"screenshots/{rowId}.png");
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private ImportRunner Runner(int retries = 2) =>
        new(_state, _drafts, new TimingOptions { RetryPerRow = retries, DelayBetweenRowsMs = 0 }, NullLogger.Instance);

    private static TransactionRow Tx(string id) => new()
    {
        Id = id, TxnCode = id, Nhom = "Khác", Vi = "TP Bank", SoTien = 1000, Ngay = new DateOnly(2026, 11, 1),
    };

    private static readonly IProgress<ImportProgress> NoProgress = new Progress<ImportProgress>(_ => { });

    private static FakeUi TrackingUi()
    {
        var ui = new FakeUi();
        ui.OnFill = id => ui.Current(id);
        return ui;
    }

    [Fact]
    public async Task Success_marks_done_and_clears_draft()
    {
        _drafts.Set("A", new RowDraft { GhiChu = "sửa tay" });
        var ui = TrackingUi();

        var s = await Runner().RunAsync(new[] { Tx("A"), Tx("B") }, ui, false, "f.xlsx", NoProgress, CancellationToken.None);

        Assert.Equal(2, s.Done);
        Assert.Equal(new[] { "A", "B" }, ui.Saved);
        Assert.Equal(ImportStatus.Done, _state.Get("A")!.Status);
        Assert.Null(_drafts.Get("A"));
    }

    [Fact]
    public async Task Dry_run_never_saves_or_touches_state()
    {
        var ui = TrackingUi();

        var s = await Runner().RunAsync(new[] { Tx("A") }, ui, true, null, NoProgress, CancellationToken.None);

        Assert.Equal(1, s.DryRunOk);
        Assert.Empty(ui.Saved);
        Assert.Equal(1, ui.Discards);
        Assert.Null(_state.Get("A"));
    }

    [Fact]
    public async Task Ui_error_is_retried_then_succeeds()
    {
        var ui = TrackingUi();
        ui.FillErrors["A"] = new Queue<Exception>(new[] { new UiStepException("không thấy nút") });

        var s = await Runner(retries: 2).RunAsync(new[] { Tx("A") }, ui, false, null, NoProgress, CancellationToken.None);

        Assert.Equal(1, s.Done);
        Assert.Equal(2, ui.FillCalls);
    }

    [Fact]
    public async Task Ui_error_after_all_retries_marks_failed_with_screenshot_and_continues()
    {
        var ui = TrackingUi();
        ui.FillErrors["A"] = new Queue<Exception>(Enumerable.Range(0, 3).Select(_ => (Exception)new UiStepException("x")));

        var s = await Runner(retries: 2).RunAsync(new[] { Tx("A"), Tx("B") }, ui, false, null, NoProgress, CancellationToken.None);

        Assert.Equal(1, s.Failed);
        Assert.Equal(1, s.Done);
        var st = _state.Get("A")!;
        Assert.Equal(ImportStatus.Failed, st.Status);
        Assert.Equal("screenshots/A.png", st.Screenshot);
        Assert.False(st.NeedsManualCheck);
    }

    [Fact]
    public async Task Data_error_fails_immediately_without_retry()
    {
        var ui = TrackingUi();
        ui.FillErrors["A"] = new Queue<Exception>(new[] { new DataNotFoundException("Không có nhóm 'X'") });

        var s = await Runner(retries: 2).RunAsync(new[] { Tx("A") }, ui, false, null, NoProgress, CancellationToken.None);

        Assert.Equal(1, s.Failed);
        Assert.Equal(1, ui.FillCalls);
        Assert.Contains("Không có nhóm", _state.Get("A")!.Error);
    }

    [Fact]
    public async Task Connection_lost_stops_everything_and_keeps_later_rows_untouched()
    {
        var ui = TrackingUi();
        ui.FillErrors["B"] = new Queue<Exception>(new[] { new ConnectionLostException("offline") });

        var s = await Runner().RunAsync(new[] { Tx("A"), Tx("B"), Tx("C") }, ui, false, null, NoProgress, CancellationToken.None);

        Assert.Equal(1, s.Done);
        Assert.Equal(2, s.NotRun);
        Assert.Contains("offline", s.StoppedReason);
        Assert.Null(_state.Get("B"));
        Assert.Null(_state.Get("C"));
    }

    [Fact]
    public async Task Connection_lost_during_save_is_uncertain_needs_manual_check()
    {
        var ui = TrackingUi();
        ui.SaveErrors["A"] = new ConnectionLostException("socket hang up");

        var s = await Runner().RunAsync(new[] { Tx("A"), Tx("B") }, ui, false, null, NoProgress, CancellationToken.None);

        Assert.Equal(1, s.Uncertain);
        Assert.Equal(1, s.NotRun);
        Assert.True(_state.Get("A")!.NeedsManualCheck);
    }

    [Fact]
    public async Task Ui_error_after_save_is_not_retried()
    {
        var ui = TrackingUi();
        ui.SaveErrors["A"] = new UiStepException("không thấy màn chính");

        var s = await Runner(retries: 2).RunAsync(new[] { Tx("A") }, ui, false, null, NoProgress, CancellationToken.None);

        Assert.Equal(1, s.Uncertain);
        Assert.Equal(1, ui.FillCalls); // không điền lại → không nhập trùng
        Assert.True(_state.Get("A")!.NeedsManualCheck);
    }

    [Fact]
    public async Task Cancel_between_rows_stops_cleanly()
    {
        using var cts = new CancellationTokenSource();
        var ui = TrackingUi();
        ui.OnFill = id => { ui.Current(id); if (id == "A") cts.Cancel(); };

        var s = await Runner().RunAsync(new[] { Tx("A"), Tx("B") }, ui, false, null, NoProgress, cts.Token);

        Assert.True(s.Cancelled);
        Assert.Empty(ui.Saved);         // A bị hủy giữa các bước (trước khi Save)
        Assert.Null(_state.Get("A"));   // giữ nguyên trạng thái chưa nhập
        Assert.True(ui.Discards >= 1);  // đã thoát màn nhập dở
    }
}
