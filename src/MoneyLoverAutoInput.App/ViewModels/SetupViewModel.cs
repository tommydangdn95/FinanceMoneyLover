using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MoneyLoverAutoInput.App.Services;
using MoneyLoverAutoInput.Core;
using MoneyLoverAutoInput.Core.Categorization;
using MoneyLoverAutoInput.Core.Configuration;
using MoneyLoverAutoInput.Core.Models;

namespace MoneyLoverAutoInput.App.ViewModels;

/// <summary>
/// Màn Thiết lập (SPEC 10.5): ví, danh sách nhóm, nhóm mặc định, luật phân loại.
/// Lần đầu mở app sẽ vào màn này. Sửa trên bản sao, chỉ ghi file khi bấm Lưu.
/// </summary>
public sealed partial class SetupViewModel : ObservableObject
{
    private readonly IOptionsMonitor<AppSettings> _settings;
    private readonly AppDataPaths _paths;
    private readonly IConfiguration _configuration;
    private readonly IDialogService _dialogs;
    private readonly ILogger<SetupViewModel> _logger;

    private bool _loading;

    /// <summary>Phát ra sau khi lưu xong lần thiết lập đầu tiên — để chuyển sang Xem trước.</summary>
    public event EventHandler? FirstSetupCompleted;

    public ObservableCollection<RuleEditorViewModel> Rules { get; } = new();

    public IReadOnlyList<ChoiceOption<RuleTypeChoice>> TypeOptions => RuleEditorViewModel.TypeOptions;
    public IReadOnlyList<ChoiceOption<RuleAction>> ActionOptions => RuleEditorViewModel.ActionOptions;

    public SetupViewModel(IOptionsMonitor<AppSettings> settings, AppDataPaths paths, IConfiguration configuration,
        IDialogService dialogs, ILogger<SetupViewModel> logger)
    {
        _settings = settings;
        _paths = paths;
        _configuration = configuration;
        _dialogs = dialogs;
        _logger = logger;

        Rules.CollectionChanged += OnRulesChanged;
        LoadFrom(_settings.CurrentValue);
    }

