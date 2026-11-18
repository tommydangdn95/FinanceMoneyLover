using System.Diagnostics;
using System.Net.Http;
using System.Text.Json;
using MoneyLoverAutoInput.Core.Configuration;
using Microsoft.Extensions.Logging;

namespace MoneyLoverAutoInput.Core.Tools;

/// <summary>
/// Tìm / kiểm tra / tự khởi động Appium server (SPEC 8.1 bước 4). Chỉ dừng server do chính app khởi động.
/// </summary>
public sealed class AppiumServerManager : IDisposable
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(3) };

    private readonly ILogger<AppiumServerManager> _logger;
    private Process? _process;

    public AppiumServerManager(ILogger<AppiumServerManager> logger) => _logger = logger;

    /// <summary>Server đang chạy là do app khởi động (sẽ tắt khi thoát app).</summary>
    public bool StartedByApp => _process is { HasExited: false };

    /// <summary>appium.cmd: Tools.AppiumCommand (đường dẫn) → %APPDATA%\npm → PATH.</summary>
    public static string? LocateCommand(ToolsOptions tools)
    {
        var cmd = tools.AppiumCommand?.Trim() ?? "";
        if (cmd.Length > 0 && (cmd.Contains('\\') || cmd.Contains('/')) && File.Exists(cmd)) return cmd;

        var npm = Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.ApplicationData), "npm", "appium.cmd");
        if (File.Exists(npm)) return npm;
        return EnvVars.FindOnPath("appium.cmd", "appium.exe");
    }

    /// <summary>Driver uiautomator2 đã cài trong APPIUM_HOME (mặc định %USERPROFILE%\.appium).</summary>
    public static bool IsUiAutomator2Installed()
    {
        var home = EnvVars.Get("APPIUM_HOME") ??
                   Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile), ".appium");
        return Directory.Exists(Path.Combine(home, "node_modules", "appium-uiautomator2-driver"));
    }

    public static async Task<(bool Ready, string? Version)> GetStatusAsync(string serverUrl, CancellationToken ct = default)
    {
        try
        {
            using var resp = await Http.GetAsync(serverUrl.TrimEnd('/') + "/status", ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode) return (false, null);
            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false));
            var value = doc.RootElement.GetProperty("value");
            var ready = value.TryGetProperty("ready", out var r) && r.GetBoolean();
            var version = value.TryGetProperty("build", out var b) && b.TryGetProperty("version", out var v)
                ? v.GetString()
                : null;
            return (ready, version);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException
                                       or KeyNotFoundException or InvalidOperationException)
        {
            return (false, null);
        }
    }

    /// <summary>Bảo đảm server sẵn sàng: đang chạy thì dùng luôn, không thì tự khởi động (nếu được phép).</summary>
    public async Task EnsureRunningAsync(AppSettings settings, CancellationToken ct = default)
    {
        if ((await GetStatusAsync(settings.Appium.ServerUrl, ct).ConfigureAwait(false)).Ready) return;
        if (!settings.Tools.AutoStartAppium)
            throw new InvalidOperationException("Appium server chưa chạy và AutoStartAppium = false. Hãy chạy 'appium' trong terminal.");
        await StartAsync(settings, ct).ConfigureAwait(false);
    }

    public async Task StartAsync(AppSettings settings, CancellationToken ct = default)
    {
        if (StartedByApp) return;

        var cmd = LocateCommand(settings.Tools)
                  ?? throw new FileNotFoundException("Không tìm thấy Appium. Cài bằng: npm i -g appium");
        var port = new Uri(settings.Appium.ServerUrl).Port;

        var psi = new ProcessStartInfo(cmd, $"--port {port} --log-level warn")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        // App có thể được mở trước khi người dùng đặt biến môi trường → lấy giá trị user/machine.
        foreach (var name in new[] { "ANDROID_HOME", "ANDROID_SDK_ROOT", "JAVA_HOME", "APPIUM_HOME" })
        {
            var v = EnvVars.Get(name);
            if (v is not null) psi.Environment[name] = v;
        }
        psi.Environment["PATH"] = string.Join(";", EnvVars.PathDirs());

        _logger.LogInformation("Khởi động Appium: {Cmd} (cổng {Port})", cmd, port);
        _process = new Process { StartInfo = psi, EnableRaisingEvents = true };
        _process.OutputDataReceived += (_, e) => { if (!string.IsNullOrWhiteSpace(e.Data)) _logger.LogDebug("appium: {Line}", e.Data); };
        _process.ErrorDataReceived += (_, e) => { if (!string.IsNullOrWhiteSpace(e.Data)) _logger.LogWarning("appium: {Line}", e.Data); };
        _process.Start();
        _process.BeginOutputReadLine();
        _process.BeginErrorReadLine();

        var deadline = DateTime.UtcNow.AddSeconds(60);
        while (DateTime.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();
            if (_process.HasExited)
                throw new InvalidOperationException($"Appium thoát ngay khi khởi động (mã {_process.ExitCode}). Xem log.");
            if ((await GetStatusAsync(settings.Appium.ServerUrl, ct).ConfigureAwait(false)).Ready)
            {
                _logger.LogInformation("Appium sẵn sàng");
                return;
            }
            await Task.Delay(1000, ct).ConfigureAwait(false);
        }
        Stop();
        throw new TimeoutException("Appium không sẵn sàng sau 60 giây.");
    }

    public void Stop()
    {
        if (_process is null) return;
        try
        {
            if (!_process.HasExited) _process.Kill(entireProcessTree: true);
            _logger.LogInformation("Đã dừng Appium do app khởi động");
        }
        catch (InvalidOperationException) { }
        _process.Dispose();
        _process = null;
    }

    public void Dispose() => Stop();
}
