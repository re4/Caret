using System.Windows;

namespace CaretInstaller;

public partial class App : Application
{
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        bool isQuiet = e.Args.Any(a => a.Equals("/quiet", StringComparison.OrdinalIgnoreCase));
        bool isUninstall = e.Args.Any(a => a.Equals("/uninstall", StringComparison.OrdinalIgnoreCase));

        if (isQuiet && isUninstall)
        {
            try
            {
                string installPath = InstallerEngine.GetInstalledPath() ?? InstallerEngine.DefaultInstallPath;
                var progress = new Progress<(int percent, string status)>(_ => { });
                await InstallerEngine.UninstallAsync(installPath, progress, CancellationToken.None);
            }
            catch { }
            Shutdown();
            return;
        }

        new MainWindow().Show();
    }
}
