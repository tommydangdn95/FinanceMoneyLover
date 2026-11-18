using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Options;
using MoneyLoverAutoInput.App.Services;
using MoneyLoverAutoInput.Core.Configuration;
using MoneyLoverAutoInput.Core.Import;
using MoneyLoverAutoInput.Core.Manual;
using MoneyLoverAutoInput.Core.Models;
using MoneyLoverAutoInput.Core.State;

namespace MoneyLoverAutoInput.App.ViewModels;

/// <summary>
/// Màn "Nhập tay" (SPEC 10.7): gõ giao dịch (VD chi tiền mặt, ví Cash) rồi nhập vào Money Lover bằng cùng
/// <c>ImportRunner</c> với sao kê. Danh sách lưu ở manual.json; trạng thái đã nhập ở state.json.
/// </summary>
public sealed partial class ManualEntryViewModel : ObservableObject, IDisposable
{
    private static readonly CultureInfo Vi = CultureInfo.GetCultureInfo("vi-VN");
    public const string SourceName = "Nhập tay";

    private readonly ManualEntryStore _store;
    private readonly StateStore _state;
    private readonly IOptionsMonitor<AppSettings> _settings;
    private readonly IDialogService _dialogs;
    private readonly IDisposable? _settingsSubscription;

    public ObservableCollection<ManualRowViewModel> Rows { get; } = new();

    public static IReadOnlyList<ChoiceOption<TransactionType>> TypeOptions => TransactionRowViewModel.TypeOptions;

    public ManualEntryViewModel(ManualEntryStore store, StateStore state, IOptionsMonitor<AppSettings> settings,
        IDialogService dialogs)
    {
        _store = store;
        _state = state;
        _settings = settings;
        _dialogs = dialogs;

        _newType = TypeOptions[0];
        _newWallet = DefaultWallet;

        _settingsSubscription = _settings.OnChange(_ =>
            Application.Current?.Dispatcher.InvokeAsync(() =>
            {
                OnPropertyChanged(nameof(NewCategoryOptions));
                OnPropertyChanged(nameof(WalletOptions));
                Refresh();
            }));

        Refresh();
    }

    private AppSettings Settings => _settings.CurrentValue;

    private string DefaultWallet =>
        (string.IsNullOrWhiteSpace(Settings.Defaults.ManualWallet) ? Settings.Defaults.Wallet : Settings.Defaults.ManualWallet).Trim();

    public IReadOnlyList<string> WalletOptions =>
        Settings.KnownWallets.Count > 0 ? Settings.KnownWallets : new[] { DefaultWallet };

    public IReadOnlyList<string> CategoryOptionsFor(TransactionType type) => Settings.GetKnownCategories(type);

    // ---------- Form thêm nhanh ----------

