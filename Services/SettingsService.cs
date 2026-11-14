using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Isla.Diagnostics;

namespace Isla.Services;

/// <summary>Cuando se aplican los efectos visuales.</summary>
internal enum EffectsMode
{
    /// <summary>Decide la deteccion de hardware (ver HardwareService).</summary>
    Auto,
    On,
    Off,
}

/// <summary>
/// Preferencias de la isla. Van a un JSON local junto al resto de datos: no se
/// sincroniza nada ni sale del equipo.
/// </summary>
internal sealed class IslaSettings
{
    /// <summary>Mostrar la isla al acercar el cursor al borde superior.</summary>
    public bool RevealOnEdge { get; set; } = true;

    /// <summary>Efectos visuales: automaticos segun la GPU, siempre o nunca.</summary>
    public EffectsMode Effects { get; set; } = EffectsMode.Auto;
}

/// <summary>Lee y guarda los ajustes en %LOCALAPPDATA%\Isla\settings.json.</summary>
internal sealed class SettingsService
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        // El JSON se lee a mano: "Auto" se entiende mejor que un 0.
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string _path = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Isla", "settings.json");

    internal IslaSettings Current { get; private set; } = new();

    /// <summary>La primera vez no hay archivo: sirve para mostrar la isla sola.</summary>
    internal bool FirstRun { get; private set; } = true;

    internal void Load()
    {
        try
        {
            if (!File.Exists(_path)) return;
            Current = JsonSerializer.Deserialize<IslaSettings>(File.ReadAllText(_path), Options)
                      ?? new IslaSettings();
            FirstRun = false;
        }
        catch (Exception ex)
        {
            Log.Write("no se pudieron leer los ajustes; se usan los de por defecto", ex);
            Current = new IslaSettings();
        }
    }

    internal void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path, JsonSerializer.Serialize(Current, Options));
        }
        catch (Exception ex)
        {
            Log.Write("no se pudieron guardar los ajustes", ex);
        }
    }
}
