using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SocketIOClient;

namespace CG_Support.Service
{
    public class Worker : BackgroundService
    {
        private readonly ILogger<Worker> _logger;
        private SocketIO? _socketClient;
        private NamedPipeServerStream? _pipeServer;
        private StreamWriter? _pipeWriter;
        private bool _isAgentConnected = false;
        private CancellationTokenSource? _pipeTokenSource;
        private string _serverUrl = "http://localhost:3000"; // Configurable en producción

        public Worker(ILogger<Worker> logger)
        {
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Servicio CG_Support iniciando...");

            // 1. Iniciar el servidor local de Named Pipes para el Agente de sesión
            StartNamedPipeServer(stoppingToken);

            // 2. Conectar al broker de Node.js en segundo plano
            await InitializeWebSocket(stoppingToken);

            // 3. Loop del Watchdog: Verifica periódicamente que el agente esté ejecutándose en la sesión activa
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    CheckAndSpawnAgent();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error en el Watchdog al verificar el agente.");
                }

                await Task.Delay(5000, stoppingToken); // Verificar cada 5 segundos
            }
        }

        // --- WATCHDOG: RE LEVANTAR PROCESO DE SESIÓN ---
        private void CheckAndSpawnAgent()
        {
            // Obtener sesión activa
            uint activeSessionId = Win32Helper.GetActiveSessionId();
            if (activeSessionId == 0xFFFFFFFF)
            {
                return; // No hay usuario logueado en la PC
            }

            // Verificar si el Agente ya está corriendo
            var processes = Process.GetProcessesByName("CG_Support.Agent");
            if (processes.Length == 0)
            {
                _logger.LogWarning("Watchdog: No se encontró el Agente corriendo en la sesión de usuario. Iniciando...");
                
                // Ruta del Agente de sesión
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                string agentPath = Path.Combine(baseDir, "CG_Support.Agent.exe");
                
                // Si estamos en entorno de desarrollo, resolver ruta
                if (!File.Exists(agentPath))
                {
                    agentPath = Path.Combine(baseDir, @"..\CG_Support.Agent\bin\Debug\net9-windows\CG_Support.Agent.exe");
                }

                if (File.Exists(agentPath))
                {
                    bool success = Win32Helper.StartProcessInActiveSession(agentPath);
                    _logger.LogInformation("Spawning Agente en Sesión {Session}: {Result}", activeSessionId, success ? "ÉXITO" : "FALLÓ");
                }
                else
                {
                    _logger.LogError("Error: No se encontró el ejecutable del Agente en: {Path}", agentPath);
                }
            }
        }

        // --- SOCKET.IO CLIENT (CONEXIÓN AL BROKER) ---
        private async Task InitializeWebSocket(CancellationToken stoppingToken)
        {
            try
            {
                _socketClient = new SocketIO(new Uri(_serverUrl));

                _socketClient.OnConnected += async (sender, e) =>
                {
                    _logger.LogInformation("Servicio conectado al broker Node.js en {Url}", _serverUrl);

                    // Registrarse como estudiante
                    string hostname = Environment.MachineName;
                    string mac = Win32Helper.GetMacAddress();
                    string userName = Environment.UserName;

                    await _socketClient.EmitAsync("student_join", new object[] { new
                    {
                        hostname = hostname,
                        mac = mac,
                        userName = userName,
                        isLocked = false,
                        activeUrl = "",
                        activeWindow = "Pantalla Activa"
                    }});
                };

                _socketClient.OnDisconnected += (sender, e) =>
                {
                    _logger.LogWarning("Conexión perdida con el broker Node.js.");
                };

                // Escuchar comandos del tutor
                _socketClient.On("student_command", response =>
                {
                    try
                    {
                        var data = response.GetValue<dynamic>(0);
                        string command = data.GetProperty("command").GetString();
                        
                        _logger.LogInformation("Comando recibido del Tutor: {Cmd}", command);

                        // Reenviar comando al Agente de sesión mediante la Named Pipe
                        if (command == "lock")
                        {
                            bool lockState = data.GetProperty("value").GetBoolean();
                            SendPipeMessage(1, lockState ? "true" : "false");
                        }
                        else if (command == "block_web")
                        {
                            var list = data.GetProperty("value");
                            string json = list.ToString();
                            SendPipeMessage(3, json);
                        }
                        else if (command == "allow_all_web")
                        {
                            SendPipeMessage(4, "allow");
                        }
                        else if (command == "block_input")
                        {
                            bool blockState = data.GetProperty("value").GetBoolean();
                            SendPipeMessage(7, blockState ? "true" : "false");
                        }
                        else if (command == "start_screen_stream")
                        {
                            int fps = data.GetProperty("value").GetInt32();
                            SendPipeMessage(5, fps.ToString());
                        }
                        else if (command == "stop_screen_stream")
                        {
                            SendPipeMessage(5, "0");
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error al procesar comando del broker.");
                    }
                    return Task.CompletedTask;
                });

                // Escuchar eventos de control remoto (Teclado y Mouse) y mandarlos al agente
                _socketClient.On("student_mouse_event", response =>
                {
                    try
                    {
                        string json = response.GetValue<dynamic>(0).ToString();
                        SendPipeMessage(10, json); // Código 10 para eventos de ratón
                    }
                    catch { }
                    return Task.CompletedTask;
                });

                _socketClient.On("student_keyboard_event", response =>
                {
                    try
                    {
                        string json = response.GetValue<dynamic>(0).ToString();
                        SendPipeMessage(11, json); // Código 11 para eventos de teclado
                    }
                    catch { }
                    return Task.CompletedTask;
                });

                await _socketClient.ConnectAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al conectar al broker Socket.io.");
            }
        }

        // --- NAMED PIPES SERVER (IPC CON EL AGENTE) ---
        private void StartNamedPipeServer(CancellationToken stoppingToken)
        {
            Task.Run(async () =>
            {
                while (!stoppingToken.IsCancellationRequested)
                {
                    try
                    {
                        _logger.LogInformation("Esperando conexión del Agente de Sesión en la Pipe...");
                        
                        _pipeServer = new NamedPipeServerStream(
                            "CG_Support_IPC", 
                            PipeDirection.InOut, 
                            1, 
                            PipeTransmissionMode.Byte, 
                            PipeOptions.Asynchronous);

                        await _pipeServer.WaitForConnectionAsync(stoppingToken);
                        
                        _logger.LogInformation("Agente conectado a la Pipe.");
                        _isAgentConnected = true;
                        _pipeWriter = new StreamWriter(_pipeServer) { AutoFlush = true };
                        _pipeTokenSource = new CancellationTokenSource();

                        // Leer los datos que nos envía el agente (capturas de pantalla y URLs)
                        await ReadPipeDataAsync(_pipeServer, stoppingToken);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning("Conexión de la Pipe cerrada: {Msg}", ex.Message);
                    }
                    finally
                    {
                        _isAgentConnected = false;
                        _pipeWriter = null;
                        _pipeServer?.Dispose();
                        _pipeServer = null;
                    }

                    await Task.Delay(1000, stoppingToken);
                }
            }, stoppingToken);
        }

        private async Task ReadPipeDataAsync(NamedPipeServerStream pipe, CancellationToken stoppingToken)
        {
            byte[] lengthBuffer = new byte[4];
            byte[] typeBuffer = new byte[1];

            while (pipe.IsConnected && !stoppingToken.IsCancellationRequested)
            {
                // 1. Leer longitud del mensaje (4 bytes)
                int bytesRead = await pipe.ReadAsync(lengthBuffer, 0, 4, stoppingToken);
                if (bytesRead < 4) break;

                int length = BitConverter.ToInt32(lengthBuffer, 0);

                // 2. Leer tipo de mensaje (1 byte)
                bytesRead = await pipe.ReadAsync(typeBuffer, 0, 1, stoppingToken);
                if (bytesRead < 1) break;

                byte msgType = typeBuffer[0];

                // 3. Leer cuerpo del mensaje (longitud bytes)
                byte[] bodyBuffer = new byte[length];
                int totalBodyRead = 0;
                while (totalBodyRead < length)
                {
                    int read = await pipe.ReadAsync(bodyBuffer, totalBodyRead, length - totalBodyRead, stoppingToken);
                    if (read <= 0) break;
                    totalBodyRead += read;
                }

                if (totalBodyRead < length) break;

                // 4. Enrutar el mensaje recibido del Agente
                if (msgType == 5) // Frame de pantalla
                {
                    if (_socketClient != null && _socketClient.Connected)
                    {
                        // Enviar frame comprimido directo por socket
                        await _socketClient.EmitAsync("screen_frame", new object[] { bodyBuffer });
                    }
                }
                else if (msgType == 6) // Actualización de estado (Ventana, URL, Bloqueo)
                {
                    string json = Encoding.UTF8.GetString(bodyBuffer);
                    var status = JsonSerializer.Deserialize<dynamic>(json);
                    
                    if (_socketClient != null && _socketClient.Connected)
                    {
                        await _socketClient.EmitAsync("status_update", new object[] { status });
                    }
                }
            }
        }

        private void SendPipeMessage(byte type, string body)
        {
            if (!_isAgentConnected || _pipeServer == null || !_pipeServer.IsConnected) return;

            try
            {
                byte[] bodyBytes = Encoding.UTF8.GetBytes(body);
                byte[] lengthBytes = BitConverter.GetBytes(bodyBytes.Length);
                byte[] typeBytes = new byte[] { type };

                // Escribir cabecera e información
                _pipeServer.Write(lengthBytes, 0, 4);
                _pipeServer.Write(typeBytes, 0, 1);
                _pipeServer.Write(bodyBytes, 0, bodyBytes.Length);
                _pipeServer.Flush();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al enviar mensaje IPC al agente.");
            }
        }

        public override async Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Servicio CG_Support deteniéndose...");

            if (_socketClient != null)
            {
                _socketClient.DisconnectAsync();
                _socketClient.Dispose();
            }

            _pipeTokenSource?.Cancel();
            _pipeServer?.Dispose();

            await base.StopAsync(cancellationToken);
        }
    }
}
