namespace Isla.Core;

/// <summary>
/// Lo que se ve en la pildora recogida, ya decidido. La app solo traduce los
/// tonos a pinceles: aqui no hay WPF.
/// </summary>
public enum PillTone
{
    /// <summary>Lavanda de la casa. Se usa cuando hay algo en marcha.</summary>
    Accent,

    /// <summary>Blanco roto: el texto que importa.</summary>
    Primary,

    /// <summary>Gris claro: informacion secundaria.</summary>
    Secondary,

    /// <summary>Gris apagado: la hora, que Windows ya ensena en la barra.</summary>
    Muted,
}

/// <summary>Estado de la musica que necesita la pildora. Nada mas.</summary>
public sealed record PillMedia(bool HasSession, string Title, string Artist, bool IsPlaying)
{
    public static readonly PillMedia None = new(false, string.Empty, string.Empty, false);
}

/// <summary>Una pildora ya resuelta: icono, texto, tono y tooltip.</summary>
public sealed record PillView(
    string Glyph,
    string Text,
    PillTone IconTone,
    PillTone TextTone,
    double DotOpacity,
    string Tooltip);

/// <summary>
/// Decide que cuenta la pildora recogida. Prioridad: operacion del Explorador,
/// musica (sonando o en pausa) y, solo si no hay nada, la hora.
/// </summary>
public static class PillPresenter
{
    public const string MusicGlyph = "\uE8D6";
    public const string ClockGlyph = "\uE823";
    public const string NothingPlaying = "Reproduciendo";
    public const string NothingAtAll = "Sin actividad en curso";

    /// <summary>
    /// Tono atenuado en pausa: de un vistazo se sabe si suena o no. La
    /// actividad del Explorador siempre manda, aunque haya musica.
    /// </summary>
    public static PillView Choose(ShellActivity? activity, PillMedia? media, DateTime now)
    {
        if (activity is { } running)
        {
            return new PillView(
                running.Glyph,
                $"{running.Verb}…",
                PillTone.Accent,
                PillTone.Primary,
                DotOpacity: 1,
                running.Title);
        }

        if (media is { HasSession: true } track)
        {
            var text = !string.IsNullOrWhiteSpace(track.Title)
                ? track.Title
                : !string.IsNullOrWhiteSpace(track.Artist) ? track.Artist : NothingPlaying;

            var tooltip = string.IsNullOrWhiteSpace(track.Artist)
                ? text
                : string.IsNullOrWhiteSpace(track.Title) ? track.Artist : $"{track.Title} — {track.Artist}";

            return new PillView(
                MusicGlyph,
                text,
                track.IsPlaying ? PillTone.Accent : PillTone.Secondary,
                track.IsPlaying ? PillTone.Primary : PillTone.Secondary,
                DotOpacity: track.IsPlaying ? 1 : 0.35,
                tooltip);
        }

        return new PillView(
            ClockGlyph,
            now.ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture),
            PillTone.Muted,
            PillTone.Muted,
            DotOpacity: 0,
            NothingAtAll);
    }
}
