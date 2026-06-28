using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using OpenWirelessDisplay.Protocol;

namespace OpenWirelessDisplay.Core;

/// <summary>
/// Responder mDNS/DNS-SD minimo en C# puro (sin dependencias NuGet) para anunciar el
/// servicio "_openwdisplay._tcp.local" en la LAN. Responde a consultas PTR/SRV/TXT/A y
/// emite anuncios gratuitos periodicos. El cliente Android lo descubre con NsdManager.
///
/// Implementacion deliberadamente acotada al caso de uso (DNS-SD para autodeteccion):
/// nombres sin compresion al escribir, parseo de la pregunta con soporte de punteros.
/// </summary>
public sealed class MdnsResponder : IDisposable
{
    private static readonly IPAddress MulticastV4 = IPAddress.Parse("224.0.0.251");
    private const int MdnsPort = 5353;

    private readonly string _instanceName;     // "OpenWirelessDisplay-PC"
    private readonly string _hostLabel;        // "PC.local"
    private readonly int _servicePort;
    private readonly IPAddress _ipv4;
    private readonly string[] _txt;

    private UdpClient? _udp;
    private Thread? _rxThread;
    private Timer? _announceTimer;
    private volatile bool _running;

    private string ServiceFqdn => $"{WireProtocol.ServiceType}.{WireProtocol.ServiceDomain}"; // _openwdisplay._tcp.local.
    private string InstanceFqdn => $"{_instanceName}.{WireProtocol.ServiceType}.{WireProtocol.ServiceDomain}";

    public MdnsResponder(string serverName, int servicePort, IPAddress? ipv4 = null)
    {
        _servicePort = servicePort;
        _ipv4 = ipv4 ?? GetLocalIPv4();
        var machine = Sanitize(Environment.MachineName);
        _instanceName = Sanitize(serverName);
        _hostLabel = $"{machine}.local.";
        _txt = new[]
        {
            $"v={WireProtocol.ProtocolVersion}",
            $"name={serverName}",
            "needpin=1",
        };
    }

    public void Start()
    {
        if (_running) return;
        _udp = new UdpClient(AddressFamily.InterNetwork);
        _udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        _udp.ExclusiveAddressUse = false;
        _udp.Client.Bind(new IPEndPoint(IPAddress.Any, MdnsPort));

        foreach (var ip in GetActiveIPv4Addresses())
        {
            try { _udp.JoinMulticastGroup(MulticastV4, ip); } catch { /* interfaz sin multicast */ }
        }
        _udp.MulticastLoopback = false;

        _running = true;
        _rxThread = new Thread(ReceiveLoop) { IsBackground = true, Name = "mdns-rx" };
        _rxThread.Start();

        // Anuncio inicial + refresco periodico (mientras el servidor esta activo).
        Announce();
        _announceTimer = new Timer(_ => SafeAnnounce(), null, 1000, 15000);
    }

    private void ReceiveLoop()
    {
        var remote = new IPEndPoint(IPAddress.Any, 0);
        while (_running && _udp != null)
        {
            byte[] data;
            try { data = _udp.Receive(ref remote); }
            catch (SocketException) { if (!_running) break; continue; }
            catch (ObjectDisposedException) { break; }

            try
            {
                if (QueryMatchesService(data))
                    Announce();
            }
            catch { /* paquete malformado: ignorar */ }
        }
    }

