using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using XamlApplication = Microsoft.UI.Xaml.Application;

namespace LoadCursor;

public partial class App : XamlApplication
{
    readonly string[] arguments;
    MonitorController? controller;
    Microsoft.UI.Xaml.Window? testLifetime;
    internal int ExitCode { get; private set; }

    internal App(string[] arguments)
    {
        this.arguments = arguments;
        InitializeComponent();
        HighContrastAdjustment = ApplicationHighContrastAdjustment.None;
        UnhandledException += (_, args) =>
        {
            args.Handled = true;
            Console.Error.WriteLine($"XAML error (0x{args.Exception.HResult:X8}): {args.Message}");
            Fail(args.Exception);
        };
    }

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        try
        {
            if (arguments is ["--self-test"])
            {
                testLifetime = new Microsoft.UI.Xaml.Window();
                await SelfTest.RunAsync();
                Shutdown();
            }
            else if (arguments is ["--demo"])
            {
                using var cursor = new SystemCursor();
                SelfTest.Demo(cursor);
                Shutdown();
            }
            else
            {
                var settings = arguments is ["--benchmark"] ?
                    new AppSettings { PauseInFullscreen = false, PauseInEnergySaver = false } :
                    AppSettings.Load(AppSettings.FilePath);
                controller = new MonitorController(settings, Shutdown, persistSettings: arguments is not ["--benchmark"]);
                if (arguments is ["--settings"])
                    controller.ShowSettings();
                else if (arguments is ["--benchmark"])
                {
                    await controller.BenchmarkAsync();
                    Shutdown();
                }
            }
        }
        catch (Exception error) { Fail(error); }
    }

    internal void Shutdown()
    {
        try { controller?.Dispose(); }
        finally
        {
            controller = null;
            testLifetime?.Close();
            testLifetime = null;
            Exit();
        }
    }

    void Fail(Exception error)
    {
        ExitCode = 1;
        Console.Error.WriteLine(error);
        if (arguments.Length == 0 || arguments is ["--settings"])
            System.Windows.Forms.MessageBox.Show(error.Message, "PulseCursor", MessageBoxButtons.OK, MessageBoxIcon.Error);
        Shutdown();
    }

    internal static void Start(string[] arguments, out int exitCode)
    {
        App? app = null;
        WinRT.ComWrappersSupport.InitializeComWrappers();
        XamlApplication.Start(_ =>
        {
            SynchronizationContext.SetSynchronizationContext(
                new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread()));
            app = new App(arguments);
        });
        exitCode = app?.ExitCode ?? 1;
    }
}
