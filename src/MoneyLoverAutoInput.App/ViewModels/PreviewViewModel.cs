using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Data;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MoneyLoverAutoInput.App.Services;
using MoneyLoverAutoInput.Core.Categorization;
using MoneyLoverAutoInput.Core.Configuration;
using MoneyLoverAutoInput.Core.Drafts;
using MoneyLoverAutoInput.Core.Import;
using MoneyLoverAutoInput.Core.Input;
using MoneyLoverAutoInput.Core.Models;
using MoneyLoverAutoInput.Core.State;

namespace MoneyLoverAutoInput.App.ViewModels;

public enum PreviewFilter
{
    All,
    New,
    Problems,
    Uncategorized,
    Edited,
    Done,
    Deleted,
}

public sealed record FilterOption(PreviewFilter Value, string Label);

/// <summary>Trang Xem trước (SPEC 10.3).</summary>
public sealed partial class PreviewViewModel : ObservableObject, IDisposable
{
    private static readonly CultureInfo Vi = CultureInfo.GetCultureInfo("vi-VN");

    private readonly IStatementReader _reader;
    private readonly IOptionsMonitor<AppSettings> _settings;
    private readonly StateStore _state;
    private readonly DraftStore _drafts;
    private readonly IFilePickerService _filePicker;
    private readonly IDialogService _dialogs;
    private readonly ILogger<PreviewViewModel> _logger;
    private readonly IDisposable? _settingsSubscription;

    private StatementReadResult? _lastRead;

    public ObservableCollection<TransactionRowViewModel> Rows { get; } = new();
    public ICollectionView RowsView { get; }

    public IReadOnlyList<FilterOption> FilterOptions { get; } = new[]
    {
        new FilterOption(PreviewFilter.All, "Tất cả"),
        new FilterOption(PreviewFilter.New, "Mới"),
        new FilterOption(PreviewFilter.Problems, "Có lỗi / cần xem"),
        new FilterOption(PreviewFilter.Uncategorized, "Chưa phân loại"),
        new FilterOption(PreviewFilter.Edited, "Đã sửa"),
        new FilterOption(PreviewFilter.Done, "Đã nhập"),
        new FilterOption(PreviewFilter.Deleted, "Đã xóa"),
    };

    public PreviewViewModel(
        IStatementReader reader,
        IOptionsMonitor<AppSettings> settings,
        StateStore state,
        DraftStore drafts,
        IFilePickerService filePicker,
        IDialogService dialogs,
        ILogger<PreviewViewModel> logger)
    {
        _reader = reader;
        _settings = settings;
        _state = state;
        _drafts = drafts;
        _filePicker = filePicker;
        _dialogs = dialogs;
        _logger = logger;

        RowsView = CollectionViewSource.GetDefaultView(Rows);
        RowsView.Filter = FilterRow;

        // Luật/cài đặt đổi (sửa file hoặc "Tải lại cấu hình") → phân loại lại, giữ chỉnh tay.
        // Debounce: một lần lưu file thường bắn nhiều sự kiện FileSystemWatcher.
        _recategorizeTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        _recategorizeTimer.Tick += (_, _) =>
        {
            _recategorizeTimer.Stop();
            Rebuild();
            _logger.LogInformation("Đã phân loại lại theo cấu hình mới");
        };
        _settingsSubscription = _settings.OnChange(_ =>
            Application.Current?.Dispatcher.InvokeAsync(() =>
            {
                _recategorizeTimer.Stop();
                _recategorizeTimer.Start();
            }));
    }

    private readonly DispatcherTimer _recategorizeTimer;

