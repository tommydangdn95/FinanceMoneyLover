using System.ComponentModel;
using System.Globalization;
using System.Text;

namespace MoneyLoverAutoInput.Core.Configuration;

/// <summary>
/// Một nhóm cấp cha trong Money Lover, kèm các nhóm con. Money Lover chỉ có 2 cấp.
/// Có TypeConverter từ string để config cũ dạng <c>"Chi": ["Ăn uống", ...]</c> vẫn bind được.
/// </summary>
[TypeConverter(typeof(CategoryNodeConverter))]
public sealed class CategoryNode
{
    public string Name { get; set; } = "";
    public List<string> Children { get; set; } = new();
}

public sealed class CategoryNodeConverter : TypeConverter
{
    public override bool CanConvertFrom(ITypeDescriptorContext? context, Type sourceType) =>
        sourceType == typeof(string) || base.CanConvertFrom(context, sourceType);

    public override object? ConvertFrom(ITypeDescriptorContext? context, CultureInfo? culture, object value) =>
        value is string s ? new CategoryNode { Name = s.Trim() } : base.ConvertFrom(context, culture, value);
}

/// <summary>
/// Định danh nhóm bằng đường dẫn: nhóm cha = "Tên", nhóm con = "Cha &gt; Con".
/// Cần đường dẫn vì Money Lover cho phép trùng tên giữa nhóm cha và nhóm con (VD "Fitness").
/// </summary>
public static class CategoryTree
{
    public const string Separator = " > ";

    public static string PathOf(string parent, string? child = null) =>
        string.IsNullOrWhiteSpace(child) ? parent.Trim() : parent.Trim() + Separator + child.Trim();

    /// <summary>Tách đường dẫn thành (cha, con). Nhóm cha thì con = null.</summary>
    public static (string Parent, string? Child) Split(string path)
    {
        var i = path.IndexOf(Separator, StringComparison.Ordinal);
        return i < 0 ? (path.Trim(), null) : (path[..i].Trim(), path[(i + Separator.Length)..].Trim());
    }

    /// <summary>Tên hiển thị trong Money Lover (phần cuối của đường dẫn).</summary>
    public static string LeafName(string path)
    {
        var (parent, child) = Split(path);
        return child ?? parent;
    }

    /// <summary>Mọi nhóm chọn được: nhóm cha rồi tới các nhóm con của nó.</summary>
    public static List<string> Flatten(IEnumerable<CategoryNode> nodes)
    {
        var result = new List<string>();
        foreach (var n in nodes)
        {
            if (string.IsNullOrWhiteSpace(n.Name)) continue;
            result.Add(n.Name.Trim());
            result.AddRange(n.Children.Where(c => !string.IsNullOrWhiteSpace(c)).Select(c => PathOf(n.Name, c)));
        }
        return result;
    }

    /// <summary>Đường dẫn xuất hiện nhiều hơn một lần (không phân biệt hoa thường).</summary>
    public static List<string> FindDuplicates(IEnumerable<CategoryNode> nodes) =>
        Flatten(nodes)
            .GroupBy(p => p, StringComparer.CurrentCultureIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();

    /// <summary>
    /// Dạng text để sửa trên màn Thiết lập: mỗi dòng một nhóm; nhóm con thụt đầu dòng hoặc bắt đầu bằng "-".
    /// </summary>
    public static string Format(IEnumerable<CategoryNode> nodes)
    {
        var sb = new StringBuilder();
        foreach (var n in nodes)
        {
            sb.Append(n.Name).Append(Environment.NewLine);
            foreach (var c in n.Children) sb.Append("  - ").Append(c).Append(Environment.NewLine);
        }
        return sb.ToString().TrimEnd();
    }

    /// <summary>Ngược lại của <see cref="Format"/>. Dòng con đứng trước mọi nhóm cha được coi là nhóm cha.</summary>
    public static List<CategoryNode> Parse(string text)
    {
        var result = new List<CategoryNode>();
        CategoryNode? current = null;

        foreach (var raw in text.Replace("\r", "").Split('\n'))
        {
            if (string.IsNullOrWhiteSpace(raw)) continue;

            var trimmed = raw.Trim();
            var isChild = char.IsWhiteSpace(raw[0]) || trimmed.StartsWith('-') || trimmed.StartsWith('•');
            var name = trimmed.TrimStart('-', '•').Trim();
            if (name.Length == 0) continue;

            if (isChild && current is not null)
            {
                current.Children.Add(name);
            }
            else
            {
                current = new CategoryNode { Name = name };
                result.Add(current);
            }
        }

        return result;
    }
}
