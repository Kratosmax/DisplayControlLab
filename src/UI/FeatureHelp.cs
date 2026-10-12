using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using MonitorInspector;

namespace DisplayControl.UI;

public static class FeatureHelp
{
    public static string For(ControlDefinition feature) => !string.IsNullOrWhiteSpace(feature.HelpText) ? feature.HelpText : feature.Id switch {
        "brightness" => "调节画面整体明亮程度。白天环境较亮时可调高，夜间可调低，以看得清且不刺眼为准。某些 HDR 或画面模式会接管亮度。",
        "contrast" => "调节亮暗之间的差距。调得过高可能让亮部细节变成一片白，过低则显得灰淡；通常先保留默认值，再按画面微调。",
        "gamma" => "伽马主要改变中间亮度区域的明暗。一般数值较低时暗部更亮，较高时画面更深；2.2 是常见起点。它与单纯提高背光亮度不同。",
        "eco-preset" => "一组预设的画面或节能方案，可能同时调整亮度、对比度等。按阅读、电影或游戏场景选择；切回标准模式便于自己调节。均匀亮补偿通常用于改善屏幕各区域亮度的一致性。",
        "dynamic-contrast" => "DCR（动态对比度）会根据画面明暗自动调整亮度或背光，以增强亮暗对比。切换场景时可能感觉亮度忽明忽暗；想保持稳定亮度可关闭，看电影时可按观感尝试开启。具体效果因型号而异。",
        "color-preset" => "调节白色偏暖还是偏冷。暖色偏黄、冷色偏蓝，6500K 是常见参考；用户自定义模式通常允许分别调节红、绿、蓝。色温与色彩空间不是同一个设置。",
        "red-gain" => "调节红色通道的增益，影响整体色彩平衡。红色太多会偏红，太少可能偏青；通常配合用户自定义色温及另外两个通道微调。",
        "green-gain" => "调节绿色通道的增益，影响整体色彩平衡。绿色太多会偏绿，太少可能偏紫；通常配合用户自定义色温及另外两个通道微调。",
        "blue-gain" => "调节蓝色通道的增益，影响整体色彩平衡。蓝色太多会显得偏冷，太少会偏黄；通常配合用户自定义色温及另外两个通道微调。",
        "hdr-preset" => "选择显示器对画面的 HDR 处理方式，如图片、电影或游戏。它与 Windows 的系统 HDR 开关是两个独立设置；部分显示器在 SDR 下也提供模拟效果。可选模式及其他功能是否锁定取决于当前信号和型号。",
        "overdrive" => "加快像素切换，减轻运动画面的拖影。强度过高可能出现亮边、反向残影；可从较低档开始，按刷新率和实际画面选择，并非越强越好。",
        "low-input-lag" => "尽量减少显示器内部图像处理带来的延迟，让操作更快反映到画面上。适合重视响应速度的游戏，但可能与部分画面处理功能互斥；它不会直接提高游戏帧率。",
        "game-color" => "调节游戏画面的色彩浓淡，通常类似饱和度。调高可能更鲜艳，过高则容易失真；各型号对数值的含义可能不同。",
        "shadow-control" => "调整暗部细节，让阴影里的物体更容易看清。增强过多可能让黑色发灰、画面层次变浅；具体调节方向以型号实现和实际效果为准。",
        "low-blue-light" => "通过改变画面色彩减少蓝光成分，画面通常会偏暖。适合按个人观感用于阅读，但会改变颜色，不适合需要准确判断色彩的工作。",
        "crosshair" => "在屏幕上叠加准星，方便在游戏中定位中心。它是显示器生成的图案，通常不会出现在电脑截图或录屏中。",
        "frame-counter" => "在屏幕角落显示显示器报告的帧率或刷新相关数值。不同型号的统计方式不同，不一定等于游戏内显示的 FPS。",
        "game-mode" or "display-preset" => "按游戏类型或使用场景套用一组画面设置，可能同时改变色彩、亮度和暗部处理。切换预设后，部分手动设置可能被覆盖或锁定。",
        "image-ratio" or "aspect-ratio" => "决定画面如何铺满屏幕。全屏可能拉伸比例，保持比例会保留黑边，1:1 通常按原始像素显示；可用选项取决于输入分辨率和型号。",
        "local-dimming" => "区域调光将背光分区控制，让暗处更黑、亮处更亮。强度较高可能增加明暗交界的光晕，字幕或鼠标附近尤其容易看到；可按电影、游戏或桌面使用效果选择。",
        "color-space" => "选择显示器使用的色彩范围。sRGB 常用于网页和普通 SDR 内容；标准或原生模式可能提供更广色域，但未做色彩管理的内容也可能显得过艳。它与色温不同。",
        "adaptive-sync" => "让显示器刷新节奏跟随显卡输出，减少画面撕裂和卡顿感。通常还需要显卡驱动和游戏配合，并受显示器支持的刷新范围限制；不会凭空提高帧率。",
        "volume" => "调节显示器的音频输出音量。是否影响内置扬声器、耳机口或其他输出取决于型号；与 Windows 的系统音量可能分别控制。",
        "mute" => "开关显示器音频输出的静音状态。它与 Windows 静音可能是两个独立设置。",
        "input-source" => "选择显示器接收画面的接口，如 DisplayPort 或 HDMI。切换后当前电脑的画面及控制连接可能暂时消失。",
        "power" => "显示器报告的开机、待机或关闭状态。它不是电脑电源状态；关闭显示器后，软件可能无法通过同一连接将它重新唤醒。",
        "osd" => "OSD 是显示器自己的屏幕菜单。此项反映菜单是否允许使用，具体锁定行为由型号决定。",
        "osd-language" => "设置显示器屏幕菜单的语言，不会改变 Windows 或本软件的语言。",
        "mccs-version" => "显示器报告的控制协议版本，主要用于诊断和适配。版本号不代表它支持该版本中的所有功能。",
        _ => "由适配包提供的自定义功能。具体作用与使用建议请以该包作者或显示器说明书为准；作者可在读取器中补充帮助说明。"
    };
}

