using Isla.Core;
using Isla.Tests;

// Pruebas de la logica pura de Isla. Sin interfaz, sin red y sin reloj real:
// todo lo que se prueba recibe sus datos como parametros.
//
//   dotnet run --project tests\Isla.Tests -c Release

CaseRunner.Area("Explorador: reconocer la operacion");

CaseRunner.Case("espanol: Copiando", () =>
{
    var a = ShellActivities.Match("Copiando 20.001 elementos de copia a destino - 85% completado");
    Check.True(a is not null, "detecta copia");
    Check.Equal(ShellActivityKind.Copy, a!.Kind, "tipo");
    Check.Equal("Copiando", a.Verb, "verbo");
});

CaseRunner.Case("ingles: Copying", () =>
{
    var a = ShellActivities.Match("Copying 12 items from A to B");
    Check.Equal(ShellActivityKind.Copy, a!.Kind, "tipo");
    Check.Equal("Copiando", a.Verb, "verbo en espanol");
});

CaseRunner.Case("mover, borrar, comprimir y extraer", () =>
{
    Check.Equal(ShellActivityKind.Move, ShellActivities.Match("Moviendo 3 archivos")!.Kind, "mover");
    Check.Equal(ShellActivityKind.Move, ShellActivities.Match("Moving 3 files")!.Kind, "move");
    Check.Equal(ShellActivityKind.Delete, ShellActivities.Match("Eliminando 9 elementos")!.Kind, "borrar");
    Check.Equal(ShellActivityKind.Delete, ShellActivities.Match("Deleting 9 items")!.Kind, "delete");
    Check.Equal(ShellActivityKind.Compress, ShellActivities.Match("Comprimiendo a .zip")!.Kind, "comprimir");
    Check.Equal(ShellActivityKind.Compress, ShellActivities.Match("Compressing to .zip")!.Kind, "compress");
    Check.Equal(ShellActivityKind.Extract, ShellActivities.Match("Extrayendo archivos")!.Kind, "extraer");
    Check.Equal(ShellActivityKind.Extract, ShellActivities.Match("Extracting files")!.Kind, "extract");
});

CaseRunner.Case("no confunde titulos corrientes", () =>
{
    Check.Null(ShellActivities.Match("Carpeta de usuario"), "carpeta");
    Check.Null(ShellActivities.Match("84% completado"), "solo porcentaje");
    Check.Null(ShellActivities.Match(null), "null");
    Check.Null(ShellActivities.Match("   "), "espacios");
});

CaseRunner.Case("mayusculas y espacios", () =>
{
    var a = ShellActivities.Match("  COPIANDO 5 elementos  ");
    Check.Equal(ShellActivityKind.Copy, a!.Kind, "tipo");
    Check.Equal("COPIANDO 5 elementos", a.Title, "titulo recortado");
});

CaseRunner.Case("cada operacion tiene su icono", () =>
{
    Check.Equal("\uE8C8", ShellActivities.Match("Copiando")!.Glyph, "copiar");
    Check.Equal("\uE8DE", ShellActivities.Match("Moviendo")!.Glyph, "mover");
    Check.Equal("\uE74D", ShellActivities.Match("Eliminando")!.Glyph, "borrar");
    Check.Equal("\uE8B7", ShellActivities.Match("Comprimiendo")!.Glyph, "comprimir");
    Check.Equal("\uE896", ShellActivities.Match("Extrayendo")!.Glyph, "extraer");
});

CaseRunner.Area("Pildora: que se cuenta y con que prioridad");

var activity = new ShellActivity(ShellActivityKind.Copy, "Copiando", "Copiando 10 elementos");
var playing = new PillMedia(true, "Cancion", "Artista", true);
var paused = new PillMedia(true, "Cancion", "Artista", false);
var noClock = new DateTime(2026, 11, 15, 9, 5, 0);

CaseRunner.Case("la operacion manda sobre la musica", () =>
{
    var v = PillPresenter.Choose(activity, playing, noClock);
    Check.Equal("\uE8C8", v.Glyph, "icono");
    Check.Equal("Copiando…", v.Text, "texto");
    Check.Equal(PillTone.Accent, v.IconTone, "tono del icono");
    Check.Equal(PillTone.Primary, v.TextTone, "tono del texto");
    Check.Equal(1d, v.DotOpacity, "punto encendido");
    Check.Equal("Copiando 10 elementos", v.Tooltip, "tooltip");
});

CaseRunner.Case("musica sonando", () =>
{
    var v = PillPresenter.Choose(null, playing, noClock);
    Check.Equal(PillPresenter.MusicGlyph, v.Glyph, "icono");
    Check.Equal("Cancion", v.Text, "texto");
    Check.Equal(1d, v.DotOpacity, "punto encendido");
    Check.Equal("Cancion — Artista", v.Tooltip, "tooltip");
    Check.Equal(PillTone.Primary, v.TextTone, "texto claro");
});

