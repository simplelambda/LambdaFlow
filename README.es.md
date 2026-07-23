# LambdaFlow

Crea aplicaciones de escritorio nativas con un frontend web y el lenguaje de backend que elijas.

Versión estable actual: **1.3.0**.

LambdaFlow empaqueta HTML, CSS y JavaScript en una ventana de escritorio nativa, inicia un ejecutable de backend arbitrario y enruta mensajes JSON entre ambos. El backend puede ser C#, Python, Java, Rust, Go, C++ o cualquier otro proceso capaz de leer y escribir JSON delimitado por líneas.

- Host Windows: WinForms + Microsoft WebView2.
- Host Linux: GTK 3 + WebKitGTK 4.1 mediante Photino.NET.
- Frontend: tecnología web estándar.
- Backend: cualquier ejecutable o intérprete.
- Herramientas: CLI .NET multiplataforma y extensión de VS Code.

## Índice

- [Cómo funciona LambdaFlow](#cómo-funciona-lambdaflow)
- [Características](#características)
- [Requisitos](#requisitos)
- [Inicio rápido](#inicio-rápido)
- [Destinos de compilación](#destinos-de-compilación)
- [Estructura de un proyecto](#estructura-de-un-proyecto)
- [Configuración](#configuración)
- [SDK de frontend](#sdk-de-frontend)
- [SDK de backend](#sdk-de-backend)
- [Protocolo](#protocolo)
- [Extensión de VS Code](#extensión-de-vs-code)
- [Modelo de seguridad](#modelo-de-seguridad)
- [Soporte de Linux](#soporte-de-linux)
- [Probar Windows desde Linux](#probar-windows-desde-linux)
- [Desarrollar LambdaFlow](#desarrollar-lambdaflow)
- [Solución de problemas](#solución-de-problemas)
- [Alcance actual](#alcance-actual)
- [Aviso de seguridad y exención de responsabilidad](#aviso-de-seguridad-y-exención-de-responsabilidad)

## Cómo funciona LambdaFlow

```text
┌──────────────────────────────────────────────────────────────────────┐
│                       Host nativo de LambdaFlow                      │
├──────────────────────────────────┬───────────────────────────────────┤
│ Windows                          │ Linux                             │
│ WinForms · WebView2              │ GTK 3 · WebKitGTK · Photino.NET   │
├──────────────────────────────────┴───────────────────────────────────┤
│ Frontend web · HTML · CSS · JavaScript · SDK JS de LambdaFlow        │
├──────────────────────────────────────────────────────────────────────┤
│ IPC · Named Pipe (Windows) · StdIO (Linux)                           │
└──────────────────────────────────┬───────────────────────────────────┘
                                   │ sobres JSON
                                   ▼
                   ┌───────────────────────────────┐
                   │ Proceso de backend            │
                   │ C# · Java · Python · otros    │
                   └───────────────────────────────┘
```

El usuario inicia el host empaquetado. El host verifica el manifiesto de integridad SHA-256, abre la ventana nativa, inicia el backend configurado y reenvía mensajes en ambas direcciones. LambdaFlow no incluye un servidor HTTP ni obliga a usar JavaScript en el backend.

## Características

- Libertad para elegir el lenguaje de backend.
- Ventanas nativas de Windows y Linux basadas en el webview del sistema.
- SDK de frontend en JavaScript puro, compatible con HTML, React, Vue, Svelte y otras herramientas web.
- SDK de backend alineados para C#, Java y Python.
- Peticiones/respuestas, eventos, sobres de error y entidades tipadas.
- `config.json` para aplicación, ventana, compilación, ejecución, depuración, plataforma y arquitectura.
- Plantillas C#, Java, Python y genérica.
- Plantillas de frontend HTML básico y Vite + React.
- Compilación cruzada de artefactos Windows desde Linux con el SDK de .NET.
- Creación, configuración, build, ejecución y depuración desde VS Code.
- Verificación SHA-256 y origen local restringido para el frontend.

## Requisitos

### Desarrollo común

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- Git
- El compilador o runtime del backend elegido:
  - C#: SDK/runtime de .NET 8
  - Java: JDK 17+ y Maven
  - Python: Python 3.10+
  - Plantilla React: Node.js y npm

El host de LambdaFlow se publica como autocontenido. El backend puede seguir necesitando su propio runtime si su comando de compilación no genera un ejecutable autocontenido.

### Runtime de Windows

- Windows 10 o Windows 11, x64 o arm64
- [Microsoft Edge WebView2 Runtime](https://developer.microsoft.com/microsoft-edge/webview2/)
  Windows 11 suele incluirlo.

### Runtime de Linux

- Distribución de escritorio basada en glibc
- GTK 3
- WebKitGTK 4.1

Los nombres de paquetes dependen de la distribución:

```bash
# Arch Linux / CachyOS
sudo pacman -S dotnet-sdk gtk3 webkit2gtk-4.1

# Debian / Ubuntu
sudo apt install dotnet-sdk-8.0 libgtk-3-0 libwebkit2gtk-4.1-0

# Fedora
sudo dnf install dotnet-sdk-8.0 gtk3 webkit2gtk4.1
```

Para ejecutar una aplicación empaquetada solo hacen falta las bibliotecas de runtime. No se necesitan paquetes de desarrollo porque el puente nativo de Linux viene incluido por Photino.NET.

## Inicio rápido

### 1. Crear un proyecto

Desde el repositorio de LambdaFlow:

```bash
dotnet run --project lambdaflow/Tools/LambdaFlow.Cli -- \
  new MyApp Apps/MyApp \
  --framework . \
  --language csharp \
  --frontend basic
```

Plantillas de backend:

- `csharp`
- `java`
- `python`
- `other`

Plantillas de frontend:

- `basic`
- `react`

Añade `--self-contained` para copiar al proyecto generado las fuentes de framework necesarias.

### 2. Compilar para el sistema actual

```bash
dotnet run --project lambdaflow/Tools/LambdaFlow.Cli -- \
  build Apps/MyApp \
  --framework .
```

En Linux x64 el resultado estará en:

```text
Apps/MyApp/Results/MyApp-1.0.0/linux-x64/
```

En Windows x64:

```text
Apps/MyApp/Results/MyApp-1.0.0/windows-x64/
```

### 3. Ejecutar

Linux:

```bash
./Apps/MyApp/Results/MyApp-1.0.0/linux-x64/MyApp
```

Windows PowerShell:

```powershell
.\Apps\MyApp\Results\MyApp-1.0.0\windows-x64\MyApp.exe
```

## Destinos de compilación

Si se omite `--target`, el CLI selecciona el sistema operativo y la arquitectura actuales.

| Destino | Valor CLI | Host | Carpeta |
|---|---|---|---|
| Windows x64 | `windows-x64` | WebView2 | `windows-x64/` |
| Windows arm64 | `windows-arm64` | WebView2 | `windows-arm64/` |
| Linux x64 | `linux-x64` | GTK/WebKitGTK | `linux-x64/` |
| Linux arm64 | `linux-arm64` | GTK/WebKitGTK | `linux-arm64/` |

Ejemplos:

```bash
# Build nativo de Linux
dotnet run --project lambdaflow/Tools/LambdaFlow.Cli -- \
  build Apps/MyApp --framework . --target linux-x64

# Compilación cruzada de Windows desde Linux
dotnet run --project lambdaflow/Tools/LambdaFlow.Cli -- \
  build Apps/MyApp --framework . --target windows-x64
```

El destino debe existir bajo `platforms` en `config.json`. La compilación cruzada demuestra que el host Windows y el backend compilan, pero sigue haciendo falta Windows o una VM para probar WebView2 y el comportamiento de named pipes.

## Estructura de un proyecto

Proyecto fuente:

```text
MyApp/
├── config.json
├── backend/
├── frontend/
├── lambdaflow/
│   └── Sdk/
│       └── <SDK de backend seleccionado>
├── .vscode/
│   ├── launch.json
│   ├── settings.json
│   └── tasks.json
└── Results/                 generado
```

Aplicación empaquetada:

```text
Results/MyApp-1.0.0/linux-x64/
├── MyApp                    MyApp.exe en Windows
├── config.json
├── frontend.pak
├── lambdaflow.integrity.json
├── backend/
│   └── archivos de ejecución del backend
└── archivos de runtime del host
```

El generador solo copia el SDK del lenguaje de backend seleccionado. El frontend siempre recibe `lambdaflow.js`.

## Configuración

`config.json` es la fuente de verdad para el desarrollo y la ejecución empaquetada.

```json
{
  "appName": "MyApp",
  "appVersion": "1.0.0",
  "organizationName": "MyCompany",
  "appIcon": "app.ico",
  "securityMode": "Hardened",
  "ipcTransport": "Auto",
  "developmentBackendFolder": "backend",
  "developmentFrontendFolder": "frontend",
  "resultFolder": "Results",
  "frontendInitialHTML": "index.html",
  "build": {
    "preBuild": [
      {
        "name": "Compilar frontend",
        "command": "npm run build",
        "workingDirectory": "frontend",
        "enabled": true,
        "continueOnError": false,
        "timeoutSeconds": 120
      }
    ]
  },
  "debug": {
    "enabled": false,
    "frontendDevTools": false,
    "openFrontendDevToolsOnStart": false,
    "captureFrontendConsole": false,
    "showBackendConsole": false,
    "backendLogLevel": "info"
  },
  "platforms": {
    "windows": {
      "archs": {
        "x64": {
          "compileCommand": "dotnet publish Backend.csproj -c Release -r win-x64 --self-contained false -o bin/win-x64",
          "compileDirectory": "bin/win-x64",
          "runCommand": "Backend.exe",
          "runArgs": []
        }
      }
    },
    "linux": {
      "archs": {
        "x64": {
          "compileCommand": "dotnet publish Backend.csproj -c Release -r linux-x64 --self-contained false -o bin/linux-x64",
          "compileDirectory": "bin/linux-x64",
          "runCommand": "Backend",
          "runArgs": []
        }
      }
    }
  },
  "window": {
    "title": "Mi aplicación",
    "width": 1000,
    "height": 700,
    "minWidth": 640,
    "minHeight": 480,
    "maxWidth": 0,
    "maxHeight": 0
  }
}
```

Reglas importantes:

- `ipcTransport: "Auto"` usa named pipes en Windows y StdIO en Linux.
- Una configuración antigua con `NamedPipe` se interpreta automáticamente como StdIO en Linux.
- Con StdIO, el backend debe reservar stdout para el protocolo y escribir logs en stderr.
- `compileCommand` se ejecuta dentro de `developmentBackendFolder`.
- `compileDirectory` es relativa a esa carpeta y se copia al paquete.
- Usa un `compileDirectory` distinto para cada objetivo nativo (como genera C#) para que un paquete no herede binarios de otro objetivo compilado antes.
- `runCommand` se busca primero dentro de `backend/` y después en `PATH`.
- `runArgs` es un array de argumentos; no se interpreta mediante un shell al ejecutar.
- Las rutas de prebuild, backend, frontend y resultados deben permanecer dentro del proyecto.
- `maxWidth` y `maxHeight` a `0` significan que no hay máximo.
- Los comandos prebuild se ejecutan en la máquina que compila. Usa comandos portables si el proyecto se construirá en varios sistemas.

## SDK de frontend

Carga el SDK antes que los scripts de la aplicación:

```html
<script src="lambdaflow.js"></script>
<script src="app.js"></script>
```

El host expone las funciones de bajo nivel `window.send(rawJson)` y `window.receive(rawJson)`. El código de aplicación debe usar `window.LambdaFlow`.

### Peticiones

```js
const result = await LambdaFlow.request(
  'uppercase',
  { text: 'Hola' },
  { timeoutMs: 5000 }
);
```

`request` correlaciona respuestas mediante `id`, admite timeout y `AbortSignal`, y rechaza con `LambdaFlow.Error` si el backend devuelve `ok: false`.

### Eventos

```js
LambdaFlow.send('telemetry.clicked', { button: 'save' });

const unsubscribe = LambdaFlow.on('backend.progress', progress => {
  console.log(progress);
});

LambdaFlow.once('backend.ready', payload => {
  console.log(payload);
});

unsubscribe();
```

`emit` es alias de `send`; `receive` es alias de `on`. `onAny` escucha todos los tipos de evento.

### Peticiones iniciadas por el backend

```js
const unregister = LambdaFlow.handle('ui.getTheme', async () => {
  return { theme: document.documentElement.dataset.theme };
});
```

El SDK envía automáticamente una respuesta de éxito o error correlacionada. Usa `unhandle(kind)` para eliminar el handler.

### Entidades

```js
const dog = LambdaFlow.entity('animals.dog', {
  name: 'Rex',
  age: 4
});

await LambdaFlow.requestEntity('describeDog', 'animals.dog', dog.data);
```

Forma de una entidad:

```json
{
  "$type": "animals.dog",
  "$v": 1,
  "data": {}
}
```

Por defecto se entrega `data` a los handlers. Los metadatos siguen incluyendo tipo, versión, payload original, sobre y hora de recepción.

### API pública completa del frontend

```text
version
configure
isHostAvailable / isAvailable
ensureHostAvailable / ensureAvailable
send / emit / sendEnvelope
request / requestEntity
on / receive / onAny / once / off
handle / unhandle
respond / reject
entity / sendEntity
isEntity / unwrapEntity / entityType / entityVersion
receiveRaw
pendingCount / clearHandlers / destroy
```

Las declaraciones TypeScript están en `lambdaflow/Sdk/JavaScript/lambdaflow.d.ts`. El módulo opcional `lambdaflowApi.ts` expone las mismas operaciones como funciones importables.

## SDK de backend

Archivos canónicos:

| Lenguaje | Archivo |
|---|---|
| C# | `lambdaflow/Sdk/CSharp/LambdaFlow.cs` |
| Java | `lambdaflow/Sdk/Java/LambdaFlow.java` |
| Python | `lambdaflow/Sdk/Python/lambdaflow.py` |

Las APIs usan las convenciones de cada lenguaje, pero comparten los mismos conceptos:

| Concepto | C# | Java | Python |
|---|---|---|---|
| Versión del SDK | `Version` | `VERSION` | `__version__`, `VERSION` |
| Configurar | `Configure` | `configure` | `configure` |
| Registrar evento/petición | `Receive`, `On`, `Handle` | `receive`, `on`, `handle` | `receive`, `on`, `handle` |
| Eliminar handler | `Unhandle`, `Off` | `unhandle`, `off` | `unhandle`, `off` |
| Enviar evento | `Send`, `Emit` | `send`, `emit` | `send`, `emit` |
| Pedir al frontend | `Request`, `RequestAsync` | `request`, `requestAsync` | `request` |
| Respuesta manual | `Respond`, `Reject` | `respond`, `reject` | `respond`, `reject` |
| Entidades | `Entity`, `SendEntity`, `RequestEntityAsync` | `entity`, `sendEntity`, `requestEntityAsync` | `entity`, `send_entity`, `request_entity` |
| Bucle | `Run`, `RunAsync`, `Stop` | `run`, `stop` | `run`, `stop` |
| Peticiones pendientes | `PendingCount` | `pendingCount` | `pending_count` |

C#:

```csharp
LambdaFlow.Receive<TextRequest, TextResponse>(
    "uppercase",
    request => new(request.Text.ToUpperInvariant()));

LambdaFlow.Run();
```

Python:

```python
import lambdaflow as lf

@lf.handle("uppercase")
def uppercase(request):
    return {"text": request["text"].upper()}

lf.run()
```

Java:

```java
LambdaFlow.handle(
    "uppercase",
    TextRequest.class,
    request -> new TextResponse(request.text.toUpperCase()));

LambdaFlow.run();
```

Buenas prácticas:

- Registra handlers antes de iniciar el bucle.
- Devuelve un valor desde el handler en vez de responder manualmente.
- Lanza una excepción para producir una respuesta `ok: false`.
- Usa `send` para eventos y `request` solo cuando haga falta una respuesta.
- Usa entidades únicamente cuando aporten identidad de tipo o versionado de esquema.
- Envía diagnósticos a stderr bajo StdIO.
- No acoples handlers al transporte; los SDK lo eligen mediante variables de entorno.

## Protocolo

Se transmite un objeto JSON UTF-8 por línea.

Petición:

```json
{
  "kind": "uppercase",
  "id": "9d42...",
  "payload": { "text": "hola" }
}
```

Éxito:

```json
{
  "kind": "uppercase.result",
  "id": "9d42...",
  "ok": true,
  "payload": { "text": "HOLA" }
}
```

Error:

```json
{
  "kind": "uppercase.result",
  "id": "9d42...",
  "ok": false,
  "error": {
    "code": "INVALID_INPUT",
    "message": "text es obligatorio",
    "details": {}
  }
}
```

Reglas:

- `kind` es una clave de enrutado obligatoria y no vacía.
- `id` está presente cuando se espera respuesta.
- La respuesta reutiliza el mismo `id`.
- Los SDK añaden `.result` al tipo de respuesta.
- `ok: false` y `error` representan una petición fallida.
- Los eventos pueden omitir `id` y `ok`.
- Las integraciones nuevas deben usar `error` en el nivel superior. Los SDK siguen aceptando el formato antiguo `payload.error`.

Variables de transporte:

```text
LAMBDAFLOW_IPC_TRANSPORT=NamedPipe
LAMBDAFLOW_PIPE_NAME=<nombre-privado>
```

Si no están presentes, los SDK usan stdin/stdout.

## Extensión de VS Code

La extensión está en `Integrations/vscode-extension`.

Comandos:

- `LambdaFlow: New Project`
- `LambdaFlow: Build`
- `LambdaFlow: Build & Run`
- `LambdaFlow: Build & Debug`
- `LambdaFlow: Edit Configuration`

Funciona en Windows y Linux. Build y Run eligen el sistema/arquitectura actuales, localizan la carpeta correcta y solo añaden `.exe` en Windows.

El editor de configuración permite modificar:

- Metadatos e icono
- Límites de la ventana
- Carpetas de frontend y backend
- Comandos prebuild ordenados
- Compilación y ejecución de Windows x64
- Compilación y ejecución de Linux x64
- Transporte Auto, NamedPipe y StdIO
- Depuración y captura de consola del frontend

Desarrollo:

```bash
cd Integrations/vscode-extension
npm install
npm run compile
```

Pulsa `F5` en VS Code con la carpeta de la extensión seleccionada para abrir un Extension Development Host.

## Modelo de seguridad

LambdaFlow solo admite actualmente `securityMode: "Hardened"`.

- El CLI genera `lambdaflow.integrity.json` con hashes SHA-256 de todos los archivos.
- El host se niega a iniciar si falta o se modifica un archivo listado.
- El frontend se sirve desde un origen local privado, no desde URLs arbitrarias del sistema de archivos.
- Se rechaza el path traversal fuera de `frontend.pak`.
- El frontend recibe una Content Security Policy restrictiva.
- Windows desactiva host objects, menús contextuales, atajos del navegador, barra de estado y DevTools salvo que debug los permita.
- Linux desactiva menús contextuales y DevTools salvo que debug los permita.
- Los named pipes de Windows son privados para el usuario actual.

El manifiesto detecta modificaciones accidentales o posteriores al build; no es una firma del publicador. Un atacante capaz de sustituir los archivos y el manifiesto puede recalcular los hashes. Para autenticidad de una release hay que añadir firma de código de la plataforma.

## Soporte de Linux

LambdaFlow usa una sola implementación Linux, no ramas separadas para Debian, Arch y Red Hat.

El host administrado y el puente Photino son iguales para todas las distribuciones de una arquitectura. Lo único específico de la distribución es instalar GTK 3 y WebKitGTK 4.1.

Características compatibles:

- Destinos x64 y arm64
- X11 y Wayland soportados por GTK/WebKitGTK
- Distribuciones basadas en glibc
- Transporte StdIO
- Mismos `frontend.pak`, configuración, integridad, API JavaScript y protocolo que Windows

Linux no usa WebView2 ni named pipes de Windows. `ipcTransport: "Auto"` resuelve esa diferencia.

## Probar Windows desde Linux

Conviene usar tres niveles:

1. Compilación cruzada en Linux:

   ```bash
   dotnet run --project lambdaflow/Tools/LambdaFlow.Cli -- \
     build Apps/MyApp --framework . --target windows-x64
   ```

   Valida compilación C#, resolución NuGet, salida del backend, empaquetado y manifiesto.

2. Pruebas automatizadas de protocolo en Linux:

   Prueba SDK y lógica del backend mediante StdIO sin GUI.

3. VM funcional de Windows:

   Usa KVM/QEMU con libvirt y virt-manager en CachyOS:

   ```bash
   sudo pacman -S qemu-full libvirt virt-manager edk2-ovmf swtpm dnsmasq
   sudo systemctl enable --now libvirtd
   ```

   Crea una VM Windows 11, instala WebView2 si no está presente, comparte o copia la salida `windows-x64` y ejecuta el paquete.

KVM es el equivalente de Linux adecuado. Windows Sandbox es ligero, pero solo funciona sobre un host Windows. Wine no reproduce con fidelidad WebView2, WinForms y la integración de named pipes.

Para CI repetible, conserva un snapshot limpio de la VM o añade un runner Windows nativo.

## Desarrollar LambdaFlow

Mapa del repositorio:

```text
lambdaflow/
├── Core/                  configuración, proceso, integridad e interfaces
├── Hosts/
│   ├── Windows/           WinForms + WebView2 + named pipe/StdIO
│   └── Linux/             Photino + GTK/WebKitGTK + StdIO
├── Sdk/
│   ├── CSharp/
│   ├── Java/
│   ├── JavaScript/
│   └── Python/
├── Tools/LambdaFlow.Cli/  comandos new/build
└── Ontology/              esquema de entidades

Integrations/
├── vscode-extension/      fuente y salida compilada de la extensión
└── vscode/                plantillas de tareas y launch

Examples/
├── CSharp/
├── Java/
└── Python/
```

Comprobaciones de build:

```bash
dotnet build lambdaflow/Tools/LambdaFlow.Cli/LambdaFlow.Cli.csproj -c Release
dotnet build lambdaflow/Hosts/Linux/lambdaflow.linux.csproj -c Release
dotnet build lambdaflow/Hosts/Windows/lambdaflow.windows.csproj -c Release

cd Integrations/vscode-extension
npm install
npm run compile
```

Smoke test del protocolo:

```bash
printf '%s\n' \
  '{"kind":"uppercase","id":"smoke-1","payload":{"text":"hola"}}' \
  | ./comando-backend
```

Respuesta lógica esperada:

```json
{"kind":"uppercase.result","id":"smoke-1","ok":true,"payload":{"text":"HOLA"}}
```

Lee `AGENTS.md` antes de realizar cambios con agentes de código. Contiene la arquitectura mínima, APIs públicas, invariantes y mapa de archivos por tipo de tarea.

## Solución de problemas

### Linux indica que falta WebKitGTK

Instala GTK 3 y WebKitGTK 4.1. Verifica:

```bash
pkg-config --modversion gtk+-3.0
pkg-config --modversion webkit2gtk-4.1
```

### Un backend C# indica que falta .NET

La plantilla C# predeterminada depende del framework. Instala el runtime .NET 8 o cambia su comando de compilación para usar `--self-contained true`.

### El backend termina inmediatamente en Linux

Comprueba que `platforms.linux.archs.<arch>.runCommand` coincida con el archivo empaquetado y que sea ejecutable.

### El JSON del protocolo aparece en logs o los logs rompen las peticiones

Con StdIO, stdout es el canal de protocolo. Escribe logs en stderr.

### Falla la integridad

No edites archivos dentro de `Results/` tras el build. Vuelve a compilar para regenerar el manifiesto.

### Las peticiones del frontend expiran

- Confirma que el backend arranca.
- Confirma que existe un handler para el `kind` exacto.
- Revisa `lambdaflow.crash.log`.
- En debug, revisa `lambdaflow.frontend.log`.
- Comprueba que ningún logger escriba en stdout bajo StdIO.

### El build Windows funciona, pero la aplicación no abre

Prueba en Windows, confirma que WebView2 Runtime esté instalado y revisa `lambdaflow.crash.log`. Un build cruzado correcto no ejecuta la pila gráfica de Windows.

## Alcance actual

- Hosts soportados: Windows y Linux.
- Arquitecturas del host: x64 y arm64.
- macOS está representado en el modelo compartido, pero todavía no tiene host.
- Backends con plantilla: C#, Java, Python y genérico.
- Frontends con plantilla: HTML básico y React.

Las contribuciones deben conservar el protocolo JSON delimitado por líneas y mantener el código de frontend independiente de la implementación del host.

## Aviso de seguridad y exención de responsabilidad

LambdaFlow ejecuta el comando de backend definido por cada aplicación y renderiza el código frontend proporcionado por ella. Compila o ejecuta únicamente proyectos, dependencias y paquetes en los que confíes. No guardes credenciales ni claves privadas en los recursos frontend, archivos de configuración, logs o control de versiones.

El manifiesto de integridad SHA-256 detecta cambios dentro de un paquete compilado, pero no es una firma digital ni demuestra quién publicó el paquete. Quien distribuya una release debe añadir la firma de código propia de la plataforma, mantener actualizados .NET, WebView2, GTK/WebKitGTK, Photino, los runtimes del backend y las dependencias de la aplicación, y revisar sus propios handlers IPC y requisitos de Content Security Policy.

Para comunicar una posible vulnerabilidad, sigue [SECURITY.md](SECURITY.md) y evita publicar detalles explotables en una issue pública.

LambdaFlow se proporciona **tal cual**, sin garantías de seguridad, disponibilidad, idoneidad para un propósito concreto o ausencia de defectos. En la máxima medida permitida por la legislación aplicable, los autores y mantenedores no serán responsables de reclamaciones, daños, pérdida de datos, incidentes de seguridad, interrupciones del servicio u otras responsabilidades derivadas del uso, mal uso, modificación o distribución del software. Cada usuario o distribuidor es responsable de evaluar si LambdaFlow resulta adecuado para su modelo de amenazas y sus obligaciones legales o regulatorias.
