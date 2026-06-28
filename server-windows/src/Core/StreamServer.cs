using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using OpenWirelessDisplay.Protocol;

namespace OpenWirelessDisplay.Core;

/// <summary>
/// Orquesta el ciclo de vida del servidor: escucha TCP, gestiona el handshake de
/// emparejamiento por PIN, transmite frames (MJPEG) e inyecta el input recibido.
/// Expone eventos para que la UI (WPF) reaccione sin acoplarse a la red.
/// </summary>
public sealed class StreamServer : IDisposable
{
    private readonly PinManager _pin;
    private readonly int _port;
    private readonly int _targetFps;
    private readonly long _jpegQuality;

    private TcpListener? _listener;
    private Thread? _acceptThread;
    private MdnsResponder? _mdns;
    private ScreenCapturer? _capturer;
    private InputInjector? _injector;
    private readonly object _captureLock = new();
    private volatile bool _running;
    private int _clientCount;

    public event Action<string>? Log;
    public event Action<int>? ClientCountChanged;
    public event Action<string>? PinChanged;

    public string ServerName { get; }
    public bool IsRunning => _running;
    public int Port => _port;
    public string CurrentPin => _pin.CurrentPin;

    private readonly int _screenIndex;
    private readonly int _maxWidth;

    public StreamServer(string serverName, int port = WireProtocol.DefaultPort,
        int targetFps = 12, long jpegQuality = 55, int screenIndex = 0, int maxWidth = 1600)
    {
        ServerName = string.IsNullOrWhiteSpace(serverName) ? Environment.MachineName : serverName;
        _port = port;
        _targetFps = Math.Clamp(targetFps, 1, 60);
        _jpegQuality = Math.Clamp(jpegQuality, 10, 95);
        _screenIndex = screenIndex;
        _maxWidth = maxWidth;
        _pin = new PinManager();
    }

    public void Start()
    {
        if (_running) return;

        _capturer = new ScreenCapturer(_screenIndex, _jpegQuality, _maxWidth);
        _injector = new InputInjector(_capturer.SourceBounds);

        _listener = new TcpListener(IPAddress.Any, _port);
        _listener.Start();
        _running = true;

        _mdns = new MdnsResponder(ServerName, _port);
        _mdns.Start();

        _acceptThread = new Thread(AcceptLoop) { IsBackground = true, Name = "tcp-accept" };
        _acceptThread.Start();

        Log?.Invoke($"Servidor iniciado en puerto {_port}. mDNS anunciando '{WireProtocol.ServiceType}'.");
        Log?.Invoke($"PIN de emparejamiento: {_pin.CurrentPin}");
    }

    private void AcceptLoop()
    {
        while (_running && _listener != null)
        {
            TcpClient client;
            try { client = _listener.AcceptTcpClient(); }
            catch (SocketException) { if (!_running) break; continue; }
            catch (ObjectDisposedException) { break; }

            var t = new Thread(() => HandleClient(client)) { IsBackground = true, Name = "client" };
            t.Start();
        }
    }

