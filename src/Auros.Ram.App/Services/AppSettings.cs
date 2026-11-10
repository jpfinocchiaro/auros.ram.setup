using System.Text.Json;
using Auros.Ram.Core.Model;
using Auros.Ram.Persistence;

namespace Auros.Ram.App.Services;

/// <summary>
/// Preferencias de la aplicación (RNF-16), fuera de los archivos de matriz.
/// La lista de recientes guarda rutas, nunca contraseñas.
/// </summary>
public sealed class AppSettings
{
    public const int MaxRecent = 10;

    public List<string> RecentFiles { get; set; } = [];
    public int AutosaveMinutes { get; set; } = 5;
    public int LockMinutes { get; set; } = 15;

    /// <summary>Cinta de opciones contraída: solo se ven sus pestañas.</summary>
    public bool RibbonCollapsed { get; set; }

    /// <summary>Código del idioma de la aplicación (es = español, el de origen). Se aplica al iniciar.</summary>
    public string Language { get; set; } = "es";

    /// <summary>Abreviaturas de los niveles ([ALT], [MED], [BAJ]) en la matriz de riesgo.</summary>
    public bool ShowLevelAbbreviations { get; set; } = true;

    /// <summary>Monto del impacto junto a cada riesgo en la matriz (casilla de la pantalla de la matriz).</summary>
    public bool ShowImpactAmount { get; set; }

    /// <summary>Fechas de evaluación: anterior a hoy al dar de alta un riesgo.</summary>
    public bool AllowPastDateOnCreate { get; set; } = true;

    /// <summary>Fechas de evaluación: al modificar un riesgo, anterior a una evaluación ya registrada (se intercala).</summary>
    public bool AllowDateBeforeRecorded { get; set; } = true;

    /// <summary>Fechas de evaluación: anterior a hoy al modificar un riesgo o corregir una evaluación.</summary>
    public bool AllowPastDateOnEdit { get; set; } = true;

    public AssessmentDatePolicy DatePolicy => new(AllowPastDateOnCreate, AllowDateBeforeRecorded, AllowPastDateOnEdit);

    /// <summary>Impacto: qué dato se pide (nivel, monto o cualquiera) y si el nivel se ajusta solo al monto.</summary>
    public EntryMode ImpactEntry { get; set; } = EntryMode.Level;
    public bool ImpactAuto { get; set; } = true;

    /// <summary>Probabilidad: qué dato se pide (nivel, porcentaje o cualquiera) y si el nivel se ajusta solo al porcentaje.</summary>
    public EntryMode ProbabilityEntry { get; set; } = EntryMode.Level;
    public bool ProbabilityAuto { get; set; } = true;

    public RiskEntryPolicy EntryPolicy => new(ImpactEntry, ImpactAuto, ProbabilityEntry, ProbabilityAuto);

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(AppPaths.SettingsFile))
            {
                var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(AppPaths.SettingsFile)) ?? new AppSettings();
                settings.AutosaveMinutes = Math.Clamp(settings.AutosaveMinutes, 1, 60);
                settings.LockMinutes = Math.Clamp(settings.LockMinutes, 1, 240);
                return settings;
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            Log.Error(ex, "Settings.Load");
        }

        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            var tmp = AppPaths.SettingsFile + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(tmp, AppPaths.SettingsFile, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Error(ex, "Settings.Save");
        }
    }

    public void AddRecent(string path)
    {
        var full = Path.GetFullPath(path);
        RecentFiles.RemoveAll(p => string.Equals(p, full, StringComparison.OrdinalIgnoreCase));
        RecentFiles.Insert(0, full);
        if (RecentFiles.Count > MaxRecent)
        {
            RecentFiles.RemoveRange(MaxRecent, RecentFiles.Count - MaxRecent);
        }

        Save();
    }

    public void RemoveRecent(string path)
    {
        RecentFiles.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
        Save();
    }
}
