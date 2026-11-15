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
    Done,
}

public sealed record FilterOption(PreviewFilter Value, string Label);

/// <summary>Trang Xem trước (SPEC 10.3).</summary>
public sealed partial class PreviewViewModel : ObservableObject, IDisposable
{
    private static readonly CultureInfo Vi = CultureInfo.GetCultureInfo("vi-VN");

    private readonly IStatementReader _reader;
    private readonly IOptionsMonitor<AppSettings> _settings;
    private readonly StateStore _state;
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
        new FilterOption(PreviewFilter.Done, "Đã nhập"),
    };

    public PreviewViewModel(
        IStatementReader reader,
        IOptionsMonitor<AppSettings> settings,
        StateStore state,
        IFilePickerService filePicker,
        IDialogService dialogs,
        ILogger<PreviewViewModel> logger)
    {
        _reader = reader;
        _settings = settings;
        _state = state;
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
            Recategorize();
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
    [NotifyCanExecuteChangedFor(nameof(OpenFileCommand), nameof(ReloadCommand))]
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
    [ObservableProperty] private int _readyCount;
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
                return (r, StatementLoader.Process(r, settings));
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

    private void Recategorize()
    {
        if (_lastRead is null || _lastRead.FileErrors.Count > 0) return;

        var edits = Rows
            .Where(r => r.IsNhomEdited || r.IsGhiChuEdited)
            .ToDictionary(r => r.Id, r => (r.IsNhomEdited ? r.Nhom : null, r.IsGhiChuEdited ? r.GhiChu : null));
        var selection = Rows.ToDictionary(r => r.Id, r => r.IsSelected);

        var settings = _settings.CurrentValue;
        var loaded = StatementLoader.Recategorize(_lastRead, settings, edits);
        Apply(loaded, settings, selection);
        _logger.LogInformation("Đã phân loại lại theo cấu hình mới");
    }

    private void Apply(LoadedStatement loaded, AppSettings settings,
        IReadOnlyDictionary<string, bool>? previousSelection = null)
    {
        foreach (var row in Rows) row.PropertyChanged -= OnRowChanged;
        Rows.Clear();

        foreach (var tx in loaded.Rows)
        {
            var status = RowStatusResolver.Resolve(tx, _state.Get(tx.Id));
            var vm = new TransactionRowViewModel(tx, status, settings.GetKnownCategories(tx.Loai));
            if (previousSelection is not null && previousSelection.TryGetValue(tx.Id, out var selected))
                vm.IsSelected = selected;
            vm.PropertyChanged += OnRowChanged;
            Rows.Add(vm);
        }

        var messages = new List<string>();
        messages.AddRange(loaded.FileErrors);
        messages.AddRange(loaded.RuleErrors);
        var unknown = UnknownCategories(loaded.Rows, settings);
        if (unknown.Count > 0)
        {
            messages.Add("Nhóm không có trong KnownCategories (kiểm tra lại tên): " + string.Join(", ", unknown) + ".");
        }
        Messages = string.Join(Environment.NewLine, messages);

        SelectedFilter ??= FilterOptions[0];
        RowsView.Refresh();
        UpdateTotals();
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
            or nameof(TransactionRowViewModel.Nhom))
        {
            UpdateTotals();
        }
    }

    private void UpdateTotals()
    {
        var ready = Rows.Where(r => r.WillImport).ToList();
        SelectedCount = Rows.Count(r => r.IsSelected);
        ReadyCount = ready.Count;
        TotalExpenseText = ready.Where(r => r.Loai == TransactionType.Chi).Sum(r => r.SoTien).ToString("#,##0", Vi);
        TotalIncomeText = ready.Where(r => r.Loai == TransactionType.Thu).Sum(r => r.SoTien).ToString("#,##0", Vi);

        var done = Rows.Count(r => r.Status == RowStatus.Done);
        var invalid = Rows.Count(r => r.Status == RowStatus.Invalid);
        var waiting = SelectedCount - ReadyCount;
        SummaryText = $"{Rows.Count} giao dịch · {done} đã nhập · {invalid} lỗi dữ liệu"
                      + (waiting > 0 ? $" · {waiting} đã chọn nhưng chưa có nhóm" : "");
    }

    private bool FilterRow(object item)
    {
        if (item is not TransactionRowViewModel row) return false;

        var pass = (SelectedFilter?.Value ?? PreviewFilter.All) switch
        {
            PreviewFilter.New => row.Status == RowStatus.New,
            PreviewFilter.Problems => row.HasErrors || row.Status is RowStatus.PreviouslyFailed
                or RowStatus.NeedsManualCheck or RowStatus.Invalid,
            PreviewFilter.Uncategorized => !row.Model.IsCategorized,
            PreviewFilter.Done => row.Status == RowStatus.Done,
            _ => true,
        };
        if (!pass) return false;

        if (string.IsNullOrWhiteSpace(SearchText)) return true;
        var q = TextNormalizer.Normalize(SearchText);
        return TextNormalizer.Normalize($"{row.Description} {row.GhiChu} {row.Nhom} {row.Id}").Contains(q);
    }

    public void Dispose()
    {
        _recategorizeTimer.Stop();
        _settingsSubscription?.Dispose();
    }
}
