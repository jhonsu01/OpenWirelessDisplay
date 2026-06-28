using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace OpenWirelessDisplay.Core;

/// <summary>
/// Captura el contenido de un monitor mediante GDI (BitBlt via CopyFromScreen) y lo
/// codifica como JPEG, con reescalado opcional para reducir ancho de banda/latencia.
/// Permite elegir QUE monitor capturar (incluido un monitor virtual creado por un driver
/// IDD, lo que habilita el "modo extendido").
///
/// NOTA DE ARQUITECTURA (guia, Fase 2): la version de alto rendimiento debe usar
/// Windows.Graphics.Capture + DirectX 11 y codificacion H.264 por hardware. GDI + JPEG es
/// suficiente para el MVP y se mantiene sin dependencias nativas.
/// </summary>
public sealed class ScreenCapturer : IDisposable
{
    private readonly Rectangle _bounds;       // bounds reales del monitor (incl. offset)
    private readonly int _outWidth;
    private readonly int _outHeight;
    private readonly bool _scale;

    private readonly Bitmap _capture;         // captura a resolucion nativa
    private readonly Graphics _captureGfx;
    private Bitmap _scaled;                    // buffer reescalado (si aplica)
    private readonly Graphics? _scaledGfx;
    private readonly EncoderParameters _encoderParams;
    private readonly ImageCodecInfo _jpegCodec;

    /// <summary>Bounds reales del monitor capturado (con offset), para mapear el input.</summary>
    public Rectangle SourceBounds => _bounds;
    /// <summary>Ancho del frame emitido (puede estar reescalado).</summary>
    public int Width => _outWidth;
    /// <summary>Alto del frame emitido (puede estar reescalado).</summary>
    public int Height => _outHeight;

    /// <param name="screenIndex">Indice del monitor (ver <see cref="ListMonitors"/>).</param>
    /// <param name="jpegQuality">Calidad JPEG 10-95.</param>
    /// <param name="maxWidth">Ancho maximo del frame; si el monitor es mas ancho se reescala (0 = sin reescalar).</param>
    public ScreenCapturer(int screenIndex = 0, long jpegQuality = 55, int maxWidth = 1600)
    {
        var screens = Screen.AllScreens;
        var screen = (screenIndex >= 0 && screenIndex < screens.Length)
            ? screens[screenIndex]
            : Screen.PrimaryScreen!;
        _bounds = screen.Bounds;

        _capture = new Bitmap(_bounds.Width, _bounds.Height, PixelFormat.Format32bppArgb);
        _captureGfx = Graphics.FromImage(_capture);

        if (maxWidth > 0 && _bounds.Width > maxWidth)
        {
            _scale = true;
            double ratio = (double)maxWidth / _bounds.Width;
            _outWidth = maxWidth;
            _outHeight = Math.Max(1, (int)(_bounds.Height * ratio));
            _scaled = new Bitmap(_outWidth, _outHeight, PixelFormat.Format32bppArgb);
            _scaledGfx = Graphics.FromImage(_scaled);
            _scaledGfx.InterpolationMode = InterpolationMode.Bilinear;
            _scaledGfx.PixelOffsetMode = PixelOffsetMode.Half;
        }
        else
        {
            _outWidth = _bounds.Width;
            _outHeight = _bounds.Height;
            _scaled = _capture;
            _scaledGfx = null;
        }

        _jpegCodec = GetEncoder(ImageFormat.Jpeg)
            ?? throw new InvalidOperationException("Codec JPEG no disponible.");
        _encoderParams = new EncoderParameters(1);
        _encoderParams.Param[0] = new EncoderParameter(Encoder.Quality, Math.Clamp(jpegQuality, 10, 95));
    }

    /// <summary>Captura un frame y lo devuelve como bytes JPEG. No thread-safe por instancia.</summary>
    public byte[] CaptureJpeg()
    {
        _captureGfx.CopyFromScreen(_bounds.Left, _bounds.Top, 0, 0, _bounds.Size, CopyPixelOperation.SourceCopy);
        DrawCursor(); // GDI no captura el cursor: lo componemos manualmente (visible en monitores virtuales)
        if (_scale)
            _scaledGfx!.DrawImage(_capture, 0, 0, _outWidth, _outHeight);
        using var ms = new MemoryStream(96 * 1024);
        _scaled.Save(ms, _jpegCodec, _encoderParams);
        return ms.ToArray();
    }

