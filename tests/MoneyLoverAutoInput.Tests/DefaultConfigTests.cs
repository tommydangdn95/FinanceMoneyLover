using Microsoft.Extensions.Configuration;
using MoneyLoverAutoInput.Core.Configuration;
using MoneyLoverAutoInput.Core.Models;

namespace MoneyLoverAutoInput.Tests;

/// <summary>File appsettings.default.json đi kèm app phải bind được vào AppSettings.</summary>
public class DefaultConfigTests
{
    private static AppSettings Bind()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "appsettings.default.json");
        var config = new ConfigurationBuilder().AddJsonFile(path, optional: false).Build();
        return config.Get<AppSettings>() ?? throw new InvalidOperationException("Bind ra null");
    }

    [Fact]
    public void Default_config_with_comments_binds()
    {
        var s = Bind();

        Assert.Equal("http://127.0.0.1:4723", s.Appium.ServerUrl);
        Assert.Equal("com.bookmark.money", s.Appium.AppPackage);
        Assert.Equal(2, s.Timing.RetryPerRow);
        Assert.Equal("Khác", s.Defaults.ExpenseCategory);
        Assert.Equal("{description}", s.Defaults.NoteTemplate);
        Assert.Contains("Ăn uống", s.GetKnownCategories(TransactionType.Chi));
        Assert.Contains("Lương", s.GetKnownCategories(TransactionType.Thu));
        Assert.Equal("Lưu", s.Selectors["SaveButton"].Value);
        Assert.Equal("id", s.Selectors["AddButton"].By);
    }

    [Fact]
    public void Rules_bind_enums_from_strings()
    {
        var rules = Bind().CategoryRules;

        Assert.NotEmpty(rules);
        var skip = rules[0];
        Assert.Equal(RuleAction.Skip, skip.Action);
        Assert.Equal(TransactionType.Chi, skip.Type);
        Assert.Contains("NAP TIEN VI MOMO", skip.Match);

        var salary = rules.Single(r => r.Match.Contains("LUONG"));
        Assert.Equal(TransactionType.Thu, salary.Type);
        Assert.Equal(RuleAction.Import, salary.Action);
        Assert.Equal("Lương", salary.Category);
    }

    [Fact]
    public void Missing_keys_fall_back_to_class_defaults()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Defaults:Wallet"] = "Ví A" })
            .Build();

        var s = config.Get<AppSettings>()!;

        Assert.Equal("Ví A", s.Defaults.Wallet);
        Assert.Equal("{description}", s.Defaults.NoteTemplate);
        Assert.Equal(10, s.Timing.DefaultWaitSec);
        Assert.Empty(s.CategoryRules);
    }
}