CaseRunner.Case("musica en pausa se atenua", () =>
{
    var v = PillPresenter.Choose(null, paused, noClock);
    Check.Near(0.35, v.DotOpacity, 0.001, "punto atenuado");
    Check.Equal(PillTone.Secondary, v.TextTone, "texto secundario");
    Check.Equal(PillTone.Secondary, v.IconTone, "icono secundario");
});

CaseRunner.Case("sin titulo se usa el artista", () =>
{
    var v = PillPresenter.Choose(null, new PillMedia(true, "", "Artista", true), noClock);
    Check.Equal("Artista", v.Text, "texto");
    Check.Equal("Artista", v.Tooltip, "tooltip");
});

CaseRunner.Case("sin titulo ni artista", () =>
{
    var v = PillPresenter.Choose(null, new PillMedia(true, " ", "", true), noClock);
    Check.Equal(PillPresenter.NothingPlaying, v.Text, "texto");
});

CaseRunner.Case("sin nada: la hora, atenuada", () =>
{
    var v = PillPresenter.Choose(null, null, noClock);
    Check.Equal(PillPresenter.ClockGlyph, v.Glyph, "icono");
    Check.Equal("09:05", v.Text, "hora");
    Check.Equal(0d, v.DotOpacity, "sin punto");
    Check.Equal(PillTone.Muted, v.IconTone, "icono apagado");
    Check.Equal(PillTone.Muted, v.TextTone, "texto apagado");
    Check.Equal(PillPresenter.NothingAtAll, v.Tooltip, "tooltip");
});

CaseRunner.Case("una sesion vacia no cuenta", () =>
{
    var v = PillPresenter.Choose(null, PillMedia.None, noClock);
    Check.Equal(PillPresenter.ClockGlyph, v.Glyph, "icono de reloj");
});

CaseRunner.Area("Medidas de la isla");

CaseRunner.Case("1920 px: la isla grande de siempre", () =>
{
    var l = IslandLayout.For(1920);
    Check.Equal(480d, l.DockWidth, "ancho desplegada");
    Check.Equal(250d, l.CompactWidth, "ancho recogida");
    Check.Equal(520d, l.WindowWidth, "ancho de ventana");
});

CaseRunner.Case("1366 px: no ocupa media pantalla", () =>
{
    var l = IslandLayout.For(1366);
    Check.Equal(410d, l.DockWidth, "ancho desplegada");
    Check.Equal(213d, l.CompactWidth, "ancho recogida");
});

CaseRunner.Case("pantallas extremas se limitan", () =>
{
    Check.Equal(480d, IslandLayout.For(3840).DockWidth, "4K no crece mas");
    Check.Equal(200d, IslandLayout.For(800).CompactWidth, "800 px no baja de 200");
    Check.Equal(340d, IslandLayout.For(800).DockWidth, "800 px no baja de 340");
    Check.True(IslandLayout.For(1366).CompactWidth < IslandLayout.For(1366).DockWidth, "recogida mas estrecha");
});

CaseRunner.Area("Estante: icono por tipo de archivo");

CaseRunner.Case("carpetas", () =>
{
    Check.Equal(FileGlyphs.Folder, FileGlyphs.For(null, true), "carpeta sin extension");
    Check.Equal(FileGlyphs.Folder, FileGlyphs.For(".png", true), "carpeta con extension");
});

CaseRunner.Case("extensiones conocidas, sin importar mayusculas", () =>
{
    Check.Equal("\uE91B", FileGlyphs.For(".PNG", false), "imagen");
    Check.Equal("\uE91B", FileGlyphs.For(".jpg", false), "imagen jpg");
    Check.Equal("\uE8D6", FileGlyphs.For(".FLAC", false), "audio");
    Check.Equal("\uE714", FileGlyphs.For(".mkv", false), "video");
    Check.Equal("\uE7B8", FileGlyphs.For(".7z", false), "comprimido");
    Check.Equal("\uE8A5", FileGlyphs.For(".PDF", false), "documento");
});

CaseRunner.Case("lo que no se conoce", () =>
{
    Check.Equal(FileGlyphs.Unknown, FileGlyphs.For(".xyz", false), "extension rara");
    Check.Equal(FileGlyphs.Unknown, FileGlyphs.For(null, false), "sin extension");
    Check.Equal(FileGlyphs.Unknown, FileGlyphs.For("", false), "extension vacia");
});

CaseRunner.Area("Recordatorios: parseo, orden y avisos");

