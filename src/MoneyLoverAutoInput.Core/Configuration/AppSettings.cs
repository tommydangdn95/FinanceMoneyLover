using MoneyLoverAutoInput.Core.Models;

namespace MoneyLoverAutoInput.Core.Configuration;

/// <summary>
/// Map với appsettings.json. Giá trị khởi tạo ở đây là mặc định khi key bị thiếu trong file người dùng
/// (ví dụ config tạo từ phiên bản cũ hơn).
/// </summary>
public sealed class AppSettings
{
    /// <summary>false = lần đầu dùng app: mở màn Thiết lập trước (SPEC 10.5).</summary>
    public bool SetupCompleted { get; set; }

    public ToolsOptions Tools { get; set; } = new();
    public AppiumOptions Appium { get; set; } = new();
    public TimingOptions Timing { get; set; } = new();
    public DefaultsOptions Defaults { get; set; } = new();

    /// <summary>Số tài khoản → tên ví trong app.</summary>
    public Dictionary<string, string> Wallets { get; set; } = new();

    /// <summary>Danh sách ví có trong Money Lover — để chọn ví cho từng dòng trên Xem trước.</summary>
    public List<string> KnownWallets { get; set; } = new();

    /// <summary>"Chi"/"Thu" → cây nhóm có trong app (nhóm cha + nhóm con).</summary>
    public Dictionary<string, List<CategoryNode>> KnownCategories { get; set; } = new();

    public List<CategoryRule> CategoryRules { get; set; } = new();

    public Dictionary<string, SelectorOptions> Selectors { get; set; } = new();

    /// <summary>Mọi nhóm chọn được của một loại, dạng đường dẫn ("Cha" hoặc "Cha &gt; Con").</summary>
    public IReadOnlyList<string> GetKnownCategories(TransactionType type) =>
        CategoryTree.Flatten(GetCategoryTree(type));

    public IReadOnlyList<CategoryNode> GetCategoryTree(TransactionType type) =>
        KnownCategories.TryGetValue(type.ToString(), out var list) ? list : Array.Empty<CategoryNode>();
}

public sealed class ToolsOptions
{
    public string AdbPath { get; set; } = "";
    public string AppiumCommand { get; set; } = "appium";
    public bool AutoStartAppium { get; set; } = true;
}

public sealed class AppiumOptions
{
    public string ServerUrl { get; set; } = "http://127.0.0.1:4723";
    public string Udid { get; set; } = "";
    public string AppPackage { get; set; } = "com.bookmark.money";
    public int NewCommandTimeoutSec { get; set; } = 300;
}

public sealed class TimingOptions
{
    public int DefaultWaitSec { get; set; } = 10;
    public int DelayBetweenRowsMs { get; set; } = 800;
    public int RetryPerRow { get; set; } = 2;
}

public sealed class DefaultsOptions
{
    public string Wallet { get; set; } = "";
    public string ExpenseCategory { get; set; } = "";
    public string IncomeCategory { get; set; } = "";

    /// <summary>Placeholder: {description}, {counterpartName}, {txnCode}.</summary>
    public string NoteTemplate { get; set; } = "{description}";
}

public sealed class SelectorOptions
{
    /// <summary>id | text | xpath | uiautomator | accessibilityId</summary>
    public string By { get; set; } = "";
    public string Value { get; set; } = "";
}
