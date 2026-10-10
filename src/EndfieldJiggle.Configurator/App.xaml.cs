using System.Windows;
using System.IO;

namespace EndfieldJiggle.Configurator;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        MainWindow window = new();
        if (e.Args is ["--smoke-test", string output])
        {
            try
            {
                window.RunIsolatedSmoke(output);
                Shutdown(0);
            }
            catch (Exception error)
            {
                string path = Path.Combine(Path.GetFullPath(output), "smoke-error.txt");
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, error.ToString());
                Shutdown(1);
            }
            return;
        }
        MainWindow = window;
        window.Show();
    }
}