    private void HandleClient(TcpClient client)
    {
        var remote = client.Client.RemoteEndPoint?.ToString() ?? "?";
        client.NoDelay = true; // desactiva Nagle (guia, optimizacion de latencia)
        bool counted = false;
        try
        {
            using var stream = client.GetStream();

            // 1) HELLO
            if (!WireProtocol.ReadMessage(stream, out byte type, out byte[] payload) || type != WireProtocol.MsgHello)
            {
                Log?.Invoke($"[{remote}] handshake invalido (sin HELLO).");
                return;
            }
            Log?.Invoke($"[{remote}] conectado, solicitando PIN.");
            WireProtocol.WriteMessage(stream, WireProtocol.MsgHelloAck,
                JsonSerializer.Serialize(new { needPin = true, serverName = ServerName, protocol = WireProtocol.ProtocolVersion }));

            // 2) PIN con reintentos
            bool paired = false;
            for (int attempt = 0; attempt < PinManager.MaxAttempts && !paired; attempt++)
            {
                if (!WireProtocol.ReadMessage(stream, out type, out payload)) return;
                if (type == WireProtocol.MsgBye) return;
                if (type != WireProtocol.MsgPin) continue;

                string candidate = Encoding.UTF8.GetString(payload);
                if (_pin.Verify(candidate))
                {
                    paired = true;
                    WireProtocol.WriteMessage(stream, WireProtocol.MsgPinOk,
                        JsonSerializer.Serialize(new { width = _capturer!.Width, height = _capturer.Height, fps = _targetFps }));
                    Log?.Invoke($"[{remote}] PIN correcto. Emparejado.");
                    // Rotar PIN para el siguiente dispositivo (seguridad).
                    var next = _pin.Rotate();
                    PinChanged?.Invoke(next);
                    Log?.Invoke($"Nuevo PIN para el proximo dispositivo: {next}");
                }
                else
                {
                    int left = PinManager.MaxAttempts - attempt - 1;
                    WireProtocol.WriteMessage(stream, WireProtocol.MsgPinFail,
                        JsonSerializer.Serialize(new { reason = "PIN incorrecto", attemptsLeft = left }));
                    Log?.Invoke($"[{remote}] PIN incorrecto. Intentos restantes: {left}.");
                }
            }
            if (!paired)
            {
                Log?.Invoke($"[{remote}] emparejamiento fallido. Cerrando.");
                return;
            }

            counted = true;
            ClientCountChanged?.Invoke(Interlocked.Increment(ref _clientCount));

            // 3) Sesion: hilo emisor de frames + lectura de input en este hilo.
            var senderAlive = true;
            var sender = new Thread(() => FrameSenderLoop(stream, ref senderAlive))
            { IsBackground = true, Name = "frames" };
            sender.Start();

            try
            {
                while (_running && client.Connected)
                {
                    if (!WireProtocol.ReadMessage(stream, out type, out payload)) break;
                    if (type == WireProtocol.MsgBye) break;
                    if (type == WireProtocol.MsgInput && payload.Length >= 9)
                    {
                        byte action = payload[0];
                        float x = WireProtocol.ReadFloatBE(payload, 1);
                        float y = WireProtocol.ReadFloatBE(payload, 5);
                        _injector?.Inject(action, x, y);
                    }
                }
            }
            finally
            {
                senderAlive = false;
                sender.Join(500);
            }
        }
        catch (Exception ex)
        {
            Log?.Invoke($"[{remote}] error: {ex.Message}");
        }
        finally
        {
            client.Close();
            if (counted)
                ClientCountChanged?.Invoke(Interlocked.Decrement(ref _clientCount));
            Log?.Invoke($"[{remote}] desconectado.");
        }
    }

    private void FrameSenderLoop(NetworkStream stream, ref bool alive)
    {
        var frameInterval = TimeSpan.FromMilliseconds(1000.0 / _targetFps);
        var sw = Stopwatch.StartNew();
        var header = new byte[8];
        while (_running && alive)
        {
            var start = sw.Elapsed;
            try
            {
                byte[] jpeg;
                int w, h;
                lock (_captureLock)
                {
                    jpeg = _capturer!.CaptureJpeg();
                    w = _capturer.Width;
                    h = _capturer.Height;
                }

                WireProtocol.WriteInt32BE(header.AsSpan(0), w);
                WireProtocol.WriteInt32BE(header.AsSpan(4), h);
                var payload = new byte[8 + jpeg.Length];
                Buffer.BlockCopy(header, 0, payload, 0, 8);
                Buffer.BlockCopy(jpeg, 0, payload, 8, jpeg.Length);
                WireProtocol.WriteMessage(stream, WireProtocol.MsgFrame, payload);
            }
            catch (IOException) { break; }
            catch (ObjectDisposedException) { break; }
            catch (Exception ex) { Log?.Invoke($"frame error: {ex.Message}"); break; }

            var elapsed = sw.Elapsed - start;
            var wait = frameInterval - elapsed;
            if (wait > TimeSpan.Zero) Thread.Sleep(wait);
        }
    }

    public void Stop()
    {
        if (!_running) return;
        _running = false;
        try { _listener?.Stop(); } catch { }
        _mdns?.Stop();
        lock (_captureLock) { _capturer?.Dispose(); _capturer = null; }
        _clientCount = 0;
        ClientCountChanged?.Invoke(0);
        Log?.Invoke("Servidor detenido.");
    }

    public void Dispose() => Stop();
}