public partial class WorkspaceWindow
{
    readonly HashSet<string> expandedFeatureHelp = [];

    StackPanel FeatureSection(ControlDefinition feature, bool compact = false)
    {
        var panel = new StackPanel();
        var heading = new Grid { Margin = new Thickness(0, 0, 0, 10) };
        heading.ColumnDefinitions.Add(new()); heading.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        var title = Text(feature.Name, 19); title.Margin = new Thickness(0, 0, 12, 0); heading.Children.Add(title);
        string explanation = FeatureHelp.For(feature);
        var text = Text(explanation, 13, "#465671"); text.Margin = new Thickness(0);
        var details = new Border { Child = text, Background = Brush("#F3F6FD"), CornerRadius = new CornerRadius(6), Padding = new Thickness(12), Margin = new Thickness(0, 0, 0, 12), Visibility = expandedFeatureHelp.Contains(feature.Id) ? Visibility.Visible : Visibility.Collapsed };
        var help = new Button { Content = compact ? "?" : expandedFeatureHelp.Contains(feature.Id) ? "收起说明" : "? 说明" };
        help.MinHeight = 28; help.Padding = new Thickness(8, 3, 8, 3); help.FontSize = 12; help.Margin = new Thickness(0); help.Foreground = Brush("#3D5DD8");
        help.ToolTip = new ToolTip { Content = Text(explanation, 13), MaxWidth = 360, Background = Brush("#F3F6FD"), BorderBrush = Brush("#DDE4F0"), Padding = new Thickness(12) };
        AutomationProperties.SetName(help, feature.Name + "：展开或收起功能说明");
        help.Click += (_, _) => {
            bool expand = details.Visibility != Visibility.Visible;
            details.Visibility = expand ? Visibility.Visible : Visibility.Collapsed;
            help.Content = compact ? "?" : expand ? "收起说明" : "? 说明";
            if (expand) expandedFeatureHelp.Add(feature.Id); else expandedFeatureHelp.Remove(feature.Id);
        };
        Grid.SetColumn(help, 1); heading.Children.Add(help); panel.Children.Add(heading); panel.Children.Add(details);
        return panel;
    }
}
