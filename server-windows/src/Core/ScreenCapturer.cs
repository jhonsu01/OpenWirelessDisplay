using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Forms;

namespace OpenWirelessDisplay.Core;

/// <summary>
/// Captura el contenido de un monitor mediante GDI (BitBlt via CopyFromScreen) y lo
/// codifica como JPEG. Es el pipeline del MVP (modo espejo).
///
/// NOTA DE ARQUITECTURA (guia, Fase 2): la version de alto rendimiento debe usar
/// Windows.Graphics.Capture + DirectX 11 (texturas ID3D11Texture2D) y codificacion H.264
/// por hardware (NVENC/AMF/QuickSync). GDI es suficiente para validar PIN + descubrimiento
/// + render + input end-to-end y mantener el build offline sin dependencias nativas.
/// El render del cliente (decodificar JPEG -> Bitmap) es intercambiable por MediaCodec H.264.
/// </summary>
public sealed class ScreenCapturer : IDisposable
{
    private readonly Rectangle _bounds;
    private Bitmap _bitmap;
    private readonly Graphics _graphics;
    private readonly EncoderParameters _encoderParams;
    private readonly ImageCodecInfo _jpegCodec;

    public int Width => _bounds.Width;
    public int Height => _bounds.Height;

    public ScreenCapturer(int screenIndex = 0, long jpegQuality = 60)
    {
        var screens = Screen.AllScreens;
        var screen = (screenIndex >= 0 && screenIndex < screens.Length)
            ? screens[screenIndex]
            : Screen.PrimaryScreen!;
        _bounds = screen.Bounds;

        _bitmap = new Bitmap(_bounds.Width, _bounds.Height, PixelFormat.Format32bppArgb);
        _graphics = Graphics.FromImage(_bitmap);

        _jpegCodec = GetEncoder(ImageFormat.Jpeg)
            ?? throw new InvalidOperationException("Codec JPEG no disponible.");
        _encoderParams = new EncoderParameters(1);
        _encoderParams.Param[0] = new EncoderParameter(Encoder.Quality, jpegQuality);
    }

    /// <summary>Captura un frame y lo devuelve como bytes JPEG. Reentrante por instancia (no thread-safe).</summary>
    public byte[] CaptureJpeg()
    {
        _graphics.CopyFromScreen(_bounds.Left, _bounds.Top, 0, 0, _bounds.Size, CopyPixelOperation.SourceCopy);
        using var ms = new MemoryStream(128 * 1024);
        _bitmap.Save(ms, _jpegCodec, _encoderParams);
        return ms.ToArray();
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
        _graphics.Dispose();
        _bitmap.Dispose();
        _encoderParams.Dispose();
    }
}
