using System.Globalization;
using ClosedXML.Excel;
using MoneyLoverAutoInput.Core.Categorization;
using MoneyLoverAutoInput.Core.Models;

namespace MoneyLoverAutoInput.Core.Input;

/// <summary>
/// Đọc file "Lịch sử giao dịch tài khoản" (SPEC 6). Tìm dòng tiêu đề và cột theo tên,
/// không hard-code vị trí.
/// </summary>
public sealed class BankStatementReader : IStatementReader
{
    internal const string ColExecutedAt = "Ngày thực hiện";
    internal const string ColEffectiveDate = "Ngày hiệu lực";
    internal const string ColDescription = "Mô tả giao dịch";
    internal const string ColDebit = "Ghi nợ";
    internal const string ColCredit = "Ghi có";
    internal const string ColCounterpartAccount = "Tài khoản đối ứng";
    internal const string ColCounterpartName = "Tên tài khoản";
    internal const string ColTxnCode = "Mã giao dịch";

    private static readonly string[] RequiredColumns =
        { ColExecutedAt, ColDescription, ColDebit, ColCredit, ColTxnCode };

    private static readonly string[] OptionalColumns =
        { ColEffectiveDate, ColCounterpartAccount, ColCounterpartName };

    private const string LabelPeriod = "Thời gian";
    private const string LabelAccount = "Tài khoản giao dịch";

    private const int HeaderScanRows = 30;
    private const int MaxDataRows = 100_000;

    private static readonly string[] DateTimeFormats =
    {
        "dd-MM-yyyy HH:mm:ss", "dd/MM/yyyy HH:mm:ss",
        "d-M-yyyy H:mm:ss", "d/M/yyyy H:mm:ss",
        "dd-MM-yyyy HH:mm", "dd/MM/yyyy HH:mm",
        "dd-MM-yyyy", "dd/MM/yyyy", "d-M-yyyy", "d/M/yyyy",
    };

    public StatementReadResult Read(string path)
    {
        // FileShare.ReadWrite: vẫn đọc được khi file đang mở trong Excel.
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        return Read(stream, path);
    }

    public StatementReadResult Read(Stream stream, string sourceFile)
    {
        XLWorkbook workbook;
        try
        {
            workbook = new XLWorkbook(stream);
        }
        catch (Exception ex)
        {
            return Fail(sourceFile, $"Không mở được file Excel: {ex.Message}");
        }

        using (workbook)
        {
            var ws = workbook.Worksheets.FirstOrDefault();
            if (ws is null) return Fail(sourceFile, "File không có sheet nào.");
            return ReadWorksheet(ws, sourceFile);
        }
    }

    /// <summary>Đọc sheet đã mở (dùng chung với <see cref="StatementReader"/>).</summary>
    internal static StatementReadResult ReadWorksheet(IXLWorksheet ws, string sourceFile)
    {
        var lastRow = ws.LastRowUsed()?.RowNumber() ?? 0;
        var lastCol = ws.LastColumnUsed()?.ColumnNumber() ?? 0;
        if (lastRow == 0) return Fail(sourceFile, "Sheet trống.");

        var (headerRow, columns) = FindHeader(ws, Math.Min(lastRow, HeaderScanRows), lastCol);
        if (headerRow == 0)
        {
            return Fail(sourceFile,
                "Không tìm thấy dòng tiêu đề. Cần đủ các cột: " + string.Join(", ", RequiredColumns) + ".");
        }

        var missing = RequiredColumns.Where(c => !columns.ContainsKey(c)).ToList();
        if (missing.Count > 0)
        {
            return Fail(sourceFile, "Thiếu cột bắt buộc: " + string.Join(", ", missing) + ".");
        }

        var info = ReadMetadata(ws, headerRow, sourceFile);
        var rows = new List<StatementRow>();

        for (var r = headerRow + 1; r <= lastRow && rows.Count < MaxDataRows; r++)
        {
            var executedText = GetText(ws.Cell(r, columns[ColExecutedAt]));
            var codeText = GetText(ws.Cell(r, columns[ColTxnCode]));
            if (executedText.Length == 0 && codeText.Length == 0) break; // hết bảng

            rows.Add(ReadRow(ws, r, columns));
        }

        return new StatementReadResult { Info = info, Rows = rows };
    }

    internal static StatementReadResult Fail(string sourceFile, string error) => new()
    {
        Info = new StatementInfo { SourceFile = sourceFile },
        FileErrors = new[] { error },
    };

    private static (int Row, Dictionary<string, int> Columns) FindHeader(IXLWorksheet ws, int scanTo, int lastCol)
    {
        var wanted = RequiredColumns.Concat(OptionalColumns)
            .ToDictionary(TextNormalizer.Normalize, c => c);

        var bestRow = 0;
        Dictionary<string, int> bestColumns = new();

        for (var r = 1; r <= scanTo; r++)
        {
            var found = new Dictionary<string, int>();
            for (var c = 1; c <= lastCol; c++)
            {
                var key = TextNormalizer.Normalize(GetText(ws.Cell(r, c)));
                if (wanted.TryGetValue(key, out var canonical) && !found.ContainsKey(canonical))
                    found[canonical] = c;
            }

            var requiredHits = RequiredColumns.Count(found.ContainsKey);
            if (requiredHits == RequiredColumns.Length) return (r, found);

            // Giữ dòng giống tiêu đề nhất để báo đúng cột thiếu.
            if (requiredHits >= 3 && requiredHits > RequiredColumns.Count(bestColumns.ContainsKey))
            {
                bestRow = r;
                bestColumns = found;
            }
        }

        return (bestRow, bestColumns);
    }

