using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace OpenWirelessDisplay.Core;

/// <summary>
/// Asistente para instalar el "Virtual Display Driver" (open-source, VirtualDrivers) que
/// habilita el modo monitor extendido. Descarga el paquete oficial de control desde GitHub,
/// lo extrae y lanza su aplicacion (que instala el driver con UAC). Si algo falla, abre la
/// pagina de releases como respaldo.
///
/// Nota: no reimplementamos la instalacion del driver (creacion del nodo de dispositivo, etc.);
/// delegamos en la herramienta oficial ya probada. Nosotros solo automatizamos descarga+lanzar.
/// </summary>
public static class VirtualDisplayInstaller
{
    private const string ReleasesApi =
        "https://api.github.com/repos/VirtualDrivers/Virtual-Display-Driver/releases/latest";
    public const string ReleasesPage =
        "https://github.com/VirtualDrivers/Virtual-Display-Driver/releases";

    public static async Task RunAsync(Action<string> log)
    {
        log("Buscando la ultima version del Virtual Display Driver...");
        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("OpenWirelessDisplay");

        string json = await http.GetStringAsync(ReleasesApi);
        using var doc = JsonDocument.Parse(json);

        string? url = null, name = null;
        foreach (var asset in doc.RootElement.GetProperty("assets").EnumerateArray())
        {
            var an = asset.GetProperty("name").GetString() ?? "";
            if (an.StartsWith("VDD.Control", StringComparison.OrdinalIgnoreCase) &&
                an.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                name = an;
                url = asset.GetProperty("browser_download_url").GetString();
                break;
            }
        }

        if (url == null || name == null)
        {
            log("No se encontro el paquete de control. Abriendo la pagina oficial de releases...");
            OpenShell(ReleasesPage);
            return;
        }

        string dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "OpenWirelessDisplay", "VDD");
        Directory.CreateDirectory(dir);
        string zipPath = Path.Combine(dir, name);

        log($"Descargando {name} (~70 MB). Esto puede tardar...");
        using (var resp = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead))
        {
            resp.EnsureSuccessStatusCode();
            await using var src = await resp.Content.ReadAsStreamAsync();
            await using var dst = File.Create(zipPath);
            await src.CopyToAsync(dst);
        }

        string extract = Path.Combine(dir, "extracted");
        try { if (Directory.Exists(extract)) Directory.Delete(extract, true); } catch { }
        log("Extrayendo paquete...");
        ZipFile.ExtractToDirectory(zipPath, extract);

        // Buscar el ejecutable de control (el mas grande con 'Control' en el nombre).
        string? exe = Directory.EnumerateFiles(extract, "*.exe", SearchOption.AllDirectories)
            .Where(f => Path.GetFileName(f).Contains("Control", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(f => new FileInfo(f).Length)
            .FirstOrDefault()
            ?? Directory.EnumerateFiles(extract, "*.exe", SearchOption.AllDirectories)
                .OrderByDescending(f => new FileInfo(f).Length)
                .FirstOrDefault();

        if (exe == null)
        {
            log("No se encontro el ejecutable. Abriendo la carpeta extraida para instalarlo manualmente...");
            OpenShell(extract);
            return;
        }

        log($"Abriendo la app del driver: {Path.GetFileName(exe)}.");
        log(">> Acepta el aviso de Windows (UAC) y pulsa 'Install Driver' en esa app.");
        Process.Start(new ProcessStartInfo
        {
            FileName = exe,
            UseShellExecute = true,
            WorkingDirectory = Path.GetDirectoryName(exe)!
        });
        log(">> Despues, en Config. de pantalla de Windows pon el monitor nuevo en 'Extender',");
        log(">> y aqui pulsa 'Actualizar lista' para que aparezca el monitor virtual.");
    }

    private static void OpenShell(string target) =>
        Process.Start(new ProcessStartInfo { FileName = target, UseShellExecute = true });
}
