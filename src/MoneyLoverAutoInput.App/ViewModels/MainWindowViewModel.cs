using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Options;
using MoneyLoverAutoInput.App.Services;
using MoneyLoverAutoInput.Core.Configuration;
using Wpf.Ui.Controls;

namespace MoneyLoverAutoInput.App.ViewModels;

public sealed record NavItem(string Title, SymbolRegular Icon, ObservableObject Page);

/// <summary>Trang chưa làm — chỉ hiện mô tả.</summary>
public sealed class PlaceholderViewModel : ObservableObject
{
    public PlaceholderViewModel(string title, string description)
    {
        Title = title;
        Description = description;
    }

    public string Title { get; }
    public string Description { get; }
}

public sealed partial class MainWindowViewModel : ObservableObject
{
    private readonly IDialogService _dialogs;

    public PreviewViewModel Preview { get; }
    public SetupViewModel Setup { get; }
    public EnvironmentViewModel Environment { get; }
    public RunViewModel Run { get; }
    public IReadOnlyList<NavItem> NavItems { get; }

    [ObservableProperty] private NavItem _selectedNav;

    public MainWindowViewModel(PreviewViewModel preview, SetupViewModel setup, EnvironmentViewModel environment,
        RunViewModel run, SettingsViewModel settings, IOptionsMonitor<AppSettings> appSettings, IDialogService dialogs)
    {
        Preview = preview;
        Setup = setup;
        Environment = environment;
        Run = run;
        _dialogs = dialogs;

        var previewNav = new NavItem("Xem trước", SymbolRegular.Table24, preview);
        var setupNav = new NavItem("Thiết lập", SymbolRegular.TextBulletListSquare24, setup);
        var runNav = new NavItem("Chạy", SymbolRegular.Play24, run);

        NavItems = new[]
        {
            previewNav,
            setupNav,
            new NavItem("Môi trường", SymbolRegular.PhoneLaptop24, environment),
            runNav,
            new NavItem("Lịch sử", SymbolRegular.History24, new PlaceholderViewModel("Lịch sử",
                "Xem và xóa trạng thái đã nhập trong state.json (SPEC 10.6).")),
            new NavItem("Cài đặt", SymbolRegular.Settings24, settings),
        };

        // Lần đầu dùng app: vào màn Thiết lập trước; lưu xong thì chuyển sang Xem trước.
        _selectedNav = appSettings.CurrentValue.SetupCompleted ? previewNav : setupNav;
        setup.FirstSetupCompleted += (_, _) => SelectedNav = previewNav;

        // Bấm "Chạy thử"/"Bắt đầu nhập" → sang trang Chạy; xong thì Xem trước đọc lại trạng thái.
        preview.ImportRequested += async (rows, dryRun, sourceFile) =>
        {
            SelectedNav = runNav;
            await run.StartAsync(rows, dryRun, sourceFile);
        };
        run.Finished += (_, _) => preview.RefreshAfterImport();
    }

    /// <summary>Kiểm tra môi trường nền khi mở app (SPEC 8.1 bước 1).</summary>
    public Task InitializeAsync() => Environment.CheckAsync();

    /// <summary>Đóng cửa sổ khi đang nhập: hỏi, rồi dừng ở bước an toàn.</summary>
    public bool ConfirmClose()
    {
        if (!Run.IsRunning) return true;
        if (!_dialogs.Confirm("Đang nhập",
                "App đang nhập vào Money Lover. Dừng và thoát?\n\nDòng đang nhập dở (chưa Lưu) sẽ không được lưu; " +
                "các dòng đã nhập không bị ảnh hưởng.")) return false;
        Run.StopCommand.Execute(null);
        return true;
    }
}
