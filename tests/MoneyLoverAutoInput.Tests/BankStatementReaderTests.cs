using MoneyLoverAutoInput.Core.Input;
using static MoneyLoverAutoInput.Tests.TestHelpers;

namespace MoneyLoverAutoInput.Tests;

public class BankStatementReaderTests
{
    [Fact]
    public void Reads_sample_file_metadata_and_all_rows()
    {
        var result = new BankStatementReader().Read(SamplePath);

        Assert.Empty(result.FileErrors);
        Assert.Equal("09990001234", result.Info.AccountNumber);
        Assert.Equal("***1234", result.Info.MaskedAccount);
        Assert.Equal("01/11/2026 - 15/11/2026", result.Info.Period);

        // 6 dòng dữ liệu; các ô có style nhưng trống phía dưới không bị tính.
        Assert.Equal(6, result.Rows.Count);
        Assert.All(result.Rows, r => Assert.Empty(r.ParseErrors));
    }

    [Fact]
    public void Parses_text_dates_empty_strings_and_numbers_from_sample()
    {
        var rows = new BankStatementReader().Read(SamplePath).Rows;

        var first = rows[0]; // mới nhất ở trên
        Assert.Equal(10, first.ExcelRow);
        Assert.Equal(new DateTime(2026, 11, 15, 20, 15, 0), first.ExecutedAt);
        Assert.Equal(new DateOnly(2026, 11, 15), first.EffectiveDate);
        Assert.Equal(54000, first.Debit);
        Assert.Null(first.Credit); // ô '' → trống
        Assert.Equal("", first.CounterpartName);
        Assert.Equal("999CTMB000000006", first.TxnCode);

        var salary = rows.Single(r => r.TxnCode == "999CTMB000000004");
        Assert.Null(salary.Debit);
        Assert.Equal(15000000, salary.Credit);
        Assert.Equal("CONG TY TNHH ABC", salary.CounterpartName);
    }

    [Fact]
    public void Finds_header_at_any_row()
    {
        var result = ReadBuilt(new[] { Row("01-11-2026 10:00:00", "A", 1000L, "", "X1") }, headerRow: 14);

        Assert.Empty(result.FileErrors);
        Assert.Single(result.Rows);
        Assert.Equal(15, result.Rows[0].ExcelRow);
        Assert.Equal("09990001234", result.Info.AccountNumber);
    }

    [Fact]
    public void Header_match_ignores_case_and_accents_and_column_order()
    {
        var headers = new[]
        {
            "MA GIAO DICH", "ngày thực hiện", "Mô tả giao dịch", "GHI NỢ", "Ghi có",
            "Số dư", "Tài khoản đối ứng", "Tên tài khoản", "Ngày hiệu lực",
        };
        var row = new object?[] { "X1", "01-11-2026 10:00:00", "Mo ta", 1000L, "", 0L, "", "", "01/11/2026" };

        var result = ReadBuilt(new[] { row }, headers: headers);

        Assert.Empty(result.FileErrors);
        Assert.Equal("X1", result.Rows[0].TxnCode);
        Assert.Equal(1000, result.Rows[0].Debit);
    }

    [Fact]
    public void Missing_required_column_is_file_error()
    {
        var headers = Headers.Select(h => h == "Mã giao dịch" ? "Ghi chú" : h).ToArray();

        var result = ReadBuilt(new[] { Row("01-11-2026 10:00:00", "A", 1000L, "", "X1") }, headers: headers);

        var error = Assert.Single(result.FileErrors);
        Assert.Contains("Mã giao dịch", error);
        Assert.Empty(result.Rows);
    }

    [Fact]
    public void Stops_at_first_blank_row()
    {
        var rows = new[]
        {
            Row("02-11-2026 10:00:00", "A", 1000L, "", "X2"),
            new object?[] { "", "", "", "", "", "", "", "", "" },
            Row("01-11-2026 10:00:00", "Footer?", 1000L, "", "X1"),
        };

        Assert.Single(ReadBuilt(rows).Rows);
    }

    [Theory]
    [InlineData("45.000", 45000L)]
    [InlineData("45,000", 45000L)]
    [InlineData("1.234.567", 1234567L)]
    [InlineData("45000", 45000L)]
    [InlineData("45.000 VND", 45000L)]
    public void Parses_amount_text(string text, long expected)
    {
        var result = ReadBuilt(new[] { Row("01-11-2026 10:00:00", "A", text, "", "X1") });

        Assert.Empty(result.Rows[0].ParseErrors);
        Assert.Equal(expected, result.Rows[0].Debit);
    }

    [Theory]
    [InlineData("45.5")]
    [InlineData("-45000")]
    [InlineData("abc")]
    public void Rejects_bad_amount_text(string text)
    {
        var row = ReadBuilt(new[] { Row("01-11-2026 10:00:00", "A", text, "", "X1") }).Rows[0];

        Assert.True(row.AmountParseFailed);
        Assert.Contains(row.ParseErrors, e => e.Contains("Ghi nợ"));
    }

    [Fact]
    public void Falls_back_to_effective_date_and_reports_when_both_bad()
    {
        var onlyEffective = new object?[] { "sai", "03/11/2026", "A", 1000L, "", 0L, "", "", "X1" };
        var bothBad = new object?[] { "sai", "cũng sai", "B", 1000L, "", 0L, "", "", "X2" };

        var rows = ReadBuilt(new[] { onlyEffective, bothBad }).Rows;

        Assert.Null(rows[0].ExecutedAt);
        Assert.Equal(new DateOnly(2026, 11, 3), rows[0].EffectiveDate);
        Assert.Empty(rows[0].ParseErrors);
        Assert.Contains(rows[1].ParseErrors, e => e.Contains("Ngày không hợp lệ"));
    }

    [Fact]
    public void Accepts_real_excel_date_cells()
    {
        var row = new object?[] { new DateTime(2026, 11, 2, 8, 30, 0), "", "A", 1000L, "", 0L, "", "", "X1" };

        var parsed = ReadBuilt(new[] { row }).Rows[0];

        Assert.Equal(new DateTime(2026, 11, 2, 8, 30, 0), parsed.ExecutedAt);
    }

    [Fact]
    public void Not_an_excel_file_is_file_error()
    {
        using var ms = new MemoryStream("not excel"u8.ToArray());

        var result = new BankStatementReader().Read(ms, "x.xlsx");

        Assert.Single(result.FileErrors);
    }

    /// <summary>
    /// Kiểm tra với sao kê thật mà không đưa file vào repo:
    /// đặt biến môi trường MLAI_REAL_STATEMENT = đường dẫn file rồi chạy test. Chỉ kiểm cấu trúc.
    /// </summary>
    [Fact]
    public void Real_statement_if_provided_parses_without_errors()
    {
        var path = Environment.GetEnvironmentVariable("MLAI_REAL_STATEMENT");
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return;

        var result = new BankStatementReader().Read(path);

        Assert.Empty(result.FileErrors);
        Assert.NotEmpty(result.Rows);
        Assert.All(result.Rows, r => Assert.Empty(r.ParseErrors));
        Assert.All(result.Rows, r => Assert.False(string.IsNullOrWhiteSpace(r.TxnCode)));
    }
}
