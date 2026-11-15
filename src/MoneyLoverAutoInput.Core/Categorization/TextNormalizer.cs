using System.Globalization;
using System.Text;

namespace MoneyLoverAutoInput.Core.Categorization;

/// <summary>Bỏ dấu tiếng Việt, chữ thường, gộp khoảng trắng — dùng cho so khớp luật và tên cột.</summary>
public static class TextNormalizer
{
    public static string Normalize(string? input)
    {
        if (string.IsNullOrEmpty(input)) return "";

        var decomposed = input.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);
        var lastWasSpace = true; // bỏ khoảng trắng đầu chuỗi

        foreach (var ch in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark) continue;

            var c = ch switch
            {
                'đ' or 'Đ' => 'd',
                _ => char.ToLowerInvariant(ch),
            };

            if (char.IsWhiteSpace(c))
            {
                if (!lastWasSpace) sb.Append(' ');
                lastWasSpace = true;
            }
            else
            {
                sb.Append(c);
                lastWasSpace = false;
            }
        }

        if (sb.Length > 0 && sb[^1] == ' ') sb.Length--;
        return sb.ToString();
    }
}
