using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;
using System.Web.Script.Serialization;

[assembly: AssemblyTitle("Acer Battery Saver")]
[assembly: AssemblyDescription("Automatic battery-saving tray app for Acer Predator PHN16-71")]
[assembly: AssemblyCompany("KaiiW31")]
[assembly: AssemblyProduct("Acer Battery Saver")]
[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]

internal sealed class Config {
    public bool AutomaticSwitching = true;
    public bool StartWithWindows = true;
    public bool EnableWindowsEnergySaver = true;
    public bool AggressiveEnergySaver = true;
    public bool ManageBluetooth = true;
    public bool PauseBackgroundApps = true;
    public string[] ProcessesToPause = new string[0];
    public int DisplayTimeoutMinutes = 3;
    public int SleepTimeoutMinutes = 10;
    public int CpuMaximumPercent = 55;
    public bool DisableCpuBoost = true;
    public bool UsePassiveCooling = true;
}

internal sealed class SavedState {
    public string OriginalPowerScheme;
    public int RefreshRate;
    public bool BluetoothWasEnabled;
    public List<int> SuspendedProcessIds = new List<int>();
    public DateTime ActivatedAt;
}

internal static class Native {
    [StructLayout(LayoutKind.Sequential)]
    internal struct SYSTEMTIME {
        public ushort Year, Month, DayOfWeek, Day, Hour, Minute, Second, Milliseconds;
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct BLUETOOTH_DEVICE_INFO {
        public int dwSize;
        public ulong Address;
        public uint ulClassofDevice;
        [MarshalAs(UnmanagedType.Bool)] public bool fConnected;
        [MarshalAs(UnmanagedType.Bool)] public bool fRemembered;
        [MarshalAs(UnmanagedType.Bool)] public bool fAuthenticated;
        public SYSTEMTIME stLastSeen, stLastUsed;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 248)] public string szName;
    }
    [StructLayout(LayoutKind.Sequential)]
    internal struct BLUETOOTH_DEVICE_SEARCH_PARAMS {
        public int dwSize;
        [MarshalAs(UnmanagedType.Bool)] public bool fReturnAuthenticated;
        [MarshalAs(UnmanagedType.Bool)] public bool fReturnRemembered;
        [MarshalAs(UnmanagedType.Bool)] public bool fReturnUnknown;
        [MarshalAs(UnmanagedType.Bool)] public bool fReturnConnected;
        [MarshalAs(UnmanagedType.Bool)] public bool fIssueInquiry;
        public byte cTimeoutMultiplier;
        public IntPtr hRadio;
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    internal struct DEVMODE {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmDeviceName;
        public short dmSpecVersion, dmDriverVersion, dmSize, dmDriverExtra;
        public int dmFields, dmPositionX, dmPositionY, dmDisplayOrientation, dmDisplayFixedOutput;
        public short dmColor, dmDuplex, dmYResolution, dmTTOption, dmCollate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmFormName;
        public short dmLogPixels;
        public int dmBitsPerPel, dmPelsWidth, dmPelsHeight, dmDisplayFlags, dmDisplayFrequency;
        public int dmICMMethod, dmICMIntent, dmMediaType, dmDitherType, dmReserved1, dmReserved2;
        public int dmPanningWidth, dmPanningHeight;
    }
    internal const int ENUM_CURRENT_SETTINGS = -1, CDS_UPDATEREGISTRY = 1, DISP_CHANGE_SUCCESSFUL = 0;
    internal const int DM_DISPLAYFREQUENCY = 0x400000;
    [DllImport("user32.dll", CharSet = CharSet.Ansi)] internal static extern bool EnumDisplaySettings(string device, int mode, ref DEVMODE dm);
    [DllImport("user32.dll", CharSet = CharSet.Ansi)] internal static extern int ChangeDisplaySettings(ref DEVMODE dm, int flags);
    [DllImport("ntdll.dll")] internal static extern int NtSuspendProcess(IntPtr handle);
    [DllImport("ntdll.dll")] internal static extern int NtResumeProcess(IntPtr handle);
    [DllImport("BluetoothApis.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern IntPtr BluetoothFindFirstDevice(ref BLUETOOTH_DEVICE_SEARCH_PARAMS search, ref BLUETOOTH_DEVICE_INFO info);
    [DllImport("BluetoothApis.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool BluetoothFindNextDevice(IntPtr find, ref BLUETOOTH_DEVICE_INFO info);
    [DllImport("BluetoothApis.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool BluetoothFindDeviceClose(IntPtr find);
}

internal sealed class TrayApp : ApplicationContext {
    private readonly NotifyIcon tray = new NotifyIcon();
    private readonly ToolStripMenuItem toggle = new ToolStripMenuItem();
    private readonly ToolStripMenuItem automatic = new ToolStripMenuItem();
    private readonly ToolStripMenuItem startup = new ToolStripMenuItem();
    private readonly ToolStripMenuItem status = new ToolStripMenuItem();
    private readonly string root = AppDomain.CurrentDomain.BaseDirectory;
    private readonly string configPath, statePath, logPath;
    private Config config;
    private SavedState saved;
    private bool active, transitioning, bluetoothConnectedOnAc;
    private System.Windows.Forms.Timer poller;

    internal TrayApp() {
        configPath = Path.Combine(root, "config.json");
        statePath = Path.Combine(root, "restore-state.json");
        logPath = Path.Combine(root, "battery-saver.log");
        config = Load<Config>(configPath) ?? new Config();
        Save(configPath, config);
        saved = Load<SavedState>(statePath);
        active = saved != null;

        tray.Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? SystemIcons.Shield;
        tray.Text = "Acer Battery Saver";
        tray.Visible = true;
        var menu = new ContextMenuStrip();
        status.Enabled = false;
        toggle.Click += delegate { SetActive(!active, "manual"); };
        automatic.CheckOnClick = true;
        automatic.Checked = config.AutomaticSwitching;
        automatic.CheckedChanged += delegate { config.AutomaticSwitching = automatic.Checked; Save(configPath, config); };
        startup.CheckOnClick = true;
        startup.Text = "Start with Windows";
        startup.Checked = IsStartupEnabled();
        startup.CheckedChanged += delegate { SetStartup(startup.Checked); config.StartWithWindows = startup.Checked; Save(configPath, config); };
        menu.Items.Add(status); menu.Items.Add(toggle); menu.Items.Add(automatic); menu.Items.Add(startup);
        menu.Items.Add(new ToolStripSeparator());
        var settings = menu.Items.Add("Open settings"); settings.Click += delegate { Process.Start("notepad.exe", configPath); };
        var restore = menu.Items.Add("Emergency restore"); restore.Click += delegate { SetActive(false, "emergency restore"); };
        var logs = menu.Items.Add("Open log"); logs.Click += delegate { EnsureLog(); Process.Start("notepad.exe", logPath); };
        menu.Items.Add(new ToolStripSeparator());
        var exit = menu.Items.Add("Exit"); exit.Click += delegate { ExitThread(); };
        tray.ContextMenuStrip = menu;
        tray.DoubleClick += delegate { SetActive(!active, "double click"); };

        if (config.StartWithWindows != IsStartupEnabled()) SetStartup(config.StartWithWindows);

        SystemEvents.PowerModeChanged += PowerChanged;
        poller = new System.Windows.Forms.Timer();
        poller.Interval = 5000;
        poller.Tick += delegate { CheckPower(); };
        poller.Start();
        CheckPower();
        UpdateUi("Ready");
        Log("Started; automatic=" + config.AutomaticSwitching + ", battery mode already active=" + active);
    }

    private void PowerChanged(object sender, PowerModeChangedEventArgs e) { if (e.Mode == PowerModes.StatusChange) CheckPower(); }
    private void CheckPower() {
        if (!config.AutomaticSwitching || transitioning) return;
        var line = SystemInformation.PowerStatus.PowerLineStatus;
        if (line == PowerLineStatus.Offline && !active) SetActive(true, "power unplugged");
        else if (line == PowerLineStatus.Online && active) SetActive(false, "power connected");
        else if (line == PowerLineStatus.Online && config.ManageBluetooth) bluetoothConnectedOnAc = HasConnectedBluetoothDevice();
    }

    private void SetActive(bool enable, string reason) {
        if (transitioning || enable == active) return;
        transitioning = true;
        try {
            if (enable) Enable(reason); else Restore(reason);
        } catch (Exception ex) { Log("ERROR: " + ex); Balloon("Battery saver error", ex.Message); }
        finally { transitioning = false; UpdateUi(enable == active ? "Ready" : "Needs attention"); }
    }

    private void Enable(string reason) {
        var s = new SavedState { OriginalPowerScheme = GetActiveScheme(), RefreshRate = GetRefreshRate(), ActivatedAt = DateTime.Now };
        Save(statePath, s); // Crash-safe before making changes.
        saved = s;
        Log("Enabling because " + reason + "; scheme=" + s.OriginalPowerScheme + ", refresh=" + s.RefreshRate);

        string duplicateOutput = Run("powercfg.exe", "/duplicatescheme " + s.OriginalPowerScheme);
        string batteryScheme = ParseGuid(duplicateOutput);
        if (String.IsNullOrEmpty(batteryScheme)) throw new InvalidOperationException("Could not create the temporary battery power plan.");
        RunPower("/changename " + batteryScheme + " \"Acer Battery Saver (temporary)\"");
        SetDc(batteryScheme, "54533251-82be-4824-96c1-47b60b740d00", "bc5038f7-23e0-4960-96da-33abaf5935ec", config.CpuMaximumPercent);
        if (config.DisableCpuBoost) SetDc(batteryScheme, "54533251-82be-4824-96c1-47b60b740d00", "be337238-0d82-4146-a960-4f3749d470c7", 0);
        if (config.UsePassiveCooling) SetDc(batteryScheme, "54533251-82be-4824-96c1-47b60b740d00", "94d3a615-a899-4ac5-ae2b-e4d8f634367f", 0);
        SetDc(batteryScheme, "2a737441-1930-4402-8d77-b2bebba308a3", "48e6b7a6-50f5-4782-a5d4-53bb8f07e226", 1);
        if (config.EnableWindowsEnergySaver) {
            // Trigger Energy Saver at every battery level for this temporary plan.
            SetDc(batteryScheme, "de830923-a562-41af-a086-e3a2c6bad2da", "e69653ca-cf7f-4f05-aa73-cb833fa90ad4", 100);
            SetDc(batteryScheme, "de830923-a562-41af-a086-e3a2c6bad2da", "5c5bb349-ad29-4ee2-9d0b-2b25270f7a81", config.AggressiveEnergySaver ? 1 : 0);
            // Energy Saver must not dim the display; the user controls brightness.
            SetDc(batteryScheme, "de830923-a562-41af-a086-e3a2c6bad2da", "13d09884-f74e-474a-a852-b6bde8ad03a8", 100);
        }
        RunPower("/setdcvalueindex " + batteryScheme + " SUB_VIDEO VIDEOIDLE " + (config.DisplayTimeoutMinutes * 60));
        RunPower("/setdcvalueindex " + batteryScheme + " SUB_SLEEP STANDBYIDLE " + (config.SleepTimeoutMinutes * 60));
        RunPower("/setactive " + batteryScheme);

        SetRefreshRate(60);
        if (config.ManageBluetooth) {
            if (bluetoothConnectedOnAc || HasConnectedBluetoothDevice()) Log("Bluetooth kept on because a Bluetooth device was connected before or during unplugging.");
            else s.BluetoothWasEnabled = SetBluetooth(false);
        }
        if (config.PauseBackgroundApps) SuspendConfigured(s);
        Save(statePath, s);
        active = true;
        Balloon("Battery saver on", "Windows Energy Saver, 60 Hz and the efficient hardware profile are active.");
    }

    private void Restore(string reason) {
        var s = saved ?? Load<SavedState>(statePath);
        if (s == null) { active = false; return; }
        Log("Restoring because " + reason);
        ResumeSaved(s);
        if (config.ManageBluetooth && s.BluetoothWasEnabled) SetBluetooth(true);
        if (s.RefreshRate > 0) SetRefreshRate(s.RefreshRate);
        if (!String.IsNullOrEmpty(s.OriginalPowerScheme)) RunPower("/setactive " + s.OriginalPowerScheme);
        DeleteTemporarySchemes(s.OriginalPowerScheme);
        if (File.Exists(statePath)) File.Delete(statePath);
        saved = null; active = false;
        Balloon("Battery saver off", "Your previous refresh rate and power plan were restored.");
    }

    private void SetDc(string scheme, string subgroup, string setting, int value) { RunPower("/setdcvalueindex " + scheme + " " + subgroup + " " + setting + " " + value); }
    private void RunPower(string args) { var output = Run("powercfg.exe", args); if (output.IndexOf("error", StringComparison.OrdinalIgnoreCase) >= 0) Log(output); }
    private string GetActiveScheme() { return ParseGuid(Run("powercfg.exe", "/getactivescheme")); }
    private string ParseGuid(string text) { var m = Regex.Match(text ?? "", "[0-9a-fA-F]{8}(?:-[0-9a-fA-F]{4}){3}-[0-9a-fA-F]{12}"); return m.Success ? m.Value : null; }
    private void DeleteTemporarySchemes(string keep) {
        string list = Run("powercfg.exe", "/list");
        foreach (Match m in Regex.Matches(list, "([0-9a-fA-F-]{36}).*Acer Battery Saver \\(temporary\\)"))
            if (!m.Groups[1].Value.Equals(keep, StringComparison.OrdinalIgnoreCase)) RunPower("/delete " + m.Groups[1].Value);
    }

    private int GetRefreshRate() { var dm = NewMode(); return Native.EnumDisplaySettings(null, Native.ENUM_CURRENT_SETTINGS, ref dm) ? dm.dmDisplayFrequency : 0; }
    private void SetRefreshRate(int hz) {
        var dm = NewMode(); if (!Native.EnumDisplaySettings(null, Native.ENUM_CURRENT_SETTINGS, ref dm)) { Log("Refresh query unavailable"); return; }
        dm.dmDisplayFrequency = hz; dm.dmFields = Native.DM_DISPLAYFREQUENCY;
        int result = Native.ChangeDisplaySettings(ref dm, Native.CDS_UPDATEREGISTRY);
        if (result != Native.DISP_CHANGE_SUCCESSFUL) Log("Refresh " + hz + " Hz rejected (code " + result + ")");
    }
    private Native.DEVMODE NewMode() { var dm = new Native.DEVMODE(); dm.dmDeviceName = new string('\0', 32); dm.dmFormName = new string('\0', 32); dm.dmSize = (short)Marshal.SizeOf(dm); return dm; }

    private bool SetBluetooth(bool enabled) {
        string verb = enabled ? "Enable-PnpDevice" : "Disable-PnpDevice";
        string status = enabled ? "OK" : "Disabled";
        string ps = "$d=Get-PnpDevice -Class Bluetooth -ErrorAction SilentlyContinue | ? {$_.InstanceId -like 'USB\\*'}; if(!$d){exit 2}; try {$d | " + verb + " -Confirm:$false -ErrorAction Stop; Start-Sleep -Milliseconds 300; $v=Get-PnpDevice -InstanceId $d[0].InstanceId -ErrorAction Stop; if($v.Status -eq '" + status + "'){'changed'}else{exit 3}}catch{exit 4}";
        string result = Run("powershell.exe", "-NoProfile -NonInteractive -Command \"" + ps.Replace("\"", "\\\"") + "\"");
        bool changed = result.IndexOf("changed", StringComparison.OrdinalIgnoreCase) >= 0;
        if (!changed) Log("Bluetooth change needs Administrator permission or no active adapter was found.");
        return changed;
    }

    private bool HasConnectedBluetoothDevice() {
        var search = new Native.BLUETOOTH_DEVICE_SEARCH_PARAMS {
            dwSize = Marshal.SizeOf(typeof(Native.BLUETOOTH_DEVICE_SEARCH_PARAMS)),
            fReturnConnected = true
        };
        var info = new Native.BLUETOOTH_DEVICE_INFO {
            dwSize = Marshal.SizeOf(typeof(Native.BLUETOOTH_DEVICE_INFO)),
            szName = new string('\0', 248)
        };
        IntPtr find = Native.BluetoothFindFirstDevice(ref search, ref info);
        if (find == IntPtr.Zero) return false;
        try {
            do {
                if (info.fConnected) {
                    return true;
                }
                info.dwSize = Marshal.SizeOf(typeof(Native.BLUETOOTH_DEVICE_INFO));
            } while (Native.BluetoothFindNextDevice(find, ref info));
        } finally { Native.BluetoothFindDeviceClose(find); }
        return false;
    }

    private void SuspendConfigured(SavedState s) {
        var protectedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "explorer", "dwm", "winlogon", "csrss", "services", "svchost", "lsass", "System", "AcerBatterySaver" };
        foreach (string raw in config.ProcessesToPause ?? new string[0]) {
            string name = Path.GetFileNameWithoutExtension(raw ?? ""); if (String.IsNullOrWhiteSpace(name) || protectedNames.Contains(name)) continue;
            foreach (var p in Process.GetProcessesByName(name)) try { if (Native.NtSuspendProcess(p.Handle) == 0) s.SuspendedProcessIds.Add(p.Id); } catch (Exception ex) { Log("Could not pause " + name + ": " + ex.Message); }
        }
    }
    private void ResumeSaved(SavedState s) { foreach (int pid in s.SuspendedProcessIds) try { using (var p = Process.GetProcessById(pid)) Native.NtResumeProcess(p.Handle); } catch { } }

    private string Run(string file, string args) {
        var psi = new ProcessStartInfo(file, args) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        using (var p = Process.Start(psi)) { string o = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd(); p.WaitForExit(15000); Log(file + " " + args + " -> " + p.ExitCode); return o; }
    }
    private bool IsStartupEnabled() {
        using (var key = Registry.CurrentUser.OpenSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Run")) {
            string configured = key == null ? null : Convert.ToString(key.GetValue("AcerBatterySaver"));
            return String.Equals(configured, "\"" + Application.ExecutablePath + "\"", StringComparison.OrdinalIgnoreCase);
        }
    }
    private void SetStartup(bool enabled) { using (var key = Registry.CurrentUser.CreateSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Run")) { if (enabled) key.SetValue("AcerBatterySaver", "\"" + Application.ExecutablePath + "\""); else key.DeleteValue("AcerBatterySaver", false); } }
    private T Load<T>(string path) where T : class { try { return File.Exists(path) ? new JavaScriptSerializer().Deserialize<T>(File.ReadAllText(path)) : null; } catch (Exception ex) { Log("Could not read " + path + ": " + ex.Message); return null; } }
    private void Save(string path, object value) { File.WriteAllText(path, new JavaScriptSerializer().Serialize(value)); }
    private void EnsureLog() { if (!File.Exists(logPath)) File.WriteAllText(logPath, ""); }
    private void Log(string msg) {
        try {
            if (File.Exists(logPath) && new FileInfo(logPath).Length >= 512 * 1024) {
                string previous = logPath + ".old";
                File.Copy(logPath, previous, true);
                File.WriteAllText(logPath, "");
            }
            File.AppendAllText(logPath, DateTime.Now.ToString("s") + " " + msg + Environment.NewLine);
        } catch { }
    }
    private void Balloon(string title, string text) { tray.BalloonTipTitle = title; tray.BalloonTipText = text; tray.ShowBalloonTip(2500); }
    private void UpdateUi(string message) { status.Text = (active ? "ON — battery profile active" : "OFF — normal profile") + " · " + message; toggle.Text = active ? "Turn battery saver OFF" : "Turn battery saver ON"; automatic.Text = "Automatic on unplug / restore on plug"; tray.Text = active ? "Acer Battery Saver — ON" : "Acer Battery Saver — OFF"; }
    protected override void ExitThreadCore() { SystemEvents.PowerModeChanged -= PowerChanged; if (poller != null) poller.Dispose(); tray.Visible = false; tray.Dispose(); base.ExitThreadCore(); }
}

internal static class Program {
    [STAThread] private static void Main(string[] args) {
        bool created; using (var mutex = new Mutex(true, "AcerBatterySaver-PHN16-71", out created)) {
            if (!created) return;
            Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false); Application.Run(new TrayApp());
        }
    }
}
