using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using Isla.Diagnostics;
using Isla.Interop;
using Isla.Services;
using Forms = System.Windows.Forms;

namespace Isla;

public partial class IslaWindow : Window
{
    private const double CompactHeight = 34;
    private const double WindowHeight = 380;

    // Geometria en unidades independientes de DPI: escala con el DPI, no con la
    // resolucion. El ancho se ajusta un poco al de la pantalla (ver constructor).
    private readonly double CompactWidth;
    private readonly double DockWidth;
    private readonly double WindowWidth;

    private readonly MediaService _media = new();
    private readonly StatsService _stats = new();
    private readonly ReminderService _reminders = new();
    private readonly LauncherService _launcher = new();
    private readonly ShelfService _shelf = new();
    private readonly SettingsService _settings = new();
    private readonly ActivityService _activity = new();
    private readonly HardwareProfile _hardware;

    private readonly DispatcherTimer _clock = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly DispatcherTimer _poll = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly DispatcherTimer _collapseDelay = new() { Interval = TimeSpan.FromMilliseconds(220) };
    private readonly DispatcherTimer _dueCheck = new() { Interval = TimeSpan.FromSeconds(20) };
    private readonly DispatcherTimer _alert = new() { Interval = TimeSpan.FromSeconds(8) };

    /// <summary>Vigila copias y otras operaciones del Explorador para la pildora.</summary>
    private readonly DispatcherTimer _activityWatch = new() { Interval = TimeSpan.FromMilliseconds(800) };

    /// <summary>Vigila el cursor para asomarse al borde y esconderse al irse.</summary>
    private readonly DispatcherTimer _edgeWatch = new() { Interval = TimeSpan.FromMilliseconds(130) };

    private bool _expanded;

    /// <summary>
    /// Mientras se escribe un recordatorio la isla no se cierra aunque el raton
    /// este fuera: si no, el primer MouseLeave se lleva por delante el campo.
    /// </summary>
    private bool _pinned;

    private bool _mediaBusy;
    private bool _statsBusy;
    private bool _heightLogged;
    private string? _lastTrackLog;
    private Brush? _normalBorder;

    // ---- pildora recogida ----
    private MediaSnapshot? _lastMedia;
    private ShellActivity? _shellActivity;

    // ---- asomarse al borde ----
    private readonly Effect? _islandShadow;
    private readonly bool _firstRun;
    private bool _autoHide;
    private bool _autoHideArmed;
    private bool _hiding;
    private DateTime _outsideSince = DateTime.MaxValue;

