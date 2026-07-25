using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

[assembly: AssemblyTitle("Acer Battery Saver Setup")]
[assembly: AssemblyDescription("Per-user installer for Acer Battery Saver")]
[assembly: AssemblyCompany("KaiiW31")]
[assembly: AssemblyProduct("Acer Battery Saver")]
[assembly: AssemblyVersion("1.0.2.0")]
[assembly: AssemblyFileVersion("1.0.2.0")]

internal static class Installer {
    private const string AppName = "Acer Battery Saver";
    private const string AppId = "AcerBatterySaver";
    private const string Version = "1.0.2";
    private const string AppResource = "AcerBatterySaver.Payload.exe";
    private const string ConfigResource = "AcerBatterySaver.DefaultConfig.json";

    private static readonly string InstallDirectory =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Programs", AppName);
    private static readonly string AppPath = Path.Combine(InstallDirectory, "AcerBatterySaver.exe");
    private static readonly string UninstallerPath = Path.Combine(InstallDirectory, "Uninstall.exe");
    private static readonly string StartMenuDirectory =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), AppName);
    private static readonly string StartMenuShortcut = Path.Combine(StartMenuDirectory, AppName + ".lnk");
    private static readonly string UninstallKey =
        @"Software\Microsoft\Windows\CurrentVersion\Uninstall\" + AppId;

    [STAThread]
    private static int Main(string[] args) {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        try {
            if (args.Any(a => a.Equals("/verify", StringComparison.OrdinalIgnoreCase)))
                return VerifyPayload() ? 0 : 2;
            if (args.Any(a => a.Equals("/uninstall", StringComparison.OrdinalIgnoreCase)))
                return Uninstall();
            return Install();
        } catch (Exception ex) {
            MessageBox.Show(ex.Message, AppName + " Setup", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
        }
    }

    private static int Install() {
        var answer = MessageBox.Show(
            "Install " + AppName + " " + Version + " for this Windows account?\n\n" +
            "Location:\n" + InstallDirectory,
            AppName + " Setup", MessageBoxButtons.OKCancel, MessageBoxIcon.Information);
        if (answer != DialogResult.OK) return 0;

        string existingConfig = StopExistingAppsForInstall();
        Directory.CreateDirectory(InstallDirectory);

        string stagedApp = Path.Combine(InstallDirectory, "AcerBatterySaver.exe.new");
        ExtractResource(AppResource, stagedApp);
        if (File.Exists(AppPath)) File.Replace(stagedApp, AppPath, null);
        else File.Move(stagedApp, AppPath);

        string configPath = Path.Combine(InstallDirectory, "config.json");
        if (!File.Exists(configPath)) {
            if (!String.IsNullOrEmpty(existingConfig)) File.Copy(existingConfig, configPath);
            else ExtractResource(ConfigResource, configPath);
        }

        File.Copy(Application.ExecutablePath, UninstallerPath, true);
        CreateStartMenuShortcut();
        RegisterUninstaller();
        using (var run = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run"))
            run.SetValue(AppId, "\"" + AppPath + "\"");
        Process.Start(new ProcessStartInfo(AppPath) { WorkingDirectory = InstallDirectory });

        MessageBox.Show(
            AppName + " was installed successfully.\n\n" +
            "It is now running in the notification area and will start with Windows.",
            AppName + " Setup", MessageBoxButtons.OK, MessageBoxIcon.Information);
        return 0;
    }

    private static int Uninstall() {
        if (File.Exists(Path.Combine(InstallDirectory, "restore-state.json"))) {
            MessageBox.Show(
                "Battery Saver is currently active. Reconnect AC power or turn Battery Saver off " +
                "from its tray menu before uninstalling, so your original power plan and refresh rate are restored.",
                AppName + " Uninstall", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return 3;
        }

        if (MessageBox.Show(
            "Uninstall " + AppName + "?\n\nSettings and logs will also be removed.",
            AppName + " Uninstall", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            return 0;

        StopInstalledApp();
        using (var run = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run"))
            run.DeleteValue(AppId, false);
        Registry.CurrentUser.DeleteSubKeyTree(UninstallKey, false);
        if (Directory.Exists(StartMenuDirectory)) Directory.Delete(StartMenuDirectory, true);

        foreach (string file in Directory.GetFiles(InstallDirectory))
            if (!Path.GetFullPath(file).Equals(Path.GetFullPath(Application.ExecutablePath),
                StringComparison.OrdinalIgnoreCase)) File.Delete(file);
        foreach (string directory in Directory.GetDirectories(InstallDirectory))
            Directory.Delete(directory, true);

        var cleanup = new ProcessStartInfo("cmd.exe",
            "/d /c timeout /t 2 /nobreak >nul & rmdir /s /q \"" + InstallDirectory + "\"") {
            UseShellExecute = false,
            CreateNoWindow = true
        };
        Process.Start(cleanup);
        MessageBox.Show(AppName + " was uninstalled.", AppName + " Uninstall",
            MessageBoxButtons.OK, MessageBoxIcon.Information);
        return 0;
    }

    private static void StopInstalledApp() {
        foreach (Process process in Process.GetProcessesByName(AppId)) {
            try {
                if (String.Equals(process.MainModule.FileName, AppPath, StringComparison.OrdinalIgnoreCase)) {
                    process.CloseMainWindow();
                    if (!process.WaitForExit(1500)) process.Kill();
                }
            } catch { }
        }
    }

    private static string StopExistingAppsForInstall() {
        string configToMigrate = null;
        foreach (Process process in Process.GetProcessesByName(AppId)) {
            try {
                string executable = process.MainModule.FileName;
                string directory = Path.GetDirectoryName(executable);
                if (File.Exists(Path.Combine(directory, "restore-state.json")))
                    throw new InvalidOperationException(
                        "Battery Saver is currently active. Reconnect AC power or turn Battery Saver off " +
                        "from its tray menu before installing, so the current power plan is restored safely.");
                string config = Path.Combine(directory, "config.json");
                if (File.Exists(config)) configToMigrate = config;
            } catch (InvalidOperationException) { throw; }
            catch { }
        }
        foreach (Process process in Process.GetProcessesByName(AppId)) {
            try {
                process.CloseMainWindow();
                if (!process.WaitForExit(1500)) process.Kill();
            } catch { }
        }
        return configToMigrate;
    }

    private static void RegisterUninstaller() {
        using (var key = Registry.CurrentUser.CreateSubKey(UninstallKey)) {
            key.SetValue("DisplayName", AppName);
            key.SetValue("DisplayVersion", Version);
            key.SetValue("Publisher", "KaiiW31");
            key.SetValue("InstallLocation", InstallDirectory);
            key.SetValue("DisplayIcon", AppPath);
            key.SetValue("UninstallString", "\"" + UninstallerPath + "\" /uninstall");
            key.SetValue("NoModify", 1, RegistryValueKind.DWord);
            key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
            key.SetValue("EstimatedSize", 256, RegistryValueKind.DWord);
        }
    }

    private static void CreateStartMenuShortcut() {
        Directory.CreateDirectory(StartMenuDirectory);
        string command =
            "$s=(New-Object -ComObject WScript.Shell).CreateShortcut('" +
            StartMenuShortcut.Replace("'", "''") + "');" +
            "$s.TargetPath='" + AppPath.Replace("'", "''") + "';" +
            "$s.WorkingDirectory='" + InstallDirectory.Replace("'", "''") + "';" +
            "$s.IconLocation='" + AppPath.Replace("'", "''") + ",0';$s.Save()";
        using (var process = Process.Start(new ProcessStartInfo("powershell.exe",
            "-NoProfile -NonInteractive -Command \"" + command.Replace("\"", "\\\"") + "\"") {
            UseShellExecute = false,
            CreateNoWindow = true
        })) {
            process.WaitForExit(10000);
            if (process.ExitCode != 0) throw new InvalidOperationException("Could not create the Start Menu shortcut.");
        }
    }

    private static void ExtractResource(string name, string destination) {
        using (Stream input = Assembly.GetExecutingAssembly().GetManifestResourceStream(name)) {
            if (input == null) throw new InvalidOperationException("Installer payload is missing: " + name);
            using (var output = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None))
                input.CopyTo(output);
        }
    }

    private static bool VerifyPayload() {
        using (Stream app = Assembly.GetExecutingAssembly().GetManifestResourceStream(AppResource))
        using (Stream config = Assembly.GetExecutingAssembly().GetManifestResourceStream(ConfigResource))
            return app != null && app.Length > 0 && config != null && config.Length > 0;
    }
}
