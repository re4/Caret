using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;

namespace CaretInstaller;

internal static class InstallerEngine
{
    public const string AppName = "Caret";
    public const string AppExeName = "Caret.exe";
    public const string UninstallerExeName = "uninstall.exe";

    private const string UninstallRegistryKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Caret";
    private const string ContextMenuFileKey = @"*\shell\Caret";
    private const string ContextMenuDirKey = @"Directory\Background\shell\Caret";

    public static string DefaultInstallPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), AppName);

    public static string Version
    {
        get
        {
            var ver = Assembly.GetExecutingAssembly().GetName().Version;
            return ver != null ? $"{ver.Major}.{ver.Minor}.{ver.Build}" : "1.0.0";
        }
    }

    public static string? GetInstalledPath()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(UninstallRegistryKey);
            return key?.GetValue("InstallLocation") as string;
        }
        catch { return null; }
    }

    public static async Task InstallAsync(
        string installPath,
        bool createDesktopIcon,
        bool addContextMenu,
        IProgress<(int percent, string status)> progress,
        CancellationToken ct)
    {
        await Task.Run(() =>
        {
            progress.Report((5, "Creating installation directory..."));
            Directory.CreateDirectory(installPath);
            ct.ThrowIfCancellationRequested();

            progress.Report((10, "Extracting application files..."));
            ExtractPayload(installPath);
            ct.ThrowIfCancellationRequested();

            progress.Report((40, "Creating Start Menu shortcut..."));
            CreateStartMenuShortcut(installPath);
            ct.ThrowIfCancellationRequested();

            if (createDesktopIcon)
            {
                progress.Report((50, "Creating desktop shortcut..."));
                CreateDesktopShortcut(installPath);
                ct.ThrowIfCancellationRequested();
            }

            if (addContextMenu)
            {
                progress.Report((60, "Adding context menu entries..."));
                AddContextMenuEntries(installPath);
                ct.ThrowIfCancellationRequested();
            }

            progress.Report((75, "Copying uninstaller..."));
            CopyUninstaller(installPath);
            ct.ThrowIfCancellationRequested();

            progress.Report((85, "Registering application..."));
            RegisterUninstaller(installPath);
            ct.ThrowIfCancellationRequested();

            Thread.Sleep(200);
            progress.Report((100, "Installation complete!"));
        }, ct);
    }

    public static async Task UninstallAsync(
        string installPath,
        IProgress<(int percent, string status)> progress,
        CancellationToken ct)
    {
        await Task.Run(() =>
        {
            progress.Report((10, "Removing context menu entries..."));
            RemoveContextMenuEntries();
            ct.ThrowIfCancellationRequested();

            progress.Report((25, "Removing Start Menu shortcut..."));
            RemoveStartMenuShortcut();
            ct.ThrowIfCancellationRequested();

            progress.Report((40, "Removing desktop shortcut..."));
            RemoveDesktopShortcut();
            ct.ThrowIfCancellationRequested();

            progress.Report((55, "Removing registry entries..."));
            RemoveUninstallRegistration();
            ct.ThrowIfCancellationRequested();

            progress.Report((70, "Removing installed files..."));
            RemoveInstalledFiles(installPath);
            ct.ThrowIfCancellationRequested();

            progress.Report((90, "Cleaning up..."));
            ScheduleSelfDeletion(installPath);

            Thread.Sleep(200);
            progress.Report((100, "Uninstallation complete!"));
        }, ct);
    }

    // ── Payload ─────────────────────────────────────────────────────────

    private static void ExtractPayload(string installPath)
    {
        var assembly = Assembly.GetExecutingAssembly();
        string? resourceName = assembly.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith("Payload.zip", StringComparison.OrdinalIgnoreCase));

        if (resourceName == null)
            throw new InvalidOperationException(
                "Installation payload not found. The installer may be corrupted.");

        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException("Could not read installation payload.");
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);

        foreach (var entry in archive.Entries)
        {
            if (string.IsNullOrEmpty(entry.Name))
                continue;

            string destPath = Path.Combine(installPath, entry.FullName);
            string? destDir = Path.GetDirectoryName(destPath);
            if (destDir != null)
                Directory.CreateDirectory(destDir);

            entry.ExtractToFile(destPath, overwrite: true);
        }
    }

    // ── Shortcuts ───────────────────────────────────────────────────────

    private static void CreateStartMenuShortcut(string installPath)
    {
        string startMenuDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu),
            "Programs");

        CreateShortcut(
            Path.Combine(startMenuDir, $"{AppName}.lnk"),
            Path.Combine(installPath, AppExeName),
            installPath,
            Path.Combine(installPath, "App.ico"),
            AppName);
    }

    private static void CreateDesktopShortcut(string installPath)
    {
        CreateShortcut(
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory),
                $"{AppName}.lnk"),
            Path.Combine(installPath, AppExeName),
            installPath,
            Path.Combine(installPath, "App.ico"),
            AppName);
    }

    private static void RemoveStartMenuShortcut()
    {
        TryDeleteFile(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu),
            "Programs", $"{AppName}.lnk"));
    }

    private static void RemoveDesktopShortcut()
    {
        TryDeleteFile(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory),
            $"{AppName}.lnk"));
    }

    private static void CreateShortcut(
        string shortcutPath, string targetPath, string workingDir,
        string iconPath, string description)
    {
        var link = (IShellLinkW)new ShellLink();
        link.SetPath(targetPath);
        link.SetWorkingDirectory(workingDir);
        link.SetDescription(description);

        if (File.Exists(iconPath))
            link.SetIconLocation(iconPath, 0);

        var file = (IPersistFile)link;
        file.Save(shortcutPath, false);

        Marshal.ReleaseComObject(link);
    }

    // ── Context Menu ────────────────────────────────────────────────────

    private static void AddContextMenuEntries(string installPath)
    {
        string exePath = Path.Combine(installPath, AppExeName);
        string iconPath = Path.Combine(installPath, "App.ico");

        // "Edit with Caret" on any file
        using (var key = Registry.ClassesRoot.CreateSubKey(ContextMenuFileKey))
        {
            key.SetValue("", $"Edit with {AppName}");
            key.SetValue("Icon", $"\"{iconPath}\"");
        }
        using (var key = Registry.ClassesRoot.CreateSubKey($@"{ContextMenuFileKey}\command"))
        {
            key.SetValue("", $"\"{exePath}\" \"%1\"");
        }

        // "Open Caret" on directory background
        using (var key = Registry.ClassesRoot.CreateSubKey(ContextMenuDirKey))
        {
            key.SetValue("", $"Open {AppName}");
            key.SetValue("Icon", $"\"{iconPath}\"");
        }
        using (var key = Registry.ClassesRoot.CreateSubKey($@"{ContextMenuDirKey}\command"))
        {
            key.SetValue("", $"\"{exePath}\"");
        }
    }

    private static void RemoveContextMenuEntries()
    {
        TryDeleteRegistryTree(Registry.ClassesRoot, ContextMenuFileKey);
        TryDeleteRegistryTree(Registry.ClassesRoot, ContextMenuDirKey);
    }

    // ── Add/Remove Programs Registration ────────────────────────────────

    private static void RegisterUninstaller(string installPath)
    {
        string uninstallExe = Path.Combine(installPath, UninstallerExeName);
        string iconPath = Path.Combine(installPath, "App.ico");

        long totalSizeKb = 0;
        try
        {
            foreach (var f in Directory.GetFiles(installPath, "*", SearchOption.AllDirectories))
                totalSizeKb += new FileInfo(f).Length;
            totalSizeKb /= 1024;
        }
        catch { }

        using var key = Registry.LocalMachine.CreateSubKey(UninstallRegistryKey);
        key.SetValue("DisplayName", AppName);
        key.SetValue("DisplayVersion", Version);
        key.SetValue("Publisher", AppName);
        key.SetValue("UninstallString", $"\"{uninstallExe}\" /uninstall");
        key.SetValue("QuietUninstallString", $"\"{uninstallExe}\" /uninstall /quiet");
        key.SetValue("DisplayIcon", iconPath);
        key.SetValue("InstallLocation", installPath);
        key.SetValue("NoModify", 1, RegistryValueKind.DWord);
        key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
        key.SetValue("EstimatedSize", (int)totalSizeKb, RegistryValueKind.DWord);
    }

    private static void RemoveUninstallRegistration()
    {
        TryDeleteRegistryTree(Registry.LocalMachine, UninstallRegistryKey);
    }

    private static void CopyUninstaller(string installPath)
    {
        string? sourcePath = Environment.ProcessPath;
        if (string.IsNullOrEmpty(sourcePath))
            throw new InvalidOperationException("Could not determine installer executable path.");

        string destPath = Path.Combine(installPath, UninstallerExeName);

        if (!string.Equals(
                Path.GetFullPath(sourcePath),
                Path.GetFullPath(destPath),
                StringComparison.OrdinalIgnoreCase))
        {
            File.Copy(sourcePath, destPath, overwrite: true);
        }
    }

    // ── Uninstall Helpers ───────────────────────────────────────────────

    private static void RemoveInstalledFiles(string installPath)
    {
        if (!Directory.Exists(installPath))
            return;

        string currentExe = Environment.ProcessPath ?? "";

        foreach (var file in Directory.GetFiles(installPath))
        {
            if (string.Equals(
                    Path.GetFullPath(file),
                    Path.GetFullPath(currentExe),
                    StringComparison.OrdinalIgnoreCase))
                continue;

            TryDeleteFile(file);
        }
    }

    private static void ScheduleSelfDeletion(string installPath)
    {
        string currentExe = Environment.ProcessPath ?? "";
        if (string.IsNullOrEmpty(currentExe))
            return;

        string script = $"/c timeout /t 2 /nobreak >nul & del /f /q \"{currentExe}\" & rmdir /s /q \"{installPath}\"";

        Process.Start(new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = script,
            WindowStyle = ProcessWindowStyle.Hidden,
            CreateNoWindow = true,
            UseShellExecute = false
        });
    }

    // ── Utilities ───────────────────────────────────────────────────────

    private static void TryDeleteFile(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch { }
    }

    private static void TryDeleteRegistryTree(RegistryKey root, string subkey)
    {
        try { root.DeleteSubKeyTree(subkey, throwOnMissingSubKey: false); }
        catch { }
    }

    // ── COM Interop for .lnk Shortcut Creation ─────────────────────────
    // Uses Windows Shell COM objects directly — no third-party dependency.

    [ComImport, Guid("00021401-0000-0000-C000-000000000046")]
    private class ShellLink { }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("000214F9-0000-0000-C000-000000000046")]
    private interface IShellLinkW
    {
        void GetPath(
            [Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszFile,
            int cch, IntPtr pfd, uint fFlags);
        void GetIDList(out IntPtr ppidl);
        void SetIDList(IntPtr pidl);
        void GetDescription(
            [Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszName, int cch);
        void SetDescription(
            [MarshalAs(UnmanagedType.LPWStr)] string pszName);
        void GetWorkingDirectory(
            [Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszDir, int cch);
        void SetWorkingDirectory(
            [MarshalAs(UnmanagedType.LPWStr)] string pszDir);
        void GetArguments(
            [Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszArgs, int cch);
        void SetArguments(
            [MarshalAs(UnmanagedType.LPWStr)] string pszArgs);
        void GetHotkey(out ushort pwHotkey);
        void SetHotkey(ushort wHotkey);
        void GetShowCmd(out int piShowCmd);
        void SetShowCmd(int iShowCmd);
        void GetIconLocation(
            [Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszIconPath,
            int cch, out int piIcon);
        void SetIconLocation(
            [MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);
        void SetRelativePath(
            [MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, uint dwReserved);
        void Resolve(IntPtr hwnd, uint fFlags);
        void SetPath(
            [MarshalAs(UnmanagedType.LPWStr)] string pszFile);
    }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("0000010b-0000-0000-C000-000000000046")]
    private interface IPersistFile
    {
        void GetClassID(out Guid pClassID);
        [PreserveSig]
        int IsDirty();
        void Load(
            [MarshalAs(UnmanagedType.LPWStr)] string pszFileName, uint dwMode);
        void Save(
            [MarshalAs(UnmanagedType.LPWStr)] string pszFileName,
            [MarshalAs(UnmanagedType.Bool)] bool fRemember);
        void SaveCompleted(
            [MarshalAs(UnmanagedType.LPWStr)] string pszFileName);
        void GetCurFile(
            [MarshalAs(UnmanagedType.LPWStr)] out string ppszFileName);
    }
}
