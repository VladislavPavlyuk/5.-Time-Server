using System.Net;

namespace TimeServer;

/// <summary>
/// Represents client information stored in the server's client list
/// </summary>
public class ClientInfo
{
    public IPAddress ClientIp { get; set; }
    public int ClientReceivePort { get; set; }
    public string DnsName { get; set; }
    public DateTime ConnectedAt { get; set; }
    public DateTime? DisconnectedAt { get; set; }
    public bool IsActive { get; set; }

    public ClientInfo(IPAddress clientIp, int clientReceivePort, string dnsName)
    {
        ClientIp = clientIp;
        ClientReceivePort = clientReceivePort;
        DnsName = dnsName;
        ConnectedAt = DateTime.Now;
        IsActive = true;
    }

    public IPEndPoint GetReceiveEndPoint()
    {
        return new IPEndPoint(ClientIp, ClientReceivePort);
    }
}

