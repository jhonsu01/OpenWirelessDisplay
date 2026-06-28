using System;
using System.IO;
using System.Text;

namespace OpenWirelessDisplay.Protocol;

/// <summary>
/// Protocolo binario compartido servidor (Windows) <-> cliente (Android).
/// Cada mensaje: [1 byte Type][4 bytes BE Length][Length bytes Payload].
/// Version del protocolo: 1.
/// </summary>
public static class WireProtocol
{
    public const byte ProtocolVersion = 1;

    // Nombre de servicio mDNS/DNS-SD anunciado por el servidor.
    public const string ServiceType = "_openwdisplay._tcp";
    public const string ServiceDomain = "local.";

    // Puerto TCP por defecto (control + stream multiplexados en la misma conexion).
    public const int DefaultPort = 7345;

    // ---- Cliente -> Servidor ----
    public const byte MsgHello = 0x01;   // payload: JSON {clientName, protocol}
    public const byte MsgPin = 0x02;     // payload: UTF8 PIN (6 digitos)
    public const byte MsgInput = 0x10;   // payload: [1 byte action][float x][float y] (BE)
    public const byte MsgBye = 0x7F;     // sin payload

    // ---- Servidor -> Cliente ----
    public const byte MsgHelloAck = 0x81; // payload: JSON {needPin, serverName, protocol}
    public const byte MsgPinOk = 0x82;    // payload: JSON {width, height, fps}
    public const byte MsgPinFail = 0x83;  // payload: JSON {reason, attemptsLeft}
    public const byte MsgFrame = 0x90;    // payload: [4 BE w][4 BE h][JPEG bytes]
    public const byte MsgError = 0xFE;    // payload: UTF8 mensaje

    // Acciones de input (touch/mouse).
    public const byte InputMove = 0x00;
    public const byte InputDown = 0x01;
    public const byte InputUp = 0x02;

    public static void WriteMessage(Stream stream, byte type, byte[]? payload)
    {
        payload ??= Array.Empty<byte>();
        Span<byte> header = stackalloc byte[5];
        header[0] = type;
        WriteInt32BE(header.Slice(1), payload.Length);
        stream.Write(header);
        if (payload.Length > 0)
            stream.Write(payload, 0, payload.Length);
        stream.Flush();
    }

    public static void WriteMessage(Stream stream, byte type, string utf8) =>
        WriteMessage(stream, type, Encoding.UTF8.GetBytes(utf8));

    /// <summary>Lee un mensaje completo. Devuelve false si la conexion se cerro.</summary>
    public static bool ReadMessage(Stream stream, out byte type, out byte[] payload)
    {
        type = 0;
        payload = Array.Empty<byte>();
        var header = new byte[5];
        if (!ReadExact(stream, header, 0, 5)) return false;
        type = header[0];
        int len = ReadInt32BE(header, 1);
        if (len < 0 || len > 64 * 1024 * 1024) throw new InvalidDataException($"Longitud invalida: {len}");
        payload = new byte[len];
        if (len > 0 && !ReadExact(stream, payload, 0, len)) return false;
        return true;
    }

    private static bool ReadExact(Stream stream, byte[] buffer, int offset, int count)
    {
        int read = 0;
        while (read < count)
        {
            int n = stream.Read(buffer, offset + read, count - read);
            if (n <= 0) return false;
            read += n;
        }
        return true;
    }

    public static void WriteInt32BE(Span<byte> dst, int value)
    {
        dst[0] = (byte)(value >> 24);
        dst[1] = (byte)(value >> 16);
        dst[2] = (byte)(value >> 8);
        dst[3] = (byte)value;
    }

    public static int ReadInt32BE(byte[] src, int offset) =>
        (src[offset] << 24) | (src[offset + 1] << 16) | (src[offset + 2] << 8) | src[offset + 3];

    public static float ReadFloatBE(byte[] src, int offset)
    {
        Span<byte> tmp = stackalloc byte[4];
        tmp[0] = src[offset + 3];
        tmp[1] = src[offset + 2];
        tmp[2] = src[offset + 1];
        tmp[3] = src[offset];
        return BitConverter.ToSingle(tmp);
    }
}
