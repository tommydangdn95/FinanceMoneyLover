using ClosedXML.Excel;
using MoneyLoverAutoInput.Core.Categorization;

namespace MoneyLoverAutoInput.Core.Input;

/// <summary>Tìm dòng tiêu đề bảng theo tên cột (đã chuẩn hóa), không hard-code vị trí.</summary>
internal static class HeaderFinder
{
    public const int ScanRows = 30;

    /// <returns>Row = 0 nếu không có dòng nào chứa đủ cột bắt buộc.</returns>
    public static (int Row, Dictionary<string, int> Columns) Find(IXLWorksheet ws, string[] required, string[] optional)
    {
        var lastRow = Math.Min(ws.LastRowUsed()?.RowNumber() ?? 0, ScanRows);
        var lastCol = ws.LastColumnUsed()?.ColumnNumber() ?? 0;
        var wanted = required.Concat(optional).ToDictionary(TextNormalizer.Normalize, c => c);

        for (var r = 1; r <= lastRow; r++)
        {
            var found = new Dictionary<string, int>();
            for (var c = 1; c <= lastCol; c++)
            {
                var key = TextNormalizer.Normalize(BankStatementReader.GetText(ws.Cell(r, c)));
                if (wanted.TryGetValue(key, out var canonical) && !found.ContainsKey(canonical))
                    found[canonical] = c;
            }

            if (required.All(found.ContainsKey)) return (r, found);
        }

        return (0, new Dictionary<string, int>());
    }
}
