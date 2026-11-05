using Microsoft.UI.Xaml;

namespace LoadCursor;

public sealed partial class PulseMeter : Microsoft.UI.Xaml.Controls.UserControl
{
    public static readonly DependencyProperty LabelProperty =
        DependencyProperty.Register(nameof(Label), typeof(string), typeof(PulseMeter), new PropertyMetadata(""));
    public static readonly DependencyProperty TextProperty =
        DependencyProperty.Register(nameof(Text), typeof(string), typeof(PulseMeter), new PropertyMetadata("N/A"));
    public static readonly DependencyProperty BarsProperty =
        DependencyProperty.Register(nameof(Bars), typeof(string), typeof(PulseMeter), new PropertyMetadata("░░░░░░░░░░"));
    public static readonly DependencyProperty StatusProperty =
        DependencyProperty.Register(nameof(Status), typeof(string), typeof(PulseMeter),
            new PropertyMetadata("Normal", OnStatusChanged));

    public string Label
    {
        get => (string)GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public string Bars
    {
        get => (string)GetValue(BarsProperty);
        set => SetValue(BarsProperty, value);
    }

    public string Status
    {
        get => (string)GetValue(StatusProperty);
        set => SetValue(StatusProperty, value);
    }

    public PulseMeter()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            Bindings.Update();
            UpdateState();
        };
    }

    static void OnStatusChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        var meter = (PulseMeter)sender;
        if (meter.IsLoaded)
            meter.UpdateState();
    }

    void UpdateState() => VisualStateManager.GoToState(this, Status, false);

    public string Filled(string bars) => bars.Replace("░", "");
    public string Empty(string bars) => bars.Replace("█", "");
    public string Describe(string label, string text, string status) =>
        $"{(label == "MEM" ? "Memory" : label)} usage: {text}; {status}";
}
