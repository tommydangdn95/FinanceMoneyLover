using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace MoneyLoverAutoInput.App.Views;

/// <summary>Banner thông báo đơn giản (tiêu đề + nội dung + vạch màu).</summary>
public partial class Banner : UserControl
{
    public static readonly DependencyProperty TitleProperty =
        DependencyProperty.Register(nameof(Title), typeof(string), typeof(Banner), new PropertyMetadata(""));

    public static readonly DependencyProperty MessageProperty =
        DependencyProperty.Register(nameof(Message), typeof(string), typeof(Banner), new PropertyMetadata(""));

    public static readonly DependencyProperty AccentBrushProperty =
        DependencyProperty.Register(nameof(AccentBrush), typeof(Brush), typeof(Banner),
            new PropertyMetadata(Brushes.SteelBlue));

    public Banner() => InitializeComponent();

    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string Message
    {
        get => (string)GetValue(MessageProperty);
        set => SetValue(MessageProperty, value);
    }

    public Brush AccentBrush
    {
        get => (Brush)GetValue(AccentBrushProperty);
        set => SetValue(AccentBrushProperty, value);
    }
}
