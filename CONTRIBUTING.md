# Cómo contribuir a Isla

Gracias por el interés. Isla es un proyecto pequeño y sin prisa: se agradece
cualquier ayuda, pero también se agradece que no haya ruido. Antes de escribir
código, abre un issue para acordar qué se va a hacer. Así nadie trabaja dos veces en
lo mismo.

## Antes de nada

- **Busca en los issues abiertos y cerrados.** Puede que ya esté reportado.
- Lee las [limitaciones honestas](README.md#limitaciones-honestas) del README. Hay
  cosas que no se van a hacer a propósito: contador de FPS, gestor de portapapeles,
  integración con la Microsoft Store.
- El proyecto no acepta dependencias externas nuevas. Ver «Reglas del proyecto».

## Reportar un fallo

Abre un [issue de fallo](https://github.com/oneirozz/Isla/issues/new?template=bug_report.yml)
con esto:

1. **Qué pasó** y **qué esperabas** que pasara.
2. **Cómo reproducirlo**: los pasos, en orden, empezando por abrir la app.
3. **Versión de Windows**: pulsa `Win+R`, escribe `winver` y copia la compilación
   (por ejemplo `Windows 10 LTSC 21H2, 19044.XXXX`).
4. **Cómo lo ejecutas**: el `.exe` del release, o compilado por ti.
5. **El log**: `%LOCALAPPDATA%\Isla\isla.log`. Adjúntalo entero si es corto, o las
   últimas cien líneas si es largo. Cada arranque deja una línea `--- arranque ---`,
   así que se ve dónde empieza la sesión del fallo.

Si el fallo es visual, una captura ayuda mucho. Recorta a la zona de la isla y, si
puedes, indica la resolución y el porcentaje de escala de Windows.

## Proponer una idea

Abre un [issue de idea](https://github.com/oneirozz/Isla/issues/new?template=feature_request.yml)
y cuenta el **problema** antes que la solución. «Cuando copio 4000 fotos no sé cuánto
queda» es más útil que «añade una barra de progreso», porque deja abiertas varias
formas de resolverlo.

Las ideas que encajan mejor son las que caben en una isla: algo breve, que se pueda
enseñar en una píldora de 300 px y que no obligue a abrir una ventana aparte.

## Enviar código

1. Haz un fork y trabaja en una rama con nombre descriptivo
   (`arreglo-pildora-vacia`, `recordatorios-repetidos`).
2. Compila y pruébalo antes de abrir el pull request. Las pruebas de la lógica pura
   se ejecutan con `dotnet run --project tests\Isla.Tests -c Release`; lo que toca la
   interfaz se comprueba abriendo la app y usando lo que has cambiado.
3. Un pull request por cosa. Si de paso arreglas tres fallos distintos, van en tres
   pull requests.
4. Explica en el pull request **qué cambia y por qué**. Si toca la interfaz, adjunta
   una captura del antes y el después.

### Compilar

```powershell
dotnet build Isla.sln -c Release
```

Para generar el ejecutable de distribución:

```powershell
dotnet publish Isla.csproj -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:EnableCompressionInSingleFile=true -p:DebugType=None -p:DebugSymbols=false `
  -o dist
```

El binario de desarrollo se compila sin errores **ni advertencias**. Si aparece una
advertencia nueva, se arregla o se justifica en el pull request.

No hay xUnit ni paquetes de prueba: `tests/Isla.Tests` es un ejecutable de consola que
imprime `ok` o `FALLO` por caso y termina con código 1 si algo falla. Si tu cambio es
de lógica pura (por ejemplo, elegir qué enseña la píldora), ponla en `Isla.Core` y
añade un caso en `tests/Isla.Tests/Program.cs`; si toca la interfaz, explica en el pull
request cómo lo comprobaste a mano.

## Reglas del proyecto

Estas no son preferencias de estilo: son lo que mantiene la app como es.

- **Cero dependencias.** Nada de NuGet. Todo lo que hace falta está en .NET 8 o en
  Win32 a través de `Interop/NativeMethods.cs`. Si crees que necesitas un paquete,
  primero abre un issue: probablemente haya una API del sistema que sirva.
- **Nada de red.** Sin telemetría, sin comprobación de actualizaciones, sin cuentas.
  La app tiene que funcionar con el cable desconectado.
- **Windows 10 LTSC es el objetivo.** Nada de APIs que solo existan en Windows 11 sin
  una comprobación previa. La compilación mínima es 19041.
- **Todo local.** Los datos viven en `%LOCALAPPDATA%\Isla` en JSON legible. Si tu
  cambio añade un archivo, documéntalo en el README.
- **Nada que tumbe la app.** Un fallo en el log, en la música o en la actividad se
  captura y se ignora; la isla sigue funcionando.

## Estilo del código

- **C# 12**, `Nullable` e `ImplicitUsings` activados, tal como está el `.csproj`.
- **Identificadores en inglés** (`ActivityService`, `_lastMedia`), **comentarios y
  texto de interfaz en español**. Es lo que hay en todo el proyecto; no lo mezcles.
- Archivos en **UTF-8 sin BOM** y fin de línea **CRLF** (el `.csproj` de la plantilla
  sí trae BOM: déjalo como está).
- Comentarios solo donde el código no se explica solo: el porqué, no el qué. Los
  nombres de Win32 y las constantes raras sí se comentan.
- Métodos cortos y con una responsabilidad. `IslaWindow.xaml.cs` ya es el archivo
  grande del proyecto; no lo engordes si puedes poner la lógica en `Services/`.
- Nada de `async void` salvo en manejadores de eventos.
- Los `P/Invoke` van en `Interop/NativeMethods.cs`, con la firma y el `DllImport`
  juntos, no repartidos por las ventanas.

## Dónde toca cada cosa

| Quiero cambiar… | Archivo |
|---|---|
| El aspecto, el tamaño o las animaciones de la isla | `IslaWindow.xaml`, `IslaWindow.xaml.cs` |
| Qué enseña la píldora recogida | `Isla.Core/PillPresenter.cs` y `Services/ActivityService.cs` |
| La música, SMTC o el respaldo de Winamp/AIMP | `Services/MediaService.cs` |
| Cuándo se asoma y cuándo se esconde | `IslaWindow.xaml.cs` (bucle de cursor y auto-ocultado) |
| La bandeja, los atajos o el arranque | `App.xaml.cs` |
| Un ajuste nuevo | `Services/SettingsService.cs` y el menú de la bandeja |
| Un `P/Invoke` nuevo | `Interop/NativeMethods.cs` |

## Depurar

- **Log**: `%LOCALAPPDATA%\Isla\isla.log`. Desde la bandeja, **Abrir carpeta de
  datos**.
- **Dos instancias**: no las hay. La app usa un mutex; si arrancas una segunda, la
  primera se asoma y la segunda se cierra. Para probar código nuevo, sal de la
  instancia anterior desde la bandeja.
- **Datos de prueba**: puedes borrar `settings.json`, `launcher.json`,
  `reminders.json` o `shelf.json` sin miedo; se regeneran con valores por defecto.
- **Efectos**: en equipos con gráfica integrada se desactivan solos. Fuerza el modo
  desde la bandeja para ver cristal y animaciones.

## Licencia

Al enviar un pull request aceptas que tu aportación se publique bajo la licencia
[MIT](LICENSE) del proyecto. No hay CLA ni cesión de derechos: tu nombre queda en el
historial de git.
