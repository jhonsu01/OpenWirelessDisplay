using System;
using System.Drawing;
using System.Runtime.InteropServices;
using OpenWirelessDisplay.Protocol;

namespace OpenWirelessDisplay.Core;

/// <summary>
/// Inyecta eventos de mouse en Windows (SendInput de Win32) a partir de coordenadas
/// normalizadas (0.0-1.0) enviadas por el cliente. Mapea al rectangulo del monitor
/// capturado usando el escritorio virtual completo (guia, Fase 4).
/// </summary>
public sealed class InputInjector
{
    private readonly Rectangle _target;

    public InputInjector(Rectangle targetMonitorBounds)
    {
        _target = targetMonitorBounds;
    }

    public void Inject(byte action, float normX, float normY)
    {
        normX = Math.Clamp(normX, 0f, 1f);
        normY = Math.Clamp(normY, 0f, 1f);

        // Pixel absoluto dentro del monitor objetivo.
        int px = _target.Left + (int)(normX * _target.Width);
        int py = _target.Top + (int)(normY * _target.Height);

        // SendInput con MOUSEEVENTF_ABSOLUTE usa coords normalizadas 0..65535
        // respecto al escritorio virtual completo (multi-monitor).
        int vsX = GetSystemMetrics(SM_XVIRTUALSCREEN);
        int vsY = GetSystemMetrics(SM_YVIRTUALSCREEN);
        int vsW = Math.Max(1, GetSystemMetrics(SM_CXVIRTUALSCREEN));
        int vsH = Math.Max(1, GetSystemMetrics(SM_CYVIRTUALSCREEN));

        int absX = (int)((px - vsX) * 65535.0 / vsW);
        int absY = (int)((py - vsY) * 65535.0 / vsH);

        uint flags = MOUSEEVENTF_MOVE | MOUSEEVENTF_ABSOLUTE | MOUSEEVENTF_VIRTUALDESK;
        flags |= action switch
        {
            WireProtocol.InputDown => MOUSEEVENTF_LEFTDOWN,
            WireProtocol.InputUp => MOUSEEVENTF_LEFTUP,
            _ => 0,
        };

        var input = new INPUT
        {
            type = INPUT_MOUSE,
            U = new InputUnion
            {
                mi = new MOUSEINPUT
                {
                    dx = absX,
                    dy = absY,
                    mouseData = 0,
                    dwFlags = flags,
                    time = 0,
                    dwExtraInfo = IntPtr.Zero,
                }
            }
        };

        SendInput(1, new[] { input }, Marshal.SizeOf<INPUT>());
    }

    #region Win32 interop
    private const int INPUT_MOUSE = 0;
    private const uint MOUSEEVENTF_MOVE = 0x0001;
    private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
    private const uint MOUSEEVENTF_LEFTUP = 0x0004;
    private const uint MOUSEEVENTF_ABSOLUTE = 0x8000;
    private const uint MOUSEEVENTF_VIRTUALDESK = 0x4000;

    private const int SM_XVIRTUALSCREEN = 76;
    private const int SM_YVIRTUALSCREEN = 77;
    private const int SM_CXVIRTUALSCREEN = 78;
    private const int SM_CYVIRTUALSCREEN = 79;

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public int type;
        public InputUnion U;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);
    #endregion
}
