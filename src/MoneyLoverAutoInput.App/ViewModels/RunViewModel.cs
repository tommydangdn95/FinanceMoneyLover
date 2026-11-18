using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MoneyLoverAutoInput.App.Services;
using MoneyLoverAutoInput.Core;
using MoneyLoverAutoInput.Core.Automation;
using MoneyLoverAutoInput.Core.Configuration;
using MoneyLoverAutoInput.Core.Drafts;
using MoneyLoverAutoInput.Core.Import;
using MoneyLoverAutoInput.Core.Models;
using MoneyLoverAutoInput.Core.State;
using MoneyLoverAutoInput.Core.Tools;

namespace MoneyLoverAutoInput.App.ViewModels;

public sealed partial class RunResultItem : ObservableObject
{
    public required int Index { get; init; }
    public required string Text { get; init; }
    public required RowOutcome Outcome { get; init; }
    public string Message { get; init; } = "";
    public string? ScreenshotPath { get; init; }

    public string Icon => Outcome switch
    {
        RowOutcome.Done => "✔",
        RowOutcome.DryRunOk => "✔",
        RowOutcome.Uncertain => "⚠",
        _ => "✖",
    };

    public string OutcomeText => Outcome switch
    {
        RowOutcome.Done => "Đã nhập",
        RowOutcome.DryRunOk => "Chạy thử OK (không lưu)",
        RowOutcome.Uncertain => "Cần kiểm tra tay",
        _ => "Lỗi",
    };

    public bool IsOk => Outcome is RowOutcome.Done or RowOutcome.DryRunOk;
    public bool HasScreenshot => ScreenshotPath is not null;
}

/// <summary>Trang Chạy (SPEC 10.4): điều phối một lần nhập/chạy thử và hiện tiến trình.</summary>
public sealed partial class RunViewModel : ObservableObject
{
    private static readonly CultureInfo Vi = CultureInfo.GetCultureInfo("vi-VN");
    private const int MaxLogLines = 2000;

    private readonly IOptionsMonitor<AppSettings> _settings;
    private readonly AppDataPaths _paths;
    private readonly StateStore _state;
    private readonly DraftStore _drafts;
    private readonly AppiumServerManager _appium;
    private readonly EnvironmentViewModel _environment;
    private readonly IDialogService _dialogs;
    private readonly IShellService _shell;
    private readonly ILogger<RunViewModel> _logger;

    private CancellationTokenSource? _cts;

    public ObservableCollection<RunResultItem> Results { get; } = new();
    public ObservableCollection<string> Log { get; } = new();

    /// <summary>Phát ra khi một lần chạy kết thúc (để Xem trước cập nhật trạng thái các dòng).</summary>
    public event EventHandler? Finished;

