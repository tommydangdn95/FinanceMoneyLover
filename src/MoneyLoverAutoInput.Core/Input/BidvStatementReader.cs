using System.Globalization;
using ClosedXML.Excel;
using MoneyLoverAutoInput.Core.Models;

namespace MoneyLoverAutoInput.Core.Input;

/// <summary>
/// Đọc bảng giao dịch tải từ BIDV (SPEC 6.6). Tiêu đề thường ở dòng 1, không có metadata:
/// Ngày giao dịch | Nội dung giao dịch | Số tiền | Số dư | Tiền tệ | Mã giao dịch.
/// <c>Số tiền</c> CÓ DẤU: âm = Chi, dương = Thu. <c>Số dư</c> không bao giờ được đọc.
/// </summary>
public static class BidvStatementReader
{
    internal const string ColDate = "Ngày giao dịch";
    internal const string ColDescription = "Nội dung giao dịch";
    internal const string ColAmount = "Số tiền";
    internal const string ColCurrency = "Tiền tệ";
    internal const string ColTxnCode = "Mã giao dịch";

    private static readonly string[] RequiredColumns = { ColDate, ColDescription, ColAmount, ColTxnCode };
    private static readonly string[] OptionalColumns = { ColCurrency };

    private const int MaxDataRows = 100_000;

    internal static bool LooksLike(IXLWorksheet ws) => HeaderFinder.Find(ws, RequiredColumns, OptionalColumns).Row > 0;

    internal static StatementReadResult ReadWorksheet(IXLWorksheet ws, string sourceFile)
    {
        var (headerRow, cols) = HeaderFinder.Find(ws, RequiredColumns, OptionalColumns);
        if (headerRow == 0)
        {
            return BankStatementReader.Fail(sourceFile,
                "Không tìm thấy dòng tiêu đề BIDV. Cần đủ các cột: " + string.Join(", ", RequiredColumns) + ".");
        }

        var lastRow = ws.LastRowUsed()?.RowNumber() ?? 0;
        var rows = new List<StatementRow>();

        for (var r = headerRow + 1; r <= lastRow && rows.Count < MaxDataRows; r++)
        {
            string Text(string col) => cols.TryGetValue(col, out var c) ? BankStatementReader.GetText(ws.Cell(r, c)) : "";
            if (Text(ColDate).Length == 0 && Text(ColTxnCode).Length == 0 && Text(ColAmount).Length == 0) break; // hết bảng

            var errors = new List<string>();
            var dateCell = ws.Cell(r, cols[ColDate]);
            var date = BankStatementReader.ParseDateTime(dateCell);
            if (date is null) errors.Add($"Ngày không hợp lệ: '{BankStatementReader.GetText(dateCell)}'.");

            var before = errors.Count;
            var signed = ParseSignedAmount(ws.Cell(r, cols[ColAmount]), errors);
            var amountFailed = errors.Count > before;

            var currency = Text(ColCurrency);
            if (currency.Length > 0 && !currency.Equals("VND", StringComparison.OrdinalIgnoreCase))
                errors.Add($"Tiền tệ '{currency}' không hỗ trợ (chỉ VND).");

            var row = new StatementRow
            {
                ExcelRow = r,
                ExecutedAt = date,
                Description = Text(ColDescription),
                Debit = signed < 0 ? -signed : null,
                Credit = signed > 0 ? signed : null,
                AmountParseFailed = amountFailed,
                TxnCode = Text(ColTxnCode),
            };
            row.ParseErrors.AddRange(errors);
            rows.Add(row);
        }

        // Không có số tài khoản trong file → ví lấy từ Defaults.BidvWallet.
        return new StatementReadResult
        {
            Info = new StatementInfo { SourceFile = sourceFile, Kind = StatementKind.Bidv },
            Rows = rows,
        };
    }

    /// <summary>Số tiền có dấu ("-45000", "120000", "-1,250,000"). null = trống hoặc lỗi.</summary>
    internal static long? ParseSignedAmount(IXLCell cell, List<string> errors)
    {
        var v = cell.Value;
        if (v.Type == XLDataType.Number)
        {
            var n = v.GetNumber();
            if (n != Math.Floor(n) || Math.Abs(n) > long.MaxValue)
            {
                errors.Add($"{ColAmount} không hợp lệ: {n.ToString(CultureInfo.InvariantCulture)}.");
                return null;
            }
            return (long)n;
        }

        var text = BankStatementReader.GetText(cell);
        if (text.Length == 0) return null;

        var negative = text.StartsWith('-');
        var body = text.TrimStart('-', '+').Trim();

        var abs = BankStatementReader.ParseAmountText(body, ColAmount, new List<string>());
        if (abs is null)
        {
            errors.Add($"{ColAmount} không hợp lệ: '{text}'.");
            return null;
        }
        return negative ? -abs.Value : abs.Value;
    }
}
