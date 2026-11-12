using Avalonia;

namespace White2UpgradeEditor;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // Offline diagnostics: useful for bug reports and release verification.
        // These modes never launch the GUI or modify the original input.
        if (args.Length == 2 && args[0] == "--type-chart-inspect")
        {
            var result = TypeChartEngine.Inspect(args[1], Directory.Exists(args[1]));
            Console.WriteLine(result.Detail);
            Console.WriteLine($"White 2 Upgrade verified: {result.White2UpgradeVerified}; chart: {result.Dimensions}×{result.Dimensions}");
            Environment.ExitCode = result.White2UpgradeVerified ? 0 : 2;
            return;
        }
        if (args.Length == 3 && args[0] == "--type-chart-repair")
        {
            TypeChartEngine.RepairToNewCopy(args[1], args[2], Directory.Exists(args[1]));
            Console.WriteLine("Created and verified repaired copy: " + args[2]);
            return;
        }
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>()
        .UsePlatformDetect().WithInterFont().LogToTrace();
}
