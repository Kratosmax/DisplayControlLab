namespace DisplayControl.Controller;
internal static class Program
{
    [STAThread] public static int Main(string[] args) => DisplayControl.UI.ToolHost.Run(false, args);
}
