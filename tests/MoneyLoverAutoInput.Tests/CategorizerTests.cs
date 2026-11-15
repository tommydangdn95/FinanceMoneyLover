using MoneyLoverAutoInput.Core.Categorization;
using MoneyLoverAutoInput.Core.Configuration;
using MoneyLoverAutoInput.Core.Models;
using static MoneyLoverAutoInput.Tests.TestHelpers;

namespace MoneyLoverAutoInput.Tests;

public class CategorizerTests
{
    private static StatementRow Stmt(string desc, long? debit = null, long? credit = null, string name = "",
        string code = "X1") => new()
    {
        ExcelRow = 10,
        ExecutedAt = new DateTime(2026, 11, 3, 7, 45, 0),
        Description = desc,
        Debit = debit,
        Credit = credit,
        CounterpartName = name,
        TxnCode = code,
    };

    [Fact]
    public void Debit_is_expense_and_credit_is_income()
    {
        var c = new Categorizer(Settings());

        var expense = c.Categorize(Stmt("A", debit: 54000), Info());
        var income = c.Categorize(Stmt("B", credit: 500000), Info());

        Assert.Equal(TransactionType.Chi, expense.Loai);
        Assert.Equal(54000, expense.SoTien);
        Assert.Equal(TransactionType.Thu, income.Loai);
        Assert.Equal(500000, income.SoTien);
        Assert.Equal(new DateOnly(2026, 11, 3), expense.Ngay);
        Assert.Equal("X1", expense.Id);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData(0L, 0L)]
    [InlineData(1000L, 2000L)]
    public void Needs_exactly_one_positive_amount(long? debit, long? credit)
    {
        var row = new Categorizer(Settings()).Categorize(Stmt("A", debit, credit), Info());

        Assert.True(row.HasDataErrors);
        Assert.False(row.CanImport);
    }

    [Fact]
    public void First_matching_rule_wins()
    {
        var c = new Categorizer(Settings(
            Rule("Di chuyển", TransactionType.Chi, "GRAB"),
            Rule("Ăn uống", TransactionType.Chi, "GRAB FOOD")));

        var row = c.Categorize(Stmt("GRAB FOOD 123", debit: 1000), Info());

        Assert.Equal("Di chuyển", row.Nhom);
        Assert.Equal("GRAB", row.MatchedRule);
    }

    [Fact]
    public void Matching_ignores_accents_case_and_extra_spaces()
    {
        var c = new Categorizer(Settings(Rule("Lương", TransactionType.Thu, "Lương  tháng")));

        var row = c.Categorize(Stmt("luong THANG 10 cong ty", credit: 1000), Info());

        Assert.Equal("Lương", row.Nhom);
    }

    [Fact]
    public void Matches_counterpart_name_too()
    {
        var c = new Categorizer(Settings(Rule("Lương", TransactionType.Thu, "CONG TY TNHH ABC")));

        var row = c.Categorize(Stmt("CK DEN", credit: 1000, name: "CONG TY TNHH ABC"), Info());

        Assert.Equal("Lương", row.Nhom);
    }

    [Fact]
    public void Rule_type_filter_is_respected()
    {
        var c = new Categorizer(Settings(Rule("Lương", TransactionType.Thu, "ABC")));

        var row = c.Categorize(Stmt("ABC", debit: 1000), Info());

        Assert.Equal("Khác", row.Nhom); // rule chỉ cho Thu → Chi rơi về mặc định
        Assert.Equal("", row.MatchedRule);
    }

    [Fact]
    public void Rule_without_type_applies_to_both()
    {
        var c = new Categorizer(Settings(Rule("Gia đình", null, "ME")));

        Assert.Equal("Gia đình", c.Categorize(Stmt("CK ME", debit: 1), Info()).Nhom);
        Assert.Equal("Gia đình", c.Categorize(Stmt("ME CK", credit: 1), Info()).Nhom);
    }

