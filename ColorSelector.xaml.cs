using Microsoft.UI.Xaml;

namespace LoadCursor;

public sealed partial class ColorSelector : Microsoft.UI.Xaml.Controls.UserControl
{
    public static readonly DependencyProperty ColorProperty =
        DependencyProperty.Register(nameof(Color), typeof(Windows.UI.Color), typeof(ColorSelector),
            new PropertyMetadata(Microsoft.UI.Colors.White));
    public static readonly DependencyProperty LabelProperty =
        DependencyProperty.Register(nameof(Label), typeof(string), typeof(ColorSelector), new PropertyMetadata(""));

    public Windows.UI.Color Color
    {
        get => (Windows.UI.Color)GetValue(ColorProperty);
        set
        {
            if (Color != value) SetValue(ColorProperty, value);
        }
    }

    public string Label
    {
        get => (string)GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }

    public ColorSelector()
    {
        InitializeComponent();
        Bindings.Initialize();
    }
}
