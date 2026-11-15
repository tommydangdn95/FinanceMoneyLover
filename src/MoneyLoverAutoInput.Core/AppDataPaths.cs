namespace MoneyLoverAutoInput.Core;

/// <summary>Thư mục dữ liệu người dùng (SPEC 5.1). Không bao giờ ghi cạnh file exe.</summary>
public sealed class AppDataPaths
{
    public const string AppFolderName = "MoneyLoverAutoInput";

    public string Root { get; }
    public string ConfigFile => Path.Combine(Root, "appsettings.json");
    public string StateFile => Path.Combine(Root, "state.json");
    public string LogsDir => Path.Combine(Root, "logs");
    public string ScreenshotsDir => Path.Combine(Root, "screenshots");

    public AppDataPaths(string root) => Root = root;

    public static AppDataPaths Default() => new(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AppFolderName));

    public void EnsureCreated()
    {
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(LogsDir);
        Directory.CreateDirectory(ScreenshotsDir);
    }
}
