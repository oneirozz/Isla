namespace Isla.Core;

/// <summary>Operacion larga que el Explorador esta haciendo en este momento.</summary>
public enum ShellActivityKind
{
    Copy,
    Move,
    Delete,
    Compress,
    Extract,
}

/// <summary>
/// Actividad detectada en el Explorador. El texto de su ventana de progreso es
/// lo unico que Windows expone publicamente (no hay API de progreso global).
/// </summary>
public sealed record ShellActivity(ShellActivityKind Kind, string Verb, string Title)
{
    /// <summary>Icono de Segoe MDL2 Assets para la pildora recogida.</summary>
    public string Glyph => Kind switch
    {
        ShellActivityKind.Copy => "\uE8C8",
        ShellActivityKind.Move => "\uE8DE",
        ShellActivityKind.Delete => "\uE74D",
        ShellActivityKind.Compress => "\uE8B7",
        ShellActivityKind.Extract => "\uE896",
        _ => "\uE8C8",
    };
}

/// <summary>
/// Reconoce la operacion a partir del texto de la ventana de progreso, en
/// espanol o en ingles. Solo se le pasan textos de ventanas del Explorador: el
/// filtro por proceso vive en la app, aqui no hay nada que tocar del sistema.
/// </summary>
public static class ShellActivities
{
    private static readonly (string Stem, ShellActivityKind Kind, string Verb)[] Patterns =
    {
        ("copiando", ShellActivityKind.Copy, "Copiando"),
        ("copying", ShellActivityKind.Copy, "Copiando"),
        ("moviendo", ShellActivityKind.Move, "Moviendo"),
        ("moving", ShellActivityKind.Move, "Moviendo"),
        ("eliminando", ShellActivityKind.Delete, "Eliminando"),
        ("deleting", ShellActivityKind.Delete, "Eliminando"),
        ("comprimiendo", ShellActivityKind.Compress, "Comprimiendo"),
        ("compressing", ShellActivityKind.Compress, "Comprimiendo"),
        ("extrayendo", ShellActivityKind.Extract, "Extrayendo"),
        ("extracting", ShellActivityKind.Extract, "Extrayendo"),
    };

    /// <summary>Primera coincidencia, o null si el texto no describe una operacion.</summary>
    public static ShellActivity? Match(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;

        foreach (var (stem, kind, verb) in Patterns)
        {
            if (text.Contains(stem, StringComparison.OrdinalIgnoreCase))
                return new ShellActivity(kind, verb, text.Trim());
        }

        return null;
    }
}
