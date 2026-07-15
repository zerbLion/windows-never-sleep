using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

[assembly: AssemblyTitle("Windows Never Sleep Installer")]
[assembly: AssemblyDescription("Installs Windows Never Sleep for the current user.")]
[assembly: AssemblyProduct("Windows Never Sleep")]
[assembly: AssemblyCompany("zerbLion")]
[assembly: AssemblyCopyright("Copyright (c) 2026 zerbLion")]
[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]

namespace WindowsNeverSleepInstaller
{
    internal static class Program
    {
        private const string AppName = "Windows Never Sleep";
        private const string RunValueName = "WindowsNeverSleep";
        private const string SleepSubgroup = "238c9fa8-0aad-41ed-83f4-97be242c8f20";
        private const string StandbyIdleSetting = "29f6c1db-86da-48c5-9fdb-f2b67b1f44da";
        private static readonly string InstallDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WindowsNeverSleep");
        private static readonly string MainExecutablePath = Path.Combine(InstallDirectory, "WindowsNeverSleep.exe");
        private static readonly string UninstallExecutablePath = Path.Combine(InstallDirectory, "Uninstall.exe");
        private static readonly string StatePath = Path.Combine(InstallDirectory, "state.ini");

        [STAThread]
        private static int Main(string[] args)
        {
            bool silent = Array.Exists(args, delegate(string arg)
            {
                return string.Equals(arg, "--silent", StringComparison.OrdinalIgnoreCase);
            });

            try
            {
                Directory.CreateDirectory(InstallDirectory);
                PreserveRollbackState();
                StopRunningInstance();
                ExtractResource("WindowsNeverSleep.exe", MainExecutablePath);
                ExtractResource("Uninstall.exe", UninstallExecutablePath);

                RollbackState state = ReadRollbackState();
                RunPowerCfg("/setacvalueindex", state.SchemeGuid, SleepSubgroup, StandbyIdleSetting, "0");
                RunPowerCfg("/setdcvalueindex", state.SchemeGuid, SleepSubgroup, StandbyIdleSetting, "0");
                RunPowerCfg("/setactive", state.SchemeGuid);

                RegisterAutoStart();
                RegisterUninstaller();
                Process.Start(new ProcessStartInfo(MainExecutablePath) { UseShellExecute = true });
                Thread.Sleep(500);

                if (!silent)
                {
                    MessageBox.Show(
                        "Installed and running.\n\n" +
                        "- Starts automatically when you sign in\n" +
                        "- Prevents idle sleep\n" +
                        "- Prevents the 10-minute idle lock\n" +
                        "- Manual Win+L still works",
                        AppName,
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                }

                return 0;
            }
            catch (Exception exception)
            {
                if (!silent)
                {
                    MessageBox.Show(
                        "Installation failed:\n\n" + exception.Message,
                        AppName,
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                }
                return 1;
            }
        }

        private static void PreserveRollbackState()
        {
            if (File.Exists(StatePath))
            {
                return;
            }

            string legacyState = Path.Combine(InstallDirectory, "state.json");
            if (File.Exists(legacyState))
            {
                string json = File.ReadAllText(legacyState);
                Match scheme = Regex.Match(json, "\\\"SchemeGuid\\\"\\s*:\\s*\\\"([^\\\"]+)\\\"");
                Match ac = Regex.Match(json, "\\\"OriginalAcSeconds\\\"\\s*:\\s*(\\d+)");
                Match dc = Regex.Match(json, "\\\"OriginalDcSeconds\\\"\\s*:\\s*(\\d+)");
                if (scheme.Success && ac.Success && dc.Success)
                {
                    WriteRollbackState(new RollbackState(
                        scheme.Groups[1].Value,
                        UInt32.Parse(ac.Groups[1].Value),
                        UInt32.Parse(dc.Groups[1].Value)));
                    return;
                }
            }

            string activeOutput = RunPowerCfg("/getactivescheme");
            Match activeScheme = Regex.Match(
                activeOutput,
                "[0-9a-fA-F]{8}-(?:[0-9a-fA-F]{4}-){3}[0-9a-fA-F]{12}");
            if (!activeScheme.Success)
            {
                throw new InvalidOperationException("Could not read the active power scheme.");
            }

            string queryOutput = RunPowerCfg(
                "/query",
                activeScheme.Value,
                SleepSubgroup,
                StandbyIdleSetting);
            MatchCollection values = Regex.Matches(queryOutput, "0x([0-9a-fA-F]{8})");
            if (values.Count < 2)
            {
                throw new InvalidOperationException("Could not read the current sleep timeout.");
            }

            UInt32 acSeconds = Convert.ToUInt32(values[values.Count - 2].Groups[1].Value, 16);
            UInt32 dcSeconds = Convert.ToUInt32(values[values.Count - 1].Groups[1].Value, 16);
            WriteRollbackState(new RollbackState(activeScheme.Value, acSeconds, dcSeconds));
        }

        private static void WriteRollbackState(RollbackState state)
        {
            string contents =
                "SchemeGuid=" + state.SchemeGuid + Environment.NewLine +
                "OriginalAcSeconds=" + state.AcSeconds + Environment.NewLine +
                "OriginalDcSeconds=" + state.DcSeconds + Environment.NewLine;
            File.WriteAllText(StatePath, contents, Encoding.ASCII);
        }

        private static RollbackState ReadRollbackState()
        {
            Dictionary<string, string> values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string line in File.ReadAllLines(StatePath))
            {
                int separator = line.IndexOf('=');
                if (separator > 0)
                {
                    values[line.Substring(0, separator)] = line.Substring(separator + 1);
                }
            }

            return new RollbackState(
                values["SchemeGuid"],
                UInt32.Parse(values["OriginalAcSeconds"]),
                UInt32.Parse(values["OriginalDcSeconds"]));
        }

        private static void StopRunningInstance()
        {
            if (File.Exists(MainExecutablePath))
            {
                try
                {
                    Process stop = Process.Start(new ProcessStartInfo(MainExecutablePath, "--stop")
                    {
                        UseShellExecute = false,
                        CreateNoWindow = true
                    });
                    if (stop != null)
                    {
                        stop.WaitForExit(2000);
                    }
                    Thread.Sleep(300);
                }
                catch
                {
                }
            }

            foreach (Process process in Process.GetProcessesByName("WindowsNeverSleep"))
            {
                try
                {
                    if (string.Equals(
                        Path.GetFullPath(process.MainModule.FileName),
                        Path.GetFullPath(MainExecutablePath),
                        StringComparison.OrdinalIgnoreCase))
                    {
                        process.Kill();
                        process.WaitForExit(2000);
                    }
                }
                catch
                {
                }
                finally
                {
                    process.Dispose();
                }
            }
        }

        private static void ExtractResource(string resourceName, string destination)
        {
            using (Stream input = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName))
            {
                if (input == null)
                {
                    throw new InvalidOperationException("Missing embedded resource: " + resourceName);
                }

                string temporaryPath = destination + ".new";
                using (FileStream output = File.Create(temporaryPath))
                {
                    input.CopyTo(output);
                }

                if (File.Exists(destination))
                {
                    File.Delete(destination);
                }
                File.Move(temporaryPath, destination);
            }
        }

