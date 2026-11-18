using System.Text.RegularExpressions;
using MoneyLoverAutoInput.Core.Configuration;
using MoneyLoverAutoInput.Core.Models;

namespace MoneyLoverAutoInput.Core.Categorization;

/// <summary>Chuyển <see cref="StatementRow"/> thành <see cref="TransactionRow"/> theo SPEC 6.3 và 7.2.</summary>
public sealed class Categorizer
{
    private readonly AppSettings _settings;
    private readonly List<CompiledRule> _rules;

    /// <summary>Lỗi trong cấu hình luật (regex sai...). Luật lỗi bị bỏ qua.</summary>
    public IReadOnlyList<string> RuleErrors { get; }

    public Categorizer(AppSettings settings)
    {
        _settings = settings;
        var errors = new List<string>();
        _rules = new List<CompiledRule>();

        for (var i = 0; i < settings.CategoryRules.Count; i++)
        {
            var rule = settings.CategoryRules[i];
            var keywords = rule.Match
                .Select(TextNormalizer.Normalize)
                .Where(k => k.Length > 0)
                .ToArray();

            Regex? regex = null;
            if (!string.IsNullOrWhiteSpace(rule.Regex))
            {
                try
                {
                    regex = new Regex(rule.Regex, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
                        TimeSpan.FromMilliseconds(200));
                }
                catch (ArgumentException ex)
                {
                    errors.Add($"Luật #{i + 1}: regex không hợp lệ ({ex.Message}).");
                    continue;
                }
            }

            if (keywords.Length == 0 && regex is null)
            {
                errors.Add($"Luật #{i + 1}: không có keyword hay regex nào.");
                continue;
            }

            _rules.Add(new CompiledRule(rule, keywords, regex));
        }

        RuleErrors = errors;
    }

    public TransactionRow Categorize(StatementRow row, StatementInfo info)
    {
        var dataErrors = new List<string>(row.ParseErrors);

        var debit = row.Debit ?? 0;
        var credit = row.Credit ?? 0;
        TransactionType type;
        long amount;

        if (debit > 0 && credit <= 0)
        {
            type = TransactionType.Chi;
            amount = debit;
        }
        else if (credit > 0 && debit <= 0)
        {
            type = TransactionType.Thu;
            amount = credit;
        }
        else
        {
            type = TransactionType.Chi;
            amount = 0;
            // Đã có lỗi parse số tiền thì không báo thêm.
            if (!row.AmountParseFailed)
            {
                dataErrors.Add(debit > 0
                    ? "Có cả Ghi nợ lẫn Ghi có, không xác định được loại."
                    : "Không có số tiền (Ghi nợ và Ghi có đều trống hoặc bằng 0).");
            }
        }

        var date = row.ExecutedAt is { } dt ? DateOnly.FromDateTime(dt) : row.EffectiveDate ?? default;
        var sortKey = row.ExecutedAt ?? row.EffectiveDate?.ToDateTime(TimeOnly.MinValue) ?? DateTime.MinValue;

        var haystack = TextNormalizer.Normalize(
            $"{row.Description} {row.CounterpartName} {row.CounterpartAccount}");
        var matched = FindRule(haystack, type);

        var fallback = type == TransactionType.Chi
            ? _settings.Defaults.ExpenseCategory
            : _settings.Defaults.IncomeCategory;

        var category = matched?.Rule.Action == RuleAction.Import && !string.IsNullOrWhiteSpace(matched.Rule.Category)
            ? matched.Rule.Category!
            : fallback;

        var tx = new TransactionRow
        {
            Id = row.TxnCode.Trim(),
            TxnCode = row.TxnCode.Trim(),
            ExcelRow = row.ExcelRow,
            SortKey = sortKey,
            OriginalNgay = date,
            OriginalLoai = type,
            OriginalSoTien = amount,
            Ngay = date,
            Loai = type,
            SoTien = amount,
            Nhom = category?.Trim() ?? "",
            Vi = ResolveWallet(info),
            GhiChu = BuildNote(row),
            Description = row.Description,
            CounterpartName = row.CounterpartName.Trim(),
            CounterpartAccount = row.CounterpartAccount.Trim(),
            MatchedRule = matched?.Rule.Describe() ?? "",
            Action = matched?.Rule.Action ?? RuleAction.Import,
        };
        tx.DataErrors.AddRange(dataErrors);
        return tx;
    }

    /// <summary>Dùng cho ô "Thử luật": trả về luật khớp (hoặc null) cho một mô tả.</summary>
    public CategoryRule? Test(string text, TransactionType type) =>
        FindRule(TextNormalizer.Normalize(text), type)?.Rule;

    private CompiledRule? FindRule(string haystack, TransactionType type)
    {
        foreach (var r in _rules)
        {
            if (r.Rule.Type is { } t && t != type) continue;
            if (r.Matches(haystack)) return r;
        }
        return null;
    }

    private string ResolveWallet(StatementInfo info)
    {
        var account = info.AccountNumber.Trim();
        if (account.Length > 0 && _settings.Wallets.TryGetValue(account, out var wallet)
                               && !string.IsNullOrWhiteSpace(wallet))
        {
            return wallet.Trim();
        }
        return _settings.Defaults.Wallet.Trim();
    }

    private string BuildNote(StatementRow row)
    {
        var template = string.IsNullOrEmpty(_settings.Defaults.NoteTemplate)
            ? "{description}"
            : _settings.Defaults.NoteTemplate;

        var note = template
            .Replace("{description}", row.Description, StringComparison.OrdinalIgnoreCase)
            .Replace("{counterpartName}", row.CounterpartName, StringComparison.OrdinalIgnoreCase)
            .Replace("{txnCode}", row.TxnCode, StringComparison.OrdinalIgnoreCase);

        // Dọn dấu phân cách thừa khi placeholder trống, ví dụ "{description} - {counterpartName}".
        return note.Trim().TrimEnd('-', '|', ',').Trim();
    }

    private sealed record CompiledRule(CategoryRule Rule, string[] Keywords, Regex? Regex)
    {
        public bool Matches(string haystack)
        {
            foreach (var k in Keywords)
            {
                if (haystack.Contains(k, StringComparison.Ordinal)) return true;
            }

            if (Regex is null) return false;
            try
            {
                return Regex.IsMatch(haystack);
            }
            catch (RegexMatchTimeoutException)
            {
                return false;
            }
        }
    }
}