    public IslaWindow()
    {
        InitializeComponent();

        _settings.Load();
        _hardware = HardwareService.Detect();

        // El ancho se limita para que en un portatil de 1366 px no ocupe media
        // pantalla, y en uno grande no quede ridiculamente estrecho.
        var screenWidth = SystemParameters.PrimaryScreenWidth;
        DockWidth = Math.Clamp(Math.Round(screenWidth * 0.30), 340, 480);
        CompactWidth = Math.Clamp(Math.Round(DockWidth * 0.52), 200, 260);
        WindowWidth = DockWidth + 40;

        Width = WindowWidth;
        Height = WindowHeight;
        Left = Math.Max(0, (screenWidth - WindowWidth) / 2);
        Top = 0;
        Isla.Width = CompactWidth;
        Isla.Height = CompactHeight;

        _islandShadow = Isla.Effect;
        Log.Write($"GPU: \"{_hardware.Gpu}\" · {_hardware.TierLabel} · {_hardware.Explanation}");
        ApplyEffectsMode(_settings.Current.Effects, persist: false);

        // Con el asomarse activo, la isla arranca oculta y sin parpadeo.
        _autoHide = _settings.Current.RevealOnEdge;
        _firstRun = _settings.FirstRun;
        if (_autoHide) Root.Opacity = 0;

        _reminders.Load();
        _launcher.Load();
        _shelf.Load();

        _clock.Tick += (_, _) => UpdateClock();
        _clock.Start();
        UpdateClock();

        _poll.Tick += async (_, _) => await PollAsync();
        _poll.Start();

        _dueCheck.Tick += (_, _) => CheckDueReminder();
        _dueCheck.Start();

        _edgeWatch.Tick += (_, _) => WatchEdge();
        _edgeWatch.Start();

        _activityWatch.Tick += (_, _) => RefreshActivity();
        _activityWatch.Start();

        _alert.Tick += (_, _) =>
        {
            _alert.Stop();
            RenderReminder();                       // quita el resaltado del aviso
            if (!Isla.IsMouseOver && !_pinned)
            {
                SetExpanded(false);
                if (_autoHide) HideIsland();        // un aviso no deja la isla fija
            }
        };

        _collapseDelay.Tick += (_, _) =>
        {
            _collapseDelay.Stop();
            if (_pinned || Isla.IsMouseOver) return;
            SetExpanded(false);
        };

        SourceInitialized += (_, _) => NativeMethods.HideFromAltTab(this);

        Loaded += async (_, _) =>
        {
            _normalBorder = Isla.BorderBrush;
            BuildLauncher();
            BuildShelf();
            RenderReminder();
            CheckDueReminder();
            UpdateStats();
            UpdateClock();
            await _media.InitializeAsync();
            await PollAsync();

            // Ya con la opacidad a cero: ocultarla aqui evita el parpadeo de
            // arranque y deja la ventana fuera del camino hasta que se asome.
            if (_autoHide) HideIsland();

            // Primer arranque: sin esto el usuario no veria nada y creeria que
            // la app no abrio. Se asoma una vez y luego vuelve a esconderse.
            if (_autoHide && _firstRun)
            {
                RevealIsland();
                _settings.Save();   // para que la proxima vez ya arranque oculta
            }
        };
    }

    // =================================================================== reloj
    private void UpdateClock()
    {
        var now = DateTime.Now;
        var time = now.ToString("HH:mm");

        ClockText.Text = time;
        DateText.Text = now.ToString("ddd d MMM",
            System.Globalization.CultureInfo.GetCultureInfo("es-ES"));

        // La hora solo se vera si la pildora no tiene nada mejor que contar.
        RenderCompact();
    }

    // =================================================================== sondeo
    private async Task PollAsync()
    {
        UpdateStats();
        await UpdateMediaAsync();
    }

    private void UpdateStats()
    {
        if (_statsBusy) return;
        _statsBusy = true;
        try
        {
            var stats = _stats.Sample();
            CpuValue.Text = $"{stats.CpuPercent:0}%";
            RamValue.Text = $"{stats.RamUsedGb:0.0} / {stats.RamTotalGb:0.0} GB";
        }
        catch (Exception ex)
        {
            Log.Write("metricas fallidas", ex);
        }
        finally
        {
            _statsBusy = false;
        }
    }

    private async Task UpdateMediaAsync()
    {
        if (_mediaBusy) return;
        _mediaBusy = true;
        try
        {
            var snapshot = await _media.ReadAsync();

            if (!snapshot.HasSession)
            {
                if (_lastTrackLog is not null)
                {
                    _lastTrackLog = null;
                    Log.Write("sin reproduccion");
                }

                _lastMedia = snapshot;
                TrackTitle.Text = "Sin reproduccion";
                TrackArtist.Text = "Ningun reproductor activo";
                TrackProgress.Value = 0;
                Artwork.Source = null;
                ArtworkPlaceholder.Visibility = Visibility.Visible;
                PlayButton.Content = "\uE768";
                PlayButton.IsEnabled = PrevButton.IsEnabled = NextButton.IsEnabled = false;
                RenderCompact();
                return;
            }

            _lastMedia = snapshot;
            TrackTitle.Text = string.IsNullOrWhiteSpace(snapshot.Title) ? "Sin titulo" : snapshot.Title;
            TrackArtist.Text = string.IsNullOrWhiteSpace(snapshot.Artist) ? "Artista desconocido" : snapshot.Artist;

            // Solo se registra cuando cambia la pista: sirve para saber que
            // reproductor entrega datos y no llena el log a golpe de segundo.
            var trackKey = $"{snapshot.SourceLabel}\u001f{snapshot.Title}\u001f{snapshot.Artist}";
            if (trackKey != _lastTrackLog)
            {
                _lastTrackLog = trackKey;
                Log.Write($"pista ({snapshot.SourceLabel}): \"{snapshot.Title}\" - \"{snapshot.Artist}\", " +
                          $"{snapshot.DurationSeconds:0}s, caratula={(snapshot.Artwork is null ? "no" : "si")}");
            }

            TrackProgress.Value = snapshot.DurationSeconds > 0
                ? Math.Clamp(snapshot.PositionSeconds / snapshot.DurationSeconds * 100, 0, 100)
                : 0;

            Artwork.Source = snapshot.Artwork;
            ArtworkPlaceholder.Visibility = snapshot.Artwork is null ? Visibility.Visible : Visibility.Collapsed;

            PlayButton.Content = snapshot.IsPlaying ? "\uE769" : "\uE768";
            PlayButton.IsEnabled = PrevButton.IsEnabled = NextButton.IsEnabled = true;
            RenderCompact();
        }
        catch (Exception ex)
        {
            Log.Write("actualizacion de musica fallida", ex);
        }
        finally
        {
            _mediaBusy = false;
        }
    }

