using ClosedXML.Excel;
using MoneyLoverAutoInput.Core.Configuration;
using MoneyLoverAutoInput.Core.Import;
using MoneyLoverAutoInput.Core.Input;
using MoneyLoverAutoInput.Core.Models;

namespace MoneyLoverAutoInput.Tests;

/// <summary>
/// Sao kê "Lịch sử giao dịch thẻ tín dụng". Dữ liệu GIẢ, cùng bố cục file thật: logo + tiêu đề, 2 dòng metadata,
/// tiêu đề bảng, các dòng không theo thứ tự ngày, ô ngày/tiền có thể là text hoặc số.
/// </summary>
public class CreditCardStatementReaderTests
{
    private static readonly string[] Headers =
        { "Ngày giao dịch", "Ngày cập nhật", "Số tiền giao dịch", "Ghi nợ", "Ghi có", "Mô tả giao dịch" };

    private static StatementReadResult Read(params object?[][] rows)
    {
        using var wb = new XLWorkbook();
        var ws = wb.AddWorksheet("Sheet1");
        ws.Cell(2, 3).Value = "LỊCH SỬ GIAO DỊCH THẺ TÍN DỤNG";
        ws.Cell(4, 1).Value = "Thời gian liệt kê giao dịch";
        ws.Cell(4, 2).Value = "01/10/2026 - 31/10/2026";
        ws.Cell(5, 1).Value = "Số thẻ:";
        ws.Cell(5, 3).Value = "411111xxxxxx9999"; // giá trị không nằm ngay cột kế bên
        for (var i = 0; i < Headers.Length; i++) ws.Cell(8, i + 1).Value = Headers[i];

        var r = 9;
        foreach (var row in rows)
        {
            for (var c = 0; c < row.Length; c++)
            {
                var cell = ws.Cell(r, c + 1);
                switch (row[c])
                {
                    case null: break;
                    case string s: cell.Value = s; break;
                    case long l: cell.Value = l; break;
                    case DateTime d: cell.Value = d; break;
                }
            }
            r++;
        }

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        ms.Position = 0;
        return new StatementReader().Read(ms, "the-tin-dung.xlsx");
    }

    private static object?[] Row(object date, object amount, object? debit, object? credit, string desc) =>
        new[] { date, date, amount, debit, credit, desc };

    private static AppSettings Settings() => new()
    {
        Defaults = new DefaultsOptions
        {
            Wallet = "Ví tài khoản", CreditCardWallet = "Ví thẻ", ExpenseCategory = "Khác", IncomeCategory = "Thu khác",
        },
    };

    [Fact]
    public void Reads_metadata_and_rows_with_text_cells()
    {
        var result = Read(
            Row("29/10/2026", "2,000,000", "", "2,000,000", "TT QUA EBANKING TEST"),
            Row("29/10/2026", "37,000", "37,000", "", "Quan an   ABC"),
            Row("12/10/2026", "1,234,500", "1,234,500", "", "DPP registration"));

        Assert.Empty(result.FileErrors);
        Assert.Equal(StatementKind.CreditCard, result.Info.Kind);
        Assert.Equal("01/10/2026 - 31/10/2026", result.Info.Period);
        Assert.Equal("***9999", result.Info.MaskedAccount);
        Assert.Equal(3, result.Rows.Count);

        var first = result.Rows[0];
        Assert.Equal(new DateTime(2026, 10, 29), first.ExecutedAt);
        Assert.Null(first.Debit);
        Assert.Equal(2_000_000, first.Credit);
        Assert.StartsWith("TTD-20261029-C2000000-", first.TxnCode);
        Assert.Equal(37_000, result.Rows[1].Debit);
        Assert.All(result.Rows, r => Assert.Empty(r.ParseErrors));
    }

    [Fact]
    public void Reads_numeric_and_date_cells()
    {
        var result = Read(Row(new DateTime(2026, 10, 24), 222_000L, 222_000L, null, "CapCut  SINGAPORE"));

        var row = Assert.Single(result.Rows);
        Assert.Equal(new DateTime(2026, 10, 24), row.ExecutedAt);
        Assert.Equal(222_000, row.Debit);
        Assert.Empty(row.ParseErrors);
    }

