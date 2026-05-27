using Microsoft.UI.Xaml;
using System.Text;

namespace AiBotApp;

public partial class App : Application
{
    private Window? _window;

    public App()
    {
        InitializeComponent();
        RequestedTheme = ApplicationTheme.Light;
        UnhandledException += App_UnhandledException;
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        try
        {
            _window = new MainWindow();
            _window.Activate();
        }
        catch (Exception ex)
        {
            WriteStartupError(ex);
            throw;
        }
    }

    private static void App_UnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        WriteStartupError(e.Exception);
    }

    private static void WriteStartupError(Exception ex)
    {
        var text = new StringBuilder()
            .AppendLine(DateTime.Now.ToString("O"))
            .AppendLine(ex.ToString())
            .ToString();

        File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "startup-error.log"), text);
    }
}
