using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using MoneyLoverAutoInput.App.ViewModels;

namespace MoneyLoverAutoInput.App.Views;

public partial class ManualEntryView : UserControl
{
    public ManualEntryView()
    {
        InitializeComponent();

        // Thuần hiển thị: thêm xong một giao dịch → con trỏ về ô Số tiền để gõ tiếp.
        DataContextChanged += (_, e) =>
        {
            if (e.OldValue is ManualEntryViewModel oldVm) oldVm.EntryAdded -= OnEntryAdded;
            if (e.NewValue is ManualEntryViewModel newVm) newVm.EntryAdded += OnEntryAdded;
        };
        Unloaded += (_, _) =>
        {
            if (DataContext is ManualEntryViewModel vm) vm.EntryAdded -= OnEntryAdded;
        };
        Loaded += (_, _) =>
        {
            if (DataContext is ManualEntryViewModel vm)
            {
                vm.EntryAdded -= OnEntryAdded;
                vm.EntryAdded += OnEntryAdded;
            }
        };
    }

    private void OnEntryAdded(object? sender, EventArgs e) =>
        Dispatcher.BeginInvoke(DispatcherPriority.Input, () => AmountBox.Focus());
}
