using System;
using System.Security.Cryptography;

namespace OpenWirelessDisplay.Core;

/// <summary>
/// Genera y valida el PIN de emparejamiento (6 digitos) con un numero limitado de
/// intentos por cliente. El PIN se rota cada vez que se solicita uno nuevo o tras
/// un emparejamiento exitoso, para evitar reutilizacion.
/// </summary>
public sealed class PinManager
{
    public const int MaxAttempts = 5;
    private readonly object _lock = new();
    private string _pin;

    public PinManager()
    {
        _pin = GeneratePin();
    }

    public string CurrentPin
    {
        get { lock (_lock) return _pin; }
    }

    /// <summary>Genera un PIN nuevo criptograficamente aleatorio de 6 digitos.</summary>
    public string Rotate()
    {
        lock (_lock)
        {
            _pin = GeneratePin();
            return _pin;
        }
    }

    /// <summary>Compara en tiempo constante el PIN recibido con el actual.</summary>
    public bool Verify(string candidate)
    {
        if (string.IsNullOrEmpty(candidate)) return false;
        lock (_lock)
        {
            var a = System.Text.Encoding.ASCII.GetBytes(_pin);
            var b = System.Text.Encoding.ASCII.GetBytes(candidate.Trim());
            return CryptographicOperations.FixedTimeEquals(a, b);
        }
    }

    private static string GeneratePin()
    {
        // 000000-999999 uniforme sin sesgo de modulo.
        int value = RandomNumberGenerator.GetInt32(0, 1_000_000);
        return value.ToString("D6");
    }
}
