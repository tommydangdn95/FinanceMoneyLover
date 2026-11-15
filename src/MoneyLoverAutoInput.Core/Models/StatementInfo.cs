using MoneyLoverAutoInput.Core.Privacy;

namespace MoneyLoverAutoInput.Core.Models;

/// <summary>Metadata ở phần đầu file sao kê.</summary>
public sealed record StatementInfo
{
    public required string SourceFile { get; init; }

    /// <summary>Số tài khoản thật. Chỉ dùng để tra <c>Wallets</c>, không hiển thị/log trực tiếp.</summary>
    public string AccountNumber { get; init; } = "";

    /// <summary>Khoảng thời gian dạng text, ví dụ "23/10/2026 - 16/11/2026".</summary>
    public string Period { get; init; } = "";

    public string MaskedAccount => Masking.MaskAccount(AccountNumber);

    /// <summary>Tên file đã che số tài khoản (dùng cho log/state).</summary>
    public string MaskedSourceFileName => Masking.MaskInText(Path.GetFileName(SourceFile), AccountNumber);
}
