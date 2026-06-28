# Modo monitor extendido (driver virtual)

El MVP, por sí solo, hace **espejo** de un monitor existente. Para usar el teléfono como un
**monitor nuevo y extendido** (arrastrar ventanas hacia él como si fuera físico), Windows
necesita un **driver de pantalla virtual (IDD)**. La vía recomendada es instalar un driver
IDD **open-source ya firmado** y luego, en OpenWirelessDisplay, **elegir ese monitor virtual**
en el desplegable "Monitor a compartir".

> No incluimos el binario del driver en este repo: es un proyecto independiente con su propia
> firma. Lo instalas una vez desde su release oficial y nuestro servidor lo captura.

## 1. Instalar el driver de pantalla virtual

Driver recomendado (open-source, MIT, firmado): **Virtual Display Driver (VDD)**.

- Repositorio: <https://github.com/VirtualDisplay/Virtual-Display-Driver>
  (mantenido por la comunidad; antes en `itsmikethetech/Virtual-Display-Driver`).

Pasos generales (los nombres exactos pueden variar según la versión del release; sigue el
README del proyecto si difiere):

1. Descarga el **último release** (ZIP o instalador) desde la pestaña *Releases*.
2. Si trae **instalador**: ejecútalo como administrador y acepta instalar el certificado/driver.
   Si trae **ZIP manual**:
   - Copia la carpeta a `C:\VirtualDisplayDriver` (o la que indique el README).
   - Instala el certificado incluido en *Equipos locales → Entidades de certificación raíz de confianza*.
   - Ejecuta el script de instalación incluido (p. ej. `install.bat`, que usa `nefconw`/`devcon`),
     o agrégalo desde el *Administrador de dispositivos → Acción → Agregar hardware heredado*.
3. Acepta el aviso de Windows de instalar un controlador no estándar.

Tras esto aparecerá un **monitor adicional** en Windows.

## 2. Ponerlo en modo "Extender"

1. Clic derecho en el escritorio → **Configuración de pantalla**.
2. Verás un monitor nuevo (el virtual). Selecciónalo y elige **"Extender estas pantallas"**
   (no "Duplicar").
3. Ajusta su **resolución** y posición. El VDD permite configurar resoluciones soportadas en
   su archivo de opciones (`options.txt`/`vdd_settings.xml`, según versión); por defecto trae
   resoluciones comunes 16:9.

## 3. Compartirlo con el teléfono

1. Abre **OpenWirelessDisplay Server**.
2. En **"Monitor a compartir"** elige el **monitor virtual** (será el de mayor índice, no el
   "(Principal)"). Su resolución suele coincidir con la que configuraste en el VDD.
3. Pulsa **Iniciar** y empareja el teléfono con el PIN.
4. Ahora puedes **arrastrar ventanas** hacia ese monitor en Windows y las verás en el teléfono:
   es un escritorio extendido real, no un espejo.

## Notas

- Si no aparece el monitor virtual en el desplegable, reinicia el servidor (relee la lista de
  monitores al abrir) o verifica en *Configuración de pantalla* que el monitor virtual está activo.
- El input táctil del teléfono controla el cursor sobre ese monitor (mapeo por coordenadas
  normalizadas; ya soporta el offset multi-monitor).
- Rendimiento: el modo extendido usa el mismo pipeline de streaming; la latencia depende de la
  red. Para latencia mínima real está pendiente la fase H.264 (ver README, hoja de ruta).
- Desinstalar: quita el driver desde su propio desinstalador o el Administrador de dispositivos.
