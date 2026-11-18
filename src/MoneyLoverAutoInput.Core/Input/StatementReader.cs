using ClosedXML.Excel;

namespace MoneyLoverAutoInput.Core.Input;

/// <summary>
/// Tự nhận dạng loại sao kê theo dòng tiêu đề: thẻ tín dụng TPBank (SPEC 6.5), bảng giao dịch BIDV (SPEC 6.6),
/// còn lại là sao kê tài khoản TPBank (mặc định, SPEC 6).
/// </summary>
public sealed class StatementReader : IStatementReader
{
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
            return BankStatementReader.Fail(sourceFile, $"Không mở được file Excel: {ex.Message}");
        }

        using (workbook)
        {
            var ws = workbook.Worksheets.FirstOrDefault();
            if (ws is null) return BankStatementReader.Fail(sourceFile, "File không có sheet nào.");

            if (CreditCardStatementReader.LooksLike(ws)) return CreditCardStatementReader.ReadWorksheet(ws, sourceFile);
            if (BidvStatementReader.LooksLike(ws)) return BidvStatementReader.ReadWorksheet(ws, sourceFile);
            return BankStatementReader.ReadWorksheet(ws, sourceFile);
        }
    }
}
