package com.openwdisplay.client

import android.media.MediaCodec
import android.media.MediaFormat
import android.util.Log
import android.view.Surface
import java.nio.ByteBuffer

/**
 * Ruta de alto rendimiento (guia, Fase 3): decodificador H.264 de baja latencia con
 * MediaCodec por hardware, renderizando directamente sobre un Surface.
 *
 * NO esta cableado en el flujo MVP (que usa MJPEG). Se incluye listo para sustituir a
 * la decodificacion JPEG cuando el servidor active el codificador H.264/H.265
 * (NVENC/AMF/QuickSync). El protocolo de red [WireProtocol] no cambia: basta enrutar
 * las NAL units recibidas en MSG_FRAME a [submitNal].
 */
class LowLatencyVideoPlayer(private val surface: Surface) {

    private var codec: MediaCodec? = null
    @Volatile private var started = false

    fun start(width: Int, height: Int, mime: String = MediaFormat.MIMETYPE_VIDEO_AVC) {
        if (started) return
        val format = MediaFormat.createVideoFormat(mime, width, height).apply {
            // Modo baja latencia (API 30+); en versiones previas se omite sin error.
            setInteger(MediaFormat.KEY_LOW_LATENCY, 1)
            setInteger(MediaFormat.KEY_PRIORITY, 0) // tiempo real
        }
        codec = MediaCodec.createDecoderByType(mime).apply {
            configure(format, surface, null, 0)
            start()
        }
        started = true
        Log.i(TAG, "Decodificador H.264 iniciado ${width}x$height ($mime)")
    }

    /** Alimenta una NAL unit (o conjunto) al decodificador. */
    fun submitNal(data: ByteArray, presentationTimeUs: Long = 0) {
        val c = codec ?: return
        val inIndex = c.dequeueInputBuffer(10_000)
        if (inIndex >= 0) {
            val buf: ByteBuffer? = c.getInputBuffer(inIndex)
            buf?.clear()
            buf?.put(data)
            c.queueInputBuffer(inIndex, 0, data.size, presentationTimeUs, 0)
        }
        drainOutput(c)
    }

    private fun drainOutput(c: MediaCodec) {
        val info = MediaCodec.BufferInfo()
        var outIndex = c.dequeueOutputBuffer(info, 0)
        while (outIndex >= 0) {
            // render = true: vuelca el frame directamente al Surface (GPU).
            c.releaseOutputBuffer(outIndex, true)
            outIndex = c.dequeueOutputBuffer(info, 0)
        }
    }

    fun stop() {
        started = false
        try { codec?.stop() } catch (_: Exception) {}
        try { codec?.release() } catch (_: Exception) {}
        codec = null
    }

    companion object {
        private const val TAG = "LowLatencyVideoPlayer"
    }
}
