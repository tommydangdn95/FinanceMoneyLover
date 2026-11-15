using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace MoneyLoverAutoInput.App.Services;

public interface IFilePickerService
{
    /// <summary>Hộp thoại chọn file sao kê. null nếu người dùng hủy.</summary>
    string? PickStatementFile();
}

public sealed class FilePickerService : IFilePickerService
{
    public string? PickStatementFile()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Chọn file sao kê ngân hàng",
            Filter = "File Excel (*.xlsx)|*.xlsx|Tất cả (*.*)|*.*",
            CheckFileExists = true,
        };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }
}

public interface IShellService
{
    void OpenFolder(string path);
    void OpenFile(string path);
}

public sealed class ShellService : IShellService
{
    public void OpenFolder(string path)
    {
        Directory.CreateDirectory(path);
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
    }

    public void OpenFile(string path) =>
        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
}
