using Microsoft.Win32;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace MonitorInspector;

public partial class MainWindow : Window
{
    private List<MonitorData> monitors = [];
    private CancellationTokenSource? scan;
    private readonly string[] args;
    private readonly ReadSession memory = new();
    private MonitorData? Selected => MonitorPicker.SelectedItem as MonitorData;
    public MainWindow(string[] arguments)
    {
        args = arguments; InitializeComponent();
        Title = "显示器读取器 · " + typeof(MainWindow).Assembly.GetName().Version?.ToString(3);
        Loaded += async (_, _) =>
        {
            await Refresh();
            int capture = Array.IndexOf(args, "--capture");
            if (capture >= 0 && capture + 1 < args.Length)
            {
                if (args.Contains("--small")) { Width = 820; Height = 580; }
                int snapshot = Array.IndexOf(args, "--snapshot");
                if (snapshot >= 0 && snapshot + 1 < args.Length)
                {
                    var saved = JsonSerializer.Deserialize<MonitorData>(File.ReadAllText(args[snapshot + 1]));
                    if (saved != null) { monitors = [saved]; MonitorPicker.ItemsSource = monitors; MonitorPicker.SelectedIndex = 0; Tabs.SelectedIndex = 1; Status.Text = "验证截图：已保存的实机读取结果"; }
                }
                await Task.Delay(300); UpdateLayout();
                var bitmap = new RenderTargetBitmap((int)ActualWidth, (int)ActualHeight, 96, 96, PixelFormats.Pbgra32);
                var background = new DrawingVisual();
                using (var drawing = background.RenderOpen()) drawing.DrawRectangle(Background, null, new Rect(0, 0, ActualWidth, ActualHeight));
                bitmap.Render(background); bitmap.Render(this);
                var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using var file = File.Create(args[capture + 1]); encoder.Save(file);
                Close();
            }
        };
        Closed += (_, _) => scan?.Cancel();
    }
    private async Task Refresh()
    {
        Busy(true, false); Status.Text = "正在识别活动显示器（不读取 DDC）…";
        try
        {
            string? prior = Selected?.DevicePath;
            monitors = await Task.Run(Native.Enumerate);
            Program.Log?.Write("monitors_detected", monitors);
            foreach (var monitor in monitors) monitor.Changes = memory.History(monitor.DevicePath);
            MonitorPicker.ItemsSource = monitors;
            MonitorPicker.SelectedItem = monitors.FirstOrDefault(m => m.DevicePath == prior) ?? monitors.FirstOrDefault();
            Status.Text = monitors.Count > 0 ? $"已识别 {monitors.Count} 台显示器。操作后再点读取，自动比较。日志：{Program.Log?.FilePath ?? "截图验证模式，不写日志"}" : "没有活动显示器。远程会话或驱动可能限制枚举。";
        }
        catch (Exception ex) { Program.Log?.Write("detection_failed", new { error = ex.ToString() }); Status.Text = "识别失败：" + ex.Message; }
        finally { Busy(false, false); ShowSelected(); }
    }
    private void Busy(bool value, bool scanning)
    {
        RefreshButton.IsEnabled = !value; MonitorPicker.IsEnabled = !value;
        ScanButton.IsEnabled = !value && Selected != null; SaveButton.IsEnabled = !value && Selected != null;
        Operation.IsEnabled = !value; CancelButton.IsEnabled = value && scanning;
    }
    private void ShowSelected()
    {
        var m = Selected;
        if (m is null) { Info.Text = "未选择显示器"; Features.ItemsSource = null; Raw.Text = ""; return; }
        Diff.Text = memory.Describe(m);
        var e = m.Edid;
        Info.Text = $"型号：{m.Name}\nWindows 显示器：{m.Device}\n显卡：{m.Adapter}\n连接：{m.Connection}\n当前桌面分辨率：{m.Resolution}\n当前刷新率：{m.RefreshRate}\n系统 HDR：{m.Hdr}\n颜色输出：{m.Color}\n\n制造商代码：{e.Manufacturer}    产品代码：{e.Product}\nEDID 型号：{(e.Name.Length > 0 ? e.Name : "未报告")}\n序列号：{e.Serial}\n生产时间：{e.ManufactureDate}\n尺寸：{e.Size}\nHDR 元数据：{e.HdrMetadata}\nEDID 校验：{e.Validation}\n\n显示器功能：{m.Ddc?.Status ?? "尚未读取；点击上方“读取并比较”"}\n\n" + string.Join("\n", m.Notes.Concat(m.Ddc?.Notes ?? [])) + "\n\n说明：系统 HDR 状态不是显示器 HDR 预设。面板类型、MiniLED 分区数、HDR1000 认证及未开放的菜单功能，不能仅凭这些接口可靠识别。";
        Features.ItemsSource = m.Ddc?.Features;
        Raw.Text = $"设备路径：{m.DevicePath}\n\n读取时间：{m.Ddc?.ReadAt.ToString("O") ?? "尚未读取 DDC"}\n\n能力字符串：\n{string.Join("\n\n", m.Ddc?.Capabilities ?? [])}\n\nEDID（十六进制）：\n{e.RawHex}\n\n完整记录：\n{JsonSerializer.Serialize(m, Program.Json)}";
    }
    private async void RefreshClick(object sender, RoutedEventArgs e) => await Refresh();
    private void SelectionChanged(object sender, SelectionChangedEventArgs e) { if (Info == null) return; ShowSelected(); Busy(false, false); }
    private async void ScanClick(object sender, RoutedEventArgs e)
    {
        var m = Selected; if (m is null || scan is not null) return;
        scan = new CancellationTokenSource(); Busy(true, true);
        Program.Log?.Write("read_started", new { device = m.DevicePath, operation = Operation.Text });
        Status.Text = $"正在读取 {m.Name} 的公开控制项；不修改设置。最多 60 秒，可以停止。";
        try
        {
            var result = await Program.Scan(m, scan.Token);
            string summary = memory.Observe(m, result, Operation.Text);
            Program.Log?.Write("read_completed", new { monitor = m, summary });
            if (Program.Log != null)
            {
                string observation = ObservationStore.Save(Path.Combine(AppContext.BaseDirectory, "observations"), m);
                Program.Log.Write("observation_saved", new { path = observation });
            }
            ShowSelected(); Tabs.SelectedIndex = result.Features.Any(x => x.Changed) ? 3 : 1;
            Status.Text = summary;
        }
        catch (Exception ex) { Program.Log?.Write("read_failed", new { device = m.DevicePath, error = ex.ToString() }); Status.Text = "读取失败：" + ex.Message; }
        finally { scan.Dispose(); scan = null; Busy(false, false); }
    }
    private void CancelClick(object sender, RoutedEventArgs e) { scan?.Cancel(); CancelButton.IsEnabled = false; }
    private void SaveClick(object sender, RoutedEventArgs e)
    {
        var m = Selected; if (m is null) return;
        var dialog = new SaveFileDialog { Filter = "显示器读取记录 (*.json)|*.json", FileName = $"monitor-{DateTime.Now:yyyyMMdd-HHmmss}.json" };
        if (dialog.ShowDialog(this) != true) return;
        try { File.WriteAllText(dialog.FileName, JsonSerializer.Serialize(m, Program.Json), new UTF8Encoding(false)); Status.Text = "信息及变化记录已导出：" + dialog.FileName; }
        catch (Exception ex) { Status.Text = "保存失败：" + ex.Message; }
    }
}
