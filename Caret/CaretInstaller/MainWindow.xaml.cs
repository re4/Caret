using System.Diagnostics;
using System.IO;
using System.Windows;

namespace CaretInstaller;

public partial class MainWindow : Window
{
    private enum Page { Welcome, Options, Installing, Complete, Uninstall, UninstallProgress, UninstallComplete }

    private Page _currentPage;
    private readonly bool _isUninstallMode;

    public MainWindow()
    {
        InitializeComponent();

        var args = Environment.GetCommandLineArgs();
        _isUninstallMode = args.Any(a => a.Equals("/uninstall", StringComparison.OrdinalIgnoreCase));

        InstallPathTextBox.Text = InstallerEngine.DefaultInstallPath;
        VersionLabel.Text = $"Version {InstallerEngine.Version}";

        if (_isUninstallMode)
        {
            Title = "Uninstall Caret";
            ShowPage(Page.Uninstall);
        }
        else
        {
            ShowPage(Page.Welcome);
        }
    }

    private void ShowPage(Page page)
    {
        _currentPage = page;

        WelcomePage.Visibility = Visibility.Collapsed;
        OptionsPage.Visibility = Visibility.Collapsed;
        InstallingPage.Visibility = Visibility.Collapsed;
        CompletePage.Visibility = Visibility.Collapsed;
        UninstallPage.Visibility = Visibility.Collapsed;
        UninstallProgressPage.Visibility = Visibility.Collapsed;
        UninstallCompletePage.Visibility = Visibility.Collapsed;

        switch (page)
        {
            case Page.Welcome:
                WelcomePage.Visibility = Visibility.Visible;
                BackButton.Visibility = Visibility.Collapsed;
                NextButton.Content = "Next ›";
                NextButton.Visibility = Visibility.Visible;
                CancelButton.Visibility = Visibility.Visible;
                break;

            case Page.Options:
                OptionsPage.Visibility = Visibility.Visible;
                BackButton.Visibility = Visibility.Visible;
                NextButton.Content = "Install";
                NextButton.Visibility = Visibility.Visible;
                CancelButton.Visibility = Visibility.Visible;
                break;

            case Page.Installing:
                InstallingPage.Visibility = Visibility.Visible;
                BackButton.Visibility = Visibility.Collapsed;
                NextButton.Visibility = Visibility.Collapsed;
                CancelButton.Visibility = Visibility.Collapsed;
                break;

            case Page.Complete:
                CompletePage.Visibility = Visibility.Visible;
                BackButton.Visibility = Visibility.Collapsed;
                NextButton.Content = "Finish";
                NextButton.Visibility = Visibility.Visible;
                CancelButton.Visibility = Visibility.Collapsed;
                break;

            case Page.Uninstall:
                UninstallPage.Visibility = Visibility.Visible;
                BackButton.Visibility = Visibility.Collapsed;
                NextButton.Content = "Uninstall";
                NextButton.Visibility = Visibility.Visible;
                CancelButton.Visibility = Visibility.Visible;
                break;

            case Page.UninstallProgress:
                UninstallProgressPage.Visibility = Visibility.Visible;
                BackButton.Visibility = Visibility.Collapsed;
                NextButton.Visibility = Visibility.Collapsed;
                CancelButton.Visibility = Visibility.Collapsed;
                break;

            case Page.UninstallComplete:
                UninstallCompletePage.Visibility = Visibility.Visible;
                BackButton.Visibility = Visibility.Collapsed;
                NextButton.Content = "Close";
                NextButton.Visibility = Visibility.Visible;
                CancelButton.Visibility = Visibility.Collapsed;
                break;
        }
    }

    private void BackButton_Click(object sender, RoutedEventArgs e)
    {
        if (_currentPage == Page.Options)
            ShowPage(Page.Welcome);
    }

    private async void NextButton_Click(object sender, RoutedEventArgs e)
    {
        switch (_currentPage)
        {
            case Page.Welcome:
                ShowPage(Page.Options);
                break;

            case Page.Options:
                await StartInstallAsync();
                break;

            case Page.Complete:
                if (LaunchCheckBox.IsChecked == true)
                {
                    string exePath = Path.Combine(InstallPathTextBox.Text, InstallerEngine.AppExeName);
                    if (File.Exists(exePath))
                        Process.Start(new ProcessStartInfo(exePath) { UseShellExecute = true });
                }
                Close();
                break;

            case Page.Uninstall:
                await StartUninstallAsync();
                break;

            case Page.UninstallComplete:
                Close();
                break;
        }
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void BrowseButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "Select Installation Folder"
        };

        if (Directory.Exists(InstallPathTextBox.Text))
            dialog.InitialDirectory = InstallPathTextBox.Text;

        if (dialog.ShowDialog() == true)
            InstallPathTextBox.Text = Path.Combine(dialog.FolderName, InstallerEngine.AppName);
    }

    private async Task StartInstallAsync()
    {
        string installPath = InstallPathTextBox.Text.Trim();

        if (string.IsNullOrEmpty(installPath))
        {
            MessageBox.Show("Please specify an installation path.", "Caret Setup",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        bool desktopIcon = DesktopIconCheckBox.IsChecked == true;
        bool contextMenu = ContextMenuCheckBox.IsChecked == true;

        ShowPage(Page.Installing);

        var progress = new Progress<(int percent, string status)>(update =>
        {
            InstallProgressBar.Value = update.percent;
            InstallStatusText.Text = update.status;
            InstallPercentText.Text = $"{update.percent}%";
        });

        try
        {
            await InstallerEngine.InstallAsync(installPath, desktopIcon, contextMenu, progress, CancellationToken.None);
            ShowPage(Page.Complete);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Installation failed:\n\n{ex.Message}", "Caret Setup",
                MessageBoxButton.OK, MessageBoxImage.Error);
            Close();
        }
    }

    private async Task StartUninstallAsync()
    {
        ShowPage(Page.UninstallProgress);

        var progress = new Progress<(int percent, string status)>(update =>
        {
            UninstallProgressBar.Value = update.percent;
            UninstallStatusText.Text = update.status;
            UninstallPercentText.Text = $"{update.percent}%";
        });

        try
        {
            string installPath = InstallerEngine.GetInstalledPath() ?? InstallerEngine.DefaultInstallPath;
            await InstallerEngine.UninstallAsync(installPath, progress, CancellationToken.None);
            ShowPage(Page.UninstallComplete);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Uninstallation failed:\n\n{ex.Message}", "Caret Setup",
                MessageBoxButton.OK, MessageBoxImage.Error);
            Close();
        }
    }
}
