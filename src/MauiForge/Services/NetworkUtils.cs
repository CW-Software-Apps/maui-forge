using System.Net.NetworkInformation;

namespace MauiForge.Services;

internal static class NetworkUtils
{
    // Virtual switches (WSL, Hyper-V, Docker, VPN, TAP) aren't reachable from other
    // physical machines on the LAN, so both LAN-address listing and broadcast discovery
    // filter them out the same way.
    public static IEnumerable<NetworkInterface> GetActiveLanInterfaces()
    {
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up) continue;
            if (nic.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel) continue;

            var label = $"{nic.Name} {nic.Description}";
            if (label.Contains("Virtual", StringComparison.OrdinalIgnoreCase) ||
                label.Contains("vEthernet", StringComparison.OrdinalIgnoreCase) ||
                label.Contains("WSL", StringComparison.OrdinalIgnoreCase) ||
                label.Contains("Hyper-V", StringComparison.OrdinalIgnoreCase) ||
                label.Contains("Docker", StringComparison.OrdinalIgnoreCase) ||
                label.Contains("VPN", StringComparison.OrdinalIgnoreCase) ||
                label.Contains("TAP", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            yield return nic;
        }
    }
}
