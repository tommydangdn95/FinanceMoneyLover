using System.IO;
using System.Reflection;
using MoneyLoverAutoInput.Core;

namespace MoneyLoverAutoInput.App.Services;

/// <summary>
/// Config người dùng nằm ở thư mục dữ liệu (SPEC 5.1). Chỉ đọc file người dùng — không merge với bản mặc định,
/// vì binder gộp mảng theo index sẽ làm luật đã xóa "sống lại". Key thiếu lấy mặc định từ class AppSettings.
/// </summary>
public static class UserConfig
{
    private const string DefaultResourceName = "appsettings.default.json";

    public static void EnsureExists(AppDataPaths paths)
    {
        if (File.Exists(paths.ConfigFile)) return;

        using var resource = Assembly.GetExecutingAssembly().GetManifestResourceStream(DefaultResourceName)
                             ?? throw new InvalidOperationException($"Thiếu resource {DefaultResourceName}.");
        using var file = File.Create(paths.ConfigFile);
        resource.CopyTo(file);
    }
}
