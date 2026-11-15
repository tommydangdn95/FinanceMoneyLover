namespace MoneyLoverAutoInput.Core.Privacy;

/// <summary>Che số tài khoản trước khi hiển thị/log/ghi state (SPEC 11).</summary>
public static class Masking
{
    public static string MaskAccount(string? account)
    {
        if (string.IsNullOrWhiteSpace(account)) return "";
        var a = account.Trim();
        return a.Length <= 4 ? "***" : "***" + a[^4..];
    }

    /// <summary>Thay mọi lần xuất hiện của số tài khoản trong text bằng bản đã che.</summary>
    public static string MaskInText(string text, string? account)
    {
        if (string.IsNullOrWhiteSpace(account) || account.Trim().Length <= 4) return text;
        return text.Replace(account.Trim(), MaskAccount(account), StringComparison.Ordinal);
    }
}
