using System;
using System.Runtime.InteropServices;

namespace CG_Support.Agent
{
    public static class InputSimulator
    {
        // --- WIN32 CONSTANTES Y STRUCTS ---
        private const int INPUT_MOUSE = 0;
        private const int INPUT_KEYBOARD = 1;

        private const uint MOUSEEVENTF_MOVE = 0x0001;
        private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
        private const uint MOUSEEVENTF_LEFTUP = 0x0004;
        private const uint MOUSEEVENTF_RIGHTDOWN = 0x0008;
        private const uint MOUSEEVENTF_RIGHTUP = 0x0010;
        private const uint MOUSEEVENTF_ABSOLUTE = 0x8000;

        private const uint KEYEVENTF_KEYDOWN = 0x0000;
        private const uint KEYEVENTF_KEYUP = 0x0002;

        [StructLayout(LayoutKind.Sequential)]
        private struct MOUSEINPUT
        {
            public int dx;
            public int dy;
            public uint mouseData;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct KEYBDINPUT
        {
            public ushort wVk;
            public ushort wScan;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Explicit)]
        private struct INPUT_UNION
        {
            [FieldOffset(0)]
            public MOUSEINPUT mi;
            [FieldOffset(0)]
            public KEYBDINPUT ki;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct INPUT
        {
            public uint type;
            public INPUT_UNION U;
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

        [DllImport("user32.dll")]
        private static extern bool BlockInput(bool fBlockIt);

        // --- MÉTODOS DE SIMULACIÓN ---

        // Bloquear entrada física
        public static void SetInputBlocked(bool block)
        {
            try
            {
                BlockInput(block);
            }
            catch { }
        }

        // Simular evento de ratón
        public static void SimulateMouse(string eventType, string button, double ratioX, double ratioY)
        {
            // Convertir coordenadas relativas (0-1) a coordenadas de pantalla absoluta de SendInput (0-65535)
            int absoluteX = (int)(ratioX * 65535);
            int absoluteY = (int)(ratioY * 65535);

            INPUT input = new INPUT
            {
                type = INPUT_MOUSE,
                U = new INPUT_UNION
                {
                    mi = new MOUSEINPUT
                    {
                        dx = absoluteX,
                        dy = absoluteY,
                        mouseData = 0,
                        dwFlags = MOUSEEVENTF_ABSOLUTE,
                        time = 0,
                        dwExtraInfo = IntPtr.Zero
                    }
                }
            };

            if (eventType == "move")
            {
                input.U.mi.dwFlags |= MOUSEEVENTF_MOVE;
            }
            else if (eventType == "down")
            {
                input.U.mi.dwFlags |= (button == "left") ? MOUSEEVENTF_LEFTDOWN : MOUSEEVENTF_RIGHTDOWN;
            }
            else if (eventType == "up")
            {
                input.U.mi.dwFlags |= (button == "left") ? MOUSEEVENTF_LEFTUP : MOUSEEVENTF_RIGHTUP;
            }

            SendInput(1, new INPUT[] { input }, Marshal.SizeOf(typeof(INPUT)));
        }

        // Simular evento de teclado
        public static void SimulateKeyboard(string eventType, int vkCode)
        {
            INPUT input = new INPUT
            {
                type = INPUT_KEYBOARD,
                U = new INPUT_UNION
                {
                    ki = new KEYBDINPUT
                    {
                        wVk = (ushort)vkCode,
                        wScan = 0,
                        dwFlags = (eventType == "up") ? KEYEVENTF_KEYUP : KEYEVENTF_KEYDOWN,
                        time = 0,
                        dwExtraInfo = IntPtr.Zero
                    }
                }
            };

            SendInput(1, new INPUT[] { input }, Marshal.SizeOf(typeof(INPUT)));
        }
    }
}
