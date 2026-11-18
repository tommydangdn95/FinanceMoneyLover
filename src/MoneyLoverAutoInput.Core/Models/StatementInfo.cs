using MoneyLoverAutoInput.Core.Privacy;

namespace MoneyLoverAutoInput.Core.Models;

public enum StatementKind
{
    /// <summary>"Lịch sử giao dịch tài khoản" (tài khoản thanh toán).</summary>
    BankAccount,

    /// <summary>"Lịch sử giao dịch thẻ tín dụng" — không có Mã giao dịch, khóa được tự tạo.</summary>
    CreditCard,

    /// <summary>Bảng giao dịch tải từ BIDV — Số tiền có dấu, không có số tài khoản trong file.</summary>
    Bidv,
}

/// <summary>Metadata ở phần đầu file sao kê.</summary>
public sealed record StatementInfo
{
    public required string SourceFile { get; init; }

    public StatementKind Kind { get; init; } = StatementKind.BankAccount;

    /// <summary>Số tài khoản (hoặc số thẻ đã che sẵn của ngân hàng) thật. Chỉ dùng để tra <c>Wallets</c>, không hiển thị/log trực tiếp.</summary>
    public string AccountNumber { get; init; } = "";

    /// <summary>Khoảng thời gian dạng text, ví dụ "23/10/2026 - 16/11/2026".</summary>
    public string Period { get; init; } = "";

    public string MaskedAccount => Masking.MaskAccount(AccountNumber);

    /// <summary>Tên file đã che số tài khoản (dùng cho log/state).</summary>
    public string MaskedSourceFileName => Masking.MaskInText(Path.GetFileName(SourceFile), AccountNumber);
}