    // ============================================================ pildora recogida

    /// <summary>
    /// Elige que muestra la pildora recogida. Prioridad: operacion del
    /// Explorador, reproduccion en curso y, solo si no hay nada, la hora.
    /// </summary>
    private void RenderCompact()
    {
        var accent = (Brush)FindResource("Accent");
        var primary = (Brush)FindResource("TextPrimary");
        var secondary = (Brush)FindResource("TextSecondary");
        var muted = (Brush)FindResource("TextMuted");

        if (_shellActivity is { } activity)
        {
            SetCompact(activity.Glyph, $"{activity.Verb}…", accent, primary, 1);
            CompactText.ToolTip = activity.Title;
            return;
        }

        if (_lastMedia is { HasSession: true } media)
        {
            var title = !string.IsNullOrWhiteSpace(media.Title)
                ? media.Title
                : !string.IsNullOrWhiteSpace(media.Artist) ? media.Artist : "Reproduciendo";

            // En pausa se atenua: de un vistazo se sabe si suena o no.
            SetCompact("\uE8D6", title,
                media.IsPlaying ? accent : secondary,
                media.IsPlaying ? primary : secondary,
                media.IsPlaying ? 1 : 0.35);

            CompactText.ToolTip = string.IsNullOrWhiteSpace(media.Artist)
                ? title
                : $"{media.Title} — {media.Artist}";
            return;
        }

        var time = DateTime.Now.ToString("HH:mm");
        SetCompact("\uE823", time, muted, muted, 0);
        CompactText.ToolTip = "Sin actividad en curso";
    }

    private void SetCompact(string glyph, string text, Brush iconBrush, Brush textBrush, double dotOpacity)
    {
        CompactIcon.Text = glyph;
        CompactIcon.Foreground = iconBrush;
        CompactText.Text = text;
        CompactText.Foreground = textBrush;
        CompactDot.Opacity = dotOpacity;
    }

    /// <summary>
    /// Vigila las ventanas de progreso del Explorador. Si empieza una operacion
    /// con la isla escondida, se asoma sola para contarlo y vuelve a irse.
    /// </summary>
    private void RefreshActivity()
    {
        // Con el panel abierto la pildora no se ve: no merece la pena sondear.
        if (_expanded) return;

        var activity = _activity.Detect();

        if (activity is not null && _shellActivity?.Kind != activity.Kind)
            Log.Write($"actividad: {activity.Verb} (\"{activity.Title}\")");

        var changed = activity?.Kind != _shellActivity?.Kind;
        _shellActivity = activity;

        if (!changed) return;

        if (activity is not null && _autoHide && !IsVisible) RevealIsland();

        RenderCompact();
    }

    // =================================================================== musica
    private async void PrevButton_Click(object sender, RoutedEventArgs e) => await _media.PreviousAsync();

    private async void PlayButton_Click(object sender, RoutedEventArgs e) => await _media.TogglePlayPauseAsync();

    private async void NextButton_Click(object sender, RoutedEventArgs e) => await _media.NextAsync();

