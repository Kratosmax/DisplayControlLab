namespace MonitorInspector;
internal static class Program
{
    [STAThread] public static int Main(string[] args) => DisplayControl.UI.ToolHost.Run(true, args);
}
