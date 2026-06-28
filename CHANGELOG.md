# Changelog

Todos los cambios notables de este proyecto se documentan aqui.
Formato basado en [Keep a Changelog](https://keepachangelog.com/es-ES/1.1.0/)
y versionado segun [SemVer](https://semver.org/lang/es/).

La version es gestionada automaticamente por `execution/bump_version.py`,
que toma `version.json` como fuente unica de verdad y la propaga al servidor
(.NET / MSI) y al cliente (Android `versionName`/`versionCode`).

## [Unreleased]

## [0.1.7] - 2026-06-28
### Added
- **Botón "+ Monitor virtual"** en el servidor: descarga el paquete oficial del *Virtual
  Display Driver* (open-source, VirtualDrivers) y lanza su instalador con UAC, para habilitar
  el **modo extendido** sin pasos manuales. Si falla, abre la página oficial como respaldo.
- **Botón "↻ Actualizar lista"**: re-lee los monitores sin reiniciar (para que el monitor
  virtual recién instalado aparezca en el desplegable).

## [0.1.6] - 2026-06-28
### Fixed
- **Tema oscuro completo:** el desplegable de monitor se veía casi blanco (popup e items con
  estilo claro del sistema). Ahora el `ComboBox` tiene plantilla oscura (caja, popup e items
  con resaltado azul). Además la **barra de título** del servidor es oscura
  (`DwmSetWindowAttribute`), eliminando las zonas claras de la ventana.

## [0.1.5] - 2026-06-28
### Fixed
- **El desplegable de monitor salía vacío** (no se podía elegir otra pantalla). Se enlazaba a
  una propiedad de un `struct` vía `DisplayMemberPath` y no mostraba texto. Ahora usa items de
  texto plano (índice del combo = índice del monitor). Verificado: lista los 3 monitores.
### Added
- **Mantener pantalla encendida en Android** mientras se comparte (`FLAG_KEEP_SCREEN_ON`):
  evita que el dispositivo se bloquee y se corte la conexión.

## [0.1.4] - 2026-06-28
### Added
- **Selector de monitor a compartir** en el servidor: elige cualquier monitor (incluido un
  monitor virtual de un driver IDD) para espejar o **extender**.
- **Guía de modo extendido** (`docs/MODO-EXTENDIDO.md`): integrar un driver de pantalla
  virtual open-source para usar el teléfono como monitor nuevo (no espejo).
### Changed
- **Menos lag:** el servidor reescala la captura (ancho máx. 1600) y baja FPS por defecto a 12;
  el cliente Android descarta cuadros atrasados y decodifica solo el más reciente en un hilo
  aparte (evita la acumulación de retraso).
### Fixed
- **Input multi-monitor:** el cursor se mapea usando los bounds reales del monitor (con offset),
  así el toque cae en el monitor correcto y no solo en el principal.

## [0.1.3] - 2026-06-28
### Fixed
- **Bug critico: pulsar "Iniciar" cerraba la app.** El estilo del boton asignaba un `Color`
  (`AccentDarkColor`) a la propiedad `Background` (que espera un `Brush`); al hacer hover/click
  WPF lanzaba `InvalidOperationException: "#FF2563EB" no es un valor valido para Background` y
  el proceso moria. Se anadio `AccentDarkBrush` y el trigger ahora usa el Brush. Verificado con
  UI Automation: hover + Iniciar funcionan y se muestra el PIN.
### Changed
- **APK con nombre versionado:** el CI ahora publica `OpenWirelessDisplay-<version>.apk`
  (antes siempre `app-debug.apk`), para diferenciar cada compilacion.

## [0.1.2] - 2026-06-28
### Fixed
- **Bug critico: la GUI del servidor no abria (crash al inicio).** El `StartupUri` se
  resolvia relativo a la carpeta de `App.xaml` (`App/`), generando `App/App/MainWindow.xaml`
  y lanzando `IOException: No se encuentra el recurso 'app/app/mainwindow.xaml'`. Corregido a
  `StartupUri="MainWindow.xaml"`. Verificado: la ventana ahora abre correctamente.

## [0.1.1] - 2026-06-28
### Fixed
- **Bug critico de descubrimiento:** el servidor anunciaba por mDNS una IP de un
  adaptador virtual (WSL/Hyper-V, ej. `172.23.128.1`) inalcanzable por el telefono.
  Ahora selecciona la IP de salida real de la LAN (truco UDP) y descarta interfaces
  sin gateway (virtuales/Bluetooth/APIPA).
### Added
- **Reglas de Firewall en el instalador** (programa + TCP 7345 + mDNS UDP 5353), para
  que Windows no bloquee el trafico entrante del telefono.
- **Conexion manual por IP** en la app Android, como alternativa si el descubrimiento
  automatico falla en la red.

## [0.1.0] - 2026-06-28
### Added
- Estructura monorepo (server-windows + client-android) bajo licencia MIT.
- **Servidor Windows (.NET 8 / WPF, tema oscuro):** captura de pantalla,
  emparejamiento por **PIN**, descubrimiento automatico via **mDNS/DNS-SD**,
  servidor de streaming TCP (MJPEG) y canal de control de input.
- **Cliente Android (Kotlin, tema oscuro Material 3):** autodescubrimiento con
  `NsdManager`, pantalla de emparejamiento por PIN, render en `SurfaceView` y
  reenvio de eventos tactiles normalizados.
- **Gestion de versiones:** `version.json` + `execution/bump_version.py` (SemVer)
  con propagacion automatica y actualizacion de este CHANGELOG.
- **CI/CD:** workflows de GitHub Actions que compilan el **MSI** (WiX) y el **APK**.
- Scaffold del **driver IDD nativo (C++/IddCx)** para el "modo monitor extendido"
  (fase futura: requiere Visual Studio + WDK + firma).
