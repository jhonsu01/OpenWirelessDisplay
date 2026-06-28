# Reglas ProGuard del cliente OpenWirelessDisplay.
# El MVP no usa reflexion sensible; se conservan las clases del protocolo por claridad.
-keep class com.openwdisplay.client.WireProtocol { *; }
-dontwarn org.jetbrains.annotations.**
