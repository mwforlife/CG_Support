using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Input;

namespace CG_Support.Agent
{
    public static class KeyboardHook
    {
        private const int WH_KEYBOARD_LL = 13;
        private const int WM_KEYDOWN = 0x0100;
        private const int WM_SYSKEYDOWN = 0x0104;

        private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);
        private static LowLevelKeyboardProc? _proc;
        private static IntPtr _hookID = IntPtr.Zero;
        private static bool _isHookActive = false;

        public static void StartHook()
        {
            if (_isHookActive) return;
            _proc = HookCallback;
            _hookID = SetHook(_proc);
            _isHookActive = true;
        }

        public static void StopHook()
        {
            if (!_isHookActive) return;
            UnhookWindowsHookEx(_hookID);
            _isHookActive = false;
        }

        private static IntPtr SetHook(LowLevelKeyboardProc proc)
        {
            using (Process curProcess = Process.GetCurrentProcess())
            using (ProcessModule curModule = curProcess.MainModule!)
            {
                return SetWindowsHookEx(WH_KEYBOARD_LL, proc, GetModuleHandle(curModule.ModuleName), 0);
            }
        }

        private static IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0 && (wParam == (IntPtr)WM_KEYDOWN || wParam == (IntPtr)WM_SYSKEYDOWN))
            {
                int vkCode = Marshal.ReadInt32(lParam);
                
                // Atajos de Windows a bloquear:
                // 1. Teclas Windows (91, 92) y Menu Contextual (93)
                // 2. Alt+Tab (vkCode == 9 y Alt presionado)
                // 3. Alt+F4 (vkCode == 115 y Alt presionado)
                // 4. Ctrl+Esc (vkCode == 27 y Ctrl presionado)
                bool alt = (Keyboard.Modifiers & ModifierKeys.Alt) != 0;
                bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;

                if (vkCode == 91 || vkCode == 92 || vkCode == 93 || // Windows Keys
                    (vkCode == 9 && alt) ||                          // Alt+Tab
                    (vkCode == 115 && alt) ||                        // Alt+F4
                    (vkCode == 27 && ctrl) ||                        // Ctrl+Esc
                    (vkCode == 27 && alt))                           // Alt+Esc
                {
                    // Retornar 1 consume la pulsación de la tecla y la deshabilita
                    return (IntPtr)1;
                }
            }
            return CallNextHookEx(_hookID, nCode, wParam, lParam);
        }

        // --- IMPORTS NATIVOS ---
        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string lpModuleName);
    }
}
