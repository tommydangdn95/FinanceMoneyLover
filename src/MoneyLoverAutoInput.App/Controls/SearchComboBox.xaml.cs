using System.Collections;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using MoneyLoverAutoInput.Core.Categorization;

namespace MoneyLoverAutoInput.App.Controls;

/// <summary>
/// Ô chọn có tìm kiếm cho nhóm/ví (SPEC 10.3). Gõ chữ → lọc danh sách theo "chứa", không phân biệt dấu/hoa
/// thường ("petrol" → "Transportation &gt; Petrol"). Giá trị chỉ được ghi vào <see cref="Value"/> khi:
/// chọn một mục (click / Enter), hoặc rời ô (nếu còn đúng 1 mục khớp thì lấy mục đó, không thì giữ chữ đã gõ).
///
/// Phím: ↓/↑ di chuyển trong danh sách, Enter chọn, Esc hủy và trả lại giá trị cũ.
/// </summary>
public partial class SearchComboBox : UserControl
{
    public static readonly DependencyProperty ItemsSourceProperty = DependencyProperty.Register(
        nameof(ItemsSource), typeof(IEnumerable), typeof(SearchComboBox),
        new PropertyMetadata(null, (d, _) => ((SearchComboBox)d).RebuildItems()));

    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value), typeof(string), typeof(SearchComboBox),
        new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
            (d, e) => ((SearchComboBox)d).OnValueChanged((string?)e.NewValue))
        {
            DefaultUpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged,
        });

    public static readonly DependencyProperty IsReadOnlyProperty = DependencyProperty.Register(
        nameof(IsReadOnly), typeof(bool), typeof(SearchComboBox), new PropertyMetadata(false));

    public IEnumerable? ItemsSource
    {
        get => (IEnumerable?)GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    public string? Value
    {
        get => (string?)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public bool IsReadOnly
    {
        get => (bool)GetValue(IsReadOnlyProperty);
        set => SetValue(IsReadOnlyProperty, value);
    }

    private List<string> _all = new();
    private ListCollectionView _view = new(new List<string>());
    private string _query = "";
    private bool _settingText;
    private DateTime _closedAt = DateTime.MinValue;

    public SearchComboBox()
    {
        InitializeComponent();

        Input.TextChanged += OnInputTextChanged;
        Input.PreviewKeyDown += OnInputPreviewKeyDown;
        Input.LostKeyboardFocus += OnInputLostFocus;
        Toggle.Click += OnToggleClick;
        List.PreviewMouseLeftButtonUp += OnListMouseUp;
        DropDown.Closed += (_, _) =>
        {
            List.SelectedIndex = -1;
            _closedAt = DateTime.UtcNow;
        };

        RebuildItems();
    }

    // ---------- Dữ liệu ----------

    private void RebuildItems()
    {
        if (List is null) return; // gọi từ PropertyMetadata trước InitializeComponent
        _all = ItemsSource?.Cast<object>().Select(o => o?.ToString() ?? "").Where(s => s.Length > 0).ToList()
               ?? new List<string>();
        _view = new ListCollectionView(_all) { Filter = o => TextNormalizer.ContainsNormalized(o as string, _query) };
        List.ItemsSource = _view;
    }

    private void OnValueChanged(string? value)
    {
        // Giá trị đổi từ ViewModel (nạp lưới, hoàn tác...) và người dùng không đang gõ → hiện lên ô.
        if (Input is null || Input.IsKeyboardFocusWithin) return;
        SetInputText(value ?? "");
    }

    // ---------- Gõ & lọc ----------

    private void OnInputTextChanged(object sender, TextChangedEventArgs e)
    {
        if (_settingText || IsReadOnly) return;

        ApplyFilter(Input.Text);
        if (Input.IsKeyboardFocusWithin) Open();
    }

    private void ApplyFilter(string text)
    {
        // Chữ đúng bằng một mục (VD vừa chọn xong) → hiện toàn bộ danh sách.
        _query = _all.Contains(text) ? "" : text;
        _view.Refresh();
        NoMatch.Visibility = _view.IsEmpty ? Visibility.Visible : Visibility.Collapsed;
        List.Visibility = _view.IsEmpty ? Visibility.Collapsed : Visibility.Visible;
        // Gõ để lọc → đánh dấu sẵn mục đầu tiên để Enter chọn luôn.
        List.SelectedIndex = _query.Length > 0 && !_view.IsEmpty ? 0 : -1;
    }

    private void Open()
    {
        if (!DropDown.IsOpen && _all.Count > 0) DropDown.IsOpen = true;
    }

    private void OnToggleClick(object sender, RoutedEventArgs e)
    {
        if (IsReadOnly || !IsEnabled) return;

        // Popup StaysOpen=False tự đóng ngay khi nhấn chuột xuống nút này → lần Click ngay sau đó là "đóng",
        // không được mở lại.
        if (DropDown.IsOpen || (DateTime.UtcNow - _closedAt).TotalMilliseconds < 250)
        {
            DropDown.IsOpen = false;
            return;
        }

        ApplyFilter(""); // nút mũi tên → hiện tất cả
        Input.Focus();
        Input.CaretIndex = Input.Text.Length;
        Open();
        ScrollToCurrent();
    }

    private void ScrollToCurrent()
    {
        var current = Input.Text;
        if (_all.Contains(current)) List.ScrollIntoView(current);
    }

    // ---------- Bàn phím ----------

    private void OnInputPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (IsReadOnly) return;

        switch (e.Key)
        {
            case Key.Down:
                if (!DropDown.IsOpen) { ApplyFilter(Input.Text); Open(); }
                Move(+1);
                e.Handled = true;
                break;

            case Key.Up:
                if (DropDown.IsOpen) { Move(-1); e.Handled = true; }
                break;

            case Key.Enter:
                if (DropDown.IsOpen && List.SelectedItem is string picked) Commit(picked);
                else CommitTyped();
                e.Handled = true; // không để DataGrid nhảy sang dòng khác
                break;

            case Key.Escape:
                if (DropDown.IsOpen)
                {
                    SetInputText(Value ?? "");
                    DropDown.IsOpen = false;
                    e.Handled = true;
                }
                break;

            case Key.Tab:
                DropDown.IsOpen = false; // LostFocus sẽ ghi giá trị
                break;
        }
    }

    private void Move(int delta)
    {
        var count = _view.Count;
        if (count == 0) return;
        var i = List.SelectedIndex < 0 ? (delta > 0 ? 0 : count - 1) : List.SelectedIndex + delta;
        List.SelectedIndex = Math.Clamp(i, 0, count - 1);
        List.ScrollIntoView(List.SelectedItem);
    }

    // ---------- Chuột ----------

    private void OnListMouseUp(object sender, MouseButtonEventArgs e)
    {
        var item = FindAncestor<ListBoxItem>(e.OriginalSource as DependencyObject);
        if (item?.DataContext is string picked)
        {
            Commit(picked);
            e.Handled = true;
        }
    }

    private static T? FindAncestor<T>(DependencyObject? d) where T : DependencyObject
    {
        while (d is not null and not T) d = VisualTreeHelper.GetParent(d);
        return d as T;
    }

    // ---------- Ghi giá trị ----------

    private void OnInputLostFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (IsReadOnly) return;
        CommitTyped();
    }

    /// <summary>Ghi chữ đang gõ: trùng 1 mục → mục đó; còn đúng 1 mục khớp → mục đó; không thì giữ nguyên chữ.</summary>
    private void CommitTyped()
    {
        var text = Input.Text.Trim();
        if (text.Length > 0 && !_all.Contains(text))
        {
            var matches = _all.Where(s => TextNormalizer.ContainsNormalized(s, text)).Take(2).ToList();
            if (matches.Count == 1) text = matches[0];
        }
        Commit(text);
    }

    private void Commit(string value)
    {
        DropDown.IsOpen = false;
        SetInputText(value);
        if (!string.Equals(Value, value, StringComparison.Ordinal))
        {
            SetCurrentValue(ValueProperty, value);
            GetBindingExpression(ValueProperty)?.UpdateSource();
        }
    }

    private void SetInputText(string text)
    {
        _settingText = true;
        try
        {
            Input.Text = text;
            Input.CaretIndex = text.Length;
        }
        finally
        {
            _settingText = false;
        }
        ApplyFilter(text);
    }
}
