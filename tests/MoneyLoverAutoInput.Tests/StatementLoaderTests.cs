using MoneyLoverAutoInput.Core.Configuration;
using MoneyLoverAutoInput.Core.Import;
using MoneyLoverAutoInput.Core.Input;
using MoneyLoverAutoInput.Core.Models;
using static MoneyLoverAutoInput.Tests.TestHelpers;

namespace MoneyLoverAutoInput.Tests;

public class StatementLoaderTests
{
    private static AppSettings SampleSettings() => Settings(
        SkipRule("NAP TIEN VI MOMO"),
        Rule("Di chuyển", TransactionType.Chi, "GRAB"),
        Rule("Mua sắm", TransactionType.Chi, "SHOPEE"),
        Rule("Ăn uống", TransactionType.Chi, "TT QR"),
        Rule("Lương", TransactionType.Thu, "LUONG"));

    [Fact]
    public void Sample_is_sorted_oldest_first_and_categorized()
    {
        var loaded = new StatementLoader(new BankStatementReader()).Load(SamplePath, SampleSettings());

        Assert.False(loaded.HasFileErrors);
        Assert.Equal(6, loaded.Rows.Count);
        Assert.Equal(loaded.Rows.OrderBy(r => r.SortKey).Select(r => r.Id), loaded.Rows.Select(r => r.Id));
        Assert.Equal("999CTMB000000001", loaded.Rows[0].Id);

        var byId = loaded.Rows.ToDictionary(r => r.Id);
        Assert.Equal("Thu nhập khác", byId["999CTMB000000001"].Nhom);
        Assert.Equal(RuleAction.Skip, byId["999TTMB000000002"].Action);
        Assert.Equal("Mua sắm", byId["999CTMB000000003"].Nhom);
        Assert.Equal("Lương", byId["999CTMB000000004"].Nhom);
        Assert.Equal("Ăn uống", byId["999CTMB000000005"].Nhom);
        Assert.Equal("Di chuyển", byId["999CTMB000000006"].Nhom);
        Assert.All(loaded.Rows, r => Assert.True(r.CanImport));
    }

    [Fact]
    public void Same_timestamp_keeps_chronological_order_of_newest_first_file()
    {
        var read = ReadBuilt(new[]
        {
            Row("01-11-2026 10:00:00", "sau", 1000L, "", "B"),
            Row("01-11-2026 10:00:00", "trước", 1000L, "", "A"),
        });

        var loaded = StatementLoader.Process(read, Settings());

        Assert.Equal(new[] { "A", "B" }, loaded.Rows.Select(r => r.Id));
    }

    [Fact]
    public void Duplicate_and_missing_codes_are_data_errors()
    {
        var read = ReadBuilt(new[]
        {
            Row("03-11-2026 10:00:00", "A", 1000L, "", "DUP"),
            Row("02-11-2026 10:00:00", "B", 1000L, "", "DUP"),
            Row("01-11-2026 10:00:00", "C", 1000L, "", ""),
            Row("01-11-2026 09:00:00", "D", 1000L, "", "OK"),
        });

        var rows = StatementLoader.Process(read, Settings()).Rows.ToDictionary(r => r.Description);

        Assert.Contains(rows["A"].DataErrors, e => e.Contains("trùng") && e.Contains("10") && e.Contains("11"));
        Assert.Contains(rows["B"].DataErrors, e => e.Contains("trùng"));
        Assert.Contains(rows["C"].DataErrors, e => e.Contains("Thiếu Mã giao dịch"));
        Assert.Empty(rows["D"].DataErrors);
    }

    [Fact]
    public void File_errors_return_no_rows()
    {
        var read = ReadBuilt(Array.Empty<object?[]>(), headers: new[] { "Cột lạ" });

        var loaded = StatementLoader.Process(read, Settings());

        Assert.True(loaded.HasFileErrors);
        Assert.Empty(loaded.Rows);
    }

    [Fact]
    public void Recategorize_keeps_manual_edits()
    {
        var read = new BankStatementReader().Read(SamplePath);
        var edits = new Dictionary<string, (string?, string?)>
        {
            ["999CTMB000000005"] = ("Cà phê", null),
            ["999CTMB000000006"] = (null, "Grab đi làm"),
        };

        var rows = StatementLoader.Recategorize(read, SampleSettings(), edits).Rows.ToDictionary(r => r.Id);

        Assert.Equal("Cà phê", rows["999CTMB000000005"].Nhom);
        Assert.Equal("Di chuyển", rows["999CTMB000000006"].Nhom);
        Assert.Equal("Grab đi làm", rows["999CTMB000000006"].GhiChu);
    }
}