        private static void RegisterAutoStart()
        {
            using (RegistryKey run = Registry.CurrentUser.CreateSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Run"))
            {
                run.SetValue(RunValueName, "\"" + MainExecutablePath + "\"", RegistryValueKind.String);
            }
        }

        private static void RegisterUninstaller()
        {
            using (RegistryKey uninstall = Registry.CurrentUser.CreateSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Uninstall\WindowsNeverSleep"))
            {
                uninstall.SetValue("DisplayName", AppName);
                uninstall.SetValue("DisplayVersion", "1.0.0");
                uninstall.SetValue("Publisher", "zerbLion");
                uninstall.SetValue("InstallLocation", InstallDirectory);
                uninstall.SetValue("DisplayIcon", MainExecutablePath);
                uninstall.SetValue("UninstallString", "\"" + UninstallExecutablePath + "\"");
                uninstall.SetValue("URLInfoAbout", "https://github.com/zerbLion/windows-never-sleep");
                uninstall.SetValue("NoModify", 1, RegistryValueKind.DWord);
                uninstall.SetValue("NoRepair", 1, RegistryValueKind.DWord);
            }
        }

        private static string RunPowerCfg(params string[] arguments)
        {
            ProcessStartInfo startInfo = new ProcessStartInfo(
                Path.Combine(Environment.SystemDirectory, "powercfg.exe"),
                string.Join(" ", arguments));
            startInfo.UseShellExecute = false;
            startInfo.CreateNoWindow = true;
            startInfo.RedirectStandardOutput = true;
            startInfo.RedirectStandardError = true;

            using (Process process = Process.Start(startInfo))
            {
                string output = process.StandardOutput.ReadToEnd();
                string error = process.StandardError.ReadToEnd();
                process.WaitForExit();
                if (process.ExitCode != 0)
                {
                    throw new InvalidOperationException("powercfg failed: " + error + output);
                }
                return output + error;
            }
        }

        private sealed class RollbackState
        {
            internal readonly string SchemeGuid;
            internal readonly UInt32 AcSeconds;
            internal readonly UInt32 DcSeconds;

            internal RollbackState(string schemeGuid, UInt32 acSeconds, UInt32 dcSeconds)
            {
                SchemeGuid = schemeGuid;
                AcSeconds = acSeconds;
                DcSeconds = dcSeconds;
            }
        }
    }
}
