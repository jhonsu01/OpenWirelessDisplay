package com.openwdisplay.client

import android.graphics.Bitmap
import android.graphics.BitmapFactory
import android.util.Log
import org.json.JSONObject
import java.io.DataInputStream
import java.io.OutputStream
import java.net.InetSocketAddress
import java.net.Socket
import kotlin.concurrent.thread

/**
 * Cliente de red: se conecta al servidor, ejecuta el handshake de emparejamiento por
 * PIN y recibe el stream de frames. En el MVP los frames son JPEG (decodificados con
 * BitmapFactory). La ruta de alto rendimiento (H.264 -> MediaCodec, ver
 * [LowLatencyVideoPlayer]) es intercambiable manteniendo el mismo protocolo.
 */
class VideoClient(
    private val host: String,
    private val port: Int,
    private val pin: String,
    private val clientName: String,
) {
    interface Listener {
        fun onPaired(width: Int, height: Int, fps: Int)
        fun onPinFailed(reason: String, attemptsLeft: Int)
        /** El servidor ofrece varios monitores; el usuario elige uno (llamar a selectMonitor). */
        fun onMonitors(labels: List<String>, indices: List<Int>, defaultIndex: Int)
        fun onFrame(bitmap: Bitmap)
        fun onError(message: String)
        fun onDisconnected()
    }

    @Volatile private var running = false
    private var socket: Socket? = null
    private var out: OutputStream? = null
    private val sendLock = Any()

    // Seleccion de monitor por el usuario (cruza del hilo UI al hilo de sesion).
    private val selectLock = Object()
    @Volatile private var selectedMonitor: Int? = null

    // Buffer de un solo hueco: el hilo de red guarda SIEMPRE el ultimo JPEG (descarta el
    // anterior si no se ha decodificado). El hilo decodificador toma solo el mas reciente.
    // Asi, si la decodificacion no alcanza, se saltan cuadros viejos y no se acumula lag.
    private val frameLock = Object()
    private var latestJpeg: ByteArray? = null
    private var decoderThread: Thread? = null

    fun connect(listener: Listener) {
        running = true
        thread(name = "video-client") { runSession(listener) }
    }

    private fun runSession(listener: Listener) {
        try {
            val sock = Socket()
            sock.tcpNoDelay = true // desactiva Nagle (baja latencia)
            sock.connect(InetSocketAddress(host, port), 5000)
            socket = sock
            val input = DataInputStream(sock.getInputStream().buffered(64 * 1024))
            out = sock.getOutputStream()

            // 1) HELLO
            val hello = JSONObject()
                .put("clientName", clientName)
                .put("protocol", WireProtocol.PROTOCOL_VERSION)
                .toString()
            WireProtocol.writeMessage(out!!, WireProtocol.MSG_HELLO, hello)

            val ack = WireProtocol.readMessage(input)
            if (ack.type != WireProtocol.MSG_HELLO_ACK) {
                listener.onError("Respuesta inesperada del servidor")
                return
            }

            // 2) PIN
            WireProtocol.writeMessage(out!!, WireProtocol.MSG_PIN, pin)
            val pinResp = WireProtocol.readMessage(input)
            when (pinResp.type) {
                WireProtocol.MSG_PIN_OK -> {
                    val j = JSONObject(String(pinResp.payload, Charsets.UTF_8))
                    listener.onPaired(j.optInt("width"), j.optInt("height"), j.optInt("fps", 15))
                }
                WireProtocol.MSG_PIN_FAIL -> {
                    val j = JSONObject(String(pinResp.payload, Charsets.UTF_8))
                    listener.onPinFailed(j.optString("reason", "PIN incorrecto"), j.optInt("attemptsLeft", 0))
                    return
                }
                else -> {
                    listener.onError("Handshake fallido")
                    return
                }
            }

            // Hilo decodificador/render: decodifica SOLO el ultimo frame disponible.
            startDecoder(listener)

            // 3) Lista de monitores (protocolo v2): el usuario elige cual ver.
            val first = WireProtocol.readMessage(input)
            when {
                first.type == WireProtocol.MSG_MONITORS -> {
                    val j = JSONObject(String(first.payload, Charsets.UTF_8))
                    val arr = j.getJSONArray("monitors")
                    val labels = ArrayList<String>(arr.length())
                    val indices = ArrayList<Int>(arr.length())
                    for (i in 0 until arr.length()) {
                        val o = arr.getJSONObject(i)
                        indices.add(o.getInt("index"))
                        labels.add(o.optString("label", "Monitor ${o.getInt("index") + 1}"))
                    }
                    val def = j.optInt("default", indices.firstOrNull() ?: 0)
                    if (indices.size > 1) listener.onMonitors(labels, indices, def)
                    val chosen = if (indices.size > 1) awaitSelection(def) else def
                    WireProtocol.writeMessage(out!!, WireProtocol.MSG_SELECT_MONITOR, WireProtocol.encodeInt32BE(chosen))
                }
                first.type == WireProtocol.MSG_FRAME && first.payload.size > 8 -> {
                    pushFrame(first.payload) // servidor antiguo: ya envia frames
                }
                first.type == WireProtocol.MSG_ERROR -> {
                    listener.onError(String(first.payload, Charsets.UTF_8)); return
                }
            }

            // 4) Recepcion de frames: leer rapido y quedarse con el mas reciente.
            while (running) {
                val msg = WireProtocol.readMessage(input)
                if (msg.type == WireProtocol.MSG_FRAME && msg.payload.size > 8) {
                    pushFrame(msg.payload)
                } else if (msg.type == WireProtocol.MSG_ERROR) {
                    listener.onError(String(msg.payload, Charsets.UTF_8))
                    break
                }
            }
        } catch (e: Exception) {
            if (running) {
                Log.w(TAG, "Sesion terminada: ${e.message}")
                listener.onError(e.message ?: "Error de conexion")
            }
        } finally {
            close()
            listener.onDisconnected()
        }
    }

    /** Guarda el ultimo frame JPEG (descartando el anterior si no se decodifico). */
    private fun pushFrame(payload: ByteArray) {
        val jpeg = payload.copyOfRange(8, payload.size)
        synchronized(frameLock) {
            latestJpeg = jpeg
            frameLock.notifyAll()
        }
    }

    /** Lo llama la UI cuando el usuario elige un monitor. */
    fun selectMonitor(index: Int) {
        synchronized(selectLock) {
            selectedMonitor = index
            selectLock.notifyAll()
        }
    }

    /** Espera la seleccion del usuario (hasta 120s); si no, usa el monitor por defecto. */
    private fun awaitSelection(defaultIndex: Int): Int {
        synchronized(selectLock) {
            val deadline = System.currentTimeMillis() + 120_000
            while (running && selectedMonitor == null) {
                val left = deadline - System.currentTimeMillis()
                if (left <= 0) break
                try { selectLock.wait(left) } catch (_: InterruptedException) {}
            }
            return selectedMonitor ?: defaultIndex
        }
    }

    private fun startDecoder(listener: Listener) {
        decoderThread = thread(name = "video-decoder") {
            while (running) {
                val jpeg: ByteArray?
                synchronized(frameLock) {
                    while (running && latestJpeg == null) {
                        try { frameLock.wait(500) } catch (_: InterruptedException) {}
                    }
                    jpeg = latestJpeg
                    latestJpeg = null
                }
                if (jpeg == null) continue
                val bmp = try { BitmapFactory.decodeByteArray(jpeg, 0, jpeg.size) } catch (_: Exception) { null }
                if (bmp != null) listener.onFrame(bmp)
            }
        }
    }

    /** Envia un evento de input con coordenadas normalizadas (0..1). */
    fun sendInput(action: Byte, normX: Float, normY: Float) {
        val o = out ?: return
        try {
            synchronized(sendLock) {
                WireProtocol.writeMessage(o, WireProtocol.MSG_INPUT, WireProtocol.encodeInput(action, normX, normY))
            }
        } catch (_: Exception) { /* conexion caida */ }
    }

    fun close() {
        running = false
        synchronized(selectLock) { selectLock.notifyAll() } // despierta espera de seleccion
        synchronized(frameLock) { frameLock.notifyAll() } // despierta al decodificador
        try {
            out?.let { WireProtocol.writeMessage(it, WireProtocol.MSG_BYE) }
        } catch (_: Exception) {}
        try { socket?.close() } catch (_: Exception) {}
        socket = null
        out = null
    }

    companion object {
        private const val TAG = "VideoClient"
    }
}
