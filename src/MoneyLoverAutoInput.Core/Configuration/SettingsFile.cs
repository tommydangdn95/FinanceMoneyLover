using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using MoneyLoverAutoInput.Core.Models;

namespace MoneyLoverAutoInput.Core.Configuration;

public sealed class SettingsValidation
{
    /// <summary>Lỗi chặn việc lưu.</summary>
    public List<string> Errors { get; } = new();

    /// <summary>Cảnh báo — vẫn cho lưu.</summary>
    public List<string> Warnings { get; } = new();

    public bool IsValid => Errors.Count == 0;
}

/// <summary>
/// Ghi config người dùng từ màn Thiết lập. Ghi toàn bộ <see cref="AppSettings"/> nên các mục UI không sửa
/// (Selectors, Appium...) được giữ nguyên; comment trong file sẽ mất (SPEC 7.1).
/// </summary>
public static class SettingsFile
{
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>Bản sao sâu — không bao giờ sửa trực tiếp instance của IOptionsMonitor.</summary>
    public static AppSettings Clone(AppSettings settings) =>
        JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(settings, JsonOptions), JsonOptions)!;

    /// <summary>Ghi atomic: file tạm rồi rename, để bộ đọc config không bao giờ thấy file dở dang.</summary>
    public static void Save(string path, AppSettings settings)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        var tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(settings, JsonOptions), new System.Text.UTF8Encoding(false));
        File.Move(tmp, path, overwrite: true);
    }

    public static SettingsValidation Validate(AppSettings s)
    {
        var v = new SettingsValidation();

        if (string.IsNullOrWhiteSpace(s.Defaults.Wallet))
            v.Errors.Add("Chưa nhập tên ví mặc định.");

        var expense = s.GetKnownCategories(TransactionType.Chi);
        var income = s.GetKnownCategories(TransactionType.Thu);
        if (expense.Count == 0) v.Warnings.Add("Danh sách nhóm Chi đang trống.");
        if (income.Count == 0) v.Warnings.Add("Danh sách nhóm Thu đang trống.");

        foreach (var type in new[] { TransactionType.Chi, TransactionType.Thu })
        {
            foreach (var dup in CategoryTree.FindDuplicates(s.GetCategoryTree(type)))
            {
                v.Warnings.Add($"Nhóm {type} '{dup}' bị trùng: app sẽ không phân biệt được — nên đổi tên một nhóm trong Money Lover.");
            }
        }

        CheckDefault(s.Defaults.ExpenseCategory, expense, "Chi", v);
        CheckDefault(s.Defaults.IncomeCategory, income, "Thu", v);

        for (var i = 0; i < s.CategoryRules.Count; i++)
        {
            var rule = s.CategoryRules[i];
            var name = $"Luật #{i + 1}";
            var hasKeyword = rule.Match.Any(m => !string.IsNullOrWhiteSpace(m));

            if (!hasKeyword && string.IsNullOrWhiteSpace(rule.Regex))
                v.Errors.Add($"{name}: chưa có từ khóa.");

            if (!string.IsNullOrWhiteSpace(rule.Regex))
            {
                try { _ = new Regex(rule.Regex); }
                catch (ArgumentException) { v.Errors.Add($"{name}: regex không hợp lệ."); }
            }

            if (rule.Action == RuleAction.Skip) continue;

            if (string.IsNullOrWhiteSpace(rule.Category))
            {
                v.Errors.Add($"{name}: chưa chọn nhóm (hoặc đổi hành động thành Bỏ qua).");
                continue;
            }

            var types = rule.Type is { } t ? new[] { t } : new[] { TransactionType.Chi, TransactionType.Thu };
            var known = types.SelectMany(t => s.GetKnownCategories(t)).ToList();
            if (known.Count > 0 && !known.Contains(rule.Category.Trim()))
                v.Warnings.Add($"{name}: nhóm '{rule.Category}' không có trong danh sách nhóm {string.Join("/", types)}.");
        }

        return v;
    }

    private static void CheckDefault(string category, IReadOnlyList<string> known, string type, SettingsValidation v)
    {
        if (string.IsNullOrWhiteSpace(category))
        {
            v.Warnings.Add($"Chưa có nhóm {type} mặc định: giao dịch không khớp luật sẽ phải chọn nhóm bằng tay.");
        }
        else if (known.Count > 0 && !known.Contains(category.Trim()))
        {
            v.Warnings.Add($"Nhóm {type} mặc định '{category}' không có trong danh sách nhóm {type}.");
        }
    }
}
