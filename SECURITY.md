# Seguridad

## Versiones con soporte

Solo la última publicada en [Releases](https://github.com/oneirozz/Isla/releases/latest).
Isla es un proyecto personal: no hay ramas de mantenimiento ni parches retroactivos.

| Versión | Soporte |
|---|---|
| 1.0.x | Sí |
| anteriores | No |

## Cómo reportar un fallo de seguridad

**No abras un issue público.** Usa la pestaña **Security → Report a vulnerability**
del repositorio
([enlace directo](https://github.com/oneirozz/Isla/security/advisories/new)). Eso abre
un aviso privado que solo vemos tú y yo.

Incluye, si puedes:

- Qué se puede conseguir y en qué condiciones.
- Pasos para reproducirlo.
- Versión de Isla (menú de la bandeja → *Acerca de Isla*) y de Windows.
- Si hace falta un usuario local concreto, o vale cualquiera.

Respondo cuando puedo, sin plazo garantizado. Si el fallo es real, se arregla, se
publica una versión nueva y se te acredita en el aviso salvo que prefieras lo contrario.

## Qué entra en el alcance

Isla es una app de escritorio sin red, sin cuentas y sin privilegios elevados. Lo que
me interesa:

- **Escalada de privilegios** desde el proceso de la app (por ejemplo, a través de la
  clave `Run` que escribe el inicio automático, o de cómo se lanza un acceso del
  lanzador).
- **Escritura o lectura fuera de `%LOCALAPPDATA%\Isla`**: rutas construidas con datos
  externos, arrastrar y soltar, estante de archivos.
- **Ejecución de código no deseada** a través de los JSON de datos
  (`launcher.json` sobre todo) o de los metadatos de una pista de música.
- **Fallos de memoria** en el código nativo: los `P/Invoke` de
  `Interop/NativeMethods.cs` reciben cadenas y estructuras de Win32.
- **Denegación de servicio local**: algo que deje la app en un bucle o consumiendo CPU
  sin forma de salir desde la bandeja.

## Qué no entra

- **El aviso de SmartScreen al descargar.** El ejecutable no está firmado con un
  certificado de código; es conocido y está documentado en el README.
- **Que la app lea el título de tus ventanas o tu música.** Es su función: la píldora
  enseña la actividad en curso, y todo se queda en el equipo.
- **Falta de cifrado de los JSON de `%LOCALAPPDATA%`.** Son datos tuyos, en tu perfil,
  legibles a propósito para que puedas editarlos.
- **Problemas que requieran que un atacante ya controle tu sesión de Windows.** En ese
  punto la isla es lo de menos.

## Buenas prácticas al usarla

- Descarga el `.exe` solo desde [Releases](https://github.com/oneirozz/Isla/releases) y
  comprueba el SHA-256 que va en las notas de la versión.
- No ejecutes `launcher.json` de otra persona sin leerlo: define programas que la isla
  puede abrir.
