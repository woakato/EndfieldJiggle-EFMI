using System.IO;
using System.Windows;

namespace EndfieldJiggle.Launcher;

public partial class App : Application
{
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        MainWindow window = new();
        if (e.Args is ["--smoke-test", string output])
        {
            try
            {
                await window.RunSmokeAsync(output);
                Shutdown(0);
            }
            catch (Exception error)
            {
                Directory.CreateDirectory(output);
                File.WriteAllText(Path.Combine(output, "smoke-error.txt"), error.ToString());
                Shutdown(1);
            }
            return;
        }
        MainWindow = window;
        window.Show();
        await window.InitializeAsync(e.Args.Contains("--launch", StringComparer.Ordinal),
            e.Args.Contains("--elevated-retry", StringComparer.Ordinal));
    }
}
