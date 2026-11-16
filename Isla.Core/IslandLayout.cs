namespace Isla.Core;

/// <summary>
/// Medidas de la isla para un ancho de pantalla dado, en unidades independientes
/// de DPI. Se limita para que en un portatil de 1366 px no ocupe media pantalla,
/// y en uno grande no quede ridiculamente estrecha.
/// </summary>
public readonly record struct IslandLayout(double DockWidth, double CompactWidth, double WindowWidth)
{
    public static IslandLayout For(double screenWidth)
    {
        var dock = Math.Clamp(Math.Round(screenWidth * 0.30), 340, 480);
        var compact = Math.Clamp(Math.Round(dock * 0.52), 200, 260);
        return new IslandLayout(dock, compact, dock + 40);
    }
}
