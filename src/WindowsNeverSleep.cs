using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;

[assembly: AssemblyTitle("Windows Never Sleep")]
[assembly: AssemblyDescription("Prevents idle sleep and idle workstation locking while preserving manual Win+L.")]
[assembly: AssemblyProduct("Windows Never Sleep")]
[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]

namespace WindowsNeverSleep
{
    internal static class NativeMethods
    {
        internal const uint PowerRequestContextVersion = 0;
        internal const uint PowerRequestContextSimpleString = 0x1;
        internal const uint EsSystemRequired = 0x00000001;
        internal const uint EsDisplayRequired = 0x00000002;
        internal const uint EsContinuous = 0x80000000;
        internal const uint KeyEventKeyUp = 0x00000002;
        internal const byte VirtualKeyF15 = 0x7E;

        [StructLayout(LayoutKind.Sequential)]
        internal struct ReasonContext
        {
            internal uint Version;
            internal uint Flags;
            internal IntPtr SimpleReasonString;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct LastInputInfo
        {
            internal uint Size;
            internal uint Time;
        }

        internal enum PowerRequestType
        {
            SystemRequired = 0,
            DisplayRequired = 1,
            AwayModeRequired = 2,
            ExecutionRequired = 3
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern IntPtr PowerCreateRequest(ref ReasonContext context);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool PowerSetRequest(IntPtr powerRequest, PowerRequestType requestType);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool PowerClearRequest(IntPtr powerRequest, PowerRequestType requestType);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool CloseHandle(IntPtr handle);

        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern uint SetThreadExecutionState(uint executionState);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetLastInputInfo(ref LastInputInfo lastInputInfo);

        [DllImport("user32.dll")]
        internal static extern void keybd_event(byte virtualKey, byte scanCode, uint flags, UIntPtr extraInfo);
    }

    internal static class Program
    {
        private const string MutexName = @"Local\WindowsNeverSleep-7E95D75F-020C-4E66-9E82-28ACBF18C60D";
        private const string StopEventName = @"Local\WindowsNeverSleep-Stop-7E95D75F-020C-4E66-9E82-28ACBF18C60D";
        private const string RequestReason = "Windows Never Sleep: user-requested idle sleep prevention";
        private static readonly string InstallDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WindowsNeverSleep");
        private static readonly string LogPath = Path.Combine(InstallDirectory, "windows-never-sleep.log");

        [STAThread]
        private static int Main(string[] args)
        {
            if (Array.Exists(args, delegate(string arg)
            {
                return string.Equals(arg, "--stop", StringComparison.OrdinalIgnoreCase);
            }))
            {
                return SignalRunningInstanceToStop();
            }

            bool keepDisplayOn = Array.Exists(args, delegate(string arg)
            {
                return string.Equals(arg, "--keep-display-on", StringComparison.OrdinalIgnoreCase);
            });
            bool allowIdleLock = Array.Exists(args, delegate(string arg)
            {
                return string.Equals(arg, "--allow-idle-lock", StringComparison.OrdinalIgnoreCase);
            });
            bool preventIdleLock = !allowIdleLock;

            bool createdMutex;
            bool createdStopEvent;
            using (Mutex singleInstance = new Mutex(true, MutexName, out createdMutex))
            using (EventWaitHandle stopEvent = new EventWaitHandle(
                false,
                EventResetMode.ManualReset,
                StopEventName,
                out createdStopEvent))
            {
                if (!createdMutex)
                {
                    WriteLog("Another instance is already running; exiting.");
                    return 0;
                }

                return HoldPowerRequest(keepDisplayOn, preventIdleLock, stopEvent);
            }
        }

        private static int SignalRunningInstanceToStop()
        {
            try
            {
                using (EventWaitHandle stopEvent = EventWaitHandle.OpenExisting(StopEventName))
                {
                    stopEvent.Set();
                    return 0;
                }
            }
            catch (WaitHandleCannotBeOpenedException)
            {
                return 3;
            }
        }

