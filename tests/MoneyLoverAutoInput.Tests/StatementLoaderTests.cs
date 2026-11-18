using MoneyLoverAutoInput.Core.Configuration;
using MoneyLoverAutoInput.Core.Drafts;
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
    public void Counterpart_name_comes_from_statement()
    {
        var rows = new StatementLoader(new BankStatementReader()).Load(SamplePath, SampleSettings())
            .Rows.ToDictionary(r => r.Id);

        Assert.Equal("NGUYEN VAN A", rows["999CTMB000000001"].CounterpartName);        // Thu: người gửi
        Assert.Equal("CTY CP DICH VU DI DONG TRUC TUYEN", rows["999TTMB000000002"].CounterpartName); // Chi: người nhận
        Assert.Equal("", rows["999CTMB000000005"].CounterpartName);                   // QR: ngân hàng không ghi
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

        // Mã trùng: chỉ cảnh báo, vẫn nhập được.
        Assert.Empty(rows["A"].DataErrors);
        Assert.Empty(rows["B"].DataErrors);
        Assert.True(rows["A"].CanImport);
        Assert.True(rows["B"].CanImport);
        Assert.Contains(rows["A"].Warnings, w => w.Contains("trùng") && w.Contains("10") && w.Contains("11"));
        Assert.Contains(rows["B"].Warnings, w => w.Contains("trùng"));

        // Thiếu mã vẫn là lỗi (không có khóa để chống trùng).
        Assert.Contains(rows["C"].DataErrors, e => e.Contains("Thiếu Mã giao dịch"));
        Assert.Empty(rows["D"].DataErrors);
        Assert.Empty(rows["D"].Warnings);
    }

    [Fact]
    public void Duplicate_codes_get_distinct_keys_in_file_order()
    {
        var read = ReadBuilt(new[]
        {
            Row("03-11-2026 10:00:00", "A", 1000L, "", "DUP"),   // dòng 10
            Row("02-11-2026 10:00:00", "B", 2000L, "", "DUP"),   // dòng 11
            Row("01-11-2026 10:00:00", "C", 3000L, "", "DUP"),   // dòng 12
        });

        var rows = StatementLoader.Process(read, Settings()).Rows.ToDictionary(r => r.Description);

        Assert.Equal("DUP", rows["A"].Id);
        Assert.Equal("DUP#2", rows["B"].Id);
        Assert.Equal("DUP#3", rows["C"].Id);
        Assert.All(rows.Values, r => Assert.Equal("DUP", r.TxnCode));
    }

    [Fact]
    public void Drafts_of_duplicate_rows_do_not_mix()
    {
        var path = Path.Combine(Path.GetTempPath(), "mlai-dup-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var drafts = DraftStore.Load(path);
            drafts.Set("DUP#2", new RowDraft { GhiChu = "chỉ dòng thứ hai" });
            var read = ReadBuilt(new[]
            {
                Row("03-11-2026 10:00:00", "A", 1000L, "", "DUP"),
                Row("02-11-2026 10:00:00", "B", 2000L, "", "DUP"),
            });

            var rows = StatementLoader.Process(read, Settings(), drafts).Rows.ToDictionary(r => r.Description);

            Assert.Equal("A", rows["A"].GhiChu);
            Assert.Equal("chỉ dòng thứ hai", rows["B"].GhiChu);
        }
        finally
        {
            File.Delete(path);
        }
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
    public void Drafts_override_rules_and_keep_bank_originals()
    {
        var path = Path.Combine(Path.GetTempPath(), "mlai-drafts-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var drafts = DraftStore.Load(path);
            drafts.Set("999CTMB000000005", new RowDraft { Nhom = "Cà phê", SoTien = 80000, Ngay = new DateOnly(2026, 11, 13) });
            drafts.Set("999CTMB000000006", new RowDraft { GhiChu = "Grab đi làm", Vi = "Cash", Loai = TransactionType.Thu });

            var read = new BankStatementReader().Read(SamplePath);
            var rows = StatementLoader.Process(read, SampleSettings(), drafts).Rows.ToDictionary(r => r.Id);

            var qr = rows["999CTMB000000005"];
            Assert.Equal("Cà phê", qr.Nhom);
            Assert.Equal(80000, qr.SoTien);
            Assert.Equal(85000, qr.OriginalSoTien);
            Assert.Equal(new DateOnly(2026, 11, 13), qr.Ngay);
            Assert.Equal(new DateOnly(2026, 11, 14), qr.OriginalNgay);

            var grab = rows["999CTMB000000006"];
            Assert.Equal("Di chuyển", grab.Nhom);       // không sửa nhóm → vẫn theo luật
            Assert.Equal("Grab đi làm", grab.GhiChu);
            Assert.Equal("Cash", grab.Vi);
            Assert.Equal(TransactionType.Thu, grab.Loai);
            Assert.Equal(TransactionType.Chi, grab.OriginalLoai);

            Assert.Equal("Thu nhập khác", rows["999CTMB000000001"].Nhom); // dòng không có nháp giữ nguyên
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Edited_amount_of_zero_blocks_import()
    {
        var row = StatementLoader.Process(new BankStatementReader().Read(SamplePath), SampleSettings()).Rows[0];
        Assert.True(row.CanImport);

        row.SoTien = 0;

        Assert.False(row.CanImport);
        Assert.Contains(TransactionRow.NonPositiveAmountError, row.Errors);
    }
}
