using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
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

        public MainWindow()
        {
            InitializeComponent();
            Loaded += MainWindow_Loaded;
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            // Ensure canvas is properly sized
            TimeCanvas.SizeChanged += TimeCanvas_SizeChanged;
        }

        private void TimeCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (_isConnected && !string.IsNullOrEmpty(_currentTime) && _currentTime != "--:--:--")
            {
                DrawTimeRegion(_currentTime);
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
                TimeCanvas.Children.Clear();
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
                            DrawTimeRegion(_currentTime);
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

        private void DrawTimeRegion(string timeString)
        {
            TimeCanvas.Children.Clear();

            if (string.IsNullOrEmpty(timeString) || timeString == "--:--:--")
                return;

            double canvasWidth = TimeCanvas.ActualWidth;
            double canvasHeight = TimeCanvas.ActualHeight;

            if (canvasWidth <= 0 || canvasHeight <= 0)
                return;

            // Create a path geometry for the time trajectory
            PathGeometry geometry = new PathGeometry();
            PathFigure figure = new PathFigure();
            
            double centerX = canvasWidth / 2;
            double centerY = canvasHeight / 2;
            double radius = Math.Min(canvasWidth, canvasHeight) * 0.35;

            // Parse time
            string[] parts = timeString.Split(':');
            if (parts.Length != 3) return;

            if (!int.TryParse(parts[0], out int hours) ||
                !int.TryParse(parts[1], out int minutes) ||
                !int.TryParse(parts[2], out int seconds))
                return;

            // Normalize values
            double hourAngle = (hours % 12) * 30 + minutes * 0.5 - 90; // -90 to start at top
            double minuteAngle = minutes * 6 + seconds * 0.1 - 90;
            double secondAngle = seconds * 6 - 90;

            // Convert to radians
            double hourRad = hourAngle * Math.PI / 180;
            double minuteRad = minuteAngle * Math.PI / 180;
            double secondRad = secondAngle * Math.PI / 180;

            // Calculate hour hand position
            double hourX = centerX + radius * 0.6 * Math.Cos(hourRad);
            double hourY = centerY + radius * 0.6 * Math.Sin(hourRad);

            // Calculate minute hand position
            double minuteX = centerX + radius * 0.8 * Math.Cos(minuteRad);
            double minuteY = centerY + radius * 0.8 * Math.Sin(minuteRad);

            // Calculate second hand position
            double secondX = centerX + radius * Math.Cos(secondRad);
            double secondY = centerY + radius * Math.Sin(secondRad);

            // Create trajectory path: center -> hour -> minute -> second -> back to center (closed region)
            figure.StartPoint = new Point(centerX, centerY);
            figure.IsClosed = true;
            figure.IsFilled = true;
            
            LineSegment hourSegment = new LineSegment(new Point(hourX, hourY), true);
            LineSegment minuteSegment = new LineSegment(new Point(minuteX, minuteY), true);
            LineSegment secondSegment = new LineSegment(new Point(secondX, secondY), true);
            
            figure.Segments.Add(hourSegment);
            figure.Segments.Add(minuteSegment);
            figure.Segments.Add(secondSegment);

            geometry.Figures.Add(figure);

            // Create filled region from geometry (trajectory region)
            Path trajectoryPath = new Path
            {
                Data = geometry,
                Fill = new SolidColorBrush(Color.FromArgb(80, 0, 255, 255)), // Semi-transparent cyan fill
                Stroke = new SolidColorBrush(Colors.Cyan),
                StrokeThickness = 2,
                StrokeLineJoin = PenLineJoin.Round,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round
            };

            TimeCanvas.Children.Add(trajectoryPath);

            // Also draw the trajectory line for better visibility
            PathGeometry lineGeometry = new PathGeometry();
            PathFigure lineFigure = new PathFigure
            {
                StartPoint = new Point(centerX, centerY),
                IsClosed = false
            };
            lineFigure.Segments.Add(new LineSegment(new Point(hourX, hourY), true));
            lineFigure.Segments.Add(new LineSegment(new Point(minuteX, minuteY), true));
            lineFigure.Segments.Add(new LineSegment(new Point(secondX, secondY), true));
            lineGeometry.Figures.Add(lineFigure);

            Path trajectoryLine = new Path
            {
                Data = lineGeometry,
                Stroke = new SolidColorBrush(Colors.Cyan),
                StrokeThickness = 3,
                StrokeLineJoin = PenLineJoin.Round,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round
            };

            TimeCanvas.Children.Add(trajectoryLine);

            // Draw clock face
            DrawClockFace(centerX, centerY, radius);

            // Draw hands
            DrawHand(centerX, centerY, hourX, hourY, Colors.White, 4);
            DrawHand(centerX, centerY, minuteX, minuteY, Colors.LightBlue, 3);
            DrawHand(centerX, centerY, secondX, secondY, Colors.Red, 2);

            // Draw center point
            Ellipse centerPoint = new Ellipse
            {
                Width = 10,
                Height = 10,
                Fill = new SolidColorBrush(Colors.White)
            };
            Canvas.SetLeft(centerPoint, centerX - 5);
            Canvas.SetTop(centerPoint, centerY - 5);
            TimeCanvas.Children.Add(centerPoint);

            // Draw time text
            TextBlock timeText = new TextBlock
            {
                Text = timeString,
                FontSize = 24,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Colors.White),
                HorizontalAlignment = HorizontalAlignment.Center
            };
            Canvas.SetLeft(timeText, centerX - 50);
            Canvas.SetTop(timeText, centerY + radius + 20);
            TimeCanvas.Children.Add(timeText);
        }

        private void DrawClockFace(double centerX, double centerY, double radius)
        {
            // Draw hour markers
            for (int i = 0; i < 12; i++)
            {
                double angle = (i * 30 - 90) * Math.PI / 180;
                double x1 = centerX + (radius - 10) * Math.Cos(angle);
                double y1 = centerY + (radius - 10) * Math.Sin(angle);
                double x2 = centerX + radius * Math.Cos(angle);
                double y2 = centerY + radius * Math.Sin(angle);

                Line marker = new Line
                {
                    X1 = x1,
                    Y1 = y1,
                    X2 = x2,
                    Y2 = y2,
                    Stroke = new SolidColorBrush(Colors.Gray),
                    StrokeThickness = 2
                };
                TimeCanvas.Children.Add(marker);
            }
        }

        private void DrawHand(double x1, double y1, double x2, double y2, Color color, double thickness)
        {
            Line hand = new Line
            {
                X1 = x1,
                Y1 = y1,
                X2 = x2,
                Y2 = y2,
                Stroke = new SolidColorBrush(color),
                StrokeThickness = thickness
            };
            TimeCanvas.Children.Add(hand);
        }

        protected override void OnClosed(EventArgs e)
        {
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

