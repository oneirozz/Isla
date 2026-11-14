using System.Windows.Media;
using Isla.Diagnostics;
using Microsoft.Win32;

namespace Isla.Services;

/// <summary>Perfil grafico del equipo, para decidir si conviene gastar efectos.</summary>
internal sealed record HardwareProfile(
    string Gpu,
    int RenderTier,
    bool Integrated,
    bool Basic,
    string Explanation)
{
    /// <summary>
    /// Deja los efectos solo cuando WPF confirma aceleracion completa y la GPU
    /// no es integrada. Es conservador a proposito: un widget siempre visible
    /// no debe robar GPU a lo demas, y el modo se puede forzar desde la bandeja.
    /// </summary>
    internal bool RecommendedEffects => !Basic && !Integrated && RenderTier >= 2;

    internal string TierLabel => RenderTier switch
    {
        0 => "tier 0 (sin aceleracion)",
        1 => "tier 1 (aceleracion parcial)",
        _ => "tier 2 (aceleracion completa)",
    };
}

/// <summary>
/// Detecta el adaptador grafico sin dependencias externas: el nombre sale del
/// registro (clase de controladores de pantalla) y la aceleracion, del propio
/// WPF (RenderCapability.Tier), que es quien de verdad la va a usar.
/// </summary>
internal static class HardwareService
{
    private const string DisplayClassKey =
        @"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}";

    internal static HardwareProfile Detect()
    {
        var tier = RenderCapability.Tier >> 16;   // 0, 1 o 2
        var gpu = ReadGpuName();

        var basic = gpu.Contains("basic display", StringComparison.OrdinalIgnoreCase)
                 || gpu.Contains("basic render", StringComparison.OrdinalIgnoreCase)
                 || gpu.Contains("indirect display", StringComparison.OrdinalIgnoreCase);

        var integrated = !basic && IsIntegrated(gpu);

        var explanation = basic
            ? "adaptador basico de Microsoft: sin aceleracion real"
            : tier < 2
                ? $"sin aceleracion completa (tier {tier})"
                : integrated
                    ? "grafico integrado: efectos desactivados por precaucion"
                    : "grafico dedicado con aceleracion completa";

        return new HardwareProfile(gpu, tier, integrated, basic, explanation);
    }

    /// <summary>
    /// Lee el nombre comercial del adaptador. La clave de clase tiene una
    /// subclave por controlador ("0000", "0001"...) con DriverDesc.
    /// </summary>
    private static string ReadGpuName()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(DisplayClassKey);
            if (key is null) return "desconocida";

            var names = new List<string>();
            foreach (var sub in key.GetSubKeyNames())
            {
                // Solo las subclaves de instancia: 0000, 0001...
                if (sub.Length != 4 || !sub.All(char.IsAsciiDigit)) continue;

                using var adapter = key.OpenSubKey(sub);
                if (adapter?.GetValue("DriverDesc") is string name && name.Trim().Length > 0)
                    names.Add(name.Trim());
            }

            // Si conviven un adaptador real y el basico de Microsoft, manda el real.
            return names.FirstOrDefault(n => !n.Contains("basic", StringComparison.OrdinalIgnoreCase))
                   ?? names.FirstOrDefault()
                   ?? "desconocida";
        }
        catch (Exception ex)
        {
            Log.Write("no se pudo leer el adaptador grafico", ex);
            return "desconocida";
        }
    }

    /// <summary>
    /// Heuristica por nombre. No pretende ser perfecta: solo separa lo que
    /// comparte memoria con la CPU (o es virtual) de una tarjeta dedicada.
    /// </summary>
    private static bool IsIntegrated(string name)
    {
        // Virtuales y de escritorio remoto: no hay GPU real detras.
        foreach (var virtualName in new[] { "vmware", "virtualbox", "hyper-v", "parallels", "qxl", "virtio" })
        {
            if (name.Contains(virtualName, StringComparison.OrdinalIgnoreCase)) return true;
        }

        // Intel: HD, UHD e Iris son integradas; Arc es dedicada.
        if (name.Contains("intel", StringComparison.OrdinalIgnoreCase))
            return !name.Contains("arc", StringComparison.OrdinalIgnoreCase);

        // AMD: Vega, "Radeon Graphics" y las HD son integradas; RX y Pro, dedicadas.
        if (name.Contains("radeon", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("amd", StringComparison.OrdinalIgnoreCase))
        {
            return !name.Contains("rx ", StringComparison.OrdinalIgnoreCase) &&
                   !name.Contains("pro ", StringComparison.OrdinalIgnoreCase) &&
                   !name.Contains("instinct", StringComparison.OrdinalIgnoreCase);
        }

        // NVIDIA y cualquier cosa no reconocida se tratan como dedicadas: es
        // mejor dejar los efectos y que el usuario los quite si le molesta.
        return false;
    }
}
