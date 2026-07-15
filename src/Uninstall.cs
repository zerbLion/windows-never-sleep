using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

[assembly: AssemblyTitle("Windows Never Sleep Uninstaller")]
[assembly: AssemblyDescription("Uninstalls Windows Never Sleep and restores previous sleep settings.")]
[assembly: AssemblyProduct("Windows Never Sleep")]
[assembly: AssemblyCompany("zerbLion")]
[assembly: AssemblyCopyright("Copyright (c) 2026 zerbLion")]
[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]

namespace WindowsNeverSleepUninstaller
{
    internal static class Program
    {
        private const string AppName = "Windows Never Sleep";
        private const string RunValueName = "WindowsNeverSleep";
        private const string SleepSubgroup = "238c9fa8-0aad-41ed-83f4-97be242c8f20";
        private const string StandbyIdleSetting = "29f6c1db-86da-48c5-9fdb-f2b67b1f44da";
        private const int MoveFileDelayUntilReboot = 0x4;
        private static readonly string InstallDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WindowsNeverSleep");
        private static readonly string MainExecutablePath = Path.Combine(InstallDirectory, "WindowsNeverSleep.exe");
        private static readonly string StatePath = Path.Combine(InstallDirectory, "state.ini");

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool MoveFileEx(string existingFile, string newFile, int flags);

        [STAThread]
        private static int Main(string[] args)
        {
            if (args.Length >= 3 && string.Equals(args[0], "--cleanup", StringComparison.OrdinalIgnoreCase))
            {
                return CleanupAfterExit(args[1], Int32.Parse(args[2]));
            }

            bool silent = Array.Exists(args, delegate(string arg)
            {
                return string.Equals(arg, "--silent", StringComparison.OrdinalIgnoreCase);
            });

            if (!silent)
            {
                DialogResult result = MessageBox.Show(
                    "Uninstall Windows Never Sleep and restore the previous sleep timeout?",
                    AppName,
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question);
                if (result != DialogResult.Yes)
                {
                    return 0;
                }
            }

            try
            {
                StopRunningInstance();
                RestorePowerSettings();
                RemoveRegistryEntries();

                string selfPath = Path.GetFullPath(Assembly.GetExecutingAssembly().Location);
                bool runningInsideInstall = selfPath.StartsWith(
                    Path.GetFullPath(InstallDirectory) + Path.DirectorySeparatorChar,
                    StringComparison.OrdinalIgnoreCase);

                if (runningInsideInstall)
                {
                    string helperPath = Path.Combine(
                        Path.GetTempPath(),
                        "WindowsNeverSleep-Uninstall-" + Guid.NewGuid().ToString("N") + ".exe");
                    File.Copy(selfPath, helperPath, true);
                    Process.Start(new ProcessStartInfo(
                        helperPath,
                        "--cleanup \"" + InstallDirectory + "\" " + Process.GetCurrentProcess().Id)
                    {
                        UseShellExecute = false,
                        CreateNoWindow = true
                    });
                }
                else
                {
                    DeleteInstallDirectory(InstallDirectory);
                }

                if (!silent)
                {
                    MessageBox.Show(
                        "Uninstalled. The original sleep timeout has been restored.",
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
                        "Uninstall failed:\n\n" + exception.Message,
                        AppName,
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                }
                return 1;
            }
        }

        private static int CleanupAfterExit(string targetDirectory, int parentProcessId)
        {
            try
            {
                try
                {
                    Process parent = Process.GetProcessById(parentProcessId);
                    parent.WaitForExit(10000);
                    parent.Dispose();
                }
                catch
                {
                }

                for (int attempt = 0; attempt < 10; attempt++)
                {
                    try
                    {
                        DeleteInstallDirectory(targetDirectory);
                        break;
                    }
                    catch (IOException)
                    {
                        Thread.Sleep(300);
                    }
                    catch (UnauthorizedAccessException)
                    {
                        Thread.Sleep(300);
                    }
                }

                MoveFileEx(Assembly.GetExecutingAssembly().Location, null, MoveFileDelayUntilReboot);
                return 0;
            }
            catch
            {
                return 1;
            }
        }

        private static void DeleteInstallDirectory(string targetDirectory)
        {
            string expected = Path.GetFullPath(InstallDirectory).TrimEnd(Path.DirectorySeparatorChar);
            string actual = Path.GetFullPath(targetDirectory).TrimEnd(Path.DirectorySeparatorChar);
            if (!string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Refusing to delete an unexpected directory: " + actual);
            }

            if (Directory.Exists(actual))
            {
                Directory.Delete(actual, true);
            }
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

        private static void RestorePowerSettings()
        {
            if (!File.Exists(StatePath))
            {
                return;
            }

            Dictionary<string, string> values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string line in File.ReadAllLines(StatePath))
            {
                int separator = line.IndexOf('=');
                if (separator > 0)
                {
                    values[line.Substring(0, separator)] = line.Substring(separator + 1);
                }
            }

            string scheme = values["SchemeGuid"];
            RunPowerCfg("/setacvalueindex", scheme, SleepSubgroup, StandbyIdleSetting, values["OriginalAcSeconds"]);
            RunPowerCfg("/setdcvalueindex", scheme, SleepSubgroup, StandbyIdleSetting, values["OriginalDcSeconds"]);

            string active = RunPowerCfg("/getactivescheme");
            if (active.IndexOf(scheme, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                RunPowerCfg("/setactive", scheme);
            }
        }

        private static void RemoveRegistryEntries()
        {
            using (RegistryKey run = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Run", true))
            {
                if (run != null)
                {
                    run.DeleteValue(RunValueName, false);
                }
            }

            using (RegistryKey uninstallRoot = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Uninstall", true))
            {
                if (uninstallRoot != null)
                {
                    uninstallRoot.DeleteSubKeyTree("WindowsNeverSleep", false);
                }
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
    }
}
