using MoneyLoverAutoInput.Core.Import;
using MoneyLoverAutoInput.Core.Manual;
using MoneyLoverAutoInput.Core.Models;
using MoneyLoverAutoInput.Core.State;

namespace MoneyLoverAutoInput.Tests;

public class ManualEntryTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "mlai-manual-" + Guid.NewGuid().ToString("N"));
    private string FilePath => Path.Combine(_dir, "manual.json");

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
    }

    private static ManualEntry Entry(int day, long amount, string note = "", TransactionType type = TransactionType.Chi) =>
        ManualEntry.Create(new DateOnly(2026, 11, day), type, amount, "Food & Beverage", "Cash", note);

    [Fact]
    public void Store_persists_add_update_remove_across_reload()
    {
        var store = ManualEntryStore.Load(FilePath);
        var a = Entry(17, 45_000, "Cà phê");
        var b = Entry(16, 30_000);
        store.Add(a);
        store.Add(b);

        a.SoTien = 50_000;
        Assert.True(store.Update(a));

        var reloaded = ManualEntryStore.Load(FilePath).All();
        Assert.Equal(new[] { b.Id, a.Id }, reloaded.Select(e => e.Id)); // sắp theo ngày
        Assert.Equal(50_000, reloaded[1].SoTien);
        Assert.Equal("Cà phê", reloaded[1].GhiChu);

        Assert.Equal(1, store.RemoveMany(new[] { b.Id }));
        Assert.Single(ManualEntryStore.Load(FilePath).All());
    }

    [Fact]
    public void All_returns_copies()
    {
        var store = ManualEntryStore.Load(FilePath);
        store.Add(Entry(17, 1_000));

        store.All()[0].SoTien = 999;

        Assert.Equal(1_000, store.All()[0].SoTien);
    }

    [Fact]
    public void Ids_are_unique_and_prefixed()
    {
        var ids = Enumerable.Range(0, 200).Select(_ => Entry(17, 1).Id).ToList();

        Assert.Equal(ids.Count, ids.Distinct().Count());
        Assert.All(ids, id => Assert.StartsWith(ManualEntry.IdPrefix, id));
    }

    [Fact]
    public void Converts_to_importable_row_and_uses_state_status()
    {
        var e = Entry(17, 45_000, "Bánh mì");
        var row = e.ToTransactionRow();

        Assert.True(row.CanImport);
        Assert.Equal(e.Id, row.Id);
        Assert.Equal("Cash", row.Vi);
        Assert.Equal("Bánh mì", row.GhiChu);

        var state = StateStore.Load(Path.Combine(_dir, "state.json"));
        Assert.Equal(RowStatus.New, RowStatusResolver.Resolve(row, state.Get(e.Id)));
        state.MarkDone(e.Id, "Nhập tay", 45_000, "Food & Beverage");
        Assert.Equal(RowStatus.Done, RowStatusResolver.Resolve(row, state.Get(e.Id)));
    }

    [Fact]
    public void Zero_amount_or_missing_category_is_not_importable()
    {
        Assert.False(Entry(17, 0).ToTransactionRow().CanImport);
        var e = Entry(17, 1_000);
        e.Nhom = "";
        Assert.False(e.ToTransactionRow().CanImport);
    }

    [Theory]
    [InlineData("45000", 45_000L)]
    [InlineData("45.000", 45_000L)]
    [InlineData("45,000", 45_000L)]
    [InlineData("45k", 45_000L)]
    [InlineData("45K", 45_000L)]
    [InlineData("1.5tr", 1_500_000L)]
    [InlineData("1,5tr", 1_500_000L)]
    [InlineData("2tr", 2_000_000L)]
    [InlineData(" 30 000 đ", 30_000L)]
    public void Amount_parser_accepts(string text, long expected)
    {
        Assert.True(AmountParser.TryParse(text, out var v));
        Assert.Equal(expected, v);
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("-5000")]
    [InlineData("1.2345k")]
    public void Amount_parser_rejects(string text) => Assert.False(AmountParser.TryParse(text, out _));
}
