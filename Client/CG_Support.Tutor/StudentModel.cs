using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;

namespace CG_Support.Tutor
{
    public class StudentModel : INotifyPropertyChanged
    {
        private string socketId = string.Empty;
        private string hostname = string.Empty;
        private string ipAddress = string.Empty;
        private string macAddress = string.Empty;
        private string userName = string.Empty;
        private bool isLocked;
        private string activeUrl = string.Empty;
        private string activeWindow = string.Empty;
        private bool isStreaming;
        private ImageSource? screenImage;

        public string SocketId
        {
            get => socketId;
            set => SetField(ref socketId, value);
        }

        public string Hostname
        {
            get => hostname;
            set => SetField(ref hostname, value);
        }

        public string IpAddress
        {
            get => ipAddress;
            set => SetField(ref ipAddress, value);
        }

        public string MacAddress
        {
            get => macAddress;
            set => SetField(ref macAddress, value);
        }

        public string UserName
        {
            get => userName;
            set => SetField(ref userName, value);
        }

        public bool IsLocked
        {
            get => isLocked;
            set => SetField(ref isLocked, value);
        }

        public string ActiveUrl
        {
            get => activeUrl;
            set => SetField(ref activeUrl, value);
        }

        public string ActiveWindow
        {
            get => activeWindow;
            set => SetField(ref activeWindow, value);
        }

        public bool IsStreaming
        {
            get => isStreaming;
            set => SetField(ref isStreaming, value);
        }

        public ImageSource? ScreenImage
        {
            get => screenImage;
            set => SetField(ref screenImage, value);
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        protected bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return false;
            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }
    }
}
