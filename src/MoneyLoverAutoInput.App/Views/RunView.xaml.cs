using System.Collections.Specialized;
using System.Windows.Controls;

namespace MoneyLoverAutoInput.App.Views;

public partial class RunView : UserControl
{
    public RunView()
    {
        InitializeComponent();

        // Thuần hiển thị: log mới → tự cuộn xuống cuối.
        ((INotifyCollectionChanged)LogList.Items).CollectionChanged += (_, e) =>
        {
            if (e.Action == NotifyCollectionChangedAction.Add && LogList.Items.Count > 0)
                LogList.ScrollIntoView(LogList.Items[^1]);
        };
    }
}
