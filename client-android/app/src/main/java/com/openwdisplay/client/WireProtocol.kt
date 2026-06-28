package com.openwdisplay.client

import java.io.DataInputStream
import java.io.InputStream
import java.io.OutputStream
import java.nio.ByteBuffer
import java.nio.ByteOrder

/**
 * Protocolo binario compartido con el servidor Windows (.NET).
 * Mensaje: [1 byte Type][4 bytes BE Length][Length bytes Payload].
 * Debe mantenerse en paridad con server-windows/src/Protocol/WireProtocol.cs.
 */
object WireProtocol {
    const val PROTOCOL_VERSION = 1
    const val SERVICE_TYPE = "_openwdisplay._tcp."
    const val DEFAULT_PORT = 7345

    // Cliente -> Servidor
    const val MSG_HELLO: Int = 0x01
    const val MSG_PIN: Int = 0x02
    const val MSG_INPUT: Int = 0x10
    const val MSG_BYE: Int = 0x7F

    // Servidor -> Cliente
    const val MSG_HELLO_ACK: Int = 0x81
    const val MSG_PIN_OK: Int = 0x82
    const val MSG_PIN_FAIL: Int = 0x83
    const val MSG_FRAME: Int = 0x90
    const val MSG_ERROR: Int = 0xFE

    // Acciones de input
    const val INPUT_MOVE: Byte = 0x00
    const val INPUT_DOWN: Byte = 0x01
    const val INPUT_UP: Byte = 0x02

    data class Message(val type: Int, val payload: ByteArray)

    fun writeMessage(out: OutputStream, type: Int, payload: ByteArray = ByteArray(0)) {
        val header = ByteArray(5)
        header[0] = type.toByte()
        header[1] = (payload.size ushr 24).toByte()
        header[2] = (payload.size ushr 16).toByte()
        header[3] = (payload.size ushr 8).toByte()
        header[4] = payload.size.toByte()
        out.write(header)
        if (payload.isNotEmpty()) out.write(payload)
        out.flush()
    }

    fun writeMessage(out: OutputStream, type: Int, utf8: String) =
        writeMessage(out, type, utf8.toByteArray(Charsets.UTF_8))

    /** Lee un mensaje completo o lanza EOFException si la conexion se cierra. */
    fun readMessage(input: DataInputStream): Message {
        val type = input.readUnsignedByte()
        val len = input.readInt() // big-endian
        require(len in 0..(64 * 1024 * 1024)) { "Longitud invalida: $len" }
        val payload = ByteArray(len)
        input.readFully(payload)
        return Message(type, payload)
    }

    fun encodeInput(action: Byte, normX: Float, normY: Float): ByteArray {
        val buf = ByteBuffer.allocate(9).order(ByteOrder.BIG_ENDIAN)
        buf.put(action)
        buf.putFloat(normX)
        buf.putFloat(normY)
        return buf.array()
    }

    fun readInt32BE(src: ByteArray, offset: Int): Int =
        ((src[offset].toInt() and 0xFF) shl 24) or
        ((src[offset + 1].toInt() and 0xFF) shl 16) or
        ((src[offset + 2].toInt() and 0xFF) shl 8) or
        (src[offset + 3].toInt() and 0xFF)
}
