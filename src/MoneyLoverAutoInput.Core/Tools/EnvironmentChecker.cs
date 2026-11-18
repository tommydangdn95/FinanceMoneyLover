using MoneyLoverAutoInput.Core.Configuration;

namespace MoneyLoverAutoInput.Core.Tools;

public enum CheckStatus
{
    Ok,
    Warning,
    Error,
}

public sealed record CheckItem(string Title, CheckStatus Status, string Detail, string Hint = "");

public sealed class EnvironmentReport
{
    public List<CheckItem> Items { get; } = new();
    public string? AdbPath { get; set; }

    /// <summary>Serial của thiết bị sẽ dùng (Udid trong config, hoặc thiết bị duy nhất).</summary>
    public string? DeviceSerial { get; set; }

    public bool AppiumReady { get; set; }

    /// <summary>Đủ điều kiện để chạy nhập (không có mục Error nào, trừ Appium chưa chạy nếu được tự khởi động).</summary>
    public bool CanRun { get; set; }

    public string DeviceSummary { get; set; } = "chưa kiểm tra";
    public string AppiumSummary { get; set; } = "chưa kiểm tra";
}

/// <summary>Kiểm tra môi trường trước khi nhập (SPEC 10.2). Chỉ đọc trạng thái, không thao tác thiết bị.</summary>
public static class EnvironmentChecker
{
    public static async Task<EnvironmentReport> CheckAsync(AppSettings s, CancellationToken ct = default)
    {
        var r = new EnvironmentReport();
        var blocking = false;

        // --- adb ---
        r.AdbPath = AdbClient.Locate(s.Tools);
        if (r.AdbPath is null)
        {
            r.Items.Add(new("adb (Android Platform-Tools)", CheckStatus.Error, "Không tìm thấy adb.exe.",
                "Cài Android Platform-Tools, đặt ANDROID_HOME hoặc điền Tools.AdbPath trong cấu hình."));
            blocking = true;
        }
        else
        {
            r.Items.Add(new("adb (Android Platform-Tools)", CheckStatus.Ok, r.AdbPath));
        }

        // --- Biến môi trường ---
        var androidHome = EnvVars.Get("ANDROID_HOME") ?? EnvVars.Get("ANDROID_SDK_ROOT");
        r.Items.Add(androidHome is null
            ? new("ANDROID_HOME", CheckStatus.Error, "Chưa đặt.", "Đặt ANDROID_HOME = thư mục Android SDK (chứa platform-tools).")
            : new("ANDROID_HOME", CheckStatus.Ok, androidHome));
        blocking |= androidHome is null;

        var javaHome = EnvVars.Get("JAVA_HOME");
        var javaOk = javaHome is not null && File.Exists(Path.Combine(javaHome, "bin", "java.exe"));
        r.Items.Add(javaOk
            ? new("JAVA_HOME", CheckStatus.Ok, javaHome!)
            : new("JAVA_HOME", CheckStatus.Error, javaHome is null ? "Chưa đặt." : $"Không có bin\\java.exe trong {javaHome}.",
                "Cài JDK 17 trở lên và đặt JAVA_HOME."));
        blocking |= !javaOk;

        // --- Node / Appium ---
        var node = EnvVars.FindOnPath("node.exe");
        r.Items.Add(node is null
            ? new("Node.js", CheckStatus.Error, "Không tìm thấy node.exe.", "Cài Node.js LTS.")
            : new("Node.js", CheckStatus.Ok, node));
        blocking |= node is null;

        var appiumCmd = AppiumServerManager.LocateCommand(s.Tools);
        r.Items.Add(appiumCmd is null
            ? new("Appium", CheckStatus.Error, "Chưa cài.", "Chạy: npm i -g appium")
            : new("Appium", CheckStatus.Ok, appiumCmd));
        blocking |= appiumCmd is null;

        var ua2 = AppiumServerManager.IsUiAutomator2Installed();
        r.Items.Add(ua2
            ? new("Driver UiAutomator2", CheckStatus.Ok, "Đã cài.")
            : new("Driver UiAutomator2", CheckStatus.Error, "Chưa cài.", "Chạy: appium driver install uiautomator2"));
        blocking |= !ua2;

        var (ready, version) = await AppiumServerManager.GetStatusAsync(s.Appium.ServerUrl, ct).ConfigureAwait(false);
        r.AppiumReady = ready;
        if (ready)
        {
            r.Items.Add(new("Appium server", CheckStatus.Ok, $"Đang chạy ({version}) tại {s.Appium.ServerUrl}."));
            r.AppiumSummary = "đang chạy";
        }
        else if (s.Tools.AutoStartAppium)
        {
            r.Items.Add(new("Appium server", CheckStatus.Warning, "Chưa chạy — app sẽ tự khởi động khi bắt đầu nhập."));
            r.AppiumSummary = "chưa chạy (tự bật khi nhập)";
        }
        else
        {
            r.Items.Add(new("Appium server", CheckStatus.Error, "Chưa chạy.", "Chạy 'appium' trong terminal hoặc bật Tools.AutoStartAppium."));
            r.AppiumSummary = "chưa chạy";
            blocking = true;
        }

        // --- Thiết bị ---
        if (r.AdbPath is not null)
        {
            var adb = new AdbClient(r.AdbPath);
            IReadOnlyList<AdbDevice> devices;
            try
            {
                devices = await adb.GetDevicesAsync(ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is TimeoutException or System.ComponentModel.Win32Exception)
            {
                devices = Array.Empty<AdbDevice>();
                r.Items.Add(new("Thiết bị", CheckStatus.Error, "Không chạy được adb: " + ex.Message));
            }

            var udid = s.Appium.Udid?.Trim() ?? "";
            var candidates = udid.Length > 0 ? devices.Where(d => d.Serial == udid).ToList() : devices.ToList();
            var unauthorized = candidates.Where(d => d.State == "unauthorized").ToList();
            var readyDevices = candidates.Where(d => d.IsReady).ToList();

            if (readyDevices.Count == 1)
            {
                r.DeviceSerial = readyDevices[0].Serial;
                var model = await adb.GetPropAsync(r.DeviceSerial, "ro.product.model", ct).ConfigureAwait(false);
                var android = await adb.GetPropAsync(r.DeviceSerial, "ro.build.version.release", ct).ConfigureAwait(false);
                r.Items.Add(new("Thiết bị", CheckStatus.Ok, $"{model} · Android {android} · {r.DeviceSerial}"));
                r.DeviceSummary = string.IsNullOrWhiteSpace(model) ? "đã kết nối" : model;
            }
            else if (unauthorized.Count > 0)
            {
                r.Items.Add(new("Thiết bị", CheckStatus.Error, "Điện thoại chưa cho phép USB debugging.",
                    "Mở khóa điện thoại và bấm 'Cho phép' trong hộp thoại USB debugging."));
                r.DeviceSummary = "chưa cho phép";
                blocking = true;
            }
            else if (readyDevices.Count > 1)
            {
                r.Items.Add(new("Thiết bị", CheckStatus.Error, $"Có {readyDevices.Count} thiết bị.",
                    "Rút bớt thiết bị hoặc điền Appium.Udid trong cấu hình."));
                r.DeviceSummary = "nhiều thiết bị";
                blocking = true;
            }
            else
            {
                r.Items.Add(new("Thiết bị", CheckStatus.Error,
                    udid.Length > 0 ? $"Không thấy thiết bị {udid}." : "Chưa có điện thoại nào kết nối.",
                    "Cắm cáp USB, bật USB debugging (Developer options)."));
                r.DeviceSummary = "chưa kết nối";
                blocking = true;
            }

            if (r.DeviceSerial is not null)
            {
                var (screenOn, locked) = await adb.GetScreenStateAsync(r.DeviceSerial, ct).ConfigureAwait(false);
                r.Items.Add(screenOn && !locked
                    ? new("Màn hình", CheckStatus.Ok, "Đang bật, đã mở khóa.")
                    : new("Màn hình", CheckStatus.Warning, screenOn ? "Đang ở màn khóa." : "Đang tắt.",
                        "Mở khóa điện thoại và bật 'Stay awake' trong Developer options."));

                var pkg = s.Appium.AppPackage;
                if (await adb.IsPackageInstalledAsync(r.DeviceSerial, pkg, ct).ConfigureAwait(false))
                {
                    var v = await adb.GetPackageVersionAsync(r.DeviceSerial, pkg, ct).ConfigureAwait(false);
                    r.Items.Add(new("Money Lover", CheckStatus.Ok, $"{pkg} {v}"));
                }
                else
                {
                    r.Items.Add(new("Money Lover", CheckStatus.Error, $"Chưa cài {pkg} trên điện thoại.",
                        "Kiểm tra Appium.AppPackage trong cấu hình."));
                    blocking = true;
                }
            }
        }

        var todo = s.Selectors.Where(kv => kv.Value.Value.Contains("TODO", StringComparison.Ordinal)).Select(kv => kv.Key).ToList();
        if (todo.Count > 0)
        {
            r.Items.Add(new("Selector Money Lover", CheckStatus.Error, "Còn TODO: " + string.Join(", ", todo),
                "Soi UI bằng uiautomator dump (SPEC mục 12)."));
            blocking = true;
        }
        else
        {
            r.Items.Add(new("Selector Money Lover", CheckStatus.Ok, $"{s.Selectors.Count} selector."));
        }

        r.CanRun = !blocking;
        return r;
    }
}
