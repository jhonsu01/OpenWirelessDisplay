# Driver IDD (Indirect Display Driver) — Fase futura

Este directorio contiene el **scaffold** del driver de pantalla virtual que habilita el
**modo "monitor extendido"** real en Windows (no solo espejo). Corresponde a la **Fase 1**
de la guía y es la pieza que el MVP (.NET, modo espejo) aún no reemplaza.

## Por qué no está compilado

El MVP entregado funciona en **modo espejo** (duplica tu pantalla principal) usando solo
.NET, lo que permite generar el **MSI** sin dependencias nativas. Crear un **monitor
nuevo extendido** requiere obligatoriamente este driver IDD, que **no se puede compilar
en el entorno actual** porque necesita:

- **Visual Studio 2022** con la carga de trabajo de C++ de escritorio.
- **Windows Driver Kit (WDK)** correspondiente a tu versión de Windows.
- **Firma de driver** (Microsoft); en desarrollo, **test-signing** habilitado:
  `bcdedit /set testsigning on` (requiere reinicio).

## Cómo construirlo (cuando tengas el WDK)

1. Crea un proyecto *User Mode Driver (UMDF V2)* en Visual Studio.
2. Agrega `Driver.h` y `driver.cpp` de esta carpeta.
3. Enlaza contra `IddCx.lib`, `WdfDriverStubUm.lib`, `d3d11.lib`, `dxgi.lib`.
4. Completa los `TODO`: EDID válido con checksum, modos, swapchain y el hilo de render
   que entrega los `ID3D11Texture2D` a la app de captura/codificación.
5. Compila e instala con el `OpenWirelessDisplay.inf` (firmado / test-signed).

## Integración con el servidor .NET

Una vez instalado el driver, el servidor `.NET` capturará el **monitor virtual** (índice
distinto del primario) en lugar de la pantalla principal: basta cambiar el `screenIndex`
de `ScreenCapturer` al índice del monitor IDD. El resto del pipeline (PIN, mDNS, streaming,
input) no cambia.

## Referencia

Sample oficial de Microsoft: `Windows-driver-samples/video/IndirectDisplay`.