        private static int HoldPowerRequest(
            bool keepDisplayOn,
            bool preventIdleLock,
            EventWaitHandle stopEvent)
        {
            IntPtr reasonPointer = IntPtr.Zero;
            IntPtr requestHandle = IntPtr.Zero;
            bool systemRequestSet = false;
            bool displayRequestSet = false;
            uint executionFlags = NativeMethods.EsContinuous | NativeMethods.EsSystemRequired;

            if (keepDisplayOn)
            {
                executionFlags |= NativeMethods.EsDisplayRequired;
            }

            try
            {
                Directory.CreateDirectory(InstallDirectory);
                RotateLogIfNeeded();

                reasonPointer = Marshal.StringToHGlobalUni(RequestReason);
                NativeMethods.ReasonContext context = new NativeMethods.ReasonContext();
                context.Version = NativeMethods.PowerRequestContextVersion;
                context.Flags = NativeMethods.PowerRequestContextSimpleString;
                context.SimpleReasonString = reasonPointer;

                requestHandle = NativeMethods.PowerCreateRequest(ref context);
                if (requestHandle != IntPtr.Zero && requestHandle != new IntPtr(-1))
                {
                    systemRequestSet = NativeMethods.PowerSetRequest(
                        requestHandle,
                        NativeMethods.PowerRequestType.SystemRequired);

                    if (keepDisplayOn)
                    {
                        displayRequestSet = NativeMethods.PowerSetRequest(
                            requestHandle,
                            NativeMethods.PowerRequestType.DisplayRequired);
                    }
                }

                uint executionStateResult = NativeMethods.SetThreadExecutionState(executionFlags);
                if (!systemRequestSet && executionStateResult == 0)
                {
                    WriteLog("ERROR: Windows rejected both keep-awake mechanisms. Win32=" + Marshal.GetLastWin32Error());
                    return 2;
                }

                WriteLog(
                    "ACTIVE: system request=" + systemRequestSet +
                    ", display request=" + displayRequestSet +
                    ", execution-state fallback=" + (executionStateResult != 0) +
                    ", prevent idle lock=" + preventIdleLock + ".");

                // Refresh the execution-state fallback periodically. The power request stays
                // active for the lifetime of requestHandle and is visible in powercfg /requests.
                while (!stopEvent.WaitOne(TimeSpan.FromSeconds(15)))
                {
                    NativeMethods.SetThreadExecutionState(executionFlags);

                    if (preventIdleLock)
                    {
                        uint idleSeconds;
                        if (TryGetIdleSeconds(out idleSeconds) && idleSeconds >= 480)
                        {
                            PulseUserActivity();
                            WriteLog("IDLE RESET: sent F15 after " + idleSeconds + " idle seconds.");
                        }
                    }
                }

                WriteLog("STOPPED: stop signal received.");
                return 0;
            }
            catch (Exception exception)
            {
                WriteLog("ERROR: " + exception);
                return 1;
            }
            finally
            {
                if (requestHandle != IntPtr.Zero && requestHandle != new IntPtr(-1))
                {
                    if (displayRequestSet)
                    {
                        NativeMethods.PowerClearRequest(requestHandle, NativeMethods.PowerRequestType.DisplayRequired);
                    }

                    if (systemRequestSet)
                    {
                        NativeMethods.PowerClearRequest(requestHandle, NativeMethods.PowerRequestType.SystemRequired);
                    }

                    NativeMethods.CloseHandle(requestHandle);
                }

                NativeMethods.SetThreadExecutionState(NativeMethods.EsContinuous);

                if (reasonPointer != IntPtr.Zero)
                {
                    Marshal.FreeHGlobal(reasonPointer);
                }
            }
        }

        private static bool TryGetIdleSeconds(out uint idleSeconds)
        {
            NativeMethods.LastInputInfo info = new NativeMethods.LastInputInfo();
            info.Size = (uint)Marshal.SizeOf(typeof(NativeMethods.LastInputInfo));
            if (!NativeMethods.GetLastInputInfo(ref info))
            {
                idleSeconds = 0;
                return false;
            }

            uint currentTick = unchecked((uint)Environment.TickCount);
            idleSeconds = unchecked(currentTick - info.Time) / 1000;
            return true;
        }

        private static void PulseUserActivity()
        {
            // F15 produces no text and is the same low-impact technique used by
            // keep-awake utilities. Manual Win+L still moves to the secure desktop.
            NativeMethods.keybd_event(NativeMethods.VirtualKeyF15, 0, 0, UIntPtr.Zero);
            NativeMethods.keybd_event(NativeMethods.VirtualKeyF15, 0, NativeMethods.KeyEventKeyUp, UIntPtr.Zero);
        }

        private static void RotateLogIfNeeded()
        {
            try
            {
                if (File.Exists(LogPath) && new FileInfo(LogPath).Length > 1024 * 1024)
                {
                    File.Delete(LogPath + ".old");
                    File.Move(LogPath, LogPath + ".old");
                }
            }
            catch
            {
                // Logging must never stop the keep-awake request.
            }
        }

        private static void WriteLog(string message)
        {
            try
            {
                Directory.CreateDirectory(InstallDirectory);
                File.AppendAllText(
                    LogPath,
                    DateTimeOffset.Now.ToString("o") + " " + message + Environment.NewLine);
            }
            catch
            {
                // Logging must never stop the keep-awake request.
            }
        }
    }
}