    /// <summary>True si el paquete es una consulta que referencia nuestro servicio/instancia/host.</summary>
    private bool QueryMatchesService(byte[] msg)
    {
        if (msg.Length < 12) return false;
        bool isResponse = (msg[2] & 0x80) != 0;
        if (isResponse) return false;
        int qd = (msg[4] << 8) | msg[5];
        int pos = 12;
        for (int i = 0; i < qd; i++)
        {
            string name = ReadName(msg, ref pos);
            pos += 4; // QTYPE + QCLASS
            if (name.Equals(Trim(ServiceFqdn), StringComparison.OrdinalIgnoreCase) ||
                name.Equals(Trim(InstanceFqdn), StringComparison.OrdinalIgnoreCase) ||
                name.Equals(Trim(_hostLabel), StringComparison.OrdinalIgnoreCase) ||
                name.Equals("_services._dns-sd._udp.local", StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private void SafeAnnounce()
    {
        try { if (_running) Announce(); } catch { }
    }

    /// <summary>Emite una respuesta DNS-SD con PTR + SRV + TXT + A al grupo multicast.</summary>
    private void Announce()
    {
        if (_udp == null) return;
        using var ms = new MemoryStream();
        // Header: ID=0, Flags=0x8400 (response, authoritative), QD=0, AN=4, NS=0, AR=0
        WriteU16(ms, 0);
        WriteU16(ms, 0x8400);
        WriteU16(ms, 0);
        WriteU16(ms, 4);
        WriteU16(ms, 0);
        WriteU16(ms, 0);

        // PTR: service -> instance (clase IN compartida, sin cache-flush)
        WriteName(ms, ServiceFqdn);
        WriteU16(ms, 12);      // TYPE PTR
        WriteU16(ms, 0x0001);  // CLASS IN
        WriteU32(ms, 4500);    // TTL
        WriteRDataName(ms, InstanceFqdn);

        // SRV: instance -> host:port (cache-flush)
        WriteName(ms, InstanceFqdn);
        WriteU16(ms, 33);      // TYPE SRV
        WriteU16(ms, 0x8001);  // CLASS IN | cache-flush
        WriteU32(ms, 120);
        using (var srv = new MemoryStream())
        {
            WriteU16(srv, 0); // priority
            WriteU16(srv, 0); // weight
            WriteU16(srv, (ushort)_servicePort);
            WriteName(srv, _hostLabel);
            WriteRData(ms, srv.ToArray());
        }

        // TXT: instance metadata (cache-flush)
        WriteName(ms, InstanceFqdn);
        WriteU16(ms, 16);      // TYPE TXT
        WriteU16(ms, 0x8001);
        WriteU32(ms, 4500);
        using (var txt = new MemoryStream())
        {
            foreach (var entry in _txt)
            {
                var b = Encoding.UTF8.GetBytes(entry);
                txt.WriteByte((byte)b.Length);
                txt.Write(b, 0, b.Length);
            }
            WriteRData(ms, txt.ToArray());
        }

        // A: host -> IPv4 (cache-flush)
        WriteName(ms, _hostLabel);
        WriteU16(ms, 1);       // TYPE A
        WriteU16(ms, 0x8001);
        WriteU32(ms, 120);
        WriteRData(ms, _ipv4.GetAddressBytes());

        var packet = ms.ToArray();
        var dst = new IPEndPoint(MulticastV4, MdnsPort);
        foreach (var ip in GetActiveIPv4Addresses())
        {
            try
            {
                _udp.Client.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastInterface,
                    ip.GetAddressBytes());
                _udp.Send(packet, packet.Length, dst);
            }
            catch { }
        }
    }

    // ---- DNS wire helpers ----
    private static void WriteU16(Stream s, int v) { s.WriteByte((byte)(v >> 8)); s.WriteByte((byte)v); }
    private static void WriteU32(Stream s, long v)
    {
        s.WriteByte((byte)(v >> 24)); s.WriteByte((byte)(v >> 16));
        s.WriteByte((byte)(v >> 8)); s.WriteByte((byte)v);
    }

    private static void WriteName(Stream s, string name)
    {
        foreach (var label in Trim(name).Split('.'))
        {
            if (label.Length == 0) continue;
            var b = Encoding.UTF8.GetBytes(label);
            s.WriteByte((byte)b.Length);
            s.Write(b, 0, b.Length);
        }
        s.WriteByte(0);
    }

    private static void WriteRData(Stream s, byte[] rdata)
    {
        WriteU16(s, rdata.Length);
        s.Write(rdata, 0, rdata.Length);
    }

    private static void WriteRDataName(Stream s, string name)
    {
        using var tmp = new MemoryStream();
        WriteName(tmp, name);
        WriteRData(s, tmp.ToArray());
    }

    private static string ReadName(byte[] msg, ref int pos)
    {
        var sb = new StringBuilder();
        bool jumped = false;
        int safety = 0;
        int original = pos;
        while (true)
        {
            if (pos >= msg.Length || safety++ > 128) break;
            int len = msg[pos];
            if (len == 0) { pos++; if (!jumped) original = pos; break; }
            if ((len & 0xC0) == 0xC0)
            {
                int ptr = ((len & 0x3F) << 8) | msg[pos + 1];
                if (!jumped) original = pos + 2;
                pos = ptr;
                jumped = true;
                continue;
            }
            pos++;
            if (sb.Length > 0) sb.Append('.');
            sb.Append(Encoding.UTF8.GetString(msg, pos, len));
            pos += len;
        }
        if (jumped) pos = original;
        return sb.ToString();
    }

    private static string Trim(string s) => s.TrimEnd('.');

    private static string Sanitize(string s)
    {
        var chars = s.Where(c => char.IsLetterOrDigit(c) || c == '-').ToArray();
        var result = new string(chars);
        return string.IsNullOrEmpty(result) ? "OpenWirelessDisplay" : result;
    }

    private static IEnumerable<IPAddress> GetActiveIPv4Addresses()
    {
        foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (ni.OperationalStatus != OperationalStatus.Up) continue;
            if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
            if (!ni.Supports(NetworkInterfaceComponent.IPv4)) continue;
            foreach (var ua in ni.GetIPProperties().UnicastAddresses)
                if (ua.Address.AddressFamily == AddressFamily.InterNetwork)
                    yield return ua.Address;
        }
    }

    public static IPAddress GetLocalIPv4()
    {
        var addr = GetActiveIPv4Addresses().FirstOrDefault();
        return addr ?? IPAddress.Loopback;
    }

    public void Stop()
    {
        _running = false;
        _announceTimer?.Dispose();
        try { _udp?.Close(); } catch { }
        _udp = null;
    }

    public void Dispose() => Stop();
}
