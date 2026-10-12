using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace DisplayControl.UI;

public partial class WorkspaceWindow
{
    Window CreateProtocolPromptWindow(string prompt, Action<string>? copy = null, Action<string>? save = null)
    {
        var window = new Window {
            Title = "私有协议 AI 提示词", Owner = IsVisible ? this : null, Icon = Icon,
            Width = 860, Height = 680, MinWidth = 620, MinHeight = 420,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, Background = Background,
            Foreground = Foreground, FontFamily = FontFamily, FontSize = 14,
            UseLayoutRounding = true, SnapsToDevicePixels = true
        };
        window.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/DisplayControl.UI;component/Theme.xaml", UriKind.Relative) });
        var layout = new Grid { Margin = new Thickness(24) };
        layout.RowDefinitions.Add(new() { Height = GridLength.Auto }); layout.RowDefinitions.Add(new());
        layout.RowDefinitions.Add(new() { Height = GridLength.Auto }); layout.RowDefinitions.Add(new() { Height = GridLength.Auto });
        layout.Children.Add(Section("复制后直接交给 AI", "包含脱敏设备资料、读数和未知项。可以直接复制，无需保存文件；生成的协议仍需验证。"));
        var text = new TextBox {
            Text = prompt, IsReadOnly = true, IsReadOnlyCaretVisible = true, AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, FontSize = 13, Padding = new Thickness(12),
            Margin = new Thickness(0, 8, 0, 12)
        };
        Grid.SetRow(text, 1); layout.Children.Add(text);
        var feedback = Text("可选择文字复制，也可以一键复制全部内容。", 13, "#64748C");
        Grid.SetRow(feedback, 2); layout.Children.Add(feedback);
        var actions = Actions();
        actions.Children.Add(Button("复制全部提示词", () => {
            try { (copy ?? Clipboard.SetText)(prompt); feedback.Text = "已复制全部提示词，可以直接粘贴给 AI。"; }
            catch (Exception) { feedback.Text = "复制失败，剪贴板可能正被占用。请重试，或在文本框中全选复制。"; }
        }, true));
        actions.Children.Add(Button("另存为文本（可选）", () => {
            try {
                if (save != null) save(prompt);
                else {
                    var dialog = new SaveFileDialog { Filter = "提示词文本|*.txt", FileName = "display-protocol-prompt.txt" };
                    if (dialog.ShowDialog(window) != true) return;
                    File.WriteAllText(dialog.FileName, prompt);
                }
                feedback.Text = "提示词已保存。";
            } catch (Exception ex) { feedback.Text = "保存失败：" + ex.Message; }
        }));
        actions.Children.Add(Button("关闭", window.Close));
        Grid.SetRow(actions, 3); layout.Children.Add(actions); window.Content = layout;
        window.PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { e.Handled = true; window.Close(); } };
        return window;
    }

    void RunProtocolPromptTests(string directory, Action<bool, string> check, string prompt)
    {
        IEnumerable<DependencyObject> Children(DependencyObject node) {
            foreach (var child in LogicalTreeHelper.GetChildren(node).OfType<DependencyObject>()) { yield return child; foreach (var nested in Children(child)) yield return nested; }
        }
        string? copied = null, saved = null;
        var window = CreateProtocolPromptWindow(prompt, s => copied = s, s => saved = s);
        var text = Children(window).OfType<TextBox>().Single();
        check(text.IsReadOnly && text.Text == prompt && text.VerticalScrollBarVisibility == ScrollBarVisibility.Auto, "提示词弹窗直接展示完整长文本，可选择复制并滚动");
        var buttons = Children(window).OfType<Button>().ToList();
        buttons.Single(b => Equals(b.Content, "复制全部提示词")).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
        check(copied == prompt && saved == null, "一键复制完整提示词，不先生成文件（注入剪贴板回调，不改真实剪贴板）");
        buttons.Single(b => Equals(b.Content, "另存为文本（可选）")).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
        check(saved == prompt, "另存为是独立可选操作，文本与复制内容一致");
        var failing = CreateProtocolPromptWindow(prompt, _ => throw new System.Runtime.InteropServices.ExternalException("clipboard busy"));
        Children(failing).OfType<Button>().Single(b => Equals(b.Content, "复制全部提示词")).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
        check(Children(failing).OfType<TextBlock>().Any(t => t.Text.Contains("复制失败")), "剪贴板被占用时弹窗反馈并保留可手动复制的内容");
        var visual = (FrameworkElement)window.Content; window.Content = null;
        var surface = new Border { Background = Background, Child = visual }; window.Content = surface;
        foreach (var (width, height, scale) in new[] { (860, 680, 1.0), (620, 420, 1.5) }) {
            surface.Width = width; surface.Height = height; surface.Measure(new Size(width, height)); surface.Arrange(new Rect(0, 0, width, height)); surface.UpdateLayout();
            check(buttons.All(b => b.TranslatePoint(new Point(), surface).Y + b.ActualHeight <= height - 16), $"提示词弹窗 {width} 宽度下复制、保存、关闭按钮完整可见");
            var bitmap = new RenderTargetBitmap((int)(width * scale), (int)(height * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
            var background = new DrawingVisual(); using (var drawing = background.RenderOpen()) drawing.DrawRectangle(Background, null, new Rect(0, 0, width, height));
            bitmap.Render(background); bitmap.Render(surface);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var output = File.Create(Path.Combine(directory, $"protocol-prompt-{width}.png")); encoder.Save(output);
        }
        check(!window.IsVisible && !failing.IsVisible, "提示词弹窗验收仅离屏渲染，不抢焦点");
    }
}
