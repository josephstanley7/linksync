package com.linksync.app.net

import java.io.InputStream
import java.io.OutputStream
import java.nio.ByteBuffer
import java.nio.ByteOrder

object LinkSyncProtocol {
    const val MAGIC_BYTE_1: Byte = 0x4C // 'L'
    const val MAGIC_BYTE_2: Byte = 0x53 // 'S'

    const val OP_HANDSHAKE: Short = 0x0001
    const val OP_CLIPBOARD_TEXT: Short = 0x0010
    const val OP_FILE_SYNC: Short = 0x0020
    const val OP_FILE_REVERSE_PUSH: Short = 0x0021
    const val OP_FILE_SYNC_ACK: Short = 0x0022
    const val OP_MIRROR_START: Short = 0x0030
    const val OP_MIRROR_STOP: Short = 0x0031
    const val OP_VIDEO_STREAM_DATA: Short = 0x0032
    const val OP_INPUT_EVENT: Short = 0x0033
    const val OP_SMS_INCOMING: Short = 0x0040
    const val OP_SMS_SEND: Short = 0x0041
    const val OP_CALL_ACTION: Short = 0x0042
    const val OP_CONTACTS_REQ: Short = 0x0043
    const val OP_CONTACTS_RESP: Short = 0x0044
    const val OP_SMS_LIST_REQ: Short = 0x0045
    const val OP_SMS_LIST_RESP: Short = 0x0046
    const val OP_CALL_STATE: Short = 0x0047
    const val OP_DEVICE_STATUS: Short = 0x0050

    fun writeFrame(out: OutputStream, opCode: Short, payload: ByteArray) {
        val header = ByteBuffer.allocate(8).order(ByteOrder.BIG_ENDIAN)
        header.put(MAGIC_BYTE_1)
        header.put(MAGIC_BYTE_2)
        header.putShort(opCode)
        header.putInt(payload.size)

        out.write(header.array())
        if (payload.isNotEmpty()) {
            out.write(payload)
        }
        out.flush()
    }

    data class Frame(val opCode: Short, val payload: ByteArray)

    fun readFrame(input: InputStream): Frame? {
        val headerBuffer = ByteArray(8)
        var bytesRead = 0
        while (bytesRead < 8) {
            val r = input.read(headerBuffer, bytesRead, 8 - bytesRead)
            if (r == -1) return null
            bytesRead += r
        }

        if (headerBuffer[0] != MAGIC_BYTE_1 || headerBuffer[1] != MAGIC_BYTE_2) {
            return null
        }

        val buf = ByteBuffer.wrap(headerBuffer).order(ByteOrder.BIG_ENDIAN)
        buf.position(2)
        val opCode = buf.short
        val payloadLen = buf.int

        val payload = ByteArray(payloadLen)
        var payloadRead = 0
        while (payloadRead < payloadLen) {
            val r = input.read(payload, payloadRead, payloadLen - payloadRead)
            if (r == -1) return null
            payloadRead += r
        }

        return Frame(opCode, payload)
    }
}
