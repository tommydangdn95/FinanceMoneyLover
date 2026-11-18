using System.Text.RegularExpressions;
using MoneyLoverAutoInput.Core.Configuration;

namespace MoneyLoverAutoInput.Core.Tools;

public sealed record AdbDevice(string Serial, string State)
{
    public bool IsReady => State == "device";
}

/// <summary>Gọi adb (SPEC 10.2). Chỉ các lệnh ĐỌC trạng thái — không thao tác trên điện thoại.</summary>
public sealed class AdbClient
{
    public string AdbPath { get; }

    public AdbClient(string adbPath) => AdbPath = adbPath;

    /// <summary>Tìm adb.exe: Tools.AdbPath → ANDROID_HOME/ANDROID_SDK_ROOT → %LOCALAPPDATA%\Android\Sdk → PATH.</summary>
    public static string? Locate(ToolsOptions tools)
    {
        if (!string.IsNullOrWhiteSpace(tools.AdbPath) && File.Exists(tools.AdbPath)) return tools.AdbPath;

        foreach (var sdk in new[]
                 {
                     EnvVars.Get("ANDROID_HOME"), EnvVars.Get("ANDROID_SDK_ROOT"),
                     Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData), "Android", "Sdk"),
                 })
        {
            if (string.IsNullOrWhiteSpace(sdk)) continue;
            var p = Path.Combine(sdk, "platform-tools", "adb.exe");
            if (File.Exists(p)) return p;
        }

        return EnvVars.FindOnPath("adb.exe");
    }

    public Task<ProcessResult> RunAsync(string args, CancellationToken ct = default, int timeoutSec = 20) =>
        ProcessRunner.RunAsync(AdbPath, args, TimeSpan.FromSeconds(timeoutSec), ct);

    private string S(string? serial) => string.IsNullOrWhiteSpace(serial) ? "" : $"-s {serial} ";

    public async Task<IReadOnlyList<AdbDevice>> GetDevicesAsync(CancellationToken ct = default)
    {
        var r = await RunAsync("devices", ct).ConfigureAwait(false);
        return ParseDevices(r.StdOut);
    }

    internal static List<AdbDevice> ParseDevices(string output) =>
        output.Split('\n')
            .Select(l => l.Trim())
            .Where(l => l.Length > 0 && !l.StartsWith("List of devices", StringComparison.Ordinal) && !l.StartsWith('*'))
            .Select(l => l.Split(new[] { '\t', ' ' }, StringSplitOptions.RemoveEmptyEntries))
            .Where(p => p.Length >= 2)
            .Select(p => new AdbDevice(p[0], p[1]))
            .ToList();

    public async Task<string> ShellAsync(string? serial, string command, CancellationToken ct = default)
    {
        var r = await RunAsync($"{S(serial)}shell {command}", ct).ConfigureAwait(false);
        return r.StdOut;
    }

    public async Task<bool> IsPackageInstalledAsync(string? serial, string package, CancellationToken ct = default)
    {
        var output = await ShellAsync(serial, $"pm list packages {package}", ct).ConfigureAwait(false);
        return output.Split('\n').Any(l => l.Trim() == "package:" + package);
    }

    public async Task<string?> GetPackageVersionAsync(string? serial, string package, CancellationToken ct = default)
    {
        var output = await ShellAsync(serial, $"dumpsys package {package}", ct).ConfigureAwait(false);
        var m = Regex.Match(output, @"versionName=(\S+)");
        return m.Success ? m.Groups[1].Value : null;
    }

    /// <summary>Màn hình đang bật và không ở màn khóa.</summary>
    public async Task<(bool ScreenOn, bool Locked)> GetScreenStateAsync(string? serial, CancellationToken ct = default)
    {
        var power = await ShellAsync(serial, "dumpsys power", ct).ConfigureAwait(false);
        var window = await ShellAsync(serial, "dumpsys window", ct).ConfigureAwait(false);
        return ParseScreenState(power, window);
    }

    internal static (bool ScreenOn, bool Locked) ParseScreenState(string power, string window)
    {
        var screenOn = !Regex.IsMatch(power, @"mWakefulness=(Asleep|Dozing)") &&
                       !Regex.IsMatch(power, @"Display Power: state=OFF");
        var locked = Regex.IsMatch(window, @"mShowingLockscreen=true") ||
                     Regex.IsMatch(window, @"mDreamingLockscreen=true") ||
                     Regex.IsMatch(window, @"isStatusBarKeyguard=true");
        return (screenOn, locked);
    }

    public async Task<string> GetPropAsync(string? serial, string prop, CancellationToken ct = default) =>
        (await ShellAsync(serial, $"getprop {prop}", ct).ConfigureAwait(false)).Trim();
}