    public RunViewModel(IOptionsMonitor<AppSettings> settings, AppDataPaths paths, StateStore state, DraftStore drafts,
        AppiumServerManager appium, EnvironmentViewModel environment, IDialogService dialogs, IShellService shell,
        ILogger<RunViewModel> logger)
    {
        _settings = settings;
        _paths = paths;
        _state = state;
        _drafts = drafts;
        _appium = appium;
        _environment = environment;
        _dialogs = dialogs;
        _shell = shell;
        _logger = logger;
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotRunning))]
    [NotifyCanExecuteChangedFor(nameof(StopCommand))]
    private bool _isRunning;

    public bool IsNotRunning => !IsRunning;

    [ObservableProperty] private string _title = "Chưa chạy";
    [ObservableProperty] private int _current;
    [ObservableProperty] private int _total = 1;
    [ObservableProperty] private string _currentRow = "";
    [ObservableProperty] private string _currentStep = "Chọn dòng ở trang Xem trước rồi bấm \"Chạy thử\" hoặc \"Bắt đầu nhập\".";
    [ObservableProperty] private string _summary = "";
    [ObservableProperty] private bool _stopRequested;

    [RelayCommand(CanExecute = nameof(IsRunning))]
    private void Stop()
    {
        StopRequested = true;
        CurrentStep = "Đang dừng… (chờ tới bước an toàn, không cắt ngang lúc đang Lưu)";
        AddLog("Yêu cầu dừng.");
        _cts?.Cancel();
    }

    [RelayCommand]
    private void OpenScreenshot(RunResultItem? item)
    {
        if (item?.ScreenshotPath is null) return;
        var full = Path.Combine(_paths.Root, item.ScreenshotPath);
        if (File.Exists(full)) _shell.OpenFile(full);
    }

    [RelayCommand]
    private void OpenScreenshotsFolder() => _shell.OpenFolder(_paths.ScreenshotsDir);

    /// <summary>Chạy nhập (hoặc chạy thử) các dòng đã chọn ở Xem trước.</summary>
    public async Task StartAsync(IReadOnlyList<TransactionRow> rows, bool dryRun, string? sourceFile)
    {
        if (IsRunning || rows.Count == 0) return;

        IsRunning = true;
        StopRequested = false;
        Results.Clear();
        Log.Clear();
        Current = 0;
        Total = rows.Count;
        Summary = "";
        Title = dryRun ? "Chạy thử (không lưu)" : $"Đang nhập {rows.Count} giao dịch";
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;
        IMoneyLoverUi? ui = null;

        try
        {
            var settings = _settings.CurrentValue;

            Step("Kiểm tra môi trường…");
            var report = await Task.Run(() => EnvironmentChecker.CheckAsync(settings, ct), ct);
            _environment.Apply(report);
            foreach (var item in report.Items.Where(i => i.Status != CheckStatus.Ok))
                AddLog($"{(item.Status == CheckStatus.Error ? "✖" : "⚠")} {item.Title}: {item.Detail}");
            if (!report.CanRun)
            {
                Finish("Chưa đủ điều kiện để chạy — xem trang Môi trường.");
                _dialogs.ShowError("Chưa chạy được", "Môi trường chưa sẵn sàng. Mở trang \"Môi trường\" để xem cần sửa gì.");
                return;
            }

            if (!report.AppiumReady)
            {
                Step("Khởi động Appium server…");
                await _appium.EnsureRunningAsync(settings, ct);
            }

            Step("Kết nối điện thoại… (lần đầu có thể mất 1–2 phút để cài UiAutomator2 lên máy)");
            ui = await MoneyLoverUi.CreateAsync(settings, report.DeviceSerial!, _paths.Root, _logger, ct);

            var runner = new ImportRunner(_state, _drafts, settings.Timing, _logger);
            var progress = new Progress<ImportProgress>(OnProgress); // tạo trên UI thread → callback về UI thread
            var result = await runner.RunAsync(rows, ui, dryRun, sourceFile, progress, ct);

            Finish(Describe(result, dryRun));
            if (result.StoppedReason is not null && !result.Cancelled)
            {
                _dialogs.ShowError("Đã dừng", result.StoppedReason +
                                              "\n\nKiểm tra cáp USB / điện thoại (mở khóa), rồi chạy lại — các dòng đã nhập sẽ không bị nhập lại.");
            }
        }
        catch (OperationCanceledException)
        {
            Finish("Đã dừng trước khi bắt đầu nhập.");
        }
        catch (Exception ex) when (ex is AutomationException or InvalidOperationException or TimeoutException
                                       or FileNotFoundException or IOException)
        {
            _logger.LogError(ex, "Chạy nhập thất bại");
            Finish("Lỗi: " + ex.Message);
            _dialogs.ShowError("Không chạy được", ex.Message);
        }
        finally
        {
            if (ui is not null)
            {
                try { await ui.DisposeAsync(); }
                catch (Exception ex) { _logger.LogDebug(ex, "Đóng phiên Appium"); }
            }
            _cts.Dispose();
            _cts = null;
            IsRunning = false;
            Finished?.Invoke(this, EventArgs.Empty);
        }
    }

    private static string Describe(ImportSummary s, bool dryRun)
    {
        var parts = new List<string>();
        if (dryRun) parts.Add(s.DryRunOk > 0 ? "Chạy thử thành công — không lưu giao dịch nào" : "Chạy thử chưa qua");
        if (s.Done > 0) parts.Add($"{s.Done} đã nhập");
        if (s.Failed > 0) parts.Add($"{s.Failed} lỗi");
        if (s.Uncertain > 0) parts.Add($"{s.Uncertain} cần kiểm tra tay");
        if (s.NotRun > 0) parts.Add($"{s.NotRun} chưa chạy");
        if (s.Cancelled) parts.Add("đã dừng theo yêu cầu");
        return string.Join(" · ", parts);
    }

    private void OnProgress(ImportProgress p)
    {
        Current = p.Outcome is null ? p.Index - 1 : p.Index;
        Total = p.Total;
        CurrentRow = $"{p.Index}/{p.Total} · {p.Row.Ngay:dd/MM/yyyy} · {p.Row.Loai} · {p.Row.SoTien.ToString("#,##0", Vi)} · {p.Row.Nhom}";

        if (p.Outcome is null)
        {
            CurrentStep = p.Step;
            AddLog($"[{p.Index}] {p.Step}");
            return;
        }

        var item = new RunResultItem
        {
            Index = p.Index,
            Text = $"{p.Row.Ngay:dd/MM/yyyy} · {p.Row.SoTien.ToString("#,##0", Vi)} · {p.Row.Nhom} · {p.Row.GhiChu}",
            Outcome = p.Outcome.Value,
            Message = p.Message ?? "",
            ScreenshotPath = p.Screenshot,
        };
        Results.Add(item);
        AddLog($"[{p.Index}] {item.Icon} {item.OutcomeText}{(item.Message.Length > 0 ? ": " + item.Message : "")}");
    }

    private void Step(string text)
    {
        CurrentStep = text;
        AddLog(text);
    }

    private void Finish(string summary)
    {
        Summary = summary;
        CurrentStep = summary;
        Title = "Đã xong";
        AddLog("— " + summary);
    }

    private void AddLog(string line)
    {
        Log.Add($"{DateTime.Now:HH:mm:ss}  {line}");
        while (Log.Count > MaxLogLines) Log.RemoveAt(0);
    }
}
