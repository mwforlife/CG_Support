using System;
using System.Diagnostics;
using System.Linq;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;

namespace CG_Support.Service
{
    public static class Win32Helper
    {
        // --- APIS PARA OBTENER LA MAC ADDRESS ---
        public static string GetMacAddress()
        {
            try
            {
                foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
                {
                    // Buscar la interfaz ethernet o wifi activa y no virtual
                    if (nic.OperationalStatus == OperationalStatus.Up && 
                        (nic.NetworkInterfaceType == NetworkInterfaceType.Ethernet || 
                         nic.NetworkInterfaceType == NetworkInterfaceType.Wireless80211) &&
                        !nic.Description.ToLower().Contains("virtual") &&
                        !nic.Description.ToLower().Contains("pseudo"))
                    {
                        return string.Join(":", nic.GetPhysicalAddress().GetAddressBytes().Select(b => b.ToString("X2")));
                    }
                }
            }
            catch { }
            return "00:00:00:00:00:00";
        }

        // --- APIS PARA LA INTRUSIÓN Y PROCESOS EN SESIÓN DE USUARIO ---
        [DllImport("wtsapi32.dll", SetLastError = true)]
        private static extern bool WTSQueryUserToken(uint sessionId, out IntPtr phToken);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr hObject);

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern bool CreateProcessAsUser(
            IntPtr hToken,
            string? lpApplicationName,
            string? lpCommandLine,
            IntPtr lpProcessAttributes,
            IntPtr lpThreadAttributes,
            bool bInheritHandles,
            uint dwCreationFlags,
            IntPtr lpEnvironment,
            string? lpCurrentDirectory,
            ref STARTUPINFO lpStartupInfo,
            out PROCESS_INFORMATION lpProcessInformation);

        [DllImport("wtsapi32.dll")]
        private static extern IntPtr WTSOpenServer(string pServerName);

        [DllImport("wtsapi32.dll")]
        private static extern void WTSCloseServer(IntPtr hServer);

        [DllImport("wtsapi32.dll")]
        private static extern int WTSEnumerateSessions(IntPtr hServer, int Reserved, int Version, ref IntPtr ppSessionInfo, ref int pCount);

        [DllImport("wtsapi32.dll")]
        private static extern void WTSFreeMemory(IntPtr pMemory);

        [StructLayout(LayoutKind.Sequential)]
        private struct WTS_SESSION_INFO
        {
            public uint SessionId;
            [MarshalAs(UnmanagedType.LPStr)]
            public string pWinStationName;
            public WTS_CONNECTSTATE_CLASS State;
        }

        private enum WTS_CONNECTSTATE_CLASS
        {
            WTSActive,
            WTSConnected,
            WTSConnectQuery,
            WTSShadow,
            WTSDisconnected,
            WTSIdle,
            WTSListen,
            WTSReset,
            WTSDown,
            WTSInit
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct STARTUPINFO
        {
            public int cb;
            public string lpReserved;
            public string lpDesktop;
            public string lpTitle;
            public int dwX;
            public int dwY;
            public int dwXSize;
            public int dwYSize;
            public int dwXCountChars;
            public int dwYCountChars;
            public int dwFillAttribute;
            public int dwFlags;
            public short wShowWindow;
            public short cbReserved2;
            public IntPtr lpReserved2;
            public IntPtr hStdInput;
            public IntPtr hStdOutput;
            public IntPtr hStdError;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct PROCESS_INFORMATION
        {
            public IntPtr hProcess;
            public IntPtr hThread;
            public int dwProcessId;
            public int dwThreadId;
        }

        // Obtener el ID de la sesión de usuario activa (usualmente Session 1 en adelante)
        public static uint GetActiveSessionId()
        {
            IntPtr server = WTSOpenServer(null!);
            IntPtr ppSessionInfo = IntPtr.Zero;
            int count = 0;
            uint activeSessionId = 0xFFFFFFFF; // Invalido

            try
            {
                if (WTSEnumerateSessions(server, 0, 1, ref ppSessionInfo, ref count) != 0)
                {
                    int structSize = Marshal.SizeOf(typeof(WTS_SESSION_INFO));
                    long current = (long)ppSessionInfo;

                    for (int i = 0; i < count; i++)
                    {
                        WTS_SESSION_INFO si = (WTS_SESSION_INFO)Marshal.PtrToStructure((IntPtr)current, typeof(WTS_SESSION_INFO))!;
                        current += structSize;

                        if (si.State == WTS_CONNECTSTATE_CLASS.WTSActive)
                        {
                            activeSessionId = si.SessionId;
                            break;
                        }
                    }
                }
            }
            catch { }
            finally
            {
                if (ppSessionInfo != IntPtr.Zero)
                {
                    WTSFreeMemory(ppSessionInfo);
                }
                WTSCloseServer(server);
            }
            return activeSessionId;
        }

        // Ejecutar un proceso en la sesión del usuario activo desde Session 0 (SYSTEM)
        public static bool StartProcessInActiveSession(string applicationPath)
        {
            uint sessionId = GetActiveSessionId();
            if (sessionId == 0xFFFFFFFF)
            {
                return false; // No hay sesión de usuario activa
            }

            IntPtr userToken = IntPtr.Zero;
            if (!WTSQueryUserToken(sessionId, out userToken))
            {
                return false;
            }

            STARTUPINFO si = new STARTUPINFO();
            si.cb = Marshal.SizeOf(si);
            si.lpDesktop = @"winsta0\default"; // Indispensable para mostrar interfaz gráfica

            PROCESS_INFORMATION pi = new PROCESS_INFORMATION();

            // Spawn del proceso
            bool result = CreateProcessAsUser(
                userToken,
                applicationPath,
                null,
                IntPtr.Zero,
                IntPtr.Zero,
                false,
                0x00000010, // CREATE_NEW_CONSOLE
                IntPtr.Zero,
                Path.GetDirectoryName(applicationPath),
                ref si,
                out pi);

            if (userToken != IntPtr.Zero) CloseHandle(userToken);
            if (pi.hProcess != IntPtr.Zero) CloseHandle(pi.hProcess);
            if (pi.hThread != IntPtr.Zero) CloseHandle(pi.hThread);

            return result;
        }
    }
}
