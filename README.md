# OpenWirelessDisplay

Alternativa **open source (MIT)** a Spacedesk: convierte uno o varios dispositivos **Android**
en **monitores secundarios inalámbricos** (espejo o **extendido**) de una PC **Windows**, en la
misma red Wi-Fi/LAN.

> **Estado: estable** ✅ — versión **v0.2.1**, probada en PC (Windows 11) y Android.
> Descarga el MSI y el APK en **[Releases](https://github.com/jhonsu01/OpenWirelessDisplay/releases/latest)**.

## Características

- 🔒 **Emparejamiento por PIN** (6 dígitos, rotación tras cada vínculo).
- 📡 **Autodetección** en la red local vía **mDNS/DNS-SD** (sin teclear IPs) + **conexión manual por IP**.
- 🖥️➡️📱 **Modo extendido**: usa el teléfono como un monitor **nuevo** (no solo espejo) mediante un
  driver de pantalla virtual open-source, instalable con un botón desde la app.
- 👥 **Multi-dispositivo**: cada teléfono puede ver **un monitor distinto** a la vez (físico o virtual).
- 🖱️ **Cursor visible** en los monitores virtuales (compuesto en el frame) para arrastrar ventanas.
- ⚡ **Baja latencia**: reescalado + descarte de cuadros atrasados (decodifica solo el más reciente).
- 🌙 **Tema oscuro** completo en ambas aplicaciones.
- 📦 Salidas: **MSI** (Windows) y **APK** (Android), generadas por CI con versión en el nombre.
- 🔖 **Gestión de versiones** automática (SemVer) desde una fuente única (`version.json`).

> **Modo extendido:** requiere instalar un driver de pantalla virtual (un clic en **"+ Monitor
> virtual"** dentro de la app). Guía: [`docs/MODO-EXTENDIDO.md`](docs/MODO-EXTENDIDO.md).
> También se incluye el *scaffold* de un driver IDD propio en
> [`server-windows/driver/`](server-windows/driver/README.md) como alternativa futura.

---

## Arquitectura

```
  SERVIDOR (Windows, .NET 8 / WPF)              CLIENTE (Android, Kotlin)
  ┌───────────────────────────────┐            ┌───────────────────────────────┐
  │ ScreenCapturer (GDI → JPEG)    │            │ DiscoveryManager (NsdManager)  │
  │ MdnsResponder (DNS-SD)         │  mDNS  ◄───┤  descubre _openwdisplay._tcp   │
  │ PinManager (PIN 6 díg.)        │            │ PairingDialog (PIN)            │
  │ StreamServer (TCP)             │  TCP   ───►│ VideoClient (handshake + RX)   │
  │   ├─ handshake PIN             │  frames    │ DisplayActivity (SurfaceView)  │
  │   ├─ frames (MJPEG)            │  ◄────►    │ LowLatencyVideoPlayer (H.264*) │
  │   └─ input (SendInput)         │  input     │ onTouchEvent → coords 0..1     │
  └───────────────────────────────┘            └───────────────────────────────┘
        * ruta H.264/MediaCodec lista para sustituir a MJPEG (ver guía, Fase 3)
```

Protocolo binario compartido: `[1 byte Type][4 bytes BE Length][Payload]`. Definición en
[`server-windows/src/Protocol/WireProtocol.cs`](server-windows/src/Protocol/WireProtocol.cs)
y su paridad en [`client-android/.../WireProtocol.kt`](client-android/app/src/main/java/com/openwdisplay/client/WireProtocol.kt).

## Estructura del repo

```
.
├── server-windows/        # Servidor .NET 8 (WPF, tema oscuro) + MSI (WiX) + driver IDD (scaffold C++)
├── client-android/        # Cliente Kotlin (tema oscuro Material 3) + Gradle
├── execution/             # bump_version.py (gestión de versiones determinística)
├── directives/            # Directiva del marco de orquestación (CLAUDE.md)
├── memory/                # Memoria persistente PARA del agente
├── .github/workflows/     # CI/CD: build.yml (MSI+APK), release.yml (GitHub Release)
├── version.json           # Fuente única de versión
└── CHANGELOG.md
```

## Compilar el servidor (Windows) → MSI

Requisitos: .NET 8 SDK.

```bash
# 1) Sincroniza la versión a los archivos de build
python execution/bump_version.py sync

# 2) Publica self-contained (no requiere runtime instalado en el destino)
cd server-windows
dotnet publish src/OpenWirelessDisplay.Server.csproj -c Release -r win-x64 --self-contained true -o installer/publish

# 3) Construye el MSI (WiX 5)
dotnet tool install --global wix --version 5.0.2
cd installer
wix build Package.wxs -arch x64 -o OpenWirelessDisplay-Server-<version>.msi
```

## Compilar el cliente (Android) → APK

Requisitos: JDK 17 + Android SDK (platform-34, build-tools 34).

```bash
cd client-android
gradle wrapper          # genera ./gradlew la primera vez
./gradlew assembleDebug # APK en app/build/outputs/apk/debug/app-debug.apk
```

O simplemente haz **push a GitHub**: el workflow [`build.yml`](.github/workflows/build.yml)
genera el MSI y el APK como artefactos. Un **tag `vX.Y.Z`** dispara
[`release.yml`](.github/workflows/release.yml), que publica una Release con ambos binarios.

## Uso

1. Instala el **MSI** en Windows y abre **OpenWirelessDisplay Server**.
2. (Opcional, para extender) Pulsa **"+ Monitor virtual"** para instalar el driver, ponlo en
   **"Extender"** (Config. de pantalla) y pulsa **"↻ Actualizar lista"**.
3. Elige el **monitor a compartir** y pulsa **Iniciar**. Verás un **PIN** y la IP.
4. Abre la app **OpenWirelessDisplay** en Android (misma Wi-Fi). El PC aparece solo (o usa
   **"Conectar por IP manualmente"**). Escribe el **PIN** y empareja.
5. Si el servidor ofrece varios monitores, **elige cuál ver** en el diálogo de la app. Los toques
   controlan el cursor sobre ese monitor; **cada dispositivo puede ver un monitor distinto**.

> Permite la app a través del **Firewall de Windows** la primera vez (el MSI ya añade las reglas
> para TCP 7345 y mDNS UDP 5353).

## Gestión de versiones

`version.json` es la fuente única. `execution/bump_version.py` propaga a `.NET` (MSI) y
`Android` (`versionName`/`versionCode`) y actualiza el `CHANGELOG`.

```bash
python execution/bump_version.py patch   # 0.1.0 → 0.1.1
python execution/bump_version.py minor   # 0.1.0 → 0.2.0
python execution/bump_version.py major   # 0.1.0 → 1.0.0
python execution/bump_version.py set 1.2.3
```

## Modo extendido (monitor nuevo, no espejo)

Para usar el teléfono como un **monitor extendido** (arrastrar ventanas) hace falta un driver
de pantalla virtual. Guía paso a paso: **[docs/MODO-EXTENDIDO.md](docs/MODO-EXTENDIDO.md)**
(instalar un driver IDD open-source firmado y seleccionarlo en "Monitor a compartir").

## Hoja de ruta

- [x] MVP: PIN + mDNS + espejo (MJPEG) + input + MSI + APK (CI).
- [x] Selector de monitor a compartir + mapeo de input multi-monitor.
- [x] Modo extendido vía driver virtual open-source (botón integrado, ver `docs/MODO-EXTENDIDO.md`).
- [x] Multi-dispositivo: cada cliente ve un monitor distinto (protocolo v2).
- [x] Cursor visible en monitores virtuales.
- [x] Baja latencia (reescalado + descarte de cuadros).
- [ ] Driver IDD nativo propio (`server-windows/driver/`, alternativa al externo).
- [ ] Codificación H.264/H.265 por hardware (NVENC/AMF/QuickSync) + MediaCodec.
- [ ] Transporte WebRTC/UDP de baja latencia (hoy TCP).
- [ ] Audio inalámbrico.

## Licencia

[MIT](LICENSE).