    [ObservableProperty] private DateTime? _newDate = DateTime.Today;
    [ObservableProperty] private ChoiceOption<TransactionType> _newType;
    [ObservableProperty] private string _newAmountText = "";
    [ObservableProperty] private string _newCategory = "";
    [ObservableProperty] private string _newWallet;
    [ObservableProperty] private string _newNote = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasFormError))]
    private string _formError = "";

    public bool HasFormError => FormError.Length > 0;

    public IReadOnlyList<string> NewCategoryOptions => CategoryOptionsFor(NewType.Value);

    partial void OnNewTypeChanged(ChoiceOption<TransactionType> value)
    {
        OnPropertyChanged(nameof(NewCategoryOptions));
        if (!NewCategoryOptions.Contains(NewCategory)) NewCategory = "";
    }

    /// <summary>Thêm xong → View đưa con trỏ về ô Số tiền để gõ tiếp.</summary>
    public event EventHandler? EntryAdded;

    [RelayCommand]
    private void Add()
    {
        var error = Validate(NewDate, NewAmountText, NewType.Value, NewCategory, NewWallet, out var amount);
        if (error is not null)
        {
            FormError = error;
            return;
        }

        _store.Add(ManualEntry.Create(DateOnly.FromDateTime(NewDate!.Value), NewType.Value, amount,
            NewCategory, NewWallet, NewNote));
        FormError = "";
        NewAmountText = "";
        NewNote = "";
        // Giữ ngày, loại, nhóm, ví: thường nhập liền nhiều khoản giống nhau.
        Refresh();
        EntryAdded?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>null = hợp lệ.</summary>
    private string? Validate(DateTime? date, string amountText, TransactionType type, string category, string wallet,
        out long amount)
    {
        amount = 0;
        if (date is null) return "Chưa chọn ngày.";
        if (date.Value.Date > DateTime.Today) return "Ngày ở tương lai — Money Lover sẽ ghi là giao dịch dự kiến.";
        if (!AmountParser.TryParse(amountText, out amount) || amount <= 0)
            return "Số tiền không hợp lệ (VD 45000, 45.000 hoặc 45k).";
        if (string.IsNullOrWhiteSpace(category)) return "Chưa chọn nhóm.";
        var known = CategoryOptionsFor(type);
        if (known.Count > 0 && !known.Contains(category.Trim()))
            return $"Nhóm '{category}' không có trong danh sách nhóm {type}.";
        if (string.IsNullOrWhiteSpace(wallet)) return "Chưa chọn ví.";
        return null;
    }

    // ---------- Danh sách ----------

    [ObservableProperty] private int _readyCount;
    [ObservableProperty] private string _totalExpenseText = "0";
    [ObservableProperty] private string _totalIncomeText = "0";
    [ObservableProperty] private string _summaryText = "";

    public bool HasRows => Rows.Count > 0;

    /// <summary>Đọc lại manual.json + state.json (sau khi thêm, sau khi chạy xong, khi config đổi).</summary>
    public void Refresh()
    {
        var snapshot = _state.Snapshot();
        Rows.Clear();
        foreach (var e in _store.All())
        {
            snapshot.TryGetValue(e.Id, out var st);
            Rows.Add(new ManualRowViewModel(e, st, this));
        }
        OnPropertyChanged(nameof(HasRows));
        UpdateTotals();
    }

    internal void RowChanged(ManualRowViewModel row)
    {
        _store.Update(row.Entry);
        UpdateTotals();
    }

    internal void UpdateTotals()
    {
        var ready = Rows.Where(r => r.WillImport).ToList();
        ReadyCount = ready.Count;
        TotalExpenseText = ready.Where(r => r.Entry.Loai == TransactionType.Chi).Sum(r => r.Entry.SoTien).ToString("#,##0", Vi);
        TotalIncomeText = ready.Where(r => r.Entry.Loai == TransactionType.Thu).Sum(r => r.Entry.SoTien).ToString("#,##0", Vi);
        var done = Rows.Count(r => r.Status == RowStatus.Done);
        var errors = Rows.Count(r => r.HasErrors);
        SummaryText = $"{Rows.Count} dòng · {done} đã nhập" + (errors > 0 ? $" · {errors} dòng cần sửa" : "");
        StartImportCommand.NotifyCanExecuteChanged();
        DryRunCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    private void DeleteRow(ManualRowViewModel? row)
    {
        if (row is null) return;
        if (row.Status != RowStatus.Done && !_dialogs.Confirm("Xóa giao dịch",
                $"Xóa giao dịch chưa nhập này?\n\n{row.Summary}")) return;
        _store.RemoveMany(new[] { row.Entry.Id });
        Refresh();
    }

    /// <summary>Dọn các dòng đã nhập xong khỏi danh sách (vẫn còn trong Money Lover và state.json).</summary>
    [RelayCommand]
    private void ClearImported()
    {
        var ids = Rows.Where(r => r.Status == RowStatus.Done).Select(r => r.Entry.Id).ToList();
        if (ids.Count == 0)
        {
            _dialogs.ShowInfo("Dọn danh sách", "Chưa có dòng nào đã nhập.");
            return;
        }
        _store.RemoveMany(ids);
        Refresh();
    }

    // ---------- Nhập vào Money Lover ----------

    /// <summary>Cùng chữ ký với Xem trước: MainWindow chuyển sang trang Chạy.</summary>
    public event Action<IReadOnlyList<TransactionRow>, bool, string?>? ImportRequested;

    private bool CanImport() => ReadyCount > 0;

    private List<TransactionRow> RowsToImport() =>
        Rows.Where(r => r.WillImport).Select(r => r.Entry.ToTransactionRow()).ToList();

    [RelayCommand(CanExecute = nameof(CanImport))]
    private void DryRun()
    {
        var rows = RowsToImport();
        if (rows.Count == 0) return;
        var r = rows[0];
        if (!_dialogs.Confirm("Chạy thử",
                $"Chạy thử với giao dịch đầu tiên:\n\n{r.Ngay:dd/MM/yyyy} · {r.Loai} · {r.SoTien.ToString("#,##0", Vi)} ₫ · {r.Nhom} · ví {r.Vi}\n\n" +
                "App đi hết các bước trên điện thoại nhưng KHÔNG bấm Lưu. Tiếp tục?")) return;
        ImportRequested?.Invoke(new[] { r }, true, SourceName);
    }

    [RelayCommand(CanExecute = nameof(CanImport))]
    private void StartImport()
    {
        var rows = RowsToImport();
        if (rows.Count == 0) return;
        var wallets = string.Join(", ", rows.Select(x => x.Vi).Distinct());
        if (!_dialogs.Confirm("Bắt đầu nhập",
                $"Nhập {rows.Count} giao dịch nhập tay vào Money Lover?\n\n" +
                $"Tổng chi: {TotalExpenseText} ₫\nTổng thu: {TotalIncomeText} ₫\nVí: {wallets}\n\n" +
                "Điện thoại phải mở khóa; đừng chạm vào điện thoại khi đang chạy.")) return;
        ImportRequested?.Invoke(rows, false, SourceName);
    }

    public void Dispose() => _settingsSubscription?.Dispose();
}

/// <summary>Một giao dịch nhập tay trên lưới. Sửa trực tiếp → lưu manual.json ngay.</summary>
public sealed class ManualRowViewModel : ObservableObject
{
    private static readonly CultureInfo Vi = CultureInfo.GetCultureInfo("vi-VN");
    private readonly ManualEntryViewModel _owner;
    private bool _isSelected;

    public ManualEntry Entry { get; }
    public RowStatus Status { get; }

    public ManualRowViewModel(ManualEntry entry, StateEntry? state, ManualEntryViewModel owner)
    {
        Entry = entry;
        _owner = owner;
        Status = RowStatusResolver.Resolve(entry.ToTransactionRow(), state);
        _isSelected = RowStatusResolver.IsSelectedByDefault(Status);
    }

    public bool IsEditable => Status != RowStatus.Done;
    public bool IsLocked => !IsEditable;
    public bool IsSelectable => Status != RowStatus.Done;
    public string StatusText => RowStatusResolver.ToDisplayText(Status);

    public IReadOnlyList<string> CategoryOptions => _owner.CategoryOptionsFor(Entry.Loai);
    public IReadOnlyList<string> WalletOptions => _owner.WalletOptions;

    public string Summary =>
        $"{Entry.Ngay:dd/MM/yyyy} · {Entry.Loai} · {Entry.SoTien.ToString("#,##0", Vi)} ₫ · {Entry.Nhom} · ví {Entry.Vi}";

    public IReadOnlyList<string> Errors
    {
        get
        {
            if (Status == RowStatus.Done) return Array.Empty<string>();
            var errors = new List<string>(Entry.ToTransactionRow().Errors);
            var known = CategoryOptions;
            if (Entry.Nhom.Length > 0 && known.Count > 0 && !known.Contains(Entry.Nhom))
                errors.Add($"Nhóm '{Entry.Nhom}' không có trong danh sách.");
            return errors;
        }
    }

    public string ErrorText => string.Join(" ", Errors);
    public bool HasErrors => Errors.Count > 0;
    public bool WillImport => IsSelected && IsSelectable && !HasErrors;
    public bool IsExpense => Entry.Loai == TransactionType.Chi;

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (value && !IsSelectable) value = false;
            if (!SetProperty(ref _isSelected, value)) return;
            OnPropertyChanged(nameof(WillImport));
            _owner.UpdateTotals();
        }
    }

    public DateTime? NgayDate
    {
        get => Entry.Ngay.ToDateTime(TimeOnly.MinValue);
        set
        {
            if (!IsEditable || value is null) return;
            var d = DateOnly.FromDateTime(value.Value);
            if (d == Entry.Ngay) return;
            Entry.Ngay = d;
            Edited(nameof(NgayDate));
        }
    }

    public ChoiceOption<TransactionType> LoaiOption
    {
        get => ManualEntryViewModel.TypeOptions.First(o => o.Value == Entry.Loai);
        set
        {
            if (value is null || !IsEditable || value.Value == Entry.Loai) return;
            Entry.Loai = value.Value;
            if (!CategoryOptions.Contains(Entry.Nhom)) Entry.Nhom = "";
            OnPropertyChanged(nameof(CategoryOptions));
            OnPropertyChanged(nameof(Nhom));
            OnPropertyChanged(nameof(IsExpense));
            Edited(nameof(LoaiOption));
        }
    }

    public string SoTienText
    {
        get => Entry.SoTien.ToString("#,##0", Vi);
        set
        {
            if (!IsEditable || !AmountParser.TryParse(value, out var amount) || amount == Entry.SoTien)
            {
                OnPropertyChanged(); // trả ô về giá trị cũ
                return;
            }
            Entry.SoTien = amount;
            Edited(nameof(SoTienText));
        }
    }

    public string Nhom
    {
        get => Entry.Nhom;
        set
        {
            var v = value?.Trim() ?? "";
            if (!IsEditable || v == Entry.Nhom) return;
            Entry.Nhom = v;
            Edited(nameof(Nhom));
        }
    }

    public string Wallet
    {
        get => Entry.Vi;
        set
        {
            var v = value?.Trim() ?? "";
            if (!IsEditable || v == Entry.Vi) return;
            Entry.Vi = v;
            Edited(nameof(Wallet));
        }
    }

    public string GhiChu
    {
        get => Entry.GhiChu;
        set
        {
            var v = value?.Trim() ?? "";
            if (!IsEditable || v == Entry.GhiChu) return;
            Entry.GhiChu = v;
            Edited(nameof(GhiChu));
        }
    }

    private void Edited(string property)
    {
        OnPropertyChanged(property);
        OnPropertyChanged(nameof(ErrorText));
        OnPropertyChanged(nameof(HasErrors));
        OnPropertyChanged(nameof(WillImport));
        _owner.RowChanged(this);
    }
}
