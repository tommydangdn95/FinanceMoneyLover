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

        // Theme sáng/tối và màu nhấn theo Windows.
        ApplicationThemeManager.ApplySystemTheme();
        SystemThemeWatcher.Watch(this);
    }
}
