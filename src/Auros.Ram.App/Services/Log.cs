using System.Globalization;
using Auros.Ram.Persistence;

namespace Auros.Ram.App.Services;

/// <summary>
/// Registro local de errores (RNF-29). Solo guarda el tipo de error y la pila de llamadas:
/// nunca el mensaje, que podría contener datos de negocio, ni contraseñas.
/// </summary>
public static class Log
{
    private static readonly object Gate = new();

    public static void Error(Exception ex, string context)
    {
        try
        {
            var file = Path.Combine(AppPaths.LogDirectory, $"auros-ram-{DateTime.Now:yyyyMMdd}.log");
            var lines = new List<string>
            {
                $"{DateTime.Now.ToString("O", CultureInfo.InvariantCulture)} [{context}] v{AppInfo.Version}",
            };
            for (var e = ex; e is not null; e = e.InnerException)
            {
                lines.Add($"  {e.GetType().FullName} (0x{e.HResult:X8})");
                lines.Add(e.StackTrace ?? "  (sin pila)");
            }

            lock (Gate)
            {
                File.AppendAllLines(file, lines);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
