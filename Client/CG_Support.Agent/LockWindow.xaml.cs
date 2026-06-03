using System.ComponentModel;
using System.Windows;

namespace CG_Support.Agent
{
    public partial class LockWindow : Window
    {
        private bool _allowClose = false;

        public LockWindow()
        {
            InitializeComponent();
            
            // Suscribirse a eventos de foco y estado de ventana
            Loaded += LockWindow_Loaded;
            Closing += LockWindow_Closing;
        }

        private void LockWindow_Loaded(object sender, RoutedEventArgs e)
        {
            // Activar el gancho global de teclado para inhabilitar atajos de escape
            KeyboardHook.StartHook();
            
            // Forzar el foco y estado topmost de forma agresiva
            Focus();
            Activate();
        }

        private void LockWindow_Closing(object sender, CancelEventArgs e)
        {
            // Evitar que la ventana sea cerrada a menos que el servicio IPC lo ordene explícitamente
            if (!_allowClose)
            {
                e.Cancel = true;
            }
        }

        // Método invocado desde el cliente IPC para desbloquear y cerrar la ventana limpiamente
        public void UnlockAndClose()
        {
            _allowClose = true;
            KeyboardHook.StopHook();
            Close();
        }
    }
}
