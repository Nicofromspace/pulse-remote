using Makaretu.Dns;
using System.Net;
using System.Net.NetworkInformation;

namespace Pulse.Remote;

// mDNS: the name stays fixed, while each response uses the PC's current addresses.
public sealed class LocalNameService : IDisposable
{
    private MulticastService? mdns;
    public string? Error { get; private set; }
    public LocalNameService(string hostName)
    {
        try
        {
            mdns = new MulticastService(nics => nics.Where(n => n.NetworkInterfaceType != NetworkInterfaceType.Loopback &&
                n.GetIPProperties().UnicastAddresses.Any(a => Program.IsLocal(a.Address)))) { UseIpv6 = false };
            mdns.QueryReceived += (_, query) =>
            {
                if (!Program.IsLocal(query.RemoteEndPoint.Address)) return;
                if (!query.Message.Questions.Any(q => q.Name == (DomainName)hostName && (q.Type == DnsType.A || q.Type == DnsType.ANY))) return;
                var answer = new Makaretu.Dns.Message { QR = true };
                foreach (string address in Program.Addresses())
                    answer.Answers.Add(new ARecord { Name = hostName, Address = IPAddress.Parse(address), TTL = TimeSpan.FromSeconds(30) });
                if (answer.Answers.Count > 0) mdns.SendAnswer(answer, query);
            };
            mdns.Start();
        }
        catch (Exception ex) when (ex is System.Net.Sockets.SocketException or InvalidOperationException or NetworkInformationException)
        {
            Error = "Permanent address could not start. Use an IP fallback or check phone-access setup. " + ex.Message;
            mdns?.Dispose(); mdns = null;
        }
    }
    public void Dispose() => mdns?.Dispose();
}
