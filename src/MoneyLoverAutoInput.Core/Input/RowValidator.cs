using MoneyLoverAutoInput.Core.Models;

namespace MoneyLoverAutoInput.Core.Input;

/// <summary>Kiểm tra liên dòng (SPEC 6.4). Lỗi mức ô đã được reader/categorizer thêm sẵn.</summary>
public static class RowValidator
{
    public const string DuplicateKeySeparator = "#";

    public static void Validate(IReadOnlyList<TransactionRow> rows)
    {
        foreach (var row in rows.Where(r => r.TxnCode.Length == 0))
        {
            row.DataErrors.Add("Thiếu Mã giao dịch.");
        }

        // Mã trùng trong file: chỉ CẢNH BÁO, vẫn cho nhập. Mỗi lần xuất hiện sau lần đầu (theo thứ tự dòng
        // trong file) nhận khóa riêng "MÃ#2", "MÃ#3"... để state.json/drafts.json không lẫn giữa các dòng.
        foreach (var group in rows.Where(r => r.TxnCode.Length > 0)
                     .GroupBy(r => r.TxnCode)
                     .Where(g => g.Count() > 1))
        {
            var ordered = group.OrderBy(r => r.ExcelRow).ToList();
            var lines = string.Join(", ", ordered.Select(r => r.ExcelRow));
            for (var i = 0; i < ordered.Count; i++)
            {
                var row = ordered[i];
                if (i > 0) row.Id = row.TxnCode + DuplicateKeySeparator + (i + 1);
                row.Warnings.Add($"Mã giao dịch {row.TxnCode} bị trùng trong file (dòng {lines}) — kiểm tra kỹ để tránh nhập 2 lần.");
            }
        }
    }
}