    private static StatementInfo ReadMetadata(IXLWorksheet ws, int headerRow, string sourceFile)
    {
        string period = "", account = "";
        var periodKey = TextNormalizer.Normalize(LabelPeriod);
        var accountKey = TextNormalizer.Normalize(LabelAccount);

        for (var r = 1; r < headerRow; r++)
        {
            var label = TextNormalizer.Normalize(GetText(ws.Cell(r, 1))).TrimEnd(':').Trim();
            if (label == periodKey) period = GetText(ws.Cell(r, 2));
            else if (label == accountKey) account = GetText(ws.Cell(r, 2));
        }

        return new StatementInfo
        {
            SourceFile = sourceFile, Period = period, AccountNumber = account, Kind = StatementKind.BankAccount,
        };
    }

    private static StatementRow ReadRow(IXLWorksheet ws, int r, IReadOnlyDictionary<string, int> cols)
    {
        string Text(string col) => cols.TryGetValue(col, out var c) ? GetText(ws.Cell(r, c)) : "";

        var errors = new List<string>();

        var executedCell = ws.Cell(r, cols[ColExecutedAt]);
        var executedAt = ParseDateTime(executedCell);

        DateOnly? effective = null;
        if (cols.TryGetValue(ColEffectiveDate, out var effCol))
        {
            var eff = ParseDateTime(ws.Cell(r, effCol));
            if (eff is not null) effective = DateOnly.FromDateTime(eff.Value);
        }

        if (executedAt is null && effective is null)
        {
            errors.Add($"Ngày không hợp lệ: '{GetText(executedCell)}'.");
        }

        var errorsBeforeAmount = errors.Count;
        var debit = ParseAmount(ws.Cell(r, cols[ColDebit]), ColDebit, errors);
        var credit = ParseAmount(ws.Cell(r, cols[ColCredit]), ColCredit, errors);
        var amountParseFailed = errors.Count > errorsBeforeAmount;

        var row = new StatementRow
        {
            ExcelRow = r,
            ExecutedAt = executedAt,
            EffectiveDate = effective,
            Description = Text(ColDescription),
            Debit = debit,
            Credit = credit,
            AmountParseFailed = amountParseFailed,
            CounterpartAccount = Text(ColCounterpartAccount),
            CounterpartName = Text(ColCounterpartName),
            TxnCode = Text(ColTxnCode),
        };
        row.ParseErrors.AddRange(errors);
        return row;
    }

    /// <summary>Đọc ô thành text đã trim. Ô trống và chuỗi rỗng '' đều thành "".</summary>
    internal static string GetText(IXLCell cell)
    {
        var v = cell.Value;
        return v.Type switch
        {
            XLDataType.Blank => "",
            XLDataType.Text => v.GetText().Trim(),
            XLDataType.Number => v.GetNumber().ToString(CultureInfo.InvariantCulture),
            XLDataType.DateTime => v.GetDateTime().ToString("dd/MM/yyyy HH:mm:ss", CultureInfo.InvariantCulture),
            XLDataType.Boolean => v.GetBoolean() ? "TRUE" : "FALSE",
            _ => cell.GetFormattedString().Trim(),
        };
    }

    internal static DateTime? ParseDateTime(IXLCell cell)
    {
        var v = cell.Value;
        if (v.Type == XLDataType.DateTime) return v.GetDateTime();

        var text = GetText(cell);
        if (text.Length == 0) return null;

        return DateTime.TryParseExact(text, DateTimeFormats, CultureInfo.InvariantCulture,
            DateTimeStyles.AllowWhiteSpaces, out var dt)
            ? dt
            : null;
    }

    /// <summary>Số tiền VND. null = ô trống. Lỗi parse được thêm vào <paramref name="errors"/>.</summary>
    internal static long? ParseAmount(IXLCell cell, string columnName, List<string> errors)
    {
        var v = cell.Value;
        if (v.Type == XLDataType.Number)
        {
            var n = v.GetNumber();
            if (n != Math.Floor(n) || n < 0 || n > long.MaxValue)
            {
                errors.Add($"{columnName} không hợp lệ: {n.ToString(CultureInfo.InvariantCulture)}.");
                return null;
            }
            return (long)n;
        }

        var text = GetText(cell);
        return text.Length == 0 ? null : ParseAmountText(text, columnName, errors);
    }

    /// <summary>Số tiền dạng text, không dấu. Lỗi được thêm vào <paramref name="errors"/> và trả null.</summary>
    internal static long? ParseAmountText(string text, string columnName, List<string> errors)
    {
        var body = text;
        foreach (var suffix in new[] { "VND", "đ", "₫" })
        {
            if (body.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) body = body[..^suffix.Length];
        }

        // Chấp nhận "45000", "45.000", "45,000", "45 000". Dấu phân cách phải đứng trước đúng 3 chữ số,
        // để "45.5" hay "-45000" bị báo lỗi thay vì âm thầm thành 455 / 45000.
        var hasOnlyAllowed = body.All(ch => char.IsDigit(ch) || ch is '.' or ',' || char.IsWhiteSpace(ch))
                             && System.Text.RegularExpressions.Regex.IsMatch(body.Trim(), @"^\d{1,3}([.,\s]?\d{3})*$");
        var cleaned = new string(body.Where(char.IsDigit).ToArray());

        if (cleaned.Length == 0 || !hasOnlyAllowed || !long.TryParse(cleaned, NumberStyles.None,
                CultureInfo.InvariantCulture, out var value))
        {
            errors.Add($"{columnName} không hợp lệ: '{text}'.");
            return null;
        }

        return value;
    }
}
