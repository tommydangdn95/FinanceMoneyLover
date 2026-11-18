using MoneyLoverAutoInput.Core.Automation;
using MoneyLoverAutoInput.Core.Tools;

namespace MoneyLoverAutoInput.Tests;

/// <summary>Logic thuần của phần tự động hóa (không cần điện thoại). XML là dữ liệu giả, cùng cấu trúc dump thật.</summary>
public class AutomationLogicTests
{
    private const string NameId = "com.bookmark.money:id/name";
    private const string ChildId = "com.bookmark.money:id/child_indicator";

    private static string Row(int top, string name, bool child, int height = 147) =>
        $"""<node class="android.widget.RelativeLayout" clickable="true" bounds="[0,{top}][1080,{top + height}]">""" +
        (child ? $"""<node resource-id="{ChildId}" bounds="[42,{top}][84,{top + 70}]"/>""" : "") +
        $"""<node resource-id="{NameId}" text="{System.Security.SecurityElement.Escape(name)}" bounds="[189,{top + 40}][1080,{top + 100}]"/></node>""";

    private static string ShowMore(int top) =>
        $"""<node class="android.widget.RelativeLayout" clickable="true" bounds="[0,{top}][1080,{top + 147}]"><node text="SHOW MORE…" bounds="[189,{top + 40}][1038,{top + 90}]"/></node>""";

    private static string Page(params string[] rows) => "<hierarchy><node>" + string.Concat(rows) + "</node></hierarchy>";

    private static List<CategoryRow> Parse(string xml) => CategoryListParser.Parse(xml, NameId, ChildId);

    [Fact]
    public void Parser_reads_parents_children_and_show_more_and_skips_clipped_rows()
    {
        var rows = Parse(Page(
            Row(342, "Bị cắt", child: true, height: 89),          // dòng bị cắt ở mép → bỏ
            Row(431, "Food & Beverage", child: false),
            Row(578, "Bánh Bao", child: true),
            ShowMore(725)));

        Assert.Equal(3, rows.Count);
        Assert.False(rows[0].IsChild);
        Assert.True(rows[1].IsChild);
        Assert.True(rows[2].IsShowMore);
        Assert.Equal(504, rows[0].Bounds.CenterY);
    }

    [Fact]
    public void Search_parent_category_taps_parent()
    {
        var rows = Parse(Page(Row(400, "Fitness", false), Row(547, "Food & Beverage", false)));

        var step = CategorySearch.Next(rows, "Fitness", null, insideParent: false);

        Assert.Equal("Fitness", Assert.IsType<CategorySearchStep.Tap>(step).Row.Name);
    }

    [Fact]
    public void Search_child_ignores_same_named_parent_elsewhere()
    {
        // "Fitness" là nhóm cha riêng VÀ là con của "Health & Fitness".
        var rows = Parse(Page(
            Row(400, "Fitness", false),
            Row(547, "Health & Fitness", false),
            Row(694, "Fitness", true)));

        var step = CategorySearch.Next(rows, "Health & Fitness", "Fitness", false);

        Assert.Equal(694, Assert.IsType<CategorySearchStep.Tap>(step).Row.Bounds.Top);
    }

    [Fact]
    public void Search_child_hidden_behind_show_more_expands_first()
    {
        var rows = Parse(Page(Row(400, "Food & Beverage", false), Row(547, "Bánh Bao", true), ShowMore(694)));

        Assert.IsType<CategorySearchStep.ExpandShowMore>(CategorySearch.Next(rows, "Food & Beverage", "Café", false));
    }

    [Fact]
    public void Search_child_continues_on_next_page_when_parent_scrolled_away()
    {
        var page1 = Parse(Page(Row(400, "Transportation", false), Row(547, "Bơm Xe", true)));
        var step1 = CategorySearch.Next(page1, "Transportation", "Petrol", false);
        var scroll = Assert.IsType<CategorySearchStep.ScrollDown>(step1);
        Assert.True(scroll.InsideParent);

        var page2 = Parse(Page(Row(400, "Parking Fees", true), Row(547, "Petrol", true)));
        var step2 = CategorySearch.Next(page2, "Transportation", "Petrol", scroll.InsideParent);
        Assert.Equal("Petrol", Assert.IsType<CategorySearchStep.Tap>(step2).Row.Name);
    }

    [Fact]
    public void Search_child_not_in_group_is_not_found()
    {
        var rows = Parse(Page(Row(400, "Bank", false), Row(547, "Nạp tiền Vào Bank", true), Row(694, "Beauty", false)));

        Assert.IsType<CategorySearchStep.NotFound>(CategorySearch.Next(rows, "Bank", "Không có", false));
    }

    [Fact]
    public void Search_parent_not_on_page_scrolls()
    {
        var rows = Parse(Page(Row(400, "Bank", false)));

        var step = CategorySearch.Next(rows, "Transportation", "Petrol", false);

        Assert.False(Assert.IsType<CategorySearchStep.ScrollDown>(step).InsideParent);
    }

    [Fact]
    public void Exact_name_match_distinguishes_diacritics()
    {
        var rows = Parse(Page(Row(400, "Beauty", false), Row(547, "Cắt Tóc", true), Row(694, "Cẳt Tóc", true)));

        var step = CategorySearch.Next(rows, "Beauty", "Cẳt Tóc", false);

        Assert.Equal(694, Assert.IsType<CategorySearchStep.Tap>(step).Row.Bounds.Top);
    }

    [Theory]
    [InlineData("3", 3L)]
    [InlineData("45,000", 45000L)]
    [InlineData("45.000", 45000L)]
    [InlineData("45,000.00", 45000L)]
    [InlineData("1,234,567", 1234567L)]
    [InlineData("0", 0L)]
    public void Amount_text_parsing(string text, long expected) =>
        Assert.Equal(expected, MoneyLoverUi.ParseAmountText(text));

    [Theory]
    [InlineData("Sunday, 15/11/2026", 2026, 11, 15)]
    [InlineData("Friday, 23/10/2026", 2026, 10, 23)]
    [InlineData("Today, 1/2/2026", 2026, 2, 1)]
    public void Date_text_parsing(string text, int y, int m, int d) =>
        Assert.Equal(new DateOnly(y, m, d), MoneyLoverUi.ParseDateText(text));

    [Fact]
    public void Date_text_unparseable_is_null() => Assert.Null(MoneyLoverUi.ParseDateText("Hôm nay"));

    [Fact]
    public void Adb_devices_output_parsing()
    {
        var devices = AdbClient.ParseDevices("List of devices attached\r\nABCD1234\tdevice\r\nabc\tunauthorized\r\n\r\n");

        Assert.Equal(2, devices.Count);
        Assert.True(devices[0].IsReady);
        Assert.Equal("unauthorized", devices[1].State);
    }

    [Fact]
    public void Screen_state_parsing()
    {
        Assert.Equal((true, false), AdbClient.ParseScreenState("mWakefulness=Awake", "mShowingLockscreen=false"));
        Assert.Equal((false, true), AdbClient.ParseScreenState("mWakefulness=Asleep", "mShowingLockscreen=true"));
    }

    [Fact]
    public void Connection_errors_are_recognised()
    {
        Assert.True(MoneyLoverUi.IsConnectionError(new OpenQA.Selenium.WebDriverException("A session is either terminated or not started")));
        Assert.True(MoneyLoverUi.IsConnectionError(new OpenQA.Selenium.WebDriverException("x", new HttpRequestException("refused"))));
        Assert.False(MoneyLoverUi.IsConnectionError(new OpenQA.Selenium.NoSuchElementException("no element")));
    }
}
