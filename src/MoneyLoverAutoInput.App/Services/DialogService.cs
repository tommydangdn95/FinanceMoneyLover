using System.Windows;

namespace MoneyLoverAutoInput.App.Services;

public interface IDialogService
{
    void ShowError(string title, string message);
    void ShowInfo(string title, string message);
    bool Confirm(string title, string message);
}

public sealed class DialogService : IDialogService
{
    private static Window? Owner => Application.Current?.MainWindow is { IsVisible: true } w ? w : null;

    public void ShowError(string title, string message) => Show(title, message, MessageBoxImage.Error);

    public void ShowInfo(string title, string message) => Show(title, message, MessageBoxImage.Information);

    public bool Confirm(string title, string message)
    {
        var owner = Owner;
        var result = owner is null
            ? MessageBox.Show(message, title, MessageBoxButton.YesNo, MessageBoxImage.Question)
            : MessageBox.Show(owner, message, title, MessageBoxButton.YesNo, MessageBoxImage.Question);
        return result == MessageBoxResult.Yes;
    }

    private static void Show(string title, string message, MessageBoxImage image)
    {
        var owner = Owner;
        if (owner is null) MessageBox.Show(message, title, MessageBoxButton.OK, image);
        else MessageBox.Show(owner, message, title, MessageBoxButton.OK, image);
    }
}
