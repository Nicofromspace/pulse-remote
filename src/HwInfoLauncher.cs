using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;

namespace Pulse.Remote;

public static class HwInfoLauncher
{
    public static bool IsRunning => Process.GetProcessesByName("HWiNFO64").Any();
    public static string? FindPath(CompanionSettings settings)
    {
        foreach (string? candidate in new[] { settings.HwInfoPath,
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "HWiNFO64", "HWiNFO64.EXE"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "HWiNFO64", "HWiNFO64.EXE") })
            if (candidate is not null && File.Exists(candidate)) return Path.GetFullPath(candidate);
        return null;
    }
    public static string Start(CompanionSettings settings)
    {
        if (IsRunning) return "HWiNFO is already running.";
        string? path = FindPath(settings);
        if (path is null) return "HWiNFO was not found. Click Choose HWiNFO to locate HWiNFO64.exe.";
        try
        {
            // Only this short-lived helper elevates. The dashboard remains a standard-user process.
            var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden };
            start.ArgumentList.Add("--start-hwinfo"); start.ArgumentList.Add(path);
            Process.Start(start)?.Dispose();
            return "Starting HWiNFO Sensors… Approve its Windows administrator prompt if asked.";
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223) { return "HWiNFO startup was cancelled. Click Start HWiNFO to try again."; }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException) { return "Could not start HWiNFO: " + ex.Message; }
    }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool WritePrivateProfileString(string section, string key, string value, string file);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    static extern uint GetPrivateProfileString(string section, string key, string fallback, StringBuilder value, uint size, string file);

    public static void RunElevated(string path)
    {
        try
        {
            Directory.CreateDirectory(Program.DataDirectory);
            File.WriteAllText(Path.Combine(Program.DataDirectory, "hwinfo-startup.txt"), "Preparing HWiNFO startup.");
            if (!new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator))
                throw new InvalidOperationException("HWiNFO startup needs administrator consent.");
            path = Path.GetFullPath(path);
            if (!File.Exists(path) || !Path.GetFileName(path).Equals("HWiNFO64.exe", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Choose the installed HWiNFO64.exe application.");
            if (IsRunning) return;
            string ini = Path.ChangeExtension(path, ".INI");
            var desired = new Dictionary<string, string> { ["SensorsOnly"] = "1", ["MinimalizeMainWnd"] = "1", ["OpenSensors"] = "1", ["MinimalizeSensors"] = "1" };
            // Preserve Shared Memory Support exactly as configured by the user. Never reset its timer.
            bool backedUp = false;
            foreach (var pair in desired)
            {
                var value = new StringBuilder(256);
                GetPrivateProfileString("Settings", pair.Key, "", value, 256, ini);
                if (value.ToString() == pair.Value) continue;
                if (!backedUp && File.Exists(ini))
                {
                    if (!File.Exists(ini + ".pulse-backup")) File.Copy(ini, ini + ".pulse-backup", false);
                    backedUp = true;
                }
                if (!WritePrivateProfileString("Settings", pair.Key, pair.Value, ini))
                    throw new Win32Exception(Marshal.GetLastWin32Error());
            }
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(path)! })?.Dispose();
            File.WriteAllText(Path.Combine(Program.DataDirectory, "hwinfo-startup.txt"), "HWiNFO launch requested successfully.");
        }
        catch (Exception ex)
        {
            File.WriteAllText(Path.Combine(Program.DataDirectory, "hwinfo-startup.txt"), ex.ToString());
            System.Windows.Forms.MessageBox.Show("Pulse could not prepare HWiNFO.\n\n" + ex.Message, "Pulse Remote");
        }
    }
}
