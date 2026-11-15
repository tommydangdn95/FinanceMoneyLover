using MoneyLoverAutoInput.Core.Models;

namespace MoneyLoverAutoInput.Core.Input;

/// <summary>Kiểm tra liên dòng (SPEC 6.4). Lỗi mức ô đã được reader/categorizer thêm sẵn.</summary>
public static class RowValidator
{
    public static void Validate(IReadOnlyList<TransactionRow> rows)
    {
        foreach (var row in rows.Where(r => r.Id.Length == 0))
        {
            row.DataErrors.Add("Thiếu Mã giao dịch.");
        }

        foreach (var group in rows.Where(r => r.Id.Length > 0).GroupBy(r => r.Id).Where(g => g.Count() > 1))
        {
            var lines = string.Join(", ", group.Select(r => r.ExcelRow));
            foreach (var row in group)
            {
                row.DataErrors.Add($"Mã giao dịch bị trùng trong file (dòng {lines}).");
            }
        }
    }
}
