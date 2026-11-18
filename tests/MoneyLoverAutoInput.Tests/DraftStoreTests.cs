using MoneyLoverAutoInput.Core.Drafts;
using MoneyLoverAutoInput.Core.Models;

namespace MoneyLoverAutoInput.Tests;

public sealed class DraftStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "mlai-draft-" + Guid.NewGuid().ToString("N"));
    private string FilePath => Path.Combine(_dir, "drafts.json");

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public void Every_edit_survives_reload()
    {
        var store = DraftStore.Load(FilePath);
        store.Set("A", new RowDraft
        {
            Ngay = new DateOnly(2026, 11, 1), Loai = TransactionType.Thu, SoTien = 45000,
            Nhom = "Food & Beverage > Café", Vi = "Cash", GhiChu = "Cà phê sáng", Selected = false,
        });

        var d = DraftStore.Load(FilePath).Get("A")!;

        Assert.Equal(new DateOnly(2026, 11, 1), d.Ngay);
        Assert.Equal(TransactionType.Thu, d.Loai);
        Assert.Equal(45000, d.SoTien);
        Assert.Equal("Food & Beverage > Café", d.Nhom);
        Assert.Equal("Cash", d.Vi);
        Assert.Equal("Cà phê sáng", d.GhiChu);
        Assert.False(d.Selected);
        Assert.False(File.Exists(FilePath + ".tmp"));
    }

    [Fact]
    public void Empty_draft_removes_entry()
    {
        var store = DraftStore.Load(FilePath);
        store.Set("A", new RowDraft { Nhom = "X" });
        store.Set("A", new RowDraft());

        Assert.Null(DraftStore.Load(FilePath).Get("A"));
        Assert.Equal(0, store.Count);
    }

    [Fact]
    public void Get_returns_a_copy()
    {
        var store = DraftStore.Load(FilePath);
        store.Set("A", new RowDraft { Nhom = "X" });

        store.Get("A")!.Nhom = "Y";

        Assert.Equal("X", store.Get("A")!.Nhom);
    }

    [Fact]
    public void Selection_only_is_not_a_content_edit()
    {
        Assert.False(new RowDraft { Selected = true }.HasContentEdits);
        Assert.True(new RowDraft { GhiChu = "" }.HasContentEdits); // xóa trắng ghi chú cũng là sửa
    }

    [Fact]
    public void Deleted_flag_survives_reload_and_restore_clears_entry()
    {
        var store = DraftStore.Load(FilePath);
        store.Set("A", new RowDraft { Deleted = true });

        var d = DraftStore.Load(FilePath).Get("A")!;
        Assert.True(d.IsDeleted);
        Assert.False(d.HasContentEdits);
        Assert.False(d.IsEmpty);

        d.Deleted = null; // khôi phục
        store.Set("A", d);
        Assert.Null(DraftStore.Load(FilePath).Get("A"));
    }

    [Fact]
    public void Remove_many()
    {
        var store = DraftStore.Load(FilePath);
        store.Set("A", new RowDraft { Nhom = "X" });
        store.Set("B", new RowDraft { Nhom = "Y" });
        store.Set("C", new RowDraft { Nhom = "Z" });

        store.RemoveMany(new[] { "A", "C", "khong-co" });

        var reloaded = DraftStore.Load(FilePath);
        Assert.Null(reloaded.Get("A"));
        Assert.NotNull(reloaded.Get("B"));
        Assert.Equal(1, reloaded.Count);
    }
}
