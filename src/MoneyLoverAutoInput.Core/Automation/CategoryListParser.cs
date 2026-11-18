using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace MoneyLoverAutoInput.Core.Automation;

public readonly record struct Rect(int Left, int Top, int Right, int Bottom)
{
    public int Height => Bottom - Top;
    public int CenterX => (Left + Right) / 2;
    public int CenterY => (Top + Bottom) / 2;

    public static Rect Parse(string? bounds)
    {
        var m = Regex.Match(bounds ?? "", @"\[(\d+),(\d+)\]\[(\d+),(\d+)\]");
        return m.Success
            ? new Rect(int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value),
                int.Parse(m.Groups[3].Value), int.Parse(m.Groups[4].Value))
            : default;
    }
}

/// <summary>Một dòng trên màn chọn nhóm của Money Lover.</summary>
public sealed record CategoryRow(string Name, bool IsChild, bool IsShowMore, Rect Bounds);

/// <summary>
/// Đọc danh sách nhóm từ page source (XML uiautomator) của màn chọn nhóm (SPEC 8.2 bước 6).
/// Dòng = RelativeLayout clickable; tên ở <c>name</c>; dòng con có <c>child_indicator</c>;
/// nhóm có &gt;2 con bị thu gọn sau dòng "SHOW MORE…". Chỉ lấy dòng hiện ĐỦ (dòng bị cắt ở mép có thể
/// mất icon thụt lề → nhận nhầm là nhóm cha).
/// </summary>
public static class CategoryListParser
{
    public const int MinFullRowHeight = 140;

    public static List<CategoryRow> Parse(string pageSource, string nameId, string childIndicatorId,
        string showMorePrefix = "SHOW MORE", int minRowHeight = MinFullRowHeight)
    {
        var doc = XDocument.Parse(pageSource);
        var rows = new List<CategoryRow>();
        var seen = new HashSet<XElement>();

        // Hỗ trợ CẢ HAI định dạng XML: `uiautomator dump` (<node class="...">) và page source của Appium
        // (thẻ đặt tên theo class: <android.widget.TextView ...>) — nên duyệt mọi phần tử, không lọc theo tên thẻ.
        // Mỗi dòng = phần tử clickable GẦN NHẤT bao quanh tên nhóm (hoặc chữ "SHOW MORE…").
        foreach (var n in doc.Descendants())
        {
            var id = (string?)n.Attribute("resource-id");
            var text = (string?)n.Attribute("text") ?? "";
            var isName = id == nameId;
            var isShowMore = !isName && text.StartsWith(showMorePrefix, StringComparison.OrdinalIgnoreCase);
            if (!isName && !isShowMore) continue;

            var row = n.AncestorsAndSelf().FirstOrDefault(a => (string?)a.Attribute("clickable") == "true");
            if (row is null || !seen.Add(row)) continue;

            var bounds = Rect.Parse((string?)row.Attribute("bounds"));
            if (bounds.Height < minRowHeight) continue;

            if (isShowMore)
            {
                rows.Add(new CategoryRow("", true, true, bounds));
                continue;
            }

            var isChild = row.Descendants().Any(d => (string?)d.Attribute("resource-id") == childIndicatorId);
            rows.Add(new CategoryRow(text, isChild, false, bounds));
        }

        return rows;
    }
}

/// <summary>Kết quả tìm nhóm trên MỘT trang đang hiển thị.</summary>
public abstract record CategorySearchStep
{
    /// <summary>Bấm vào dòng này là xong.</summary>
    public sealed record Tap(CategoryRow Row) : CategorySearchStep;

    /// <summary>Bấm "SHOW MORE…" của nhóm cha rồi đọc lại trang.</summary>
    public sealed record ExpandShowMore(CategoryRow Row) : CategorySearchStep;

    /// <summary>Chưa thấy — cuộn xuống. <see cref="InsideParent"/> = trang này kết thúc khi vẫn đang trong nhóm cha.</summary>
    public sealed record ScrollDown(bool InsideParent) : CategorySearchStep;

    /// <summary>Chắc chắn không có (đã qua hết nhóm cha mà không thấy nhóm con).</summary>
    public sealed record NotFound(string Reason) : CategorySearchStep;
}

/// <summary>
/// Quyết định bước tiếp theo khi tìm nhóm "Cha" hoặc "Cha &gt; Con" trên trang hiện tại.
/// Tìm nhóm con NGAY SAU nhóm cha của nó — không tìm theo tên con trên cả danh sách (có tên trùng giữa
/// nhóm cha và nhóm con, VD "Fitness" và "Health &amp; Fitness &gt; Fitness").
/// </summary>
public static class CategorySearch
{
    /// <param name="insideParent">Trang trước kết thúc khi đang ở trong các dòng con của nhóm cha cần tìm.</param>
    public static CategorySearchStep Next(IReadOnlyList<CategoryRow> rows, string parent, string? child, bool insideParent)
    {
        // Luôn tìm nhóm cha trên trang hiện tại trước: các trang cuộn chồng lấn nhau nên dòng cha có thể vẫn còn.
        var parentIndex = -1;
        for (var i = 0; i < rows.Count; i++)
        {
            if (!rows[i].IsChild && !rows[i].IsShowMore && rows[i].Name == parent) { parentIndex = i; break; }
        }

        int start;
        if (parentIndex >= 0)
        {
            if (child is null) return new CategorySearchStep.Tap(rows[parentIndex]);
            start = parentIndex + 1;
        }
        else if (insideParent)
        {
            start = 0; // nhóm cha đã cuộn qua khỏi màn, trang này bắt đầu bằng các dòng con của nó
        }
        else
        {
            return new CategorySearchStep.ScrollDown(false);
        }

        for (var i = start; i < rows.Count; i++)
        {
            var r = rows[i];
            if (r.IsShowMore) return new CategorySearchStep.ExpandShowMore(r);
            if (!r.IsChild)
            {
                // Sang nhóm cha khác. Nếu đang "insideParent" từ trang trước mà dòng đầu trang đã là nhóm cha khác
                // thì cũng là hết nhóm con.
                return new CategorySearchStep.NotFound($"Không thấy nhóm con '{child}' trong nhóm '{parent}'.");
            }
            if (r.Name == child) return new CategorySearchStep.Tap(r);
        }

        return new CategorySearchStep.ScrollDown(true);
    }
}
