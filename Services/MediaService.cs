using System.IO;
using System.Text;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Isla.Diagnostics;
using Isla.Interop;
using Windows.Media.Control;
using Windows.Storage.Streams;

namespace Isla.Services;

/// <summary>De donde salieron los datos de la pista que se esta mostrando.</summary>
internal enum MediaSource
{
    None,
    Smtc,
    Winamp,
}

internal sealed record MediaSnapshot(
    bool HasSession,
    string Title,
    string Artist,
    bool IsPlaying,
    double PositionSeconds,
    double DurationSeconds,
    ImageSource? Artwork,
    MediaSource Source = MediaSource.None)
{
    internal static readonly MediaSnapshot None =
        new(false, string.Empty, string.Empty, false, 0, 0, null);

    /// <summary>Nombre corto para el log: SMTC, Winamp o ninguna.</summary>
    internal string SourceLabel => Source switch
    {
        MediaSource.Smtc => "SMTC",
        MediaSource.Winamp => "Winamp",
        _ => "ninguna",
    };
}

/// <summary>
/// Lee y controla la musica del sistema. Primero prueba SMTC, la API que usa el
/// panel de volumen de Windows (Spotify, navegadores, Groove...). Si no hay nada
/// sonando por ahi, pregunta a los reproductores con API de Winamp (AIMP, Winamp,
/// QCD), que Windows no anuncia por SMTC.
/// </summary>
internal sealed class MediaService
{
    private GlobalSystemMediaTransportControlsSessionManager? _manager;
    private string? _artworkKey;
    private ImageSource? _artwork;

    /// <summary>Origen que se esta mostrando: los botones actuan sobre el.</summary>
    private MediaSource _lastSource = MediaSource.None;

    internal async Task InitializeAsync()
    {
        try
        {
            _manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            Log.Write("SMTC disponible");
        }
        catch (Exception ex)
        {
            Log.Write("SMTC no disponible", ex);
        }
    }

    private GlobalSystemMediaTransportControlsSession? Current =>
        _manager?.GetCurrentSession();

    /// <summary>
    /// Pista que debe verse. Prioridad: SMTC sonando, Winamp sonando, SMTC en
    /// pausa y, por ultimo, Winamp en pausa. Asi AIMP aparece sin tapar a Spotify
    /// o al navegador cuando alguno de esos si publica SMTC.
    /// </summary>
    internal async Task<MediaSnapshot> ReadAsync()
    {
        var smtc = await ReadSmtcAsync();
        if (smtc.IsPlaying) return Remember(smtc);

        // SendMessage puede bloquear si el reproductor no responde: fuera del
        // hilo de la interfaz.
        var winamp = await Task.Run(Winamp.Read);
        if (winamp.IsPlaying) return Remember(winamp);

        return Remember(smtc.HasSession ? smtc : winamp);
    }

    private MediaSnapshot Remember(MediaSnapshot snapshot)
    {
        _lastSource = snapshot.Source;
        return snapshot;
    }

    private async Task<MediaSnapshot> ReadSmtcAsync()
    {
        var session = Current;
        if (session is null) return MediaSnapshot.None;

        try
        {
            var properties = await session.TryGetMediaPropertiesAsync();
            var playback = session.GetPlaybackInfo();
            var timeline = session.GetTimelineProperties();

            var title = properties.Title ?? string.Empty;
            var artist = properties.Artist ?? string.Empty;

            // La caratula solo se recarga cuando cambia la pista.
            var key = $"{title}\u001f{artist}\u001f{properties.AlbumTitle}";
            if (!string.Equals(key, _artworkKey, StringComparison.Ordinal))
            {
                _artworkKey = key;
                _artwork = await LoadArtworkAsync(properties.Thumbnail);
            }

            var duration = timeline.EndTime.TotalSeconds - timeline.StartTime.TotalSeconds;
            var position = timeline.Position.TotalSeconds - timeline.StartTime.TotalSeconds;

            return new MediaSnapshot(
                true,
                title,
                string.IsNullOrWhiteSpace(artist) ? (properties.AlbumArtist ?? string.Empty) : artist,
                playback.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing,
                Math.Max(0, position),
                Math.Max(0, duration),
                _artwork,
                MediaSource.Smtc);
        }
        catch (Exception ex)
        {
            // Una sesion puede morir entre la lectura y el acceso a sus datos.
            Log.Write("lectura de SMTC fallida", ex);
            return MediaSnapshot.None;
        }
    }

