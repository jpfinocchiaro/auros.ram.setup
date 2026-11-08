using System.Diagnostics;
using System.Text.RegularExpressions;
using Auros.Ram.Core.Licensing;
using Auros.Ram.Core.Services;
using Auros.Ram.Persistence;
using Microsoft.Win32;

namespace Auros.Ram.App.Services;

/// <summary>
/// Edición en uso: gratuita (cada matriz admite hasta 1 empresa y 5 riesgos) o completa, con una licencia
/// para este equipo. La licencia se verifica con la clave pública, sin conexión a internet.
/// </summary>
public static partial class Edition
{
    /// <summary>Clave pública de las licencias (la privada la tiene solo la herramienta tools/Auros.Ram.Licencias).</summary>
    private const string PublicKey =
        "MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAE8ygzEMt9alkudRhS9X2V4yBXa8/OX/yBm5UHloOKKg2DtZ7h9YMqfTeQN41IL+nhtURC9U7DVbT5tYxjXbZNrQ==";

    private static readonly Lazy<string> _machineCode = new(() => License.MachineCodeFrom(MachineId()));

    /// <summary>Código de este equipo: el cliente lo envía para pedir su licencia.</summary>
    public static string MachineCode => _machineCode.Value;

    public static License? Current { get; private set; }

    public static bool IsLicensed => Current is not null;

    public static EditionLimits Limits => IsLicensed ? EditionLimits.Unlimited : EditionLimits.Free;

    /// <summary>Se activó una licencia (las matrices abiertas pasan a no tener límites).</summary>
    public static event EventHandler? Changed;

    /// <summary>Lee la licencia guardada, si la hay y sigue siendo válida para este equipo.</summary>
    public static void Load()
    {
        try
        {
            if (File.Exists(AppPaths.LicenseFile))
            {
                Current = License.Verify(File.ReadAllText(AppPaths.LicenseFile), MachineCode, Convert.FromBase64String(PublicKey));
            }
        }
        catch (DomainException ex)
        {
            Log.Error(ex, "Edition.Load");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Error(ex, "Edition.Load");
        }
    }

    /// <summary>
    /// Verifica la clave y la guarda. Lanza <see cref="DomainException"/> si no es válida o es de otro equipo,
    /// o <see cref="IOException"/> si no se pudo guardar.
    /// </summary>
    public static License Activate(string key)
    {
        var license = License.Verify(key, MachineCode, Convert.FromBase64String(PublicKey));
        File.WriteAllText(AppPaths.LicenseFile, key.Trim());
        Current = license;
        Changed?.Invoke(null, EventArgs.Empty);
        return license;
    }

    /// <summary>Pruebas: simula una licencia (o la versión gratuita, con null) sin tocar el archivo de licencia.</summary>
    internal static void UseForTests(License? license)
    {
        Current = license;
        Changed?.Invoke(null, EventArgs.Empty);
    }

    /// <summary>Identificador estable del equipo según el sistema operativo (cambia si se reinstala el sistema).</summary>
    private static string MachineId()
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                using var key = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64)
                    .OpenSubKey(@"SOFTWARE\Microsoft\Cryptography");
                if (key?.GetValue("MachineGuid") is string guid && guid.Length > 0)
                {
                    return guid;
                }
            }
            else if (OperatingSystem.IsMacOS())
            {
                using var process = Process.Start(new ProcessStartInfo("/usr/sbin/ioreg", "-rd1 -c IOPlatformExpertDevice")
                {
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                });
                var output = process?.StandardOutput.ReadToEnd() ?? "";
                process?.WaitForExit(5000);
                if (PlatformUuid().Match(output) is { Success: true } match)
                {
                    return match.Groups[1].Value;
                }
            }
            else
            {
                foreach (var file in new[] { "/etc/machine-id", "/var/lib/dbus/machine-id" })
                {
                    if (File.Exists(file) && File.ReadAllText(file).Trim() is { Length: > 0 } id)
                    {
                        return id;
                    }
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException or System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            Log.Error(ex, "Edition.MachineId");
        }

        return Environment.MachineName;
    }

    [GeneratedRegex("\"IOPlatformUUID\"\\s*=\\s*\"([^\"]+)\"")]
    private static partial Regex PlatformUuid();
}
