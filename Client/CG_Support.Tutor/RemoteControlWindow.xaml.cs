using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using SocketIOClient;

namespace CG_Support.Tutor
{
    public partial class RemoteControlWindow : Window
    {
        private readonly StudentModel student;
        private readonly SocketIO socketClient;

        public RemoteControlWindow(StudentModel student, SocketIO socketClient)
        {
            InitializeComponent();
            this.student = student;
            this.socketClient = socketClient;

            TxtStudentName.Text = student.Hostname;
            TxtStudentIp.Text = $"({student.IpAddress})";
            Title = $"Control Remoto - {student.Hostname}";

            // Subscribir al evento de actualización de frames usando una lambda implicita
            socketClient.On("screen_frame_received", response =>
            {
                try
                {
                    var data = response.GetValue<dynamic>(0);
                    string socketId = data.GetProperty("socketId").GetString();
                    
                    // Ignorar frames que no sean de este estudiante
                    if (socketId != student.SocketId) return Task.CompletedTask;

                    byte[] imageBytes = data.GetProperty("imageBytes").GetBytes();

                    Dispatcher.Invoke(() =>
                    {
                        ImgRemoteScreen.Source = BytesToBitmapImage(imageBytes);
                    });
                }
                catch { }
                return Task.CompletedTask;
            });

            // Aumentar la velocidad de actualización de este estudiante
            socketClient.EmitAsync("tutor_command", new object[] { new { targetSocketId = student.SocketId, command = "start_screen_stream", value = 15 } });

            // Capturar eventos de teclado de la ventana completa
            PreviewKeyDown += OnWindowKeyDown;
            PreviewKeyUp += OnWindowKeyUp;
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
            image.Freeze();
            return image;
        }

        // --- MANEJO DE ENTRADAS DEL MOUSE (ENVIADO COMO COORDENADAS RELATIVAS 0-1) ---

        private void ImgRemoteScreen_MouseMove(object sender, MouseEventArgs e)
        {
            SendMouseEvent("move", e);
        }

        private void ImgRemoteScreen_MouseDown(object sender, MouseButtonEventArgs e)
        {
            string button = e.ChangedButton == MouseButton.Left ? "left" : "right";
            SendMouseEvent("down", e, button);
        }

        private void ImgRemoteScreen_MouseUp(object sender, MouseButtonEventArgs e)
        {
            string button = e.ChangedButton == MouseButton.Left ? "left" : "right";
            SendMouseEvent("up", e, button);
        }

        private void SendMouseEvent(string eventType, MouseEventArgs e, string button = "left")
        {
            if (socketClient == null || !socketClient.Connected) return;

            // Calcular coordenadas relativas (porcentaje) para independizar la resolución
            double width = ImgRemoteScreen.ActualWidth;
            double height = ImgRemoteScreen.ActualHeight;

            if (width <= 0 || height <= 0) return;

            Point pos = e.GetPosition(ImgRemoteScreen);
            double ratioX = pos.X / width;
            double ratioY = pos.Y / height;

            // Validar límites
            if (ratioX < 0 || ratioX > 1 || ratioY < 0 || ratioY > 1) return;

            // Emitir evento al broker Node.js
            socketClient.EmitAsync("tutor_mouse_event", new object[] { new
            {
                targetSocketId = student.SocketId,
                eventType = eventType,
                button = button,
                ratioX = ratioX,
                ratioY = ratioY
            }});
        }

        // --- MANEJO DE TECLADO ---

        private void OnWindowKeyDown(object sender, KeyEventArgs e)
        {
            SendKeyboardEvent("down", e);
            e.Handled = true; // Evitar que WPF procese la tecla localmente
        }

        private void OnWindowKeyUp(object sender, KeyEventArgs e)
        {
            SendKeyboardEvent("up", e);
            e.Handled = true;
        }

        private void SendKeyboardEvent(string eventType, KeyEventArgs e)
        {
            if (socketClient == null || !socketClient.Connected) return;

            // Convertir WPF Key a Virtual Key de Win32
            int vkCode = KeyInterop.VirtualKeyFromKey(e.Key);

            socketClient.EmitAsync("tutor_keyboard_event", new object[] { new
            {
                targetSocketId = student.SocketId,
                eventType = eventType,
                keyCode = vkCode
            }});
        }

        // --- ACCIONES DE BLOQUEO DE INTRUSIÓN ---

        private void BtnBlockInput_Checked(object sender, RoutedEventArgs e)
        {
            if (socketClient != null && socketClient.Connected)
            {
                socketClient.EmitAsync("tutor_command", new object[] { new { targetSocketId = student.SocketId, command = "block_input", value = true } });
            }
        }

        private void BtnBlockInput_Unchecked(object sender, RoutedEventArgs e)
        {
            if (socketClient != null && socketClient.Connected)
            {
                socketClient.EmitAsync("tutor_command", new object[] { new { targetSocketId = student.SocketId, command = "block_input", value = false } });
            }
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        protected override void OnClosed(EventArgs e)
        {
            // Apagar comandos y bajar frecuencia de captura a normal (1 fps)
            if (socketClient != null && socketClient.Connected)
            {
                // Asegurar desbloqueo de entrada al salir del control
                socketClient.EmitAsync("tutor_command", new object[] { new { targetSocketId = student.SocketId, command = "block_input", value = false } });
                
                // Bajar frames de captura a 1 fps
                socketClient.EmitAsync("tutor_command", new object[] { new { targetSocketId = student.SocketId, command = "start_screen_stream", value = 1 } });
            }

            base.OnClosed(e);
        }
    }
}
