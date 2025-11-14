using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace TimeServer;

class Program
{
    private static UdpClient? _udpServer;
    private static List<ClientInfo> _clients = new();
    private static System.Timers.Timer? _broadcastTimer;
    private static int _serverPort = 49152;
    private static readonly object _lockObject = new();
    private static bool _isRunning = false;

    static void Main(string[] args)
    {
        Console.WriteLine("=== Time Server ===");
        Console.WriteLine($"Default server port: {_serverPort}");
        Console.WriteLine("Enter server port (press Enter for default):");
        
        string? portInput = Console.ReadLine();
        if (!string.IsNullOrWhiteSpace(portInput) && int.TryParse(portInput, out int customPort))
        {
            _serverPort = customPort;
        }

        StartServer();
        
        Console.WriteLine("Press 'q' to quit, 'p' to change port");
        Console.WriteLine("Server is running...\n");

        while (_isRunning)
        {
            var key = Console.ReadKey(true);
            if (key.KeyChar == 'q' || key.KeyChar == 'Q')
            {
                StopServer();
                break;
            }
            else if (key.KeyChar == 'p' || key.KeyChar == 'P')
            {
                ChangePort();
            }
        }
    }

    static void StartServer()
    {
        try
        {
            _udpServer = new UdpClient(_serverPort);
            _isRunning = true;

            // Start receiving messages in a separate thread
            Task.Run(ReceiveMessages);

            // Start timer to broadcast time every 2 seconds
            _broadcastTimer = new System.Timers.Timer(2000);
            _broadcastTimer.Elapsed += BroadcastTime;
            _broadcastTimer.AutoReset = true;
            _broadcastTimer.Start();

            LogMessage($"Server started on port {_serverPort}");
        }
        catch (Exception ex)
        {
            LogMessage($"Error starting server: {ex.Message}");
        }
    }

    static void StopServer()
    {
        _isRunning = false;
        _broadcastTimer?.Stop();
        _broadcastTimer?.Dispose();
        _udpServer?.Close();
        _udpServer?.Dispose();

        lock (_lockObject)
        {
            foreach (var client in _clients.Where(c => c.IsActive))
            {
                client.DisconnectedAt = DateTime.Now;
                client.IsActive = false;
                LogMessage($"Client disconnected: {client.ClientIp} (receive port: {client.ClientReceivePort}) ({client.DnsName}) - Disconnected at: {client.DisconnectedAt}");
            }
            _clients.Clear();
        }

        LogMessage("Server stopped");
    }

    static void ChangePort()
    {
        Console.WriteLine($"Current port: {_serverPort}");
        Console.WriteLine("Enter new port:");
        string? portInput = Console.ReadLine();
        if (!string.IsNullOrWhiteSpace(portInput) && int.TryParse(portInput, out int newPort))
        {
            StopServer();
            _serverPort = newPort;
            StartServer();
        }
    }

    static async Task ReceiveMessages()
    {
        while (_isRunning && _udpServer != null)
        {
            try
            {
                UdpReceiveResult result = await _udpServer.ReceiveAsync();
                IPEndPoint clientEndPoint = result.RemoteEndPoint;
                string message = Encoding.UTF8.GetString(result.Buffer);

                // Handle client connection/disconnection messages
                if (message.StartsWith("CONNECT"))
                {
                    HandleClientConnect(clientEndPoint, message);
                }
                else if (message.StartsWith("DISCONNECT"))
                {
                    HandleClientDisconnect(clientEndPoint, message);
                }
            }
            catch (ObjectDisposedException)
            {
                // Server was closed
                break;
            }
            catch (Exception ex)
            {
                LogMessage($"Error receiving message: {ex.Message}");
            }
        }
    }

    static void HandleClientConnect(IPEndPoint clientEndPoint, string message)
    {
        lock (_lockObject)
        {
            // Parse message: "CONNECT:port" or just "CONNECT" (default port 49153)
            int clientReceivePort = 49153;
            if (message.Contains(':'))
            {
                string[] parts = message.Split(':');
                if (parts.Length == 2 && int.TryParse(parts[1], out int port))
                {
                    clientReceivePort = port;
                }
            }

            // Check if client already exists (by IP and receive port)
            var existingClient = _clients.FirstOrDefault(c => 
                c.ClientIp.Equals(clientEndPoint.Address) && 
                c.ClientReceivePort == clientReceivePort);

            if (existingClient != null)
            {
                if (!existingClient.IsActive)
                {
                    // Reconnect
                    existingClient.IsActive = true;
                    existingClient.ConnectedAt = DateTime.Now;
                    existingClient.DisconnectedAt = null;
                    LogMessage($"Client reconnected: {clientEndPoint.Address} (receive port: {clientReceivePort}) ({existingClient.DnsName}) - Connected at: {existingClient.ConnectedAt}");
                }
                return;
            }

            // Get DNS name
            string dnsName = "Unknown";
            try
            {
                IPHostEntry hostEntry = Dns.GetHostEntry(clientEndPoint.Address);
                dnsName = hostEntry.HostName;
            }
            catch
            {
                dnsName = clientEndPoint.Address.ToString();
            }

            // Add new client
            var clientInfo = new ClientInfo(clientEndPoint.Address, clientReceivePort, dnsName);
            _clients.Add(clientInfo);

            LogMessage($"Client connected: {clientEndPoint.Address} (receive port: {clientReceivePort}) ({dnsName}) - Connected at: {clientInfo.ConnectedAt}");
        }
    }

    static void HandleClientDisconnect(IPEndPoint clientEndPoint, string message)
    {
        lock (_lockObject)
        {
            // Parse message to get receive port if provided
            int clientReceivePort = 49153;
            if (message.Contains(':'))
            {
                string[] parts = message.Split(':');
                if (parts.Length == 2 && int.TryParse(parts[1], out int port))
                {
                    clientReceivePort = port;
                }
            }

            var client = _clients.FirstOrDefault(c => 
                c.ClientIp.Equals(clientEndPoint.Address) && 
                c.ClientReceivePort == clientReceivePort &&
                c.IsActive);

            if (client != null)
            {
                client.IsActive = false;
                client.DisconnectedAt = DateTime.Now;
                LogMessage($"Client disconnected: {client.ClientIp} (receive port: {client.ClientReceivePort}) ({client.DnsName}) - Disconnected at: {client.DisconnectedAt}");
            }
        }
    }

    static void BroadcastTime(object? sender, System.Timers.ElapsedEventArgs e)
    {
        if (_udpServer == null || !_isRunning)
            return;

        DateTime currentTime = DateTime.Now;
        string timeString = currentTime.ToString("HH:mm:ss");
        
        // Create message with time
        var message = new
        {
            Time = timeString,
            Timestamp = currentTime
        };
        string jsonMessage = JsonSerializer.Serialize(message);
        byte[] data = Encoding.UTF8.GetBytes(jsonMessage);

        lock (_lockObject)
        {
            // Send to all active clients
            var activeClients = _clients.Where(c => c.IsActive).ToList();
            
            foreach (var client in activeClients)
            {
                try
                {
                    // Send to client's receive port
                    IPEndPoint clientEndpoint = client.GetReceiveEndPoint();
                    _udpServer.Send(data, data.Length, clientEndpoint);
                }
                catch (Exception ex)
                {
                    LogMessage($"Error sending time to {client.ClientIp}:{client.ClientReceivePort}: {ex.Message}");
                }
            }

            if (activeClients.Count > 0)
            {
                LogMessage($"Broadcasted time '{timeString}' to {activeClients.Count} client(s)");
            }
        }
    }

    static void LogMessage(string message)
    {
        Console.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}");
    }
}