    [Fact]
    public void Skip_rule_sets_action_and_keeps_fallback_category()
    {
        var c = new Categorizer(Settings(SkipRule("NAP TIEN VI MOMO")));

        var row = c.Categorize(Stmt("NAP TIEN VI MOMO - 0900000000", debit: 200000), Info());

        Assert.Equal(RuleAction.Skip, row.Action);
        Assert.Equal("Khác", row.Nhom); // để người dùng tick lại vẫn có nhóm
        Assert.Empty(row.Errors);
    }

    [Fact]
    public void Uses_defaults_when_no_rule_matches()
    {
        var c = new Categorizer(Settings(Rule("Di chuyển", TransactionType.Chi, "GRAB")));

        Assert.Equal("Khác", c.Categorize(Stmt("XYZ", debit: 1), Info()).Nhom);
        Assert.Equal("Thu nhập khác", c.Categorize(Stmt("XYZ", credit: 1), Info()).Nhom);
    }

    [Fact]
    public void Empty_default_category_means_uncategorized_error_until_user_picks()
    {
        var settings = Settings();
        settings.Defaults.ExpenseCategory = "";
        var row = new Categorizer(settings).Categorize(Stmt("XYZ", debit: 1), Info());

        Assert.False(row.CanImport);
        Assert.Contains(TransactionRow.UncategorizedError, row.Errors);
        Assert.False(row.HasDataErrors);

        row.Nhom = "Ăn uống"; // người dùng chọn trên lưới
        Assert.True(row.CanImport);
        Assert.Empty(row.Errors);
    }

    [Fact]
    public void Regex_rule_matches_normalized_text()
    {
        var rule = new CategoryRule { Regex = @"^tt qr - hy\d+$", Category = "Ăn uống" };
        var c = new Categorizer(Settings(rule));

        Assert.Equal("Ăn uống", c.Categorize(Stmt("TT QR - HY00000000000001", debit: 1), Info()).Nhom);
    }

    [Fact]
    public void Invalid_rules_are_reported_and_ignored()
    {
        var c = new Categorizer(Settings(
            new CategoryRule { Regex = "(", Category = "A" },
            new CategoryRule { Match = new() { "  " }, Category = "B" },
            Rule("C", null, "X")));

        Assert.Equal(2, c.RuleErrors.Count);
        Assert.Equal("C", c.Categorize(Stmt("X", debit: 1), Info()).Nhom);
    }

    [Fact]
    public void Wallet_from_account_mapping_else_default()
    {
        var settings = Settings();
        settings.Wallets["09990001234"] = "Ví ngân hàng ABC";
        var c = new Categorizer(settings);

        Assert.Equal("Ví ngân hàng ABC", c.Categorize(Stmt("A", debit: 1), Info("09990001234")).Vi);
        Assert.Equal("Tài khoản ngân hàng", c.Categorize(Stmt("A", debit: 1), Info("111")).Vi);
    }

    [Fact]
    public void Note_template_replaces_placeholders_and_trims_dangling_separator()
    {
        var settings = Settings();
        settings.Defaults.NoteTemplate = "{description} - {counterpartName}";
        var c = new Categorizer(settings);

        Assert.Equal("CK DEN - NGUYEN VAN A", c.Categorize(Stmt("CK DEN", credit: 1, name: "NGUYEN VAN A"), Info()).GhiChu);
        Assert.Equal("CK DEN", c.Categorize(Stmt("CK DEN", credit: 1), Info()).GhiChu);
    }

    [Fact]
    public void Test_method_returns_matching_rule()
    {
        var grab = Rule("Di chuyển", TransactionType.Chi, "GRAB");
        var c = new Categorizer(Settings(grab));

        Assert.Same(grab, c.Test("grab*a-123", TransactionType.Chi));
        Assert.Null(c.Test("grab*a-123", TransactionType.Thu));
    }

    [Theory]
    [InlineData("Tiền Mặt  Đã Rút", "tien mat da rut")]
    [InlineData("  ĐƯỜNG  ", "duong")]
    [InlineData("", "")]
    public void Normalizer_strips_diacritics(string input, string expected) =>
        Assert.Equal(expected, TextNormalizer.Normalize(input));
}