    // =================================================================== recordatorios
    private void RenderReminder()
    {
        var next = _reminders.Next();
        if (next is null)
        {
            ReminderValue.Text = "ninguno";
            ReminderValue.Foreground = (Brush)FindResource("TextMuted");
            ReminderValue.FontWeight = FontWeights.Normal;
            ReminderButton.ToolTip = "Clic para añadir";
            return;
        }

        ReminderValue.Text = next.Due is { } due
            ? $"{due:HH:mm} · {next.Text}"
            : next.Text;
        ReminderValue.Foreground = (Brush)FindResource("TextPrimary");
        ReminderValue.FontWeight = FontWeights.SemiBold;
        ReminderButton.ToolTip = "Clic para marcarlo como hecho";
    }

    private void ReminderButton_Click(object sender, RoutedEventArgs e)
    {
        var next = _reminders.Next();
        if (next is null)
        {
            ShowReminderInput();
            return;
        }

        _reminders.Complete(next.Id);
        RenderReminder();
    }

    private void AddReminderButton_Click(object sender, RoutedEventArgs e) => ShowReminderInput();

    internal void ShowReminderInput()
    {
        // Puede llegar desde el atajo global con la isla oculta.
        RevealIsland(auto: true);

        _pinned = true;
        _collapseDelay.Stop();
        if (!_expanded) SetExpanded(true);
        Activate();

        ReminderInputRow.Visibility = Visibility.Visible;
        ReminderInput.Focus();
        Keyboard.Focus(ReminderInput);
        RefitHeight();
    }

    private void HideReminderInput()
    {
        _pinned = false;
        ReminderInputRow.Visibility = Visibility.Collapsed;
        ReminderInput.Text = string.Empty;
        RefitHeight();

        // Se deja ver el cambio un momento antes de plegarse.
        ScheduleCollapse(1100);
    }

    /// <summary>Cierra la isla tras un margen, salvo que siga fijada o con el raton dentro.</summary>
    private void ScheduleCollapse(int milliseconds)
    {
        _collapseDelay.Stop();
        _collapseDelay.Interval = TimeSpan.FromMilliseconds(milliseconds);
        _collapseDelay.Start();
    }

