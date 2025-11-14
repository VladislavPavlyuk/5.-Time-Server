using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace TimeClient
{
    public partial class MainWindow : Window
    {
        private UdpClient? _udpClient;
        private IPEndPoint? _serverEndPoint;
        private int _clientPort = 49153;
        private bool _isConnected = false;
        private DispatcherTimer? _updateTimer;
        private string _currentTime = "--:--:--";
        private TimeDisplayWindow? _timeDisplayWindow;

        public MainWindow()
        {
            InitializeComponent();
            
            // Set window icon
            try
            {
                using (var stream = System.Windows.Application.GetResourceStream(
                    new Uri("pack://application:,,,/app.ico"))?.Stream)
                {
                    if (stream != null)
                    {
                        this.Icon = System.Windows.Media.Imaging.BitmapFrame.Create(stream);
                    }
                }
            }
            catch
            {
                // Icon file not found or invalid, continue without icon
            }
        }

        private async void ConnectButton_Click(object sender, RoutedEventArgs e)
        {
            if (!_isConnected)
            {
                await Connect();
            }
            else
            {
                Disconnect();
            }
        }

        private async Task Connect()
        {
            try
            {
                if (!IPAddress.TryParse(ServerIpTextBox.Text, out IPAddress? serverIp))
                {
                    MessageBox.Show("Invalid server IP address", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                if (!int.TryParse(ServerPortTextBox.Text, out int serverPort))
                {
                    MessageBox.Show("Invalid server port", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                if (!int.TryParse(ClientPortTextBox.Text, out _clientPort))
                {
                    MessageBox.Show("Invalid client port", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                _serverEndPoint = new IPEndPoint(serverIp, serverPort);
                _udpClient = new UdpClient(_clientPort);

                // Send connection message to server with client receive port
                string connectMessage = $"CONNECT:{_clientPort}";
                byte[] connectData = Encoding.UTF8.GetBytes(connectMessage);
                await _udpClient.SendAsync(connectData, connectData.Length, _serverEndPoint);

                _isConnected = true;
                ConnectButton.Content = "Disconnect";
                StatusTextBlock.Text = $"Connected to {serverIp}:{serverPort}";
                ServerIpTextBox.IsEnabled = false;
                ServerPortTextBox.IsEnabled = false;
                ClientPortTextBox.IsEnabled = false;

                // Create and show time display window
                _timeDisplayWindow = new TimeDisplayWindow();
                _timeDisplayWindow.Show();

                // Start receiving messages
                _ = Task.Run(ReceiveMessages);

                // Start update timer for UI refresh
                _updateTimer = new DispatcherTimer();
                _updateTimer.Interval = TimeSpan.FromMilliseconds(100);
                _updateTimer.Tick += UpdateTimer_Tick;
                _updateTimer.Start();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error connecting: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void Disconnect()
        {
            try
            {
                if (_udpClient != null && _serverEndPoint != null)
                {
                    // Send disconnect message with client receive port
                    string disconnectMessage = $"DISCONNECT:{_clientPort}";
                    byte[] disconnectData = Encoding.UTF8.GetBytes(disconnectMessage);
                    _udpClient.Send(disconnectData, disconnectData.Length, _serverEndPoint);
                }

                _udpClient?.Close();
                _udpClient?.Dispose();
                _udpClient = null;

                _updateTimer?.Stop();
                _updateTimer = null;

                _isConnected = false;
                ConnectButton.Content = "Connect";
                StatusTextBlock.Text = "Disconnected";
                ServerIpTextBox.IsEnabled = true;
                ServerPortTextBox.IsEnabled = true;
                ClientPortTextBox.IsEnabled = true;

                _currentTime = "--:--:--";
                TimeTextBlock.Text = _currentTime;

                // Close time display window
                _timeDisplayWindow?.Close();
                _timeDisplayWindow = null;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error disconnecting: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async Task ReceiveMessages()
        {
            while (_isConnected && _udpClient != null)
            {
                try
                {
                    UdpReceiveResult result = await _udpClient.ReceiveAsync();
                    string message = Encoding.UTF8.GetString(result.Buffer);

                    // Parse JSON message
                    var timeData = JsonSerializer.Deserialize<TimeData>(message);
                    if (timeData != null)
                    {
                        _currentTime = timeData.Time;
                        
                        // Update UI on UI thread
                        Dispatcher.Invoke(() =>
                        {
                            TimeTextBlock.Text = _currentTime;
                            _timeDisplayWindow?.UpdateTime(_currentTime);
                        });
                    }
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Dispatcher.Invoke(() =>
                    {
                        StatusTextBlock.Text = $"Error: {ex.Message}";
                    });
                }
            }
        }

        private void UpdateTimer_Tick(object? sender, EventArgs e)
        {
            // This timer ensures UI updates smoothly
        }

        protected override void OnClosed(EventArgs e)
        {
            _timeDisplayWindow?.Close();
            Disconnect();
            base.OnClosed(e);
        }
    }

    public class TimeData
    {
        public string Time { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; }
    }
}

