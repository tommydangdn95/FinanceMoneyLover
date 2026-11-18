using System.Globalization;

namespace MoneyLoverAutoInput.Core.Manual;

/// <summary>Số tiền gõ tay: "45000", "45.000", "45,000", "45k" (= 45.000), "1.5tr" (= 1.500.000).</summary>
public static class AmountParser
{
    public static bool TryParse(string? text, out long amount)
    {
        amount = 0;
        var t = (text ?? "").Trim().ToLowerInvariant().Replace(" ", "").TrimEnd('đ', '₫');
        if (t.Length == 0) return false;

        long multiplier = 1;
        if (t.EndsWith("tr")) { multiplier = 1_000_000; t = t[..^2]; }
        else if (t.EndsWith('k')) { multiplier = 1_000; t = t[..^1]; }

        if (multiplier > 1)
        {
            // "1.5tr", "1,5tr", "45k": phần thập phân cho phép một dấu . hoặc ,
            if (!decimal.TryParse(t.Replace(',', '.'), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var d))
                return false;
            var v = d * multiplier;
            if (v != Math.Floor(v) || v > long.MaxValue) return false;
            amount = (long)v;
            return true;
        }

        if (!t.All(c => char.IsDigit(c) || c is '.' or ',')) return false;
        return long.TryParse(new string(t.Where(char.IsDigit).ToArray()), out amount);
    }
}