    internal async Task TogglePlayPauseAsync()
    {
        if (_lastSource == MediaSource.Winamp)
        {
            await Task.Run(Winamp.TogglePlayPause);
            return;
        }

        if (Current is not { } session) return;
        try
        {
            if (session.GetPlaybackInfo().PlaybackStatus ==
                GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing)
            {
                await session.TryPauseAsync();
            }
            else
            {
                await session.TryPlayAsync();
            }
        }
        catch (Exception ex)
        {
            Log.Write("play/pausa fallido", ex);
        }
    }

    internal async Task NextAsync()
    {
        if (_lastSource == MediaSource.Winamp)
        {
            await Task.Run(Winamp.Next);
            return;
        }

        if (Current is not { } session) return;
        try { await session.TrySkipNextAsync(); }
        catch (Exception ex) { Log.Write("siguiente fallido", ex); }
    }

    internal async Task PreviousAsync()
    {
        if (_lastSource == MediaSource.Winamp)
        {
            await Task.Run(Winamp.Previous);
            return;
        }

        if (Current is not { } session) return;
        try { await session.TrySkipPreviousAsync(); }
        catch (Exception ex) { Log.Write("anterior fallido", ex); }
    }

    private static async Task<ImageSource?> LoadArtworkAsync(IRandomAccessStreamReference? reference)
    {
        if (reference is null) return null;

        try
        {
            using var stream = await reference.OpenReadAsync();
            var size = (uint)stream.Size;
            if (size == 0 || size > 8_000_000) return null;

            var bytes = new byte[size];
            using (var reader = new DataReader(stream.GetInputStreamAt(0)))
            {
                await reader.LoadAsync(size);
                reader.ReadBytes(bytes);
            }

            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.StreamSource = new MemoryStream(bytes);
            bitmap.DecodePixelWidth = 112;    // el doble del tamano en pantalla, para nitidez
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch (Exception ex)
        {
            Log.Write("caratula no disponible", ex);
            return null;
        }
    }
}

/// <summary>
/// Puente con la API de Winamp, que AIMP emula igual que Winamp y QCD: una
/// ventana oculta de clase "Winamp v1.x" que responde a WM_USER para consultar
/// el estado y a WM_COMMAND para los botones. Es de solo lectura salvo que el
/// usuario pulse un boton. AIMP no entrega caratula por esta via.
/// </summary>
internal static class Winamp
{
    private const string WindowClass = "Winamp v1.x";

    // Consultas (WM_USER): lParam = id, wParam = dato.
    private const int IpcIsPlaying = 104;       // 0 parado, 1 sonando, 3 en pausa
    private const int IpcGetOutputTime = 105;   // dato 0: posicion en ms, 1: duracion en s

    // Botones (WM_COMMAND): wParam = id.
    private const int ButtonPrevious = 40044;
    private const int ButtonPlay = 40045;
    private const int ButtonPause = 40046;
    private const int ButtonNext = 40048;

    // Si el reproductor no responde, la isla no debe esperar.
    private const uint SmtoAbortIfHung = 0x0002;
    private const uint TimeoutMs = 150;

    private static IntPtr _handle;
    private static DateTime _nextLookupUtc = DateTime.MinValue;
    private static readonly TimeSpan LookupInterval = TimeSpan.FromSeconds(3);

    /// <summary>
    /// Ventana de la API de Winamp. FindWindow no la encuentra (AIMP la crea
    /// oculta fuera del arbol normal), asi que se enumera por clase.
    /// </summary>
    private static IntPtr Handle
    {
        get
        {
            if (_handle != IntPtr.Zero && NativeMethods.IsWindow(_handle)) return _handle;

            _handle = IntPtr.Zero;
            if (DateTime.UtcNow < _nextLookupUtc) return IntPtr.Zero;
            _nextLookupUtc = DateTime.UtcNow + LookupInterval;

            _handle = FindWindow();
            return _handle;
        }
    }

    private static IntPtr FindWindow()
    {
        var found = IntPtr.Zero;
        var name = new StringBuilder(128);

        NativeMethods.EnumWindows((window, _) =>
        {
            name.Clear();
            NativeMethods.GetClassNameW(window, name, name.Capacity);
            if (!string.Equals(name.ToString(), WindowClass, StringComparison.OrdinalIgnoreCase))
                return true;

            found = window;
            return false;    // ya esta, no seguir enumerando
        }, IntPtr.Zero);

        return found;
    }

