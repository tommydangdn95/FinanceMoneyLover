using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MoneyLoverAutoInput.App.Services;
using MoneyLoverAutoInput.Core.Configuration;
using MoneyLoverAutoInput.Core.Tools;

namespace MoneyLoverAutoInput.App.ViewModels;

public sealed record CheckItemViewModel(CheckItem Item)
{
    public string Title => Item.Title;
    public string Detail => Item.Detail;
    public string Hint => Item.Hint;
    public bool HasHint => Item.Hint.Length > 0;
    public string Icon => Item.Status switch { CheckStatus.Ok => "✔", CheckStatus.Warning => "⚠", _ => "✖" };
    public CheckStatus Status => Item.Status;
}

/// <summary>Trang Môi trường (SPEC 10.2): adb, Appium, thiết bị, Money Lover.</summary>
public sealed partial class EnvironmentViewModel : ObservableObject
{
    private readonly IOptionsMonitor<AppSettings> _settings;
    private readonly AppiumServerManager _appium;
    private readonly IDialogService _dialogs;
    private readonly ILogger<EnvironmentViewModel> _logger;

    public ObservableCollection<CheckItemViewModel> Items { get; } = new();

    public EnvironmentViewModel(IOptionsMonitor<AppSettings> settings, AppiumServerManager appium,
        IDialogService dialogs, ILogger<EnvironmentViewModel> logger)
    {
        _settings = settings;
        _appium = appium;
        _dialogs = dialogs;
        _logger = logger;
    }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CheckCommand), nameof(StartAppiumCommand), nameof(StopAppiumCommand))]
    private bool _isBusy;

    [ObservableProperty] private string _deviceSummary = "chưa kiểm tra";
    [ObservableProperty] private string _appiumSummary = "chưa kiểm tra";
    [ObservableProperty] private string _overall = "Bấm \"Kiểm tra lại\" để kiểm tra.";
    [ObservableProperty] private bool _canRun;
    [ObservableProperty] private DateTime? _checkedAt;

    public EnvironmentReport? LastReport { get; private set; }

    private bool NotBusy() => !IsBusy;

    [RelayCommand(CanExecute = nameof(NotBusy))]
    public async Task CheckAsync()
    {
        IsBusy = true;
        Overall = "Đang kiểm tra…";
        try
        {
            var report = await Task.Run(() => EnvironmentChecker.CheckAsync(_settings.CurrentValue));
            Apply(report);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Kiểm tra môi trường lỗi");
            Overall = "Kiểm tra lỗi: " + ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public void Apply(EnvironmentReport report)
    {
        LastReport = report;
        Items.Clear();
        foreach (var item in report.Items) Items.Add(new CheckItemViewModel(item));
        DeviceSummary = report.DeviceSummary;
        AppiumSummary = report.AppiumSummary;
        CanRun = report.CanRun;
        CheckedAt = DateTime.Now;
        var errors = report.Items.Count(i => i.Status == CheckStatus.Error);
        Overall = report.CanRun
            ? "Sẵn sàng nhập vào Money Lover."
            : $"Còn {errors} mục cần sửa trước khi nhập (xem hướng dẫn bên dưới).";
    }

    [RelayCommand(CanExecute = nameof(NotBusy))]
    private async Task StartAppiumAsync()
    {
        IsBusy = true;
        Overall = "Đang khởi động Appium…";
        try
        {
            await _appium.StartAsync(_settings.CurrentValue);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Không khởi động được Appium");
            _dialogs.ShowError("Appium", ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
        await CheckAsync();
    }

    [RelayCommand(CanExecute = nameof(NotBusy))]
    private async Task StopAppiumAsync()
    {
        if (!_appium.StartedByApp)
        {
            _dialogs.ShowInfo("Appium", "Appium đang chạy không phải do app này bật (VD bạn chạy trong terminal) — hãy tắt ở đó.");
            return;
        }
        _appium.Stop();
        await CheckAsync();
    }
}
