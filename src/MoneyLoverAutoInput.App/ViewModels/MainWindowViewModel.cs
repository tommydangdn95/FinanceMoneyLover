using CommunityToolkit.Mvvm.ComponentModel;
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
    public PreviewViewModel Preview { get; }
    public IReadOnlyList<NavItem> NavItems { get; }

    [ObservableProperty] private NavItem _selectedNav;

    public MainWindowViewModel(PreviewViewModel preview, SettingsViewModel settings)
    {
        Preview = preview;
        NavItems = new[]
        {
            new NavItem("Xem trước", SymbolRegular.Table24, preview),
            new NavItem("Môi trường", SymbolRegular.PhoneLaptop24, new PlaceholderViewModel("Môi trường",
                "Kiểm tra adb, Appium, thiết bị và kết nối Wi-Fi (SPEC 10.2). Sẽ làm ở bước tiếp theo.")),
            new NavItem("Chạy", SymbolRegular.Play24, new PlaceholderViewModel("Chạy",
                "Tiến trình nhập, Dừng/Tiếp tục, log trực tiếp (SPEC 10.4). Cần có selector của Money Lover trước.")),
            new NavItem("Luật phân loại", SymbolRegular.TextBulletListSquare24, new PlaceholderViewModel("Luật phân loại",
                "Trình sửa luật (SPEC 10.5). Hiện tại sửa CategoryRules trong appsettings.json — lưu file là app tự phân loại lại.")),
            new NavItem("Lịch sử", SymbolRegular.History24, new PlaceholderViewModel("Lịch sử",
                "Xem và xóa trạng thái đã nhập trong state.json (SPEC 10.6).")),
            new NavItem("Cài đặt", SymbolRegular.Settings24, settings),
        };
        _selectedNav = NavItems[0];
    }
}
