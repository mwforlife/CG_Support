using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace CG_Support.Agent
{
    public partial class App : System.Windows.Application
    {
        private NamedPipeClientStream? pipeClient;
        private CancellationTokenSource? cts;
        private LockWindow? lockWindow;
        private string[] restrictedUrls = Array.Empty<string>();
        private bool isStreaming = false;
        private int streamingFps = 1;

        // --- WIN32 APIS PARA VENTANA ACTIVA ---
        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int count);

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            cts = new CancellationTokenSource();

            // Iniciar conexión con el servicio local permanente (SYSTEM)
            ConnectToServicePipe();

            // Iniciar hilo de monitoreo web y ventana activa
            StartStatusMonitor();

            // Iniciar hilo de captura de pantalla
            StartScreenStreaming();
        }

        // --- CONEXIÓN IPC (NAMED PIPE CLIENT) ---
        private void ConnectToServicePipe()
        {
            Task.Run(async () =>
            {
                while (!cts!.IsCancellationRequested)
                {
                    try
                    {
                        pipeClient = new NamedPipeClientStream(".", "CG_Support_IPC", PipeDirection.InOut);
                        await pipeClient.ConnectAsync(3000); // Esperar hasta 3s por el servicio

                        // Leer comandos desde el servicio
                        await ReadCommandsAsync(pipeClient);
                    }
                    catch
                    {
                        // Esperar 2 segundos si el servicio no está levantado
                        await Task.Delay(2000);
                    }
                }
            });
        }

        private async Task ReadCommandsAsync(NamedPipeClientStream pipe)
        {
            byte[] lengthBuffer = new byte[4];
            byte[] typeBuffer = new byte[1];

            while (pipe.IsConnected && !cts!.IsCancellationRequested)
            {
                int bytesRead = await pipe.ReadAsync(lengthBuffer, 0, 4);
                if (bytesRead < 4) break;

                int length = BitConverter.ToInt32(lengthBuffer, 0);

                bytesRead = await pipe.ReadAsync(typeBuffer, 0, 1);
                if (bytesRead < 1) break;

                byte type = typeBuffer[0];

                byte[] bodyBuffer = new byte[length];
                int totalRead = 0;
                while (totalRead < length)
                {
                    int read = await pipe.ReadAsync(bodyBuffer, totalRead, length - totalRead);
                    if (read <= 0) break;
                    totalRead += read;
                }

                if (totalRead < length) break;

                string body = Encoding.UTF8.GetString(bodyBuffer);

                // Procesar comando en el hilo principal
                ProcessCommand(type, body);
            }
        }

        private void ProcessCommand(byte type, string body)
        {
            Dispatcher.Invoke(() =>
            {
                switch (type)
                {
                    case 1: // Bloquear pantalla
                        if (body == "true")
                        {
                            if (lockWindow == null)
                            {
                                lockWindow = new LockWindow();
                                lockWindow.Show();
                            }
                        }
                        else
                        {
                            if (lockWindow != null)
                            {
                                lockWindow.UnlockAndClose();
                                lockWindow = null;
                            }
                        }
                        break;

                    case 3: // Configurar lista de restricción web
                        try
                        {
                            restrictedUrls = JsonSerializer.Deserialize<string[]>(body) ?? Array.Empty<string>();
                        }
                        catch { }
                        break;

                    case 4: // Permitir todas las webs (limpiar restricción)
                        restrictedUrls = Array.Empty<string>();
                        break;

                    case 5: // Activar/desactivar streaming de pantalla
                        if (int.TryParse(body, out int fps))
                        {
                            isStreaming = fps > 0;
                            streamingFps = fps > 0 ? fps : 1;
                        }
                        break;

                    case 7: // Bloquear entrada física de estudiante
                        InputSimulator.SetInputBlocked(body == "true");
                        break;

                    case 10: // Simulación de Mouse remoto
                        try
                        {
                            var mouse = JsonSerializer.Deserialize<JsonElement>(body);
                            string eventType = mouse.GetProperty("eventType").GetString() ?? "";
                            string button = mouse.GetProperty("button").GetString() ?? "left";
                            double ratioX = mouse.GetProperty("ratioX").GetDouble();
                            double ratioY = mouse.GetProperty("ratioY").GetDouble();

                            InputSimulator.SimulateMouse(eventType, button, ratioX, ratioY);
                        }
                        catch { }
                        break;

                    case 11: // Simulación de Teclado remoto
                        try
                        {
                            var keyboard = JsonSerializer.Deserialize<JsonElement>(body);
                            string eventType = keyboard.GetProperty("eventType").GetString() ?? "";
                            int keyCode = keyboard.GetProperty("keyCode").GetInt32();

                            InputSimulator.SimulateKeyboard(eventType, keyCode);
                        }
                        catch { }
                        break;
                }
            });
        }

        // --- ENVIAR DATOS AL SERVICIO IPC ---
        private void SendServiceMessage(byte type, byte[] bodyBytes)
        {
            if (pipeClient == null || !pipeClient.IsConnected) return;

            try
            {
                byte[] lengthBytes = BitConverter.GetBytes(bodyBytes.Length);
                byte[] typeBytes = new byte[] { type };

                lock (pipeClient)
                {
                    pipeClient.Write(lengthBytes, 0, 4);
                    pipeClient.Write(typeBytes, 0, 1);
                    pipeClient.Write(bodyBytes, 0, bodyBytes.Length);
                    pipeClient.Flush();
                }
            }
            catch { }
        }

        // --- MONITOREO DE ESTADO (URL & VENTANA) ---
        private void StartStatusMonitor()
        {
            Task.Run(async () =>
            {
                while (!cts!.IsCancellationRequested)
                {
                    try
                    {
                        // 1. Obtener ventana activa
                        string activeWindow = GetActiveWindowTitle();

                        // 2. Obtener URL del navegador activo
                        string activeUrl = BrowserMonitor.GetActiveBrowserUrl();

                        // 3. Bloqueo Web local si es necesario
                        if (!string.IsNullOrEmpty(activeUrl))
                        {
                            foreach (var url in restrictedUrls)
                            {
                                if (activeUrl.ToLower().Contains(url.ToLower()))
                                {
                                    // URL bloqueada: simular cierre de pestaña (Ctrl + W)
                                    InputSimulator.SimulateKeyboard("down", 17); // Ctrl
                                    InputSimulator.SimulateKeyboard("down", 87); // W
                                    InputSimulator.SimulateKeyboard("up", 87);
                                    InputSimulator.SimulateKeyboard("up", 17);
                                    activeWindow = "Navegación Bloqueada por Tutor";
                                    activeUrl = "Contenido Prohibido";
                                    break;
                                }
                            }
                        }

                        // 4. Reportar estado al servicio permanente
                        string jsonStatus = JsonSerializer.Serialize(new
                        {
                            activeWindow = activeWindow,
                            activeUrl = activeUrl,
                            isLocked = lockWindow != null,
                            userName = Environment.UserName
                        });

                        byte[] jsonBytes = Encoding.UTF8.GetBytes(jsonStatus);
                        SendServiceMessage(6, jsonBytes);
                    }
                    catch { }

                    await Task.Delay(2000); // Muestreo cada 2 segundos
                }
            });
        }

        private string GetActiveWindowTitle()
        {
            StringBuilder sb = new StringBuilder(256);
            IntPtr handle = GetForegroundWindow();
            if (GetWindowText(handle, sb, 256) > 0)
            {
                return sb.ToString();
            }
            return "Escritorio";
        }

        // --- CAPTURA Y STREAMING DE PANTALLA ---
        private void StartScreenStreaming()
        {
            Task.Run(async () =>
            {
                while (!cts!.IsCancellationRequested)
                {
                    if (isStreaming)
                    {
                        try
                        {
                            byte[] frame;

                            // Si la frecuencia de stream es alta (control remoto individual), mandar a pantalla completa
                            // Si es normal (miniatura de la grilla), mandar redimensionada para optimizar ancho de banda
                            if (streamingFps > 5)
                            {
                                frame = ScreenCapturer.CaptureScreen(0, 0, 50); // Alta velocidad, calidad media
                            }
                            else
                            {
                                frame = ScreenCapturer.CaptureScreen(320, 240, 60); // Miniatura liviana
                            }

                            if (frame.Length > 0)
                            {
                                SendServiceMessage(5, frame);
                            }
                        }
                        catch { }
                    }

                    int delayMs = 1000 / streamingFps;
                    await Task.Delay(Math.Max(10, delayMs));
                }
            });
        }

        protected override void OnExit(ExitEventArgs e)
        {
            cts?.Cancel();
            pipeClient?.Dispose();
            KeyboardHook.StopHook();
            base.OnExit(e);
        }
    }
}