    /// <summary>Dibuja el cursor del sistema dentro del frame capturado si esta sobre este monitor.</summary>
    private void DrawCursor()
    {
        var ci = new CURSORINFO { cbSize = Marshal.SizeOf<CURSORINFO>() };
        if (!GetCursorInfo(ref ci) || ci.flags != CURSOR_SHOWING || ci.hCursor == IntPtr.Zero)
            return;

        int relX = ci.ptScreenPos.X - _bounds.Left;
        int relY = ci.ptScreenPos.Y - _bounds.Top;
        if (relX < 0 || relY < 0 || relX >= _bounds.Width || relY >= _bounds.Height)
            return; // el cursor no esta sobre este monitor

        int hotX = 0, hotY = 0;
        if (GetIconInfo(ci.hCursor, out var ii))
        {
            hotX = ii.xHotspot;
            hotY = ii.yHotspot;
            if (ii.hbmMask != IntPtr.Zero) DeleteObject(ii.hbmMask);
            if (ii.hbmColor != IntPtr.Zero) DeleteObject(ii.hbmColor);
        }

        IntPtr hdc = _captureGfx.GetHdc();
        try { DrawIconEx(hdc, relX - hotX, relY - hotY, ci.hCursor, 0, 0, 0, IntPtr.Zero, DI_NORMAL); }
        finally { _captureGfx.ReleaseHdc(hdc); }
    }

    #region Win32 cursor interop
    private const int CURSOR_SHOWING = 0x00000001;
    private const int DI_NORMAL = 0x0003;

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X; public int Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct CURSORINFO
    {
        public int cbSize;
        public int flags;
        public IntPtr hCursor;
        public POINT ptScreenPos;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ICONINFO
    {
        public bool fIcon;
        public int xHotspot;
        public int yHotspot;
        public IntPtr hbmMask;
        public IntPtr hbmColor;
    }

    [DllImport("user32.dll")]
    private static extern bool GetCursorInfo(ref CURSORINFO pci);

    [DllImport("user32.dll")]
    private static extern bool GetIconInfo(IntPtr hIcon, out ICONINFO piconinfo);

    [DllImport("user32.dll")]
    private static extern bool DrawIconEx(IntPtr hdc, int xLeft, int yTop, IntPtr hIcon,
        int cxWidth, int cyHeight, int istepIfAniCur, IntPtr hbrFlickerFreeDraw, int diFlags);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr hObject);
    #endregion

    /// <summary>Tamaño (ancho, alto) de un monitor por indice, para datos informativos.</summary>
    public static (int Width, int Height) GetMonitorSize(int index)
    {
        var screens = Screen.AllScreens;
        var s = (index >= 0 && index < screens.Length) ? screens[index] : Screen.PrimaryScreen!;
        return (s.Bounds.Width, s.Bounds.Height);
    }

    /// <summary>Lista los monitores disponibles para mostrar en la UI.</summary>
    public static IReadOnlyList<MonitorInfo> ListMonitors()
    {
        var list = new List<MonitorInfo>();
        var screens = Screen.AllScreens;
        for (int i = 0; i < screens.Length; i++)
        {
            var s = screens[i];
            string label = $"Monitor {i + 1} — {s.Bounds.Width}x{s.Bounds.Height}" +
                           (s.Primary ? " (Principal)" : "");
            list.Add(new MonitorInfo(i, label, s.Primary));
        }
        return list;
    }

    private static ImageCodecInfo? GetEncoder(ImageFormat format)
    {
        foreach (var codec in ImageCodecInfo.GetImageEncoders())
            if (codec.FormatID == format.Guid)
                return codec;
        return null;
    }

    public void Dispose()
    {
        _captureGfx.Dispose();
        _scaledGfx?.Dispose();
        if (_scale) _scaled.Dispose();
        _capture.Dispose();
        _encoderParams.Dispose();
    }

    public readonly record struct MonitorInfo(int Index, string Label, bool Primary);
}
