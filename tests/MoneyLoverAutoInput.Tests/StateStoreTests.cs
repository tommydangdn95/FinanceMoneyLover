using MoneyLoverAutoInput.Core.Configuration;
using MoneyLoverAutoInput.Core.Import;
using MoneyLoverAutoInput.Core.Models;
using MoneyLoverAutoInput.Core.Privacy;
using MoneyLoverAutoInput.Core.State;

namespace MoneyLoverAutoInput.Tests;

public sealed class StateStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "mlai-tests-" + Guid.NewGuid().ToString("N"));
    private string StatePath => Path.Combine(_dir, "sub", "state.json");

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public void Missing_file_starts_empty()
    {
        var store = StateStore.Load(StatePath);

        Assert.Empty(store.Snapshot());
        Assert.False(File.Exists(StatePath));
    }

    [Fact]
    public void Every_change_is_persisted_immediately_and_reloads()
    {
        var store = StateStore.Load(StatePath);
        store.MarkDone("A", "file-***1234.xlsx", 54000, "Di chuyển");
        store.MarkFailed("B", null, "CategoryItem 'Cafe' not found", "screenshots/B.png");
        store.MarkSkipped("C", null, "Rule: NAP TIEN VI MOMO");

        // Giả lập app bị tắt giữa chừng: không có Save/Dispose nào cả, đọc lại từ đĩa.
        var reloaded = StateStore.Load(StatePath);

        Assert.Equal(ImportStatus.Done, reloaded.Get("A")!.Status);
        Assert.Equal(54000, reloaded.Get("A")!.Amount);
        Assert.Equal("Di chuyển", reloaded.Get("A")!.Category);
        Assert.Equal("screenshots/B.png", reloaded.Get("B")!.Screenshot);
        Assert.Equal(ImportStatus.Skipped, reloaded.Get("C")!.Status);
        Assert.False(File.Exists(StatePath + ".tmp"));
    }

    [Fact]
    public void Json_uses_readable_enums_and_unescaped_vietnamese()
    {
        StateStore.Load(StatePath).MarkDone("A", null, 1, "Ăn uống");

        var json = File.ReadAllText(StatePath);

        Assert.Contains("\"status\": \"Done\"", json);
        Assert.Contains("Ăn uống", json);
        Assert.DoesNotContain("\"error\"", json); // null không ghi ra
    }

    [Fact]
    public void Remove_and_clear()
    {
        var store = StateStore.Load(StatePath);
        store.MarkDone("A", null, 1, "x");
        store.MarkDone("B", null, 1, "x");

        Assert.True(store.Remove("A"));
        Assert.False(store.Remove("A"));
        Assert.Null(StateStore.Load(StatePath).Get("A"));

        store.Clear();
        Assert.Empty(StateStore.Load(StatePath).Snapshot());
    }

    [Fact]
    public void Newer_version_is_rejected()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(StatePath)!);
        File.WriteAllText(StatePath, """{ "version": 99, "rows": {} }""");

        Assert.Throws<InvalidDataException>(() => StateStore.Load(StatePath));
    }

    private static TransactionRow Tx(RuleAction action = RuleAction.Import, bool dataError = false)
    {
        var row = new TransactionRow { Id = "A", Nhom = "Khác", Action = action };
        if (dataError) row.DataErrors.Add("lỗi");
        return row;
    }

    [Fact]
    public void Status_resolution_follows_spec()
    {
        var done = new StateEntry { Status = ImportStatus.Done };
        var failed = new StateEntry { Status = ImportStatus.Failed };
        var unsure = new StateEntry { Status = ImportStatus.Failed, NeedsManualCheck = true };
        var skipped = new StateEntry { Status = ImportStatus.Skipped };

        Assert.Equal(RowStatus.New, RowStatusResolver.Resolve(Tx(), null));
        Assert.Equal(RowStatus.Done, RowStatusResolver.Resolve(Tx(dataError: true), done));
        Assert.Equal(RowStatus.Invalid, RowStatusResolver.Resolve(Tx(dataError: true), failed));
        Assert.Equal(RowStatus.PreviouslyFailed, RowStatusResolver.Resolve(Tx(), failed));
        Assert.Equal(RowStatus.NeedsManualCheck, RowStatusResolver.Resolve(Tx(), unsure));
        Assert.Equal(RowStatus.Skipped, RowStatusResolver.Resolve(Tx(RuleAction.Skip), null));
        // Luật đã đổi, không còn Skip → đánh giá lại thành Mới.
        Assert.Equal(RowStatus.New, RowStatusResolver.Resolve(Tx(), skipped));
    }

    [Fact]
    public void Selection_rules()
    {
        Assert.True(RowStatusResolver.IsSelectedByDefault(RowStatus.New));
        Assert.True(RowStatusResolver.IsSelectedByDefault(RowStatus.PreviouslyFailed));
        Assert.False(RowStatusResolver.IsSelectedByDefault(RowStatus.NeedsManualCheck));
        Assert.False(RowStatusResolver.IsSelectedByDefault(RowStatus.Skipped));
        Assert.False(RowStatusResolver.IsSelectable(RowStatus.Done));
        Assert.False(RowStatusResolver.IsSelectable(RowStatus.Invalid));
        Assert.True(RowStatusResolver.IsSelectable(RowStatus.Skipped));
    }

    [Theory]
    [InlineData("09990005678", "***5678")]
    [InlineData("123", "***")]
    [InlineData("", "")]
    public void Masks_account(string account, string expected) =>
        Assert.Equal(expected, Masking.MaskAccount(account));

    [Fact]
    public void Masks_account_inside_file_name()
    {
        Assert.Equal("Account Enquiry-23_10_2026-***1234.xlsx",
            Masking.MaskInText("Account Enquiry-23_10_2026-09990001234.xlsx", "09990001234"));
    }
}