var today = new DateTime(2026, 11, 15, 10, 0, 0);

CaseRunner.Case("texto sin hora", () =>
{
    var p = ReminderRules.Parse("  comprar pan  ", today);
    Check.Equal("comprar pan", p.Text, "texto");
    Check.Null(p.Due, "sin hora");
});

CaseRunner.Case("texto con hora", () =>
{
    var p = ReminderRules.Parse("comprar pan @18:30", today);
    Check.Equal("comprar pan", p.Text, "texto");
    Check.True(p.Due is not null, "con hora");
    Check.Equal(18, p.Due!.Value.Hour, "hora");
    Check.Equal(30, p.Due!.Value.Minute, "minuto");
});

CaseRunner.Case("manana, con y sin enye", () =>
{
    var sin = ReminderRules.Parse("reunion @manana", today);
    var con = ReminderRules.Parse("reunion @mañana", today);
    Check.Equal(today.Date.AddDays(1).AddHours(9), sin.Due!.Value, "manana");
    Check.Equal(sin.Due!.Value, con.Due!.Value, "mañana");
    Check.Equal("reunion", sin.Text, "texto");
});

CaseRunner.Case("una arroba que no es una hora no recorta nada", () =>
{
    var p = ReminderRules.Parse("escribir a @nadie", today);
    Check.Equal("escribir a @nadie", p.Text, "texto entero");
    Check.Null(p.Due, "sin hora");
});

CaseRunner.Case("solo la arroba se queda tal cual", () =>
{
    var p = ReminderRules.Parse("@18:30", today);
    Check.Equal("@18:30", p.Text, "texto");
    Check.Null(p.Due, "sin texto delante no hay hora");
});

CaseRunner.Case("null y vacio", () =>
{
    Check.Equal(string.Empty, ReminderRules.Parse(null, today).Text, "null");
    Check.Equal(string.Empty, ReminderRules.Parse("   ", today).Text, "espacios");
});

CaseRunner.Case("el siguiente: hora mas cercana primero", () =>
{
    var items = new[]
    {
        new ReminderItem("sin hora", null),
        new ReminderItem("tarde", today.AddHours(5)),
        new ReminderItem("pronto", today.AddHours(1)),
    };
    Check.Equal("pronto", ReminderRules.Next(items)!.Text, "primero el mas cercano");
});

CaseRunner.Case("el siguiente: los de sin hora, alfabeticos", () =>
{
    var items = new[]
    {
        new ReminderItem("zeta", null),
        new ReminderItem("alfa", null),
    };
    Check.Equal("alfa", ReminderRules.Next(items)!.Text, "orden alfabetico");
});

CaseRunner.Case("el siguiente ignora los hechos", () =>
{
    var items = new[]
    {
        new ReminderItem("hecho", today.AddHours(-1), Done: true),
        new ReminderItem("pendiente", today.AddHours(3)),
    };
    Check.Equal("pendiente", ReminderRules.Next(items)!.Text, "salta los hechos");
});

CaseRunner.Case("el siguiente: nada pendiente", () =>
{
    Check.True(ReminderRules.Next(Array.Empty<ReminderItem>()) is null, "lista vacia");
    Check.True(ReminderRules.Next(new[] { new ReminderItem("x", null, Done: true) }) is null, "todo hecho");
});

CaseRunner.Case("vencidos: el mas antiguo sin avisar", () =>
{
    var items = new[]
    {
        new ReminderItem("futuro", today.AddHours(1)),
        new ReminderItem("viejo", today.AddHours(-3)),
        new ReminderItem("reciente", today.AddHours(-1)),
        new ReminderItem("avisado", today.AddHours(-9), Notified: true),
        new ReminderItem("sin hora", null),
        new ReminderItem("hecho", today.AddHours(-5), Done: true),
    };
    Check.Equal("viejo", ReminderRules.FirstDue(items, today)!.Text, "el mas antiguo");
});

CaseRunner.Case("vencidos: nada que avisar", () =>
{
    var items = new[]
    {
        new ReminderItem("futuro", today.AddHours(1)),
        new ReminderItem("sin hora", null),
        new ReminderItem("avisado", today.AddHours(-1), Notified: true),
    };
    Check.True(ReminderRules.FirstDue(items, today) is null, "nada vencido");
});

CaseRunner.Case("etiqueta del panel", () =>
{
    Check.Equal("ninguno", ReminderRules.Label(null), "sin recordatorio");
    Check.Equal("18:30 · pan", ReminderRules.Label(new ReminderItem("pan", today.Date.AddHours(18).AddMinutes(30))), "con hora");
    Check.Equal("pan", ReminderRules.Label(new ReminderItem("pan", null)), "sin hora");
});

return CaseRunner.Summary();
