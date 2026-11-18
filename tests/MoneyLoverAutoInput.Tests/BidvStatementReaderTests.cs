using ClosedXML.Excel;
using MoneyLoverAutoInput.Core.Configuration;
using MoneyLoverAutoInput.Core.Import;
using MoneyLoverAutoInput.Core.Input;
using MoneyLoverAutoInput.Core.Models;

namespace MoneyLoverAutoInput.Tests;

/// <summary>
/// Bảng giao dịch BIDV. Dữ liệu GIẢ, cùng bố cục file thật: tiêu đề ở dòng 1, Số tiền có dấu, mới nhất ở trên,
/// có thể có mã giao dịch trùng.
/// </summary>
public class BidvStatementReaderTests
{
    private static readonly string[] Headers =
        { "Ngày giao dịch", "Nội dung giao dịch", "Số tiền", "Số dư", "Tiền tệ", "Mã giao dịch" };

    private static StatementReadResult Read(params object?[][] rows)
    {
        using var wb = new XLWorkbook();
        var ws = wb.AddWorksheet("Sheet1");
        for (var i = 0; i < Headers.Length; i++) ws.Cell(1, i + 1).Value = Headers[i];

        var r = 2;
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
        return new StatementReader().Read(ms, "bidv.xlsx");
    }

    private static object?[] Row(object date, string desc, object amount, string code, string currency = "VND") =>
        new[] { date, desc, amount, 999_999L, currency, code };

    private static AppSettings Settings() => new()
    {
        Defaults = new DefaultsOptions
        {
            Wallet = "Ví TK", BidvWallet = "Ví BIDV", ExpenseCategory = "Khác", IncomeCategory = "Thu khác",
        },
    };

    [Fact]
    public void Signed_amount_maps_to_expense_and_income()
    {
        var result = Read(
            Row("16/11/2026", "PHI TEST", -99L, "AAA1"),
            Row("13/11/2026", "CHUYEN DEN TEST", 500_000L, "AAA2"),
            Row("14/11/2026", "Retail TEST", "-333,444", "AAA3"));

        Assert.Empty(result.FileErrors);
        Assert.Equal(StatementKind.Bidv, result.Info.Kind);
        Assert.Equal(3, result.Rows.Count);
        Assert.Equal(99, result.Rows[0].Debit);
        Assert.Null(result.Rows[0].Credit);
        Assert.Equal(500_000, result.Rows[1].Credit);
        Assert.Equal(333_444, result.Rows[2].Debit);
        Assert.All(result.Rows, r => Assert.Empty(r.ParseErrors));
    }

    [Fact]
    public void Loader_sorts_oldest_first_uses_bidv_wallet_and_warns_on_duplicate_code()
    {
        var loaded = StatementLoader.Process(Read(
            Row("14/11/2026", "Retail A", -12_345L, "DUP1"),
            Row("14/11/2026", "Retail B", -333_444L, "DUP1"),
            Row("25/10/2026", "Thanh toan lai", 9L, "LAI1")), Settings());

        Assert.Equal(new[] { "LAI1", "DUP1", "DUP1" }, loaded.Rows.Select(r => r.TxnCode));
        // Cùng ngày: dòng dưới (Retail B) xảy ra trước.
        Assert.Equal(333_444, loaded.Rows[1].SoTien);
        Assert.Equal(2, loaded.Rows.Select(r => r.Id).Distinct().Count(id => id.StartsWith("DUP1")));
        Assert.All(loaded.Rows, r => Assert.Equal("Ví BIDV", r.Vi));
        Assert.Equal(TransactionType.Thu, loaded.Rows[0].Loai);
        Assert.All(loaded.Rows, r => Assert.True(r.CanImport, string.Join("; ", r.Errors)));
        Assert.Contains(loaded.Rows[1].Warnings, w => w.Contains("trùng"));
    }

    [Fact]
    public void Date_with_time_and_date_cell_are_accepted()
    {
        var result = Read(
            Row("16/11/2026 10:20:30", "X", -1_000L, "T1"),
            Row(new DateTime(2026, 11, 15), "Y", -2_000L, "T2"));

        Assert.Equal(new DateTime(2026, 11, 16, 10, 20, 30), result.Rows[0].ExecutedAt);
        Assert.Equal(new DateTime(2026, 11, 15), result.Rows[1].ExecutedAt);
    }

    [Fact]
    public void Foreign_currency_zero_and_bad_amount_are_errors()
    {
        var loaded = StatementLoader.Process(Read(
            Row("16/11/2026", "USD", -5L, "E1", currency: "USD"),
            Row("16/11/2026", "Zero", 0L, "E2"),
            Row("16/11/2026", "Bad", "-12.5", "E3")), Settings());

        Assert.All(loaded.Rows, r => Assert.False(r.CanImport));
        Assert.Contains(loaded.Rows.Single(r => r.TxnCode == "E1").DataErrors, e => e.Contains("USD"));
    }

    [Fact]
    public void Without_bidv_wallet_falls_back_to_default_wallet()
    {
        var settings = Settings();
        settings.Defaults.BidvWallet = "";

        var loaded = StatementLoader.Process(Read(Row("16/11/2026", "X", -1_000L, "T1")), settings);

        Assert.Equal("Ví TK", loaded.Rows[0].Vi);
    }
}