    [Fact]
    public void Loader_sorts_by_date_categorizes_and_uses_credit_card_wallet()
    {
        var read = Read(
            Row("29/10/2026", "37,000", "37,000", "", "Quan an ABC"),
            Row("31/10/2026", "1,111,222", "", "1,111,222", "TT QUA EBANKING TEST"),
            Row("12/10/2026", "1,234,500", "1,234,500", "", "DPP registration"));

        var loaded = StatementLoader.Process(read, Settings());

        Assert.Equal(new[] { 12, 29, 31 }, loaded.Rows.Select(r => r.Ngay.Day));
        Assert.All(loaded.Rows, r => Assert.Equal("Ví thẻ", r.Vi));
        Assert.Equal(TransactionType.Thu, loaded.Rows[2].Loai);
        Assert.Equal(1_111_222, loaded.Rows[2].SoTien);
        Assert.All(loaded.Rows, r => Assert.True(r.CanImport, string.Join("; ", r.Errors)));
    }

    [Fact]
    public void Without_credit_card_wallet_falls_back_to_default_wallet()
    {
        var settings = Settings();
        settings.Defaults.CreditCardWallet = "";

        var loaded = StatementLoader.Process(Read(Row("29/10/2026", "37,000", "37,000", "", "Quan an")), settings);

        Assert.Equal("Ví tài khoản", loaded.Rows[0].Vi);
    }

    [Fact]
    public void Identical_rows_get_distinct_ids_and_a_warning()
    {
        var loaded = StatementLoader.Process(Read(
            Row("29/10/2026", "37,000", "37,000", "", "Quan an ABC"),
            Row("29/10/2026", "37,000", "37,000", "", "Quan an ABC")), Settings());

        Assert.Equal(2, loaded.Rows.Select(r => r.Id).Distinct().Count());
        Assert.All(loaded.Rows, r => Assert.Contains(r.Warnings, w => w.Contains("giống hệt")));
        Assert.All(loaded.Rows, r => Assert.True(r.CanImport));
    }

    [Fact]
    public void Key_is_stable_and_distinguishes_amount_side_and_description()
    {
        var d = new DateOnly(2026, 10, 12);
        var key = CreditCardStatementReader.BuildKey(d, 1_234_500, null, "DPP registration");

        // Ổn định qua khác biệt khoảng trắng/hoa thường (cùng giao dịch ở 2 kỳ sao kê chồng lấn).
        Assert.Equal(key, CreditCardStatementReader.BuildKey(d, 1_234_500, null, "  dpp   REGISTRATION "));
        Assert.NotEqual(key, CreditCardStatementReader.BuildKey(d, 1_234_499, null, "DPP registration"));
        Assert.NotEqual(key, CreditCardStatementReader.BuildKey(d, null, 1_234_500, "DPP registration"));
        Assert.NotEqual(key, CreditCardStatementReader.BuildKey(d, 1_234_500, null, "Other"));
        Assert.DoesNotContain("DPP", key, StringComparison.OrdinalIgnoreCase); // không lộ mô tả
    }

    [Fact]
    public void Bad_date_and_amount_are_row_errors()
    {
        var loaded = StatementLoader.Process(Read(Row("31/02/2026", "abc", "abc", "", "X")), Settings());

        var row = Assert.Single(loaded.Rows);
        Assert.False(row.CanImport);
        Assert.Contains(row.DataErrors, e => e.Contains("Ngày"));
        Assert.Contains(row.DataErrors, e => e.Contains("Ghi nợ"));
    }

    [Fact]
    public void Dispatcher_still_reads_bank_account_statement()
    {
        var result = new StatementReader().Read(TestHelpers.SamplePath);

        Assert.Empty(result.FileErrors);
        Assert.Equal(StatementKind.BankAccount, result.Info.Kind);
        Assert.NotEmpty(result.Rows);
    }
}
