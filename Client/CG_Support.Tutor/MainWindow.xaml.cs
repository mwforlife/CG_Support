using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SocketIOClient;

namespace CG_Support.Tutor
{
    public partial class MainWindow : Window
    {
        private Process? nodeProcess;
        private SocketIO? socketClient;
        public ObservableCollection<StudentModel> ConnectedStudents { get; set; } = new ObservableCollection<StudentModel>();

        public MainWindow()
        {
            InitializeComponent();
            StudentsItemsControl.ItemsSource = ConnectedStudents;

            // Arrancar el Servidor Node.js en segundo plano
            StartNodeServer();

            // Conectar el cliente WebSocket al Servidor Node.js
            InitializeWebSocket();
        }

        private void StartNodeServer()
        {
            try
            {
                // Ruta del servidor local en XAMPP
                string serverPath = @"c:\xampp\htdocs\CG_Support\Server";
                
                // Si la ruta absoluta no existe, buscar de manera relativa al ejecutable (para publicación)
                if (!Directory.Exists(serverPath))
                {
                    serverPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Server");
                }

                if (!Directory.Exists(serverPath))
                {
                    MessageBox.Show($"Advertencia: No se encontró la carpeta del servidor en '{serverPath}'. Asegúrate de que Node.js esté corriendo.", "Servidor No Encontrado", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                ProcessStartInfo startInfo = new ProcessStartInfo
                {
                    FileName = "node",
                    Arguments = "server.js",
                    WorkingDirectory = serverPath,
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = false,
                    RedirectStandardError = false
                };

                nodeProcess = new Process { StartInfo = startInfo };
                nodeProcess.Start();

                TxtStatus.Text = "Servidor iniciado localmente. Conectando...";
                LedConnection.Fill = new SolidColorBrush(Color.FromRgb(255, 204, 0)); // Amarillo (Conectando)
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error al intentar levantar el servidor Node.js: {ex.Message}\n\nPor favor, verifica que tengas Node.js instalado en el PATH del sistema.", "Error de Inicialización", MessageBoxButton.OK, MessageBoxImage.Error);
                TxtStatus.Text = "Error al iniciar servidor.";
                LedConnection.Fill = new SolidColorBrush(Color.FromRgb(255, 59, 48)); // Rojo
            }
        }

        private async void InitializeWebSocket()
        {
            try
            {
                // Esperar 1 segundo para dar tiempo a que el servidor inicialice sus puertos
                await Task.Delay(1000);

                // SocketIO requiere una instancia de Uri en esta versión
                socketClient = new SocketIO(new Uri("http://localhost:3000"));

                socketClient.OnConnected += (sender, e) =>
                {
                    Dispatcher.Invoke(() =>
                    {
                        LedConnection.Fill = new SolidColorBrush(Color.FromRgb(39, 174, 96)); // Verde
                        TxtStatus.Text = "Servidor Activo (Conectado)";
                        TxtInfoFooter.Text = "Conectado al broker de comunicaciones en http://localhost:3000";
                    });

                    // Registrarse como Tutor ante el broker
                    socketClient.EmitAsync("tutor_join");
                };

                socketClient.OnDisconnected += (sender, e) =>
                {
                    Dispatcher.Invoke(() =>
                    {
                        LedConnection.Fill = new SolidColorBrush(Color.FromRgb(255, 59, 48)); // Rojo
                        TxtStatus.Text = "Desconectado. Reintentando...";
                        ConnectedStudents.Clear();
                        TxtCounters.Text = "PCs Conectados: 0 / 150";
                    });
                };

                // Evento: Carga la lista inicial de estudiantes
                socketClient.On("students_list", response =>
                {
                    var students = response.GetValue<StudentModel[]>(0);
                    Dispatcher.Invoke(() =>
                    {
                        ConnectedStudents.Clear();
                        foreach (var student in students)
                        {
                            // Iniciar captura para este estudiante de manera automática en el broker
                            socketClient.EmitAsync("tutor_command", new object[] { new { targetSocketId = student.SocketId, command = "start_screen_stream", value = 1 } });
                            student.IsStreaming = true;
                            ConnectedStudents.Add(student);
                        }
                        UpdateCounters();
                    });
                    return Task.CompletedTask;
                });

                // Evento: Se conecta un estudiante nuevo
                socketClient.On("student_connected", response =>
                {
                    var student = response.GetValue<StudentModel>(0);
                    Dispatcher.Invoke(() =>
                    {
                        if (ConnectedStudents.All(s => s.SocketId != student.SocketId))
                        {
                            // Iniciar captura para este estudiante de manera automática
                            socketClient.EmitAsync("tutor_command", new object[] { new { targetSocketId = student.SocketId, command = "start_screen_stream", value = 1 } });
                            student.IsStreaming = true;
                            ConnectedStudents.Add(student);
                            UpdateCounters();
                        }
                    });
                    return Task.CompletedTask;
                });

                // Evento: Se desconecta un estudiante
                socketClient.On("student_disconnected", response =>
                {
                    var data = response.GetValue<dynamic>(0);
                    string socketId = data.GetProperty("socketId").GetString();
                    Dispatcher.Invoke(() =>
                    {
                        var student = ConnectedStudents.FirstOrDefault(s => s.SocketId == socketId);
                        if (student != null)
                        {
                            ConnectedStudents.Remove(student);
                            UpdateCounters();
                        }
                    });
                    return Task.CompletedTask;
                });

                // Evento: Actualización de estado del estudiante (URL, Ventana Activa, Bloqueo)
                socketClient.On("student_updated", response =>
                {
                    var updatedData = response.GetValue<StudentModel>(0);
                    Dispatcher.Invoke(() =>
                    {
                        var student = ConnectedStudents.FirstOrDefault(s => s.SocketId == updatedData.SocketId);
                        if (student != null)
                        {
                            student.ActiveUrl = updatedData.ActiveUrl;
                            student.ActiveWindow = updatedData.ActiveWindow;
                            student.IsLocked = updatedData.IsLocked;
                            student.UserName = updatedData.UserName;
                        }
                    });
                    return Task.CompletedTask;
                });

                // Evento: Recibe un frame de pantalla comprimido
                socketClient.On("screen_frame_received", response =>
                {
                    var data = response.GetValue<dynamic>(0);
                    string socketId = data.GetProperty("socketId").GetString();
                    byte[] imageBytes = data.GetProperty("imageBytes").GetBytes();

                    Dispatcher.Invoke(() =>
                    {
                        var student = ConnectedStudents.FirstOrDefault(s => s.SocketId == socketId);
                        if (student != null)
                        {
                            student.ScreenImage = BytesToBitmapImage(imageBytes);
                        }
                    });
                    return Task.CompletedTask;
                });

                await socketClient.ConnectAsync();
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() =>
                {
                    TxtInfoFooter.Text = $"Error de comunicación socket: {ex.Message}";
                });
            }
        }

        private BitmapImage BytesToBitmapImage(byte[] bytes)
        {
            var image = new BitmapImage();
            using (var ms = new MemoryStream(bytes))
            {
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.StreamSource = ms;
                image.EndInit();
            }
            image.Freeze(); // Permite usar la imagen en hilos UI de manera segura
            return image;
        }

        private void UpdateCounters()
        {
            TxtCounters.Text = $"PCs Conectados: {ConnectedStudents.Count} / 150";
        }

        // --- MANEJADORES DE CLICS DEL ENCABEZADO ---

        private void BtnWol_Click(object sender, RoutedEventArgs e)
        {
            if (socketClient != null && socketClient.Connected)
            {
                MessageBoxResult result = MessageBox.Show("¿Deseas enviar señal de encendido Wake-on-LAN a todos los equipos configurados?", "Wake-on-LAN", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (result == MessageBoxResult.Yes)
                {
                    // Enviar WOL de prueba o recorrer historial
                    socketClient.EmitAsync("tutor_wol_request", new object[] { new { macAddress = "FF-FF-FF-FF-FF-FF" } });
                    TxtInfoFooter.Text = "Señal Wake-on-LAN enviada a la red local.";
                }
            }
        }

        private void BtnLockAll_Click(object sender, RoutedEventArgs e)
        {
            if (socketClient != null && socketClient.Connected)
            {
                socketClient.EmitAsync("tutor_command", new object[] { new { targetSocketId = "all", command = "lock", value = true } });
                TxtInfoFooter.Text = "Comando enviado: Bloquear pantallas de todos los estudiantes.";
            }
        }

        private void BtnUnlockAll_Click(object sender, RoutedEventArgs e)
        {
            if (socketClient != null && socketClient.Connected)
            {
                socketClient.EmitAsync("tutor_command", new object[] { new { targetSocketId = "all", command = "lock", value = false } });
                TxtInfoFooter.Text = "Comando enviado: Desbloquear pantallas de todos los estudiantes.";
            }
        }

        private void BtnBlockWeb_Click(object sender, RoutedEventArgs e)
        {
            if (socketClient != null && socketClient.Connected)
            {
                // Enviar lista negra de ejemplo (redes sociales y juegos comunes)
                string[] restrictedUrls = { "facebook.com", "youtube.com", "instagram.com", "friv.com", "krunker.io" };
                socketClient.EmitAsync("tutor_command", new object[] { new { targetSocketId = "all", command = "block_web", value = restrictedUrls } });
                TxtInfoFooter.Text = "Comando enviado: Activar restricción web (Bloqueo de redes sociales y juegos).";
            }
        }

        private void BtnAllowWeb_Click(object sender, RoutedEventArgs e)
        {
            if (socketClient != null && socketClient.Connected)
            {
                socketClient.EmitAsync("tutor_command", new object[] { new { targetSocketId = "all", command = "allow_all_web", value = true } });
                TxtInfoFooter.Text = "Comando enviado: Permitir acceso completo a internet.";
            }
        }

        // --- ACCIONES INDIVIDUALES POR TARJETA ---

        private void BtnLockIndividual_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is StudentModel student && socketClient != null && socketClient.Connected)
            {
                bool newLockState = !student.IsLocked;
                socketClient.EmitAsync("tutor_command", new object[] { new { targetSocketId = student.SocketId, command = "lock", value = newLockState } });
                TxtInfoFooter.Text = $"Comando enviado: {(newLockState ? "Bloquear" : "Desbloquear")} PC {student.Hostname}";
            }
        }

        private void BtnMonitorIndividual_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is StudentModel student)
            {
                OpenRemoteControl(student);
            }
        }

        private void OpenRemoteControl(StudentModel student)
        {
            if (socketClient == null || !socketClient.Connected) return;

            // Abrir la ventana nativa de control remoto
            RemoteControlWindow remoteWindow = new RemoteControlWindow(student, socketClient);
            remoteWindow.Owner = this;
            remoteWindow.Show();
        }

        protected override void OnClosed(EventArgs e)
        {
            // Desconectar sockets
            if (socketClient != null)
            {
                socketClient.DisconnectAsync();
                socketClient.Dispose();
            }

            // Matar proceso del servidor Node.js para que no quede huérfano
            if (nodeProcess != null && !nodeProcess.HasExited)
            {
                try
                {
                    nodeProcess.Kill();
                    nodeProcess.Dispose();
                }
                catch { }
            }

            base.OnClosed(e);
        }
    }
}