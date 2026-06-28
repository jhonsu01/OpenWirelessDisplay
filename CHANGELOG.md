# Changelog

Todos los cambios notables de este proyecto se documentan aqui.
Formato basado en [Keep a Changelog](https://keepachangelog.com/es-ES/1.1.0/)
y versionado segun [SemVer](https://semver.org/lang/es/).

La version es gestionada automaticamente por `execution/bump_version.py`,
que toma `version.json` como fuente unica de verdad y la propaga al servidor
(.NET / MSI) y al cliente (Android `versionName`/`versionCode`).

## [Unreleased]

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
