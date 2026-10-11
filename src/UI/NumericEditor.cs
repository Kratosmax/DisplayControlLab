using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;

namespace DisplayControl.UI;

// Owns draft input for one mounted control. Repainting cannot submit old input.
internal sealed class NumericEditor : IDisposable
{
    public StackPanel View { get; } = new();
    public Slider Slider { get; }
    public TextBox Input { get; }
    public Button? Apply { get; }
    readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(160) };
    readonly Action<string> submit;
    readonly bool manual;
    bool disposed, mirroring, textDirty, gesture;
    string desired;
    public NumericEditor(uint minimum, uint maximum, uint current, uint step, bool enabled, bool manual, Action<string> submit, bool compact = false)
    {
        this.manual = manual; this.submit = submit; desired = current.ToString();
        var row = new Grid(); row.ColumnDefinitions.Add(new()); row.ColumnDefinitions.Add(new() { Width = new GridLength(80) });
        Slider = new() { Minimum = minimum, Maximum = maximum, Value = current, TickFrequency = step, IsSnapToTickEnabled = true, IsMoveToPointEnabled = true, IsEnabled = enabled, Margin = new Thickness(0, 0, 20, 0), VerticalAlignment = VerticalAlignment.Center };
        Input = new() { Text = desired, IsReadOnly = !enabled, MinHeight = 36, VerticalContentAlignment = VerticalAlignment.Center };
        row.Children.Add(Slider); Grid.SetColumn(Input, 1); row.Children.Add(Input); View.Children.Add(row);
        if (compact) {
            row.ColumnDefinitions[1].Width = new GridLength(48);
            Input.MinHeight = 28; Input.Padding = new Thickness(4, 2, 4, 2); Input.TextAlignment = TextAlignment.Center;
            Slider.Margin = new Thickness(0, 0, 8, 0);
        }
        if (manual) {
            Apply = new() { Content = "应用", IsEnabled = enabled, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 8, 0, 0) };
            Apply.Click += (_, _) => CommitText();
            if (compact) { row.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); Grid.SetColumn(Apply, 2); Apply.Margin = new Thickness(6, 0, 0, 0); Apply.Padding = new Thickness(6, 3, 6, 3); row.Children.Add(Apply); }
            else View.Children.Add(Apply);
        }
        timer.Tick += (_, _) => { if (Slider.IsMouseCaptureWithin) return; timer.Stop(); if (!disposed) submit(desired); };
        Slider.PreviewMouseLeftButtonDown += (_, _) => gesture = true;
        Slider.PreviewMouseLeftButtonUp += (_, _) => { gesture = false; if (!manual) Schedule(); };
        Slider.AddHandler(Thumb.DragCompletedEvent, new DragCompletedEventHandler((_, _) => { gesture = false; if (!manual) Schedule(); }));
        Slider.PreviewKeyDown += (_, _) => gesture = true;
        Slider.PreviewKeyUp += (_, _) => { gesture = false; if (!manual) Schedule(); };
        Slider.ValueChanged += (_, _) => {
            if (disposed || mirroring) return;
            desired = Slider.Value.ToString("0"); mirroring = true; Input.Text = desired; mirroring = false; textDirty = false;
            if (!manual && (gesture || Slider.IsMouseCaptureWithin)) Schedule();
        };
        Input.TextChanged += (_, _) => { if (!mirroring && !disposed) textDirty = true; };
        Input.KeyDown += (_, e) => { if (e.Key == Key.Enter && !disposed) { CommitText(); e.Handled = true; } };
        Input.LostKeyboardFocus += (_, _) => { if (!disposed && textDirty && !manual) CommitText(); };
    }
    void CommitText()
    {
        if (disposed || Input.IsReadOnly || !uint.TryParse(Input.Text, out uint n) || n < Slider.Minimum || n > Slider.Maximum) return;
        desired = n.ToString(); mirroring = true; Slider.Value = n; Input.Text = desired; mirroring = false; textDirty = false; timer.Stop(); submit(desired);
    }
    void Schedule() { if (disposed || !Slider.IsEnabled) return; timer.Stop(); timer.Start(); }
    public void Dispose() { disposed = true; timer.Stop(); }
}
