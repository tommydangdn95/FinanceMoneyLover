using System.Security.Cryptography;
using System.Text;
using ClosedXML.Excel;
using MoneyLoverAutoInput.Core.Categorization;
using MoneyLoverAutoInput.Core.Models;

namespace MoneyLoverAutoInput.Core.Input;

/// <summary>
/// Đọc file "Lịch sử giao dịch thẻ tín dụng" (SPEC 6.5). Cột:
/// Ngày giao dịch | Ngày cập nhật | Số tiền giao dịch | Ghi nợ | Ghi có | Mô tả giao dịch.
/// Khác sao kê tài khoản: KHÔNG có Mã giao dịch / Tài khoản đối ứng / giờ; các dòng không theo thứ tự.
/// Khóa chống trùng được tự tạo từ ngày + chiều + số tiền + mô tả (<see cref="BuildKey"/>).
/// </summary>
public static class CreditCardStatementReader
{
    internal const string ColTxnDate = "Ngày giao dịch";
    internal const string ColPostedDate = "Ngày cập nhật";
    internal const string ColAmount = "Số tiền giao dịch";
    internal const string ColDescription = "Mô tả giao dịch";
    internal const string ColDebit = "Ghi nợ";
    internal const string ColCredit = "Ghi có";

    /// <summary>Tiền tố của khóa tự tạo — để phân biệt với mã giao dịch thật của ngân hàng.</summary>
    public const string KeyPrefix = "TTD-";

    private static readonly string[] RequiredColumns = { ColTxnDate, ColDescription, ColDebit, ColCredit };
    private static readonly string[] OptionalColumns = { ColPostedDate, ColAmount };

    private const string LabelPeriod = "Thời gian liệt kê giao dịch";
    private const string LabelCard = "Số thẻ";

    private const int MaxDataRows = 100_000;

    /// <summary>Sheet có dòng tiêu đề của sao kê thẻ tín dụng không.</summary>
    internal static bool LooksLike(IXLWorksheet ws) => FindHeader(ws).Row > 0;

    internal static StatementReadResult ReadWorksheet(IXLWorksheet ws, string sourceFile)
    {
        var (headerRow, columns) = FindHeader(ws);
        if (headerRow == 0)
        {
            return BankStatementReader.Fail(sourceFile,
                "Không tìm thấy dòng tiêu đề sao kê thẻ. Cần đủ các cột: " + string.Join(", ", RequiredColumns) + ".");
        }

        var info = ReadMetadata(ws, headerRow, sourceFile);
        var lastRow = ws.LastRowUsed()?.RowNumber() ?? 0;
        var rows = new List<StatementRow>();

        for (var r = headerRow + 1; r <= lastRow && rows.Count < MaxDataRows; r++)
        {
            string Text(string col) => columns.TryGetValue(col, out var c) ? BankStatementReader.GetText(ws.Cell(r, c)) : "";

            // Hết bảng: dòng không có ngày, mô tả lẫn số tiền.
            if (Text(ColTxnDate).Length == 0 && Text(ColDescription).Length == 0
                && Text(ColDebit).Length == 0 && Text(ColCredit).Length == 0) break;

            rows.Add(ReadRow(ws, r, columns));
        }

        return new StatementReadResult { Info = info, Rows = rows };
    }

    private static (int Row, Dictionary<string, int> Columns) FindHeader(IXLWorksheet ws) =>
        HeaderFinder.Find(ws, RequiredColumns, OptionalColumns);

    /// <summary>Nhãn ở một ô, giá trị ở ô có dữ liệu đầu tiên bên phải (vị trí cột không cố định).</summary>
    private static StatementInfo ReadMetadata(IXLWorksheet ws, int headerRow, string sourceFile)
    {
        string period = "", card = "";
        var periodKey = TextNormalizer.Normalize(LabelPeriod);
        var cardKey = TextNormalizer.Normalize(LabelCard);
        var lastCol = ws.LastColumnUsed()?.ColumnNumber() ?? 0;

        for (var r = 1; r < headerRow; r++)
        {
            for (var c = 1; c < lastCol; c++)
            {
                var label = TextNormalizer.Normalize(BankStatementReader.GetText(ws.Cell(r, c))).TrimEnd(':').Trim();
                if (label != periodKey && label != cardKey) continue;

                var value = "";
                for (var v = c + 1; v <= lastCol && value.Length == 0; v++)
                    value = BankStatementReader.GetText(ws.Cell(r, v));

                if (label == periodKey) period = value;
                else card = value;
                break;
            }
        }

        return new StatementInfo
        {
            SourceFile = sourceFile, Period = period, AccountNumber = card, Kind = StatementKind.CreditCard,
        };
    }

    private static StatementRow ReadRow(IXLWorksheet ws, int r, IReadOnlyDictionary<string, int> cols)
    {
        var errors = new List<string>();

        var dateCell = ws.Cell(r, cols[ColTxnDate]);
        var txnDate = BankStatementReader.ParseDateTime(dateCell);
        DateOnly? posted = null;
        if (cols.TryGetValue(ColPostedDate, out var pc) && BankStatementReader.ParseDateTime(ws.Cell(r, pc)) is { } p)
            posted = DateOnly.FromDateTime(p);

        if (txnDate is null && posted is null)
            errors.Add($"Ngày không hợp lệ: '{BankStatementReader.GetText(dateCell)}'.");

        var before = errors.Count;
        var debit = BankStatementReader.ParseAmount(ws.Cell(r, cols[ColDebit]), ColDebit, errors);
        var credit = BankStatementReader.ParseAmount(ws.Cell(r, cols[ColCredit]), ColCredit, errors);
        var amountFailed = errors.Count > before;

        var description = BankStatementReader.GetText(ws.Cell(r, cols[ColDescription]));

        // Không có giờ → chỉ giữ phần ngày; ngày giao dịch ưu tiên hơn ngày cập nhật.
        var date = txnDate is { } d ? DateOnly.FromDateTime(d) : posted;
        var key = date is { } k && !amountFailed ? BuildKey(k, debit, credit, description) : "";

        var row = new StatementRow
        {
            ExcelRow = r,
            ExecutedAt = date?.ToDateTime(TimeOnly.MinValue),
            EffectiveDate = posted,
            Description = description,
            Debit = debit,
            Credit = credit,
            AmountParseFailed = amountFailed,
            TxnCode = key,
        };
        row.ParseErrors.AddRange(errors);
        return row;
    }

    /// <summary>
    /// Khóa ổn định giữa các lần tải sao kê (kỳ sau thường chồng lấn kỳ trước):
    /// <c>TTD-yyyyMMdd-N|C{số tiền}-{8 hex của SHA-256(mô tả đã chuẩn hóa)}</c>.
    /// Không chứa nguyên văn mô tả (có thể có SĐT, tên người). Hai giao dịch giống hệt nhau trong cùng file
    /// được <see cref="RowValidator"/> đánh số #2, #3...
    /// </summary>
    public static string BuildKey(DateOnly date, long? debit, long? credit, string description)
    {
        var side = (debit ?? 0) > 0 ? "N" : "C";
        var amount = (debit ?? 0) > 0 ? debit!.Value : credit ?? 0;
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(TextNormalizer.Normalize(description)));
        return $"{KeyPrefix}{date:yyyyMMdd}-{side}{amount}-{Convert.ToHexString(hash, 0, 4)}";
    }
}
