using System.Net;
using System.Net.Sockets;
using System.Text;

namespace MauiForge.Services;

public class RemoteDiscoveryService
{
    private CancellationTokenSource? _cts;

    public void StartResponder(int discoveryPort = 5124, int webPort = 5123, string? token = null)
    {
        _cts = new CancellationTokenSource();
        var token2 = _cts.Token;

        _ = Task.Run(async () =>
        {
            try
            {
                using var udp = new UdpClient();
                udp.Client.ReuseAddress = true;
                udp.Client.Bind(new IPEndPoint(IPAddress.Any, discoveryPort));
                udp.Client.ReceiveTimeout = 5000;

                while (!token2.IsCancellationRequested)
                {
                    try
                    {
                        var result = await udp.ReceiveAsync(token2);
                        var msg = Encoding.UTF8.GetString(result.Buffer).Trim();

                        if (msg == "MAUI_FORGE_PING")
                        {
                            var response = $"MAUI_FORGE_PONG|{Environment.MachineName}|{webPort}|{(token != null ? "1" : "0")}";
                            var data = Encoding.UTF8.GetBytes(response);
                            await udp.SendAsync(data, data.Length, result.RemoteEndPoint);
                        }
                    }
                    catch (OperationCanceledException) { break; }
                    catch (SocketException) { /* timeout, loop */ }
                }
            }
            catch { /* socket bind failed — prevent crash */ }
        }, token2);
    }

    public void StopResponder()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
    }

    public static List<RemoteServerInfo> Discover(int discoveryPort = 5124, int timeoutMs = 3000)
    {
        var servers = new List<RemoteServerInfo>();
        using var udp = new UdpClient();
        udp.Client.Bind(new IPEndPoint(IPAddress.Any, 0));
        udp.EnableBroadcast = true;
        udp.Client.ReceiveTimeout = timeoutMs;

        var ping = Encoding.UTF8.GetBytes("MAUI_FORGE_PING");

        // The global broadcast address (255.255.255.255) can get routed out the wrong
        // network adapter when the machine has more than one (VPN, virtual switches,
        // multiple physical NICs), so it silently never reaches the actual LAN. Sending
        // directly to each active subnet's own directed broadcast address (e.g.
        // 192.168.1.255) is what actually gets it there reliably.
        var targets = new List<IPEndPoint> { new(IPAddress.Broadcast, discoveryPort) };
        foreach (var addr in GetSubnetBroadcastAddresses())
            targets.Add(new IPEndPoint(addr, discoveryPort));

        foreach (var target in targets)
        {
            try { udp.Send(ping, ping.Length, target); }
            catch (SocketException) { /* that subnet isn't reachable from here — skip it */ }
        }

        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);

        while (DateTime.UtcNow < deadline)
        {
            try
            {
                var remaining = (int)(deadline - DateTime.UtcNow).TotalMilliseconds;
                if (remaining <= 0) break;
                udp.Client.ReceiveTimeout = Math.Min(remaining, 500);

                var ep = new IPEndPoint(IPAddress.Any, 0);
                var data = udp.Receive(ref ep);
                var msg = Encoding.UTF8.GetString(data).Trim();

                if (msg.StartsWith("MAUI_FORGE_PONG|"))
                {
                    var parts = msg.Split('|');
                    if (parts.Length >= 4 && int.TryParse(parts[2], out var port))
                    {
                        var server = new RemoteServerInfo
                        {
                            Host = ep.Address.ToString(),
                            Port = port,
                            Hostname = parts[1],
                            TokenRequired = parts[3] == "1"
                        };
                        if (!servers.Any(s => s.Host == server.Host && s.Port == server.Port))
                            servers.Add(server);
                    }
                }
            }
            catch (SocketException) { /* timeout, keep waiting */ }
        }

        return servers;
    }

    private static List<IPAddress> GetSubnetBroadcastAddresses()
    {
        var result = new List<IPAddress>();
        try
        {
            foreach (var nic in NetworkUtils.GetActiveLanInterfaces())
            {
                foreach (var ua in nic.GetIPProperties().UnicastAddresses)
                {
                    if (ua.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                    if (IPAddress.IsLoopback(ua.Address)) continue;
                    if (ua.IPv4Mask == null) continue;

                    var ipBytes = ua.Address.GetAddressBytes();
                    var maskBytes = ua.IPv4Mask.GetAddressBytes();
                    var broadcastBytes = new byte[4];
                    for (var i = 0; i < 4; i++)
                        broadcastBytes[i] = (byte)(ipBytes[i] | ~maskBytes[i]);
                    result.Add(new IPAddress(broadcastBytes));
                }
            }
        }
        catch { /* best effort — falls back to the global broadcast address only */ }
        return result;
    }
}

public class RemoteServerInfo
{
    public string Host { get; set; } = "";
    public int Port { get; set; } = 5123;
    public string Hostname { get; set; } = "";
    public bool TokenRequired { get; set; }
    public override string ToString() => $"{Hostname} ({Host}:{Port})";
}
