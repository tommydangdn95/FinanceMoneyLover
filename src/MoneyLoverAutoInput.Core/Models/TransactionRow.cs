using MoneyLoverAutoInput.Core.Configuration;

namespace MoneyLoverAutoInput.Core.Models;

/// <summary>Giao dịch sẽ nhập vào Money Lover (sau phân loại, có thể bị người dùng sửa).</summary>
public sealed class TransactionRow
{
    public const string UncategorizedError = "Chưa phân loại: không khớp luật nào và chưa có nhóm mặc định.";

    /// <summary>Mã giao dịch — khóa chống trùng.</summary>
    public required string Id { get; init; }
    public int ExcelRow { get; init; }

    /// <summary>Thời điểm thực hiện, dùng để sắp xếp.</summary>
    public DateTime SortKey { get; init; }

    public DateOnly Ngay { get; init; }
    public TransactionType Loai { get; init; }
    public long SoTien { get; init; }
    public string Nhom { get; set; } = "";
    public string Vi { get; set; } = "";
    public string GhiChu { get; set; } = "";

    /// <summary>Mô tả gốc trong sao kê (để hiển thị/tìm kiếm).</summary>
    public string Description { get; init; } = "";

    /// <summary>Mô tả ngắn của luật đã khớp, rỗng nếu dùng nhóm mặc định.</summary>
    public string MatchedRule { get; init; } = "";
    public RuleAction Action { get; init; } = RuleAction.Import;

    /// <summary>Lỗi dữ liệu cố định (ngày, số tiền, mã GD...). Người dùng không sửa được trên UI.</summary>
    public List<string> DataErrors { get; } = new();

    public bool HasDataErrors => DataErrors.Count > 0;

    public bool IsCategorized => !string.IsNullOrWhiteSpace(Nhom);

    /// <summary>Đủ điều kiện để nhập vào app.</summary>
    public bool CanImport => !HasDataErrors && IsCategorized;

    /// <summary>Toàn bộ lỗi để hiển thị. Dòng bị luật Skip thì không báo "Chưa phân loại".</summary>
    public IReadOnlyList<string> Errors =>
        Action == RuleAction.Import && !IsCategorized
            ? DataErrors.Append(UncategorizedError).ToList()
            : DataErrors;
}