    /// <summary>Lee la pista actual. Nunca lanza: si algo falla, no hay sesion.</summary>
    internal static MediaSnapshot Read()
    {
        try
        {
            var handle = Handle;
            if (handle == IntPtr.Zero) return MediaSnapshot.None;

            var status = (int)Send(handle, NativeMethods.WM_USER, IntPtr.Zero, new IntPtr(IpcIsPlaying));
            if (status == 0) return MediaSnapshot.None;    // parado

            var raw = new StringBuilder(512);
            var length = NativeMethods.GetWindowTextW(handle, raw, raw.Capacity);
            if (length <= 0) return MediaSnapshot.None;

            var (artist, title) = SplitTrack(raw.ToString());
            if (string.IsNullOrWhiteSpace(title)) return MediaSnapshot.None;

            // En pausa AIMP anade " [paused]" al titulo, asi que el titulo limpio
            // sirve para emparejar la caratula... que por aqui no existe.
            var positionMs = Send(handle, NativeMethods.WM_USER, IntPtr.Zero, new IntPtr(IpcGetOutputTime)).ToInt64();
            var durationSeconds = Send(handle, NativeMethods.WM_USER, new IntPtr(1), new IntPtr(IpcGetOutputTime)).ToInt64();

            return new MediaSnapshot(
                true,
                title,
                artist,
                status == 1,
                Math.Max(0, positionMs / 1000.0),
                Math.Max(0, durationSeconds),
                null,
                MediaSource.Winamp);
        }
        catch (Exception ex)
        {
            Log.Write("lectura de Winamp fallida", ex);
            return MediaSnapshot.None;
        }
    }

    internal static void TogglePlayPause()
    {
        try
        {
            var handle = Handle;
            if (handle == IntPtr.Zero) return;

            var status = (int)Send(handle, NativeMethods.WM_USER, IntPtr.Zero, new IntPtr(IpcIsPlaying));
            Send(handle, NativeMethods.WM_COMMAND,
                new IntPtr(status == 1 ? ButtonPause : ButtonPlay), IntPtr.Zero);
        }
        catch (Exception ex)
        {
            Log.Write("play/pausa de Winamp fallido", ex);
        }
    }

    internal static void Next()
    {
        try
        {
            var handle = Handle;
            if (handle != IntPtr.Zero)
                Send(handle, NativeMethods.WM_COMMAND, new IntPtr(ButtonNext), IntPtr.Zero);
        }
        catch (Exception ex)
        {
            Log.Write("siguiente de Winamp fallido", ex);
        }
    }

    internal static void Previous()
    {
        try
        {
            var handle = Handle;
            if (handle != IntPtr.Zero)
                Send(handle, NativeMethods.WM_COMMAND, new IntPtr(ButtonPrevious), IntPtr.Zero);
        }
        catch (Exception ex)
        {
            Log.Write("anterior de Winamp fallido", ex);
        }
    }

    private static IntPtr Send(IntPtr handle, int message, IntPtr wParam, IntPtr lParam)
    {
        NativeMethods.SendMessageTimeoutW(
            handle, message, wParam, lParam, SmtoAbortIfHung, TimeoutMs, out var result);
        return result;
    }

    /// <summary>
    /// AIMP titula la ventana como "16. Exodus - Scar Spangled Banner - Winamp"
    /// (y anade " [paused]" en pausa). Se quita el estado, el sufijo del
    /// reproductor, el numero de pista y se parte por el primer " - ".
    /// </summary>
    private static (string Artist, string Title) SplitTrack(string raw)
    {
        var text = raw.Trim();

        foreach (var state in new[] { "[paused]", "[stopped]", "[playing]" })
        {
            if (text.EndsWith(state, StringComparison.OrdinalIgnoreCase))
            {
                text = text[..^state.Length].TrimEnd();
                break;
            }
        }

        foreach (var suffix in new[] { " - Winamp", " - AIMP", " - QCD" })
        {
            if (text.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                text = text[..^suffix.Length].TrimEnd();
                break;
            }
        }

        // Numero de pista delante: "16. ".
        var dot = text.IndexOf(". ", StringComparison.Ordinal);
        if (dot > 0 && dot <= 4 && text[..dot].All(char.IsDigit))
            text = text[(dot + 2)..].TrimStart();

        var separator = text.IndexOf(" - ", StringComparison.Ordinal);
        if (separator > 0)
            return (text[..separator].Trim(), text[(separator + 3)..].Trim());

        return (string.Empty, text.Trim());
    }
}