    // ---------- Trạng thái ----------

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SaveButtonText))]
    private bool _isFirstRun;

    public string SaveButtonText => IsFirstRun ? "Lưu và bắt đầu" : "Lưu thiết lập";

    [ObservableProperty] private bool _isDirty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasValidationMessages))]
    private string _validationMessages = "";

    public bool HasValidationMessages => ValidationMessages.Length > 0;

    // ---------- Ví & nhóm ----------

    [ObservableProperty] private string _defaultWallet = "";

    /// <summary>Danh sách ví, mỗi dòng một ví.</summary>
    [ObservableProperty] private string _walletsText = "";

    public IReadOnlyList<string> KnownWallets =>
        WalletsText.Replace("\r", "").Split('\n')
            .Select(w => w.Trim())
            .Where(w => w.Length > 0)
            .Distinct(StringComparer.CurrentCultureIgnoreCase)
            .ToList();

    partial void OnWalletsTextChanged(string value)
    {
        OnPropertyChanged(nameof(KnownWallets));
        MarkDirty();
    }

    /// <summary>Mỗi dòng một nhóm.</summary>
    [ObservableProperty] private string _expenseCategoriesText = "";
    [ObservableProperty] private string _incomeCategoriesText = "";

    [ObservableProperty] private string _defaultExpenseCategory = "";
    [ObservableProperty] private string _defaultIncomeCategory = "";

    /// <summary>Mọi nhóm chọn được, dạng đường dẫn "Cha" / "Cha &gt; Con".</summary>
    public IReadOnlyList<string> ExpenseCategories => CategoryTree.Flatten(CategoryTree.Parse(ExpenseCategoriesText));
    public IReadOnlyList<string> IncomeCategories => CategoryTree.Flatten(CategoryTree.Parse(IncomeCategoriesText));

    partial void OnExpenseCategoriesTextChanged(string value) => OnCategoryListsChanged();
    partial void OnIncomeCategoriesTextChanged(string value) => OnCategoryListsChanged();
    partial void OnDefaultWalletChanged(string value) => MarkDirty();
    partial void OnDefaultExpenseCategoryChanged(string value) => MarkDirty();
    partial void OnDefaultIncomeCategoryChanged(string value) => MarkDirty();

    private void OnCategoryListsChanged()
    {
        OnPropertyChanged(nameof(ExpenseCategories));
        OnPropertyChanged(nameof(IncomeCategories));
        foreach (var r in Rules) r.RefreshCategoryOptions();
        MarkDirty();
    }

    private IReadOnlyList<string> CategoryOptionsFor(RuleTypeChoice type) => type switch
    {
        RuleTypeChoice.Chi => ExpenseCategories,
        RuleTypeChoice.Thu => IncomeCategories,
        _ => ExpenseCategories.Concat(IncomeCategories).Distinct().ToList(),
    };

    // ---------- Luật ----------

    /// <summary>Thêm luật mới vào đầu danh sách (ưu tiên cao nhất) để người dùng thấy ngay.</summary>
    [RelayCommand]
    private void AddRule() => Rules.Insert(0, NewRuleEditor(new CategoryRule { Type = TransactionType.Chi }));

    [RelayCommand]
    private void RemoveRule(RuleEditorViewModel? rule)
    {
        if (rule is not null) Rules.Remove(rule);
    }

    [RelayCommand]
    private void MoveRuleUp(RuleEditorViewModel? rule) => Move(rule, -1);

    [RelayCommand]
    private void MoveRuleDown(RuleEditorViewModel? rule) => Move(rule, +1);

    private void Move(RuleEditorViewModel? rule, int delta)
    {
        if (rule is null) return;
        var from = Rules.IndexOf(rule);
        var to = from + delta;
        if (from < 0 || to < 0 || to >= Rules.Count) return;
        Rules.Move(from, to);
    }

    private RuleEditorViewModel NewRuleEditor(CategoryRule rule)
    {
        var vm = new RuleEditorViewModel(rule, CategoryOptionsFor);
        vm.PropertyChanged += OnRuleChanged;
        return vm;
    }

    private void OnRuleChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(RuleEditorViewModel.Order) or nameof(RuleEditorViewModel.CategoryOptions)
            or nameof(RuleEditorViewModel.IsImport)) return;
        MarkDirty();
    }

    private void OnRulesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        for (var i = 0; i < Rules.Count; i++) Rules[i].Order = i + 1;
        if (e.OldItems is not null)
        {
            foreach (RuleEditorViewModel r in e.OldItems) r.PropertyChanged -= OnRuleChanged;
        }
        MarkDirty();
    }

    // ---------- Thử luật ----------

    [ObservableProperty] private string _testText = "";
    [ObservableProperty] private RuleTypeChoice _testType = RuleTypeChoice.Chi;
    [ObservableProperty] private string _testResult = "";

    partial void OnTestTextChanged(string value) => RunTest();
    partial void OnTestTypeChanged(RuleTypeChoice value) => RunTest();

    public IReadOnlyList<ChoiceOption<RuleTypeChoice>> TestTypeOptions { get; } =
        RuleEditorViewModel.TypeOptions.Where(o => o.Value != RuleTypeChoice.Both).ToList();

    public ChoiceOption<RuleTypeChoice> TestTypeOption
    {
        get => TestTypeOptions.First(o => o.Value == TestType);
        set { if (value is not null) TestType = value.Value; }
    }

    [RelayCommand]
    private void RunTest()
    {
        if (string.IsNullOrWhiteSpace(TestText))
        {
            TestResult = "";
            return;
        }

        var settings = BuildSettings();
        var type = TestType == RuleTypeChoice.Thu ? TransactionType.Thu : TransactionType.Chi;
        var rule = new Categorizer(settings).Test(TestText, type);

        if (rule is null)
        {
            var fallback = type == TransactionType.Chi ? settings.Defaults.ExpenseCategory : settings.Defaults.IncomeCategory;
            TestResult = string.IsNullOrWhiteSpace(fallback)
                ? "Không khớp luật nào → chưa phân loại (phải chọn nhóm bằng tay)."
                : $"Không khớp luật nào → nhóm mặc định \"{fallback}\".";
            return;
        }

        var index = settings.CategoryRules.IndexOf(rule) + 1;
        TestResult = rule.Action == RuleAction.Skip
            ? $"Khớp luật #{index} ({rule.Describe()}) → Bỏ qua, không nhập."
            : $"Khớp luật #{index} ({rule.Describe()}) → nhóm \"{rule.Category}\".";
    }

    // ---------- Lưu / tải ----------

    [RelayCommand]
    private void Save()
    {
        var settings = BuildSettings();
        var validation = SettingsFile.Validate(settings);
        ValidationMessages = string.Join(Environment.NewLine,
            validation.Errors.Select(e => "✖ " + e).Concat(validation.Warnings.Select(w => "⚠ " + w)));

        if (!validation.IsValid)
        {
            _dialogs.ShowError("Chưa lưu được", "Hãy sửa các lỗi được đánh dấu ✖ ở đầu trang.");
            return;
        }

        var wasFirstRun = IsFirstRun;
        settings.SetupCompleted = true;

        try
        {
            SettingsFile.Save(_paths.ConfigFile, settings);
            // Không chờ FileSystemWatcher: nạp lại ngay để Xem trước phân loại lại.
            (_configuration as IConfigurationRoot)?.Reload();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogError(ex, "Không ghi được file cấu hình");
            _dialogs.ShowError("Không lưu được", ex.Message);
            return;
        }

        _logger.LogInformation("Đã lưu thiết lập: {Rules} luật, {Expense} nhóm Chi, {Income} nhóm Thu",
            settings.CategoryRules.Count, ExpenseCategories.Count, IncomeCategories.Count);

        IsFirstRun = false;
        IsDirty = false;
        if (wasFirstRun) FirstSetupCompleted?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Bỏ các thay đổi chưa lưu, đọc lại từ file cấu hình.</summary>
    [RelayCommand]
    private void Revert()
    {
        if (IsDirty && !_dialogs.Confirm("Hoàn tác", "Bỏ mọi thay đổi chưa lưu?")) return;
        LoadFrom(_settings.CurrentValue);
    }

    private void LoadFrom(AppSettings source)
    {
        _loading = true;
        try
        {
            var s = SettingsFile.Clone(source);
            IsFirstRun = !s.SetupCompleted;
            DefaultWallet = s.Defaults.Wallet;
            WalletsText = string.Join(Environment.NewLine, s.KnownWallets);
            ExpenseCategoriesText = CategoryTree.Format(s.GetCategoryTree(TransactionType.Chi));
            IncomeCategoriesText = CategoryTree.Format(s.GetCategoryTree(TransactionType.Thu));
            DefaultExpenseCategory = s.Defaults.ExpenseCategory;
            DefaultIncomeCategory = s.Defaults.IncomeCategory;

            foreach (var r in Rules) r.PropertyChanged -= OnRuleChanged; // Clear() không báo OldItems
            Rules.Clear();
            foreach (var rule in s.CategoryRules) Rules.Add(NewRuleEditor(rule));
            ValidationMessages = "";
        }
        finally
        {
            _loading = false;
        }
        IsDirty = false;
    }

    /// <summary>Ghép những gì đang sửa trên màn hình vào bản sao của cấu hình hiện tại.</summary>
    private AppSettings BuildSettings()
    {
        var s = SettingsFile.Clone(_settings.CurrentValue);
        s.Defaults.Wallet = DefaultWallet.Trim();
        s.KnownWallets = KnownWallets.ToList();
        s.Defaults.ExpenseCategory = DefaultExpenseCategory.Trim();
        s.Defaults.IncomeCategory = DefaultIncomeCategory.Trim();
        s.KnownCategories = new Dictionary<string, List<CategoryNode>>
        {
            [nameof(TransactionType.Chi)] = CategoryTree.Parse(ExpenseCategoriesText),
            [nameof(TransactionType.Thu)] = CategoryTree.Parse(IncomeCategoriesText),
        };
        s.CategoryRules = Rules.Select(r => r.ToRule()).ToList();
        return s;
    }

    private void MarkDirty()
    {
        if (!_loading) IsDirty = true;
    }
}
