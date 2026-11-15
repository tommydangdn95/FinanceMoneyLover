using MoneyLoverAutoInput.Core.Models;

namespace MoneyLoverAutoInput.Core.Configuration;

public enum RuleAction
{
    Import,
    Skip,
}

/// <summary>Một luật phân loại (SPEC 7.2). Luật đầu tiên khớp sẽ thắng.</summary>
public sealed class CategoryRule
{
    /// <summary>Keyword; khớp "chứa chuỗi" sau khi chuẩn hóa (bỏ dấu, lowercase).</summary>
    public List<string> Match { get; set; } = new();

    /// <summary>Regex tùy chọn, áp lên chuỗi đã chuẩn hóa, không phân biệt hoa thường.</summary>
    public string? Regex { get; set; }

    /// <summary>null = áp cho cả Chi và Thu.</summary>
    public TransactionType? Type { get; set; }

    public string? Category { get; set; }

    public RuleAction Action { get; set; } = RuleAction.Import;

    /// <summary>Mô tả ngắn để hiển thị ở cột "Luật khớp".</summary>
    public string Describe()
    {
        var parts = Match.Where(m => !string.IsNullOrWhiteSpace(m)).ToList();
        if (!string.IsNullOrWhiteSpace(Regex)) parts.Add($"/{Regex}/");
        return string.Join(", ", parts);
    }
}
