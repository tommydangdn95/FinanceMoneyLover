using MoneyLoverAutoInput.Core.Configuration;

namespace MoneyLoverAutoInput.Core.Models;

/// <summary>Giao dịch sẽ nhập vào Money Lover (sau phân loại, có thể bị người dùng sửa trên Xem trước).</summary>
public sealed class TransactionRow
{
    public const string UncategorizedError = "Chưa phân loại: không khớp luật nào và chưa có nhóm mặc định.";
    public const string NonPositiveAmountError = "Số tiền phải lớn hơn 0.";
    public const string NoWalletError = "Chưa có ví.";

    /// <summary>
    /// Khóa duy nhất của dòng (state.json, drafts.json). Thường = <see cref="TxnCode"/>; nếu mã bị trùng
    /// trong file thì lần xuất hiện thứ n (theo thứ tự trong file) là "MÃ#n" (SPEC 6.4).
    /// </summary>
    public required string Id { get; set; }

    /// <summary>Mã giao dịch gốc trong sao kê.</summary>
    public string TxnCode { get; init; } = "";

    public int ExcelRow { get; init; }

    /// <summary>Cảnh báo — chỉ hiển thị, KHÔNG chặn việc nhập (VD mã giao dịch trùng trong file).</summary>
    public List<string> Warnings { get; } = new();

    /// <summary>Thời điểm thực hiện theo ngân hàng, dùng để sắp xếp (không đổi khi sửa ngày).</summary>
    public DateTime SortKey { get; init; }

    // ---- Giá trị gốc theo ngân hàng (để đối chiếu / hoàn tác) ----
    public DateOnly OriginalNgay { get; init; }
    public TransactionType OriginalLoai { get; init; }
    public long OriginalSoTien { get; init; }

    // ---- Giá trị sẽ nhập (người dùng sửa được) ----
    public DateOnly Ngay { get; set; }
    public TransactionType Loai { get; set; }
    public long SoTien { get; set; }
    public string Nhom { get; set; } = "";
    public string Vi { get; set; } = "";
    public string GhiChu { get; set; } = "";

    /// <summary>Mô tả gốc trong sao kê (để hiển thị/tìm kiếm).</summary>
    public string Description { get; init; } = "";

    /// <summary>Tên bên kia của giao dịch (cột "Tên tài khoản"): người nhận nếu Chi, người gửi nếu Thu.</summary>
    public string CounterpartName { get; init; } = "";

    /// <summary>Số tài khoản bên kia (cột "Tài khoản đối ứng"). Chỉ hiển thị dạng đã che.</summary>
    public string CounterpartAccount { get; init; } = "";

    /// <summary>Mô tả ngắn của luật đã khớp, rỗng nếu dùng nhóm mặc định.</summary>
    public string MatchedRule { get; init; } = "";
    public RuleAction Action { get; init; } = RuleAction.Import;

    /// <summary>Lỗi dữ liệu cố định (ngày, số tiền, mã GD...). Dòng có lỗi này bị khóa trên UI.</summary>
    public List<string> DataErrors { get; } = new();

    public bool HasDataErrors => DataErrors.Count > 0;

    public bool IsCategorized => !string.IsNullOrWhiteSpace(Nhom);

    /// <summary>Đủ điều kiện để nhập vào app.</summary>
    public bool CanImport => !HasDataErrors && IsCategorized && SoTien > 0 && !string.IsNullOrWhiteSpace(Vi);

    /// <summary>Toàn bộ lỗi để hiển thị. Dòng bị luật Skip thì không báo lỗi phân loại/ví.</summary>
    public IReadOnlyList<string> Errors
    {
        get
        {
            if (HasDataErrors) return DataErrors;
            var errors = new List<string>();
            if (SoTien <= 0) errors.Add(NonPositiveAmountError);
            if (Action == RuleAction.Import)
            {
                if (!IsCategorized) errors.Add(UncategorizedError);
                if (string.IsNullOrWhiteSpace(Vi)) errors.Add(NoWalletError);
            }
            return errors;
        }
    }
}