    private void ReminderInput_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) CommitReminder();
        else if (e.Key == Key.Escape) HideReminderInput();
    }

    private void ReminderSaveButton_Click(object sender, RoutedEventArgs e) => CommitReminder();

    private void CommitReminder()
    {
        var text = ReminderInput.Text?.Trim() ?? string.Empty;
        if (text.Length == 0)
        {
            HideReminderInput();
            return;
        }

        _reminders.Add(text);
        RenderReminder();
        HideReminderInput();
    }

    /// <summary>Cada 20 s mira si alguno venció y, si toca, abre la isla para avisar.</summary>
    private void CheckDueReminder()
    {
        // No se interrumpe a quien está escribiendo un recordatorio.
        if (_pinned) return;

        var due = _reminders.TakeDue(DateTime.Now);
        if (due is null) return;

        Log.Write($"recordatorio vencido: \"{due.Text}\"");
        ShowReminderAlert(due);
    }

    private void ShowReminderAlert(Reminder reminder)
    {
        // Se asoma sola y, al terminar el aviso, se vuelve a esconder.
        RevealIsland(auto: true);

        ReminderValue.Text = $"AHORA · {reminder.Text}";
        ReminderValue.Foreground = (Brush)FindResource("Accent");
        ReminderValue.FontWeight = FontWeights.Bold;

        SetExpanded(true);
        NativeMethods.PlayNotificationSound();

        // A los 8 s vuelve el aspecto normal (y se pliega si el ratón no está).
        _alert.Stop();
        _alert.Start();
    }

    // =================================================================== lanzador
    private void BuildLauncher()
    {
        LauncherBar.Children.Clear();

        foreach (var item in _launcher.Items)
        {
            var content = new StackPanel { Orientation = Orientation.Horizontal };

            var glyph = new TextBlock
            {
                FontFamily = (FontFamily)FindResource("IconFont"),
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 6, 0),
                Text = string.IsNullOrWhiteSpace(item.Glyph)
                    ? item.Name[..1].ToUpperInvariant()
                    : item.Glyph,
            };
            if (string.IsNullOrWhiteSpace(item.Glyph))
            {
                glyph.FontFamily = (FontFamily)FindResource("UiFont");
                glyph.FontWeight = FontWeights.SemiBold;
            }

            content.Children.Add(glyph);
            content.Children.Add(new TextBlock
            {
                Text = item.Name,
                VerticalAlignment = VerticalAlignment.Center,
            });

            var chip = new Button
            {
                Style = (Style)FindResource("Chip"),
                Content = content,
                ToolTip = item.Target,
            };
            chip.Click += (_, _) => _launcher.Open(item);

            LauncherBar.Children.Add(chip);
        }
    }

    // =================================================================== estante
    private void BuildShelf()
    {
        ShelfBar.Children.Clear();

        foreach (var path in _shelf.Items)
        {
            ShelfBar.Children.Add(CreateShelfChip(path));
        }

        var empty = _shelf.Items.Count == 0;
        ShelfHint.Text = "arrastra archivos aquí";
        ShelfHint.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
    }

    private UIElement CreateShelfChip(string path)
    {
        var isFolder = Directory.Exists(path);
        var name = isFolder ? new DirectoryInfo(path).Name : Path.GetFileName(path);

        var glyph = new TextBlock
        {
            FontFamily = (FontFamily)FindResource("IconFont"),
            FontSize = 11,
            Text = isFolder ? "\uE8B7" : GlyphFor(Path.GetExtension(path)),
            Foreground = (Brush)FindResource("Accent"),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 6, 0),
        };

        var label = new TextBlock
        {
            Text = name,
            VerticalAlignment = VerticalAlignment.Center,
            MaxWidth = 118,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };

        var content = new StackPanel { Orientation = Orientation.Horizontal };
        content.Children.Add(glyph);
        content.Children.Add(label);

        // La x solo aparece al pasar el raton por encima, para no llenar el
        // estante de botones cuando hay varios archivos.
        var close = new Button
        {
            Style = (Style)FindResource("IconButton"),
            Content = "\uE711",
            Width = 14,
            Height = 14,
            FontSize = 7,
            Opacity = 0,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, -5, -5, 0),
            ToolTip = "Quitar del estante",
        };

        var outer = new Grid();
        outer.Children.Add(content);
        outer.Children.Add(close);

        var chip = new Button
        {
            Style = (Style)FindResource("Chip"),
            Content = outer,
            ToolTip = $"{path}\nClic para abrir · arrastra para sacarlo",
        };

        chip.MouseEnter += (_, _) => close.Opacity = 1;
        chip.MouseLeave += (_, _) => close.Opacity = 0;
        chip.Click += (_, _) => _shelf.Open(path);

        close.Click += (_, args) =>
        {
            args.Handled = true;    // que el clic no abra el archivo
            _shelf.Remove(path);
            BuildShelf();
            RefitHeight();
        };

        // Arrastrar la ficha fuera la convierte otra vez en un archivo normal.
        Point start = default;
        content.PreviewMouseLeftButtonDown += (_, args) => start = args.GetPosition(content);
        content.MouseMove += (_, args) =>
        {
            if (args.LeftButton != MouseButtonState.Pressed) return;

            var delta = args.GetPosition(content) - start;
            if (Math.Abs(delta.X) < 4 && Math.Abs(delta.Y) < 4) return;

            DragDrop.DoDragDrop(content,
                new DataObject(DataFormats.FileDrop, new[] { path }),
                DragDropEffects.Copy | DragDropEffects.Link);
        };

        return chip;
    }

    private static string GlyphFor(string extension) => extension.ToLowerInvariant() switch
    {
        ".png" or ".jpg" or ".jpeg" or ".gif" or ".bmp" or ".webp" or ".svg" => "\uE91B",
        ".mp3" or ".wav" or ".flac" or ".m4a" or ".ogg" => "\uE8D6",
        ".mp4" or ".mkv" or ".webm" or ".avi" or ".mov" => "\uE714",
        ".zip" or ".7z" or ".rar" or ".tar" or ".gz" => "\uE7B8",
        ".pdf" or ".doc" or ".docx" or ".txt" or ".md" or ".rtf" or ".odt" => "\uE8A5",
        _ => "\uE7C3",
    };

    /// <summary>Añade rutas al estante (desde arrastre o desde el menu de la bandeja).</summary>
    internal void AddToShelf(IEnumerable<string> paths)
    {
        var added = _shelf.Add(paths);
        if (added == 0) return;

        Log.Write($"estante: {_shelf.Items.Count} archivo(s) (+{added})");

        BuildShelf();
        if (!_expanded) SetExpanded(true);
        else RefitHeight();
    }

    /// <summary>Alternativa al arrastre: elegir archivos con el explorador.</summary>
    internal void PickFilesForShelf()
    {
        var dialog = new Forms.OpenFileDialog
        {
            Title = "Añadir al estante",
            Multiselect = true,
            CheckFileExists = true,
        };

        if (dialog.ShowDialog() == Forms.DialogResult.OK)
        {
            AddToShelf(dialog.FileNames);
        }
    }

    // =================================================================== arrastre
    private void Isla_DragEnter(object sender, DragEventArgs e) => UpdateDragState(e);

    private void Isla_DragOver(object sender, DragEventArgs e) => UpdateDragState(e);

    private void UpdateDragState(DragEventArgs e)
    {
        var hasFiles = e.Data.GetDataPresent(DataFormats.FileDrop);
        e.Effects = hasFiles ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;

        if (!hasFiles) return;

        Isla.BorderBrush = (Brush)FindResource("Accent");
        ShelfHint.Text = "suelta para guardar";
        ShelfHint.Visibility = Visibility.Visible;

        if (!_expanded)
        {
            _pinned = true;
            SetExpanded(true);
        }
    }

    private void Isla_DragLeave(object sender, DragEventArgs e)
    {
        // Los hijos también disparan DragLeave: solo cuenta salir de la isla.
        var point = e.GetPosition(Isla);
        if (point.X >= 0 && point.Y >= 0 &&
            point.X <= Isla.ActualWidth && point.Y <= Isla.ActualHeight)
        {
            return;
        }

        RestoreDragState();
    }

    private void RestoreDragState()
    {
        Isla.BorderBrush = _normalBorder;
        _pinned = false;

        var empty = _shelf.Items.Count == 0;
        ShelfHint.Text = "arrastra archivos aquí";
        ShelfHint.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Isla_Drop(object sender, DragEventArgs e)
    {
        RestoreDragState();

        if (e.Data.GetData(DataFormats.FileDrop) is string[] paths)
        {
            AddToShelf(paths);
        }
    }

    // =================================================================== hover
    private void Isla_MouseEnter(object sender, MouseEventArgs e)
    {
        // Al entrar, la isla pasa a esconderse sola cuando el raton se vaya,
        // aunque se hubiera mostrado con el atajo o desde la bandeja.
        _autoHideArmed = _autoHide;
        _collapseDelay.Stop();
        SetExpanded(true);
    }

    private void Isla_MouseLeave(object sender, MouseEventArgs e)
    {
        // Retardo corto: evita cierres al mover el raton entre el borde y los botones.
        ScheduleCollapse(220);
    }

    private void SetExpanded(bool expanded)
    {
        if (_expanded == expanded) return;
        _expanded = expanded;

        var targetWidth = expanded ? DockWidth : CompactWidth;
        var targetHeight = expanded ? MeasureDockHeight() : CompactHeight;
        var duration = TimeSpan.FromMilliseconds(expanded ? 320 : 220);
        var ease = new CubicEase { EasingMode = expanded ? EasingMode.EaseOut : EasingMode.EaseInOut };

        Isla.BeginAnimation(WidthProperty, Animate(targetWidth, duration, ease));
        Isla.BeginAnimation(HeightProperty, Animate(targetHeight, duration, ease));

        FadeTo(Compact, expanded ? 0 : 1, expanded ? 110 : 170, expanded ? 0 : 130);
        FadeTo(Dock, expanded ? 1 : 0, expanded ? 210 : 110, expanded ? 70 : 0);

        Dock.Visibility = Visibility.Visible;
        Dock.IsHitTestVisible = expanded;

        if (expanded && !_heightLogged)
        {
            _heightLogged = true;
            Log.Write($"alto del panel medido: {MeasureDockHeight():0.#} (objetivo {targetHeight:0.#})");
        }
    }

    /// <summary>Alto que ocupa el contenido ahora mismo, para que la isla no sobre.</summary>
    private double MeasureDockHeight()
    {
        // DesiredSize ya incluye los margenes del panel; solo falta el borde.
        Dock.Measure(new Size(DockWidth - 42, double.PositiveInfinity));
        return Math.Ceiling(Dock.DesiredSize.Height) + 2;
    }

    private void RefitHeight()
    {
        if (!_expanded) return;
        Isla.BeginAnimation(HeightProperty,
            Animate(MeasureDockHeight(), TimeSpan.FromMilliseconds(180),
                new CubicEase { EasingMode = EasingMode.EaseOut }));
    }

    private static DoubleAnimation Animate(double to, TimeSpan duration, IEasingFunction ease) =>
        new(to, new Duration(duration)) { EasingFunction = ease, FillBehavior = FillBehavior.HoldEnd };

    private static void FadeTo(UIElement element, double to, int milliseconds, int delayMs)
    {
        element.BeginAnimation(OpacityProperty,
            new DoubleAnimation(to, new Duration(TimeSpan.FromMilliseconds(milliseconds)))
            {
                BeginTime = TimeSpan.FromMilliseconds(delayMs),
                FillBehavior = FillBehavior.HoldEnd,
            });
    }

    // =================================================================== asomarse
    /// <summary>
    /// Cada 130 ms mira donde esta el cursor. Si la isla esta oculta y el cursor
    /// toca el borde superior por el centro, se asoma; si esta visible y el raton
    /// se fue (y no hay nada que la retenga), se esconde.
    /// </summary>
    private void WatchEdge()
    {
        if (!_autoHide) return;

        var cursor = CursorPosition();
        if (cursor is null) return;

        if (!IsVisible)
        {
            if (IsAtTopEdge(cursor.Value)) RevealIsland(auto: true);
            return;
        }

        if (_hiding || _pinned || _expanded || _alert.IsEnabled)
        {
            _outsideSince = DateTime.MaxValue;
            return;
        }

        if (Isla.IsMouseOver || IsOverIsland(cursor.Value))
        {
            _outsideSince = DateTime.MaxValue;
            return;
        }

        // Si se mostro con el atajo o desde la bandeja y el raton aun no ha
        // entrado, se le da mas margen; si ya entro y salio, se va enseguida.
        var limit = _autoHideArmed ? 850 : 6000;

        if (_outsideSince == DateTime.MaxValue)
        {
            _outsideSince = DateTime.UtcNow;
        }
        else if (DateTime.UtcNow - _outsideSince > TimeSpan.FromMilliseconds(limit))
        {
            HideIsland();
        }
    }

    /// <summary>Posicion del cursor en unidades independientes de DPI.</summary>
    private Point? CursorPosition()
    {
        if (!NativeMethods.GetCursorPos(out var point)) return null;

        // La ventana puede estar oculta: su DPI sigue siendo valido.
        var dpi = VisualTreeHelper.GetDpi(this);
        return new Point(point.X / dpi.DpiScaleX, point.Y / dpi.DpiScaleY);
    }

    /// <summary>Franja invisible del borde superior que hace asomar la isla.</summary>
    private static bool IsAtTopEdge(Point cursor)
    {
        var half = Math.Clamp(SystemParameters.PrimaryScreenWidth * 0.12, 140, 260);
        var center = SystemParameters.PrimaryScreenWidth / 2;
        return cursor.Y <= 3 && Math.Abs(cursor.X - center) <= half;
    }

    private bool IsOverIsland(Point cursor)
    {
        if (Isla.ActualWidth <= 0) return false;

        var origin = Isla.TransformToAncestor(this).Transform(new Point(0, 0));
        var rect = new Rect(Left + origin.X, Top + origin.Y, Isla.ActualWidth, Isla.ActualHeight);
        rect.Inflate(14, 14);
        return rect.Contains(cursor);
    }

    /// <summary>
    /// Muestra la isla deslizandola desde arriba. Si <paramref name="auto"/> es
    /// true (asomarse o un aviso) se volvera a esconder sola; si es una accion
    /// explicita (atajo, bandeja) se queda hasta que el raton entre y salga.
    /// </summary>
    internal void RevealIsland(bool auto = false, bool activate = false)
    {
        _hiding = false;
        _autoHideArmed = auto || !_autoHide;
        _outsideSince = DateTime.MaxValue;

        if (!IsVisible) Show();

        Root.BeginAnimation(OpacityProperty,
            new DoubleAnimation(1, TimeSpan.FromMilliseconds(160))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
            });

        IslaShift.BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(0, TimeSpan.FromMilliseconds(240))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            });

        if (activate) Activate();
    }

    /// <summary>Desvanece la isla y la oculta al terminar.</summary>
    internal void HideIsland()
    {
        if (!IsVisible || _hiding) return;

        _hiding = true;
        _autoHideArmed = false;
        _outsideSince = DateTime.MaxValue;

        // Si se oculta a proposito mientras se escribe, el campo se cancela:
        // dejar _pinned activo la mantendria fija en el siguiente asomo.
        if (_pinned)
        {
            _pinned = false;
            ReminderInputRow.Visibility = Visibility.Collapsed;
            ReminderInput.Text = string.Empty;
        }

        if (_expanded) SetExpanded(false);   // que vuelva a pildora antes de irse

        var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(150))
        {
            FillBehavior = FillBehavior.HoldEnd,
        };

        fade.Completed += (_, _) =>
        {
            if (!_hiding) return;    // mientras tanto se volvio a mostrar
            _hiding = false;
            Hide();
        };

        Root.BeginAnimation(OpacityProperty, fade);
        IslaShift.BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(-18, TimeSpan.FromMilliseconds(200))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn },
            });
    }

    // =================================================================== efectos
    internal EffectsMode EffectsMode => _settings.Current.Effects;

    internal bool EdgeReveal => _settings.Current.RevealOnEdge;

    /// <summary>Aplica (y guarda) el modo de efectos visuales.</summary>
    internal void ApplyEffectsMode(EffectsMode mode, bool persist = true)
    {
        if (persist)
        {
            _settings.Current.Effects = mode;
            _settings.Save();
        }

        var enabled = mode switch
        {
            EffectsMode.On => true,
            EffectsMode.Off => false,
            _ => _hardware.RecommendedEffects,
        };

        // La sombra es el unico efecto caro: la calcula la GPU en cada fotograma.
        Isla.Effect = enabled ? _islandShadow : null;

        Log.Write($"efectos visuales: {(enabled ? "activados" : "desactivados")} " +
                  $"(modo {EffectsModeLabel(mode)})");
    }

    /// <summary>Activa o desactiva el asomarse al borde superior.</summary>
    internal void SetEdgeReveal(bool enabled)
    {
        _autoHide = enabled;
        _settings.Current.RevealOnEdge = enabled;
        _settings.Save();

        if (enabled)
        {
            if (!Isla.IsMouseOver) HideIsland();
        }
        else
        {
            RevealIsland();    // sin asomarse, la isla se queda visible
        }

        Log.Write($"asomarse al borde: {(enabled ? "si" : "no")}");
    }

    private static string EffectsModeLabel(EffectsMode mode) => mode switch
    {
        EffectsMode.On => "siempre",
        EffectsMode.Off => "nunca",
        _ => "automatico",
    };

    // =================================================================== API
    internal void ToggleVisible()
    {
        if (IsVisible) HideIsland();
        else RevealIsland();
    }

    /// <summary>Play/pausa desde el atajo global.</summary>
    internal void TogglePlayback() => _ = _media.TogglePlayPauseAsync();

    /// <summary>Trae la isla delante (por ejemplo al abrir el .exe otra vez).</summary>
    internal void ShowIsland() => RevealIsland();
}
