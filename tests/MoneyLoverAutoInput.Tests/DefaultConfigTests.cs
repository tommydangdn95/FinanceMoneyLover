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
        Assert.False(s.SetupCompleted);
        Assert.Equal("TP Bank", s.Defaults.Wallet);
        Assert.Equal("Other Expense", s.Defaults.ExpenseCategory);
        Assert.Equal("{description}", s.Defaults.NoteTemplate);
        Assert.Contains("Bills & Utilities > Water Bill", s.GetKnownCategories(TransactionType.Chi));
        Assert.Contains("Other Expense", s.GetKnownCategories(TransactionType.Chi));
        Assert.Contains("Incoming transfer", s.GetKnownCategories(TransactionType.Thu));
        Assert.Equal("Save", s.Selectors["SaveButton"].Value);
        Assert.Equal("Expense", s.Selectors["TypeTabExpense"].Value);
        Assert.Equal("id", s.Selectors["AddButton"].By);
    }

    [Fact]
    public void Rules_bind_enums_from_strings()
    {
        var rules = Bind().CategoryRules;

        Assert.NotEmpty(rules);
        var salary = rules.Single(r => r.Match.Contains("LUONG"));
        Assert.Equal(TransactionType.Thu, salary.Type);
        Assert.Equal(RuleAction.Import, salary.Action);
        Assert.Equal("Salary", salary.Category);
    }

    [Fact]
    public void Default_config_is_valid_and_consistent()
    {
        var v = SettingsFile.Validate(Bind());

        Assert.Empty(v.Errors);
        Assert.Empty(v.Warnings); // mọi nhóm dùng trong luật/mặc định đều có trong KnownCategories
    }

    /// <summary>
    /// Kiểm tra config người dùng thật mà không đưa vào repo:
    /// MLAI_USER_CONFIG = đường dẫn appsettings.json trong %LOCALAPPDATA%\MoneyLoverAutoInput.
    /// </summary>
    [Fact]
    public void User_config_if_provided_binds_and_has_no_errors()
    {
        var path = Environment.GetEnvironmentVariable("MLAI_USER_CONFIG");
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return;

        var s = new ConfigurationBuilder().AddJsonFile(path).Build().Get<AppSettings>()!;
        var v = SettingsFile.Validate(s);

        foreach (var w in v.Warnings) Console.WriteLine("WARN: " + w);
        Console.WriteLine($"Chi: {s.GetKnownCategories(TransactionType.Chi).Count} nhóm, " +
                          $"Thu: {s.GetKnownCategories(TransactionType.Thu).Count} nhóm, " +
                          $"{s.CategoryRules.Count} luật");
        Assert.Empty(v.Errors);
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
