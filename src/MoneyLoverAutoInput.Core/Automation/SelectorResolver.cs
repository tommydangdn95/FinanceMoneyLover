using MoneyLoverAutoInput.Core.Configuration;
using OpenQA.Selenium;
using OpenQA.Selenium.Appium;

namespace MoneyLoverAutoInput.Core.Automation;

/// <summary>Chuyển selector trong config (SPEC 7.1) thành <see cref="By"/> của Appium.</summary>
public sealed class SelectorResolver
{
    private readonly IReadOnlyDictionary<string, SelectorOptions> _selectors;

    public SelectorResolver(IReadOnlyDictionary<string, SelectorOptions> selectors) => _selectors = selectors;

    public SelectorOptions Get(string key) =>
        _selectors.TryGetValue(key, out var s) && !string.IsNullOrWhiteSpace(s.Value)
            ? s
            : throw new InvalidOperationException($"Thiếu selector '{key}' trong cấu hình.");

    /// <summary>Giá trị thô (VD resource-id) — dùng khi đọc page source.</summary>
    public string Value(string key, IReadOnlyDictionary<string, string>? args = null) => Fill(Get(key).Value, args, escape: false);

    public By By(string key, IReadOnlyDictionary<string, string>? args = null)
    {
        var s = Get(key);
        var uiArg = s.By.Equals("uiautomator", StringComparison.OrdinalIgnoreCase);
        var value = Fill(s.Value, args, escape: uiArg);

        return s.By.ToLowerInvariant() switch
        {
            "id" => OpenQA.Selenium.By.Id(value),
            "accessibilityid" => MobileBy.AccessibilityId(value),
            "uiautomator" => MobileBy.AndroidUIAutomator(value),
            "xpath" => OpenQA.Selenium.By.XPath(value),
            "text" => MobileBy.AndroidUIAutomator($"new UiSelector().text(\"{EscapeUiAutomator(value)}\")"),
            _ => throw new InvalidOperationException($"Selector '{key}': kiểu '{s.By}' không hỗ trợ."),
        };
    }

    public By By(string key, string placeholder, string value) =>
        By(key, new Dictionary<string, string> { [placeholder] = value });

    private static string Fill(string template, IReadOnlyDictionary<string, string>? args, bool escape)
    {
        if (args is null) return template;
        foreach (var (k, v) in args)
        {
            template = template.Replace("{" + k + "}", escape ? EscapeUiAutomator(v) : v, StringComparison.Ordinal);
        }
        return template;
    }

    /// <summary>Escape chuỗi đặt trong "..." của biểu thức UiSelector (Java string).</summary>
    public static string EscapeUiAutomator(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"");
}
