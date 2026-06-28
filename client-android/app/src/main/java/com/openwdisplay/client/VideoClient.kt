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
        fun onFrame(bitmap: Bitmap)
        fun onError(message: String)
        fun onDisconnected()
    }

    @Volatile private var running = false
    private var socket: Socket? = null
    private var out: OutputStream? = null
    private val sendLock = Any()

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

            // 3) Recepcion de frames
            while (running) {
                val msg = WireProtocol.readMessage(input)
                if (msg.type == WireProtocol.MSG_FRAME && msg.payload.size > 8) {
                    val jpegOffset = 8
                    val bmp = BitmapFactory.decodeByteArray(
                        msg.payload, jpegOffset, msg.payload.size - jpegOffset
                    )
                    if (bmp != null) listener.onFrame(bmp)
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
