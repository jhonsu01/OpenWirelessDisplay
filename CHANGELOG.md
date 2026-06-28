# Changelog

Todos los cambios notables de este proyecto se documentan aqui.
Formato basado en [Keep a Changelog](https://keepachangelog.com/es-ES/1.1.0/)
y versionado segun [SemVer](https://semver.org/lang/es/).

La version es gestionada automaticamente por `execution/bump_version.py`,
que toma `version.json` como fuente unica de verdad y la propaga al servidor
(.NET / MSI) y al cliente (Android `versionName`/`versionCode`).

## [Unreleased]

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
