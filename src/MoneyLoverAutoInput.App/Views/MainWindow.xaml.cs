using System.Windows;
using MoneyLoverAutoInput.App.ViewModels;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace MoneyLoverAutoInput.App.Views;

public partial class MainWindow : FluentWindow
{
    public MainWindow(MainWindowViewModel viewModel)
    {
        DataContext = viewModel;
        InitializeComponent();

        // Lưới Xem trước cần ~1400px để hiện đủ cột; màn hình nhỏ hơn thì vừa khít vùng làm việc.
        var work = SystemParameters.WorkArea;
        Width = Math.Min(Width, work.Width - 32);
        Height = Math.Min(Height, work.Height - 32);

        // Theme sáng/tối và màu nhấn theo Windows.
        ApplicationThemeManager.ApplySystemTheme();
        SystemThemeWatcher.Watch(this);

        Loaded += async (_, _) => await viewModel.InitializeAsync();
        Closing += (_, e) => e.Cancel = !viewModel.ConfirmClose();
    }
}
