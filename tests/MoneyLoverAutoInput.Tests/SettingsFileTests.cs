using Microsoft.Extensions.Configuration;
using MoneyLoverAutoInput.Core.Configuration;
using MoneyLoverAutoInput.Core.Models;

namespace MoneyLoverAutoInput.Tests;

public sealed class SettingsFileTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "mlai-settings-" + Guid.NewGuid().ToString("N"));
    private string ConfigPath => Path.Combine(_dir, "appsettings.json");

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    private static AppSettings LoadDefault()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "appsettings.default.json");
        return new ConfigurationBuilder().AddJsonFile(path).Build().Get<AppSettings>()!;
    }

    private AppSettings Rebind() =>
        new ConfigurationBuilder().AddJsonFile(ConfigPath).Build().Get<AppSettings>()!;

    private static AppSettings Valid() => new()
    {
        Defaults = new DefaultsOptions { Wallet = "Ví A", ExpenseCategory = "Khác", IncomeCategory = "Thu khác" },
        KnownCategories = new()
        {
            ["Chi"] = new() { new() { Name = "Ăn uống" }, new() { Name = "Khác" } },
            ["Thu"] = new() { new() { Name = "Lương" }, new() { Name = "Thu khác" } },
        },
        CategoryRules = new()
        {
            new CategoryRule { Match = new() { "GRAB" }, Type = TransactionType.Chi, Category = "Ăn uống" },
        },
    };

    [Fact]
    public void Saved_file_binds_back_to_same_values_and_keeps_untouched_sections()
    {
        var s = SettingsFile.Clone(LoadDefault());
        s.SetupCompleted = true;
        s.Defaults.Wallet = "Ví Vietcombank";
        s.KnownCategories["Chi"] = new() { new() { Name = "Food & Beverage", Children = new() { "Cà phê" } }, new() { Name = "Khác" } };
        s.CategoryRules = new()
        {
            new CategoryRule { Match = new() { "HIGHLANDS", "PHUC LONG" }, Type = TransactionType.Chi, Category = "Food & Beverage > Cà phê" },
            new CategoryRule { Match = new() { "MOMO" }, Action = RuleAction.Skip },
            new CategoryRule { Match = new() { "ME" }, Category = "Khác" }, // Type = null (cả hai)
        };

        SettingsFile.Save(ConfigPath, s);
        var back = Rebind();

        Assert.True(back.SetupCompleted);
        Assert.Equal("Ví Vietcombank", back.Defaults.Wallet);
        Assert.Equal(new[] { "Food & Beverage", "Food & Beverage > Cà phê", "Khác" }, back.GetKnownCategories(TransactionType.Chi));
        Assert.Equal("Food & Beverage > Cà phê", back.CategoryRules[0].Category);
        Assert.Equal(3, back.CategoryRules.Count);
        Assert.Equal(new[] { "HIGHLANDS", "PHUC LONG" }, back.CategoryRules[0].Match);
        Assert.Equal(TransactionType.Chi, back.CategoryRules[0].Type);
        Assert.Equal(RuleAction.Skip, back.CategoryRules[1].Action);
        Assert.Null(back.CategoryRules[2].Type);

        // Mục màn Thiết lập không đụng tới vẫn còn nguyên.
        Assert.Equal("com.bookmark.money:id/btn_save", back.Selectors["SaveButton"].Value);
        Assert.Equal("com.bookmark.money", back.Appium.AppPackage);
        Assert.False(File.Exists(ConfigPath + ".tmp"));
    }

    [Fact]
    public void Saved_json_is_readable()
    {
        SettingsFile.Save(ConfigPath, Valid());
        var json = File.ReadAllText(ConfigPath);

        Assert.Contains("Ăn uống", json);           // không escape \uXXXX
        Assert.Contains("\"Type\": \"Chi\"", json); // enum dạng chữ
        Assert.DoesNotContain("\"Regex\"", json);   // null bị bỏ
    }

    [Fact]
    public void Clone_is_independent()
    {
        var a = Valid();
        var b = SettingsFile.Clone(a);
        b.KnownCategories["Chi"].Add(new CategoryNode { Name = "Mới" });
        b.KnownCategories["Thu"][0].Children.Add("Thưởng");
        b.CategoryRules[0].Match.Add("BE");

        Assert.Equal(2, a.KnownCategories["Chi"].Count);
        Assert.Empty(a.KnownCategories["Thu"][0].Children);
        Assert.Single(a.CategoryRules[0].Match);
    }

    [Fact]
    public void Valid_settings_have_no_errors_or_warnings()
    {
        var v = SettingsFile.Validate(Valid());

        Assert.Empty(v.Errors);
        Assert.Empty(v.Warnings);
    }

    [Fact]
    public void Missing_wallet_and_bad_rules_are_errors()
    {
        var s = Valid();
        s.Defaults.Wallet = " ";
        s.CategoryRules.Add(new CategoryRule { Match = new() { " " }, Category = "Khác" });
        s.CategoryRules.Add(new CategoryRule { Match = new() { "X" } });               // Import mà không có nhóm
        s.CategoryRules.Add(new CategoryRule { Regex = "(", Category = "Khác" });
        s.CategoryRules.Add(new CategoryRule { Match = new() { "Y" }, Action = RuleAction.Skip }); // hợp lệ

        var v = SettingsFile.Validate(s);

        Assert.Equal(4, v.Errors.Count);
        Assert.Contains(v.Errors, e => e.Contains("ví"));
        Assert.Contains(v.Errors, e => e.StartsWith("Luật #2") && e.Contains("từ khóa"));
        Assert.Contains(v.Errors, e => e.StartsWith("Luật #3") && e.Contains("nhóm"));
        Assert.Contains(v.Errors, e => e.StartsWith("Luật #4") && e.Contains("regex"));
    }

    [Fact]
    public void Unknown_categories_are_warnings_only()
    {
        var s = Valid();
        s.Defaults.ExpenseCategory = "Không có";
        s.CategoryRules[0].Category = "Lương"; // nhóm Thu nhưng luật cho Chi

        var v = SettingsFile.Validate(s);

        Assert.True(v.IsValid);
        Assert.Equal(2, v.Warnings.Count);
    }

    [Fact]
    public void Duplicate_category_path_is_a_warning()
    {
        var s = Valid();
        s.KnownCategories["Chi"].Add(new CategoryNode { Name = "Beauty", Children = new() { "Cắt Tóc", "Cắt Tóc" } });

        var v = SettingsFile.Validate(s);

        Assert.True(v.IsValid);
        Assert.Contains(v.Warnings, w => w.Contains("Beauty > Cắt Tóc") && w.Contains("trùng"));
    }
}
