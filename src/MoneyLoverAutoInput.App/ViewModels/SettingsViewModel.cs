using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MoneyLoverAutoInput.App.Services;
using MoneyLoverAutoInput.Core;

namespace MoneyLoverAutoInput.App.ViewModels;

/// <summary>Trang Cài đặt (SPEC 10.7) — bản đầu: chỉ các nút thao tác file cấu hình.</summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly AppDataPaths _paths;
    private readonly IConfiguration _configuration;
    private readonly IShellService _shell;
    private readonly IDialogService _dialogs;
    private readonly ILogger<SettingsViewModel> _logger;

    public SettingsViewModel(AppDataPaths paths, IConfiguration configuration, IShellService shell,
        IDialogService dialogs, ILogger<SettingsViewModel> logger)
    {
        _paths = paths;
        _configuration = configuration;
        _shell = shell;
        _dialogs = dialogs;
        _logger = logger;
    }

    public string DataFolder => _paths.Root;
    public string ConfigFile => _paths.ConfigFile;

    [RelayCommand]
    private void OpenDataFolder() => _shell.OpenFolder(_paths.Root);

    [RelayCommand]
    private void OpenConfigFile() => _shell.OpenFile(_paths.ConfigFile);

    [RelayCommand]
    private void ReloadConfig()
    {
        try
        {
            (_configuration as IConfigurationRoot)?.Reload();
            _logger.LogInformation("Đã tải lại cấu hình");
            _dialogs.ShowInfo("Cài đặt", "Đã tải lại cấu hình.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Tải lại cấu hình thất bại");
            _dialogs.ShowError("Cấu hình không hợp lệ", ex.Message);
        }
    }
}