    // ---------- Thông tin file ----------

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasFile))]
    private string? _filePath;

    public bool HasFile => FilePath is not null;
    /// <summary>Tên file đã che số tài khoản.</summary>
    public string FileName => FilePath is null ? "" : _lastRead?.Info.MaskedSourceFileName ?? Path.GetFileName(FilePath);

    [ObservableProperty] private string _accountMasked = "";
    [ObservableProperty] private string _period = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OpenFileCommand), nameof(ReloadCommand), nameof(StartImportCommand), nameof(DryRunCommand))]
    private bool _isLoading;

    /// <summary>Lỗi file / lỗi luật hiển thị trên InfoBar.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMessages))]
    private string _messages = "";

    public bool HasMessages => Messages.Length > 0;

    // ---------- Lọc ----------

    [ObservableProperty] private FilterOption? _selectedFilter;
    [ObservableProperty] private string _searchText = "";

    partial void OnSelectedFilterChanged(FilterOption? value) => RowsView.Refresh();
    partial void OnSearchTextChanged(string value) => RowsView.Refresh();

    // ---------- Tổng ----------

    [ObservableProperty] private int _selectedCount;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartImportCommand), nameof(DryRunCommand))]
    private int _readyCount;
    [ObservableProperty] private string _totalExpenseText = "0";
    [ObservableProperty] private string _totalIncomeText = "0";
    [ObservableProperty] private string _summaryText = "";

    // ---------- Lệnh ----------

    private bool CanOpen() => !IsLoading;

    [RelayCommand(CanExecute = nameof(CanOpen))]
    private async Task OpenFileAsync()
    {
        var path = _filePicker.PickStatementFile();
        if (path is not null) await LoadFileAsync(path);
    }

    private bool CanReload() => !IsLoading && FilePath is not null;

    [RelayCommand(CanExecute = nameof(CanReload))]
    private Task ReloadAsync() => FilePath is null ? Task.CompletedTask : LoadFileAsync(FilePath);

    /// <summary>Chọn tất cả dòng đang hiển thị (theo bộ lọc) mà được phép chọn.</summary>
    [RelayCommand]
    private void SelectAll() => SetSelectionForVisible(true);

    [RelayCommand]
    private void UnselectAll() => SetSelectionForVisible(false);

    private void SetSelectionForVisible(bool value)
    {
        foreach (var row in RowsView.Cast<TransactionRowViewModel>()) row.IsSelected = value;
        UpdateTotals();
    }

    /// <summary>Dùng cho kéo thả và nút Chọn file.</summary>
    public async Task LoadFileAsync(string path)
    {
        if (IsLoading) return;
        if (!path.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase))
        {
            _dialogs.ShowError("Không đúng định dạng", "Chỉ hỗ trợ file Excel .xlsx.");
            return;
        }

        IsLoading = true;
        try
        {
            var settings = _settings.CurrentValue;
            var (read, loaded) = await Task.Run(() =>
            {
                var r = _reader.Read(path);
                return (r, StatementLoader.Process(r, settings, _drafts));
            });

            _lastRead = read;
            FilePath = path;
            OnPropertyChanged(nameof(FileName));
            AccountMasked = loaded.Info.MaskedAccount;
            Period = loaded.Info.Period;
            Apply(loaded, settings);

            _logger.LogInformation("Đã mở sao kê {File}: {Count} giao dịch", loaded.Info.MaskedSourceFileName,
                loaded.Rows.Count);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Không đọc được file sao kê");
            _dialogs.ShowError("Không đọc được file", ex.Message);
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>Các dòng sẽ được nhập (đã chọn và hợp lệ), theo thứ tự thời gian.</summary>
    public IReadOnlyList<TransactionRow> GetRowsToImport() =>
        Rows.Where(r => r.WillImport).Select(r => r.Model).ToList();

    // ---------- Nhập vào Money Lover ----------

    /// <summary>Yêu cầu chạy (MainWindow chuyển sang trang Chạy và bắt đầu).</summary>
    public event Action<IReadOnlyList<TransactionRow>, bool, string?>? ImportRequested;

    private bool CanImport() => ReadyCount > 0 && !IsLoading;

    /// <summary>Chạy thử dòng đầu tiên: đi hết các bước nhưng KHÔNG bấm Lưu (SPEC 8.1).</summary>
    [RelayCommand(CanExecute = nameof(CanImport))]
    private void DryRun()
    {
        var rows = GetRowsToImport();
        if (rows.Count == 0) return;
        var r = rows[0];
        if (!_dialogs.Confirm("Chạy thử",
                $"Chạy thử với giao dịch đầu tiên:\n\n{r.Ngay:dd/MM/yyyy} · {r.Loai} · {r.SoTien.ToString("#,##0", Vi)} ₫ · {r.Nhom} · ví {r.Vi}\n\n" +
                "App sẽ điều khiển điện thoại đi hết các bước (chọn loại, số tiền, ví, nhóm, ngày, ghi chú) nhưng KHÔNG bấm Lưu.\n\n" +
                "Điện thoại phải mở khóa; đừng chạm vào điện thoại khi đang chạy. Tiếp tục?")) return;

        ImportRequested?.Invoke(new[] { r }, true, _lastRead?.Info.MaskedSourceFileName);
    }

    [RelayCommand(CanExecute = nameof(CanImport))]
    private void StartImport()
    {
        var rows = GetRowsToImport();
        if (rows.Count == 0) return;
        var chi = rows.Where(x => x.Loai == TransactionType.Chi).Sum(x => x.SoTien);
        var thu = rows.Where(x => x.Loai == TransactionType.Thu).Sum(x => x.SoTien);
        var wallets = string.Join(", ", rows.Select(x => x.Vi).Distinct());
        if (!_dialogs.Confirm("Bắt đầu nhập",
                $"Nhập {rows.Count} giao dịch vào Money Lover?\n\n" +
                $"Tổng chi: {chi.ToString("#,##0", Vi)} ₫\nTổng thu: {thu.ToString("#,##0", Vi)} ₫\nVí: {wallets}\n\n" +
                "Điện thoại phải mở khóa; đừng chạm vào điện thoại khi đang chạy. Có thể bấm Dừng bất cứ lúc nào " +
                "(app dừng ở bước an toàn, các dòng đã nhập không bị nhập lại).")) return;

        ImportRequested?.Invoke(rows, false, _lastRead?.Info.MaskedSourceFileName);
    }

    /// <summary>Sau khi chạy xong: đọc lại trạng thái (dòng đã nhập bị khóa, dòng lỗi được chọn sẵn để nhập lại).</summary>
    public void RefreshAfterImport() => Rebuild();

    /// <summary>Xóa một dòng khỏi danh sách nhập (không hỏi — khôi phục được ở bộ lọc "Đã xóa").</summary>
    [RelayCommand]
    private void DeleteRow(TransactionRowViewModel? row)
    {
        if (row is null || !row.SetDeleted(true)) return;
        AfterDeleteOrRestore();
    }

    /// <summary>Xóa các dòng đang được chọn (bôi đen) trên lưới; nhiều dòng thì hỏi xác nhận.</summary>
    [RelayCommand]
    private void DeleteRows(System.Collections.IList? selected)
    {
        var rows = selected?.OfType<TransactionRowViewModel>().Where(r => r.CanDelete).ToList() ?? new();
        if (rows.Count == 0)
        {
            _dialogs.ShowInfo("Xóa dòng",
                "Chưa chọn dòng nào. Bấm vào dòng trên lưới (giữ Ctrl hoặc Shift để chọn nhiều dòng) rồi bấm Xóa hoặc phím Delete.\n\nDòng đã nhập vào Money Lover không xóa được.");
            return;
        }
        if (rows.Count > 1 && !_dialogs.Confirm("Xóa dòng",
                $"Xóa {rows.Count} dòng khỏi danh sách nhập?\n\nCó thể khôi phục ở bộ lọc \"Đã xóa\".")) return;

        foreach (var r in rows) r.SetDeleted(true);
        AfterDeleteOrRestore();
        _logger.LogInformation("Đã xóa {Count} dòng khỏi danh sách nhập", rows.Count);
    }

    [RelayCommand]
    private void RestoreRow(TransactionRowViewModel? row)
    {
        if (row is null || !row.SetDeleted(false)) return;
        AfterDeleteOrRestore();
    }

    private void AfterDeleteOrRestore()
    {
        RowsView.Refresh();
        UpdateTotals();
    }

    /// <summary>Hoàn tác mọi chỉnh sửa của một dòng về giá trị theo sao kê + luật.</summary>
    [RelayCommand]
    private void ResetRow(TransactionRowViewModel? row)
    {
        if (row is null || !row.IsEdited) return;
        _drafts.Remove(row.Id);
        Rebuild();
    }

    /// <summary>Xóa mọi chỉnh sửa (kể cả chọn/bỏ chọn) của các dòng trong file đang mở.</summary>
    [RelayCommand]
    private void ResetAllEdits()
    {
        var edited = Rows.Count(r => r.IsEdited);
        var deleted = Rows.Count(r => r.IsDeleted);
        if (!_dialogs.Confirm("Hoàn tác tất cả",
                $"Bỏ mọi chỉnh sửa của {edited} dòng đã sửa, khôi phục {deleted} dòng đã xóa và mọi lựa chọn chọn/bỏ chọn trong file này?")) return;
        _drafts.RemoveMany(Rows.Select(r => r.Id));
        Rebuild();
    }

    /// <summary>Phân loại lại theo luật hiện tại rồi áp nháp. Dùng khi đổi cấu hình hoặc hoàn tác.</summary>
    private void Rebuild()
    {
        if (_lastRead is null || _lastRead.FileErrors.Count > 0) return;
        var settings = _settings.CurrentValue;
        Apply(StatementLoader.Process(_lastRead, settings, _drafts), settings);
    }

    private void Apply(LoadedStatement loaded, AppSettings settings)
    {
        foreach (var row in Rows) row.PropertyChanged -= OnRowChanged;
        Rows.Clear();

        var wallets = settings.KnownWallets.Count > 0
            ? settings.KnownWallets
            : new[] { settings.Defaults.Wallet }.Where(w => !string.IsNullOrWhiteSpace(w)).ToList();
        var ctx = new RowEditContext(
            CategoryOptions: type => settings.GetKnownCategories(type),
            DefaultCategory: type => type == TransactionType.Chi
                ? settings.Defaults.ExpenseCategory
                : settings.Defaults.IncomeCategory,
            WalletOptions: wallets,
            DraftChanged: SaveDraft);

        foreach (var tx in loaded.Rows)
        {
            var status = RowStatusResolver.Resolve(tx, _state.Get(tx.Id));
            var vm = new TransactionRowViewModel(tx, status, _drafts.Get(tx.Id), ctx);
            vm.PropertyChanged += OnRowChanged;
            Rows.Add(vm);
        }

        var messages = new List<string>();
        messages.AddRange(loaded.FileErrors);
        messages.AddRange(loaded.RuleErrors);
        var unknown = UnknownCategories(loaded.Rows, settings);
        if (unknown.Count > 0)
        {
            messages.Add("Nhóm không có trong danh sách nhóm (kiểm tra lại tên): " + string.Join(", ", unknown) + ".");
        }
        var duplicateRows = loaded.Rows.Count(r => r.Warnings.Count > 0);
        if (duplicateRows > 0)
        {
            messages.Add($"{duplicateRows} dòng có mã giao dịch bị trùng trong file (vẫn nhập được) — " +
                         "xem bộ lọc \"Có lỗi / cần xem\" và bỏ chọn/xóa dòng nếu là giao dịch lặp.");
        }
        var unknownWallets = settings.KnownWallets.Count == 0
            ? new List<string>()
            : loaded.Rows.Select(r => r.Vi).Where(v => v.Length > 0 && !settings.KnownWallets.Contains(v)).Distinct().ToList();
        if (unknownWallets.Count > 0)
        {
            messages.Add("Ví không có trong danh sách ví: " + string.Join(", ", unknownWallets) + ".");
        }
        Messages = string.Join(Environment.NewLine, messages);

        SelectedFilter ??= FilterOptions[0];
        RowsView.Refresh();
        UpdateTotals();
    }

    private bool _draftSaveErrorShown;

    /// <summary>Lưu nháp ngay sau mỗi chỉnh sửa (đóng app vẫn giữ được).</summary>
    private void SaveDraft(TransactionRowViewModel row)
    {
        try
        {
            _drafts.Set(row.Id, row.Draft);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogError(ex, "Không lưu được nháp chỉnh sửa");
            if (!_draftSaveErrorShown)
            {
                _draftSaveErrorShown = true;
                _dialogs.ShowError("Không lưu được nháp",
                    $"Chỉnh sửa vẫn có hiệu lực nhưng có thể mất khi đóng app.\n\n{ex.Message}");
            }
        }
    }

    private static List<string> UnknownCategories(IEnumerable<TransactionRow> rows, AppSettings settings)
    {
        if (settings.KnownCategories.Count == 0) return new();
        return rows
            .Where(r => r.IsCategorized && !settings.GetKnownCategories(r.Loai).Contains(r.Nhom))
            .Select(r => r.Nhom)
            .Distinct()
            .ToList();
    }

    private void OnRowChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(TransactionRowViewModel.IsSelected) or nameof(TransactionRowViewModel.WillImport)
            or nameof(TransactionRowViewModel.SoTien) or nameof(TransactionRowViewModel.Loai)
            or nameof(TransactionRowViewModel.IsEdited) or nameof(TransactionRowViewModel.IsDeleted))
        {
            UpdateTotals();
        }
    }

    private void UpdateTotals()
    {
        var ready = Rows.Where(r => r.WillImport).ToList();
        SelectedCount = Rows.Count(r => r.IsSelected && !r.IsDeleted);
        ReadyCount = ready.Count;
        TotalExpenseText = ready.Where(r => r.Loai == TransactionType.Chi).Sum(r => r.SoTien).ToString("#,##0", Vi);
        TotalIncomeText = ready.Where(r => r.Loai == TransactionType.Thu).Sum(r => r.SoTien).ToString("#,##0", Vi);

        var done = Rows.Count(r => r.Status == RowStatus.Done);
        var invalid = Rows.Count(r => r.Status == RowStatus.Invalid);
        var waiting = SelectedCount - ReadyCount;
        var edited = Rows.Count(r => r.IsEdited && !r.IsDeleted);
        var deleted = Rows.Count(r => r.IsDeleted);
        SummaryText = $"{Rows.Count} giao dịch · {done} đã nhập · {invalid} lỗi dữ liệu · {edited} đã sửa · {deleted} đã xóa (tự lưu nháp)"
                      + (waiting > 0 ? $" · {waiting} đã chọn nhưng còn lỗi (xem cột Trạng thái)" : "");
    }

    private bool FilterRow(object item)
    {
        if (item is not TransactionRowViewModel row) return false;

        // Dòng đã xóa chỉ hiện ở bộ lọc "Đã xóa" (và ngược lại).
        var showDeleted = SelectedFilter?.Value == PreviewFilter.Deleted;
        if (row.IsDeleted != showDeleted) return false;

        var pass = (SelectedFilter?.Value ?? PreviewFilter.All) switch
        {
            PreviewFilter.New => row.Status == RowStatus.New,
            PreviewFilter.Problems => row.HasErrors || row.HasWarnings || row.Status is RowStatus.PreviouslyFailed
                or RowStatus.NeedsManualCheck or RowStatus.Invalid,
            PreviewFilter.Uncategorized => !row.Model.IsCategorized,
            PreviewFilter.Edited => row.IsEdited,
            PreviewFilter.Done => row.Status == RowStatus.Done,
            _ => true,
        };
        if (!pass) return false;

        if (string.IsNullOrWhiteSpace(SearchText)) return true;
        var q = TextNormalizer.Normalize(SearchText);
        return TextNormalizer.Normalize($"{row.Description} {row.GhiChu} {row.Nhom} {row.Id} {row.Model.CounterpartName}").Contains(q);
    }

    public void Dispose()
    {
        _recategorizeTimer.Stop();
        _settingsSubscription?.Dispose();
    }
}
