using CommunityToolkit.Mvvm.ComponentModel;
using MoneyLoverAutoInput.Core.Configuration;
using MoneyLoverAutoInput.Core.Models;

namespace MoneyLoverAutoInput.App.ViewModels;

/// <summary>Loại giao dịch luật áp dụng. Không dùng enum nullable vì ComboBox WPF không chọn được giá trị null.</summary>
public enum RuleTypeChoice
{
    Both,
    Chi,
    Thu,
}

public sealed record ChoiceOption<T>(T Value, string Label);

/// <summary>Một dòng trong bảng luật ở màn Thiết lập.</summary>
public sealed partial class RuleEditorViewModel : ObservableObject
{
    public static IReadOnlyList<ChoiceOption<RuleTypeChoice>> TypeOptions { get; } = new[]
    {
        new ChoiceOption<RuleTypeChoice>(RuleTypeChoice.Chi, "Chi"),
        new ChoiceOption<RuleTypeChoice>(RuleTypeChoice.Thu, "Thu"),
        new ChoiceOption<RuleTypeChoice>(RuleTypeChoice.Both, "Cả hai"),
    };

    public static IReadOnlyList<ChoiceOption<RuleAction>> ActionOptions { get; } = new[]
    {
        new ChoiceOption<RuleAction>(RuleAction.Import, "Nhập"),
        new ChoiceOption<RuleAction>(RuleAction.Skip, "Bỏ qua"),
    };

    private readonly Func<RuleTypeChoice, IReadOnlyList<string>> _categoryOptions;

    /// <summary>Regex không sửa trên UI nhưng phải giữ lại khi lưu.</summary>
    private readonly string? _regex;

    public RuleEditorViewModel(CategoryRule rule, Func<RuleTypeChoice, IReadOnlyList<string>> categoryOptions)
    {
        _categoryOptions = categoryOptions;
        _regex = rule.Regex;
        _keywordsText = string.Join(", ", rule.Match);
        _type = rule.Type switch
        {
            TransactionType.Chi => RuleTypeChoice.Chi,
            TransactionType.Thu => RuleTypeChoice.Thu,
            _ => RuleTypeChoice.Both,
        };
        _action = rule.Action;
        _category = rule.Category ?? "";
    }

    /// <summary>Từ khóa, cách nhau bởi dấu phẩy.</summary>
    [ObservableProperty] private string _keywordsText;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CategoryOptions))]
    private RuleTypeChoice _type;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsImport))]
    private RuleAction _action;

    [ObservableProperty] private string _category;

    /// <summary>Thứ tự ưu tiên (1 = cao nhất), do màn Thiết lập đánh lại sau mỗi thay đổi.</summary>
    [ObservableProperty] private int _order;

    public bool IsImport => Action == RuleAction.Import;
    public bool HasRegex => !string.IsNullOrWhiteSpace(_regex);

    public IReadOnlyList<string> CategoryOptions => _categoryOptions(Type);

    public void RefreshCategoryOptions() => OnPropertyChanged(nameof(CategoryOptions));

    public CategoryRule ToRule() => new()
    {
        Match = KeywordsText.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList(),
        Regex = _regex,
        Type = Type switch
        {
            RuleTypeChoice.Chi => TransactionType.Chi,
            RuleTypeChoice.Thu => TransactionType.Thu,
            _ => null,
        },
        Action = Action,
        Category = Action == RuleAction.Skip || string.IsNullOrWhiteSpace(Category) ? null : Category.Trim(),
    };
}
