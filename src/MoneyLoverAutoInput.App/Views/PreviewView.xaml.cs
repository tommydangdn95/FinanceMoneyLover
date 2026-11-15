using System.Windows;
using System.Windows.Controls;
using MoneyLoverAutoInput.App.ViewModels;

namespace MoneyLoverAutoInput.App.Views;

public partial class PreviewView : UserControl
{
    public PreviewView() => InitializeComponent();

    // Kéo thả là sự kiện thuần UI: chỉ lấy đường dẫn rồi chuyển cho ViewModel.
    private void OnDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private async void OnDrop(object sender, DragEventArgs e)
    {
        if (DataContext is not PreviewViewModel vm) return;
        if (e.Data.GetData(DataFormats.FileDrop) is string[] { Length: > 0 } files)
        {
            await vm.LoadFileAsync(files[0]);
        }
    }
}
