using System.Diagnostics;
using Serilog;

namespace MoneyLoverAutoInput.App.Services;

/// <summary>
/// Ghi lỗi binding WPF vào log của app. Mặc định WPF chỉ in ra cửa sổ Debug, nên lỗi binding
/// (sai tên property, sai kiểu...) im lặng hoàn toàn khi chạy bản build thường.
/// </summary>
public sealed class BindingErrorListener : TraceListener
{
    public static void Register()
    {
        PresentationTraceSources.Refresh();
        PresentationTraceSources.DataBindingSource.Listeners.Add(new BindingErrorListener());
        PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Warning;
    }

    public override void Write(string? message) { }

    public override void WriteLine(string? message)
    {
        if (!string.IsNullOrWhiteSpace(message)) Log.Warning("Lỗi binding WPF: {Message}", message);
    }
}
