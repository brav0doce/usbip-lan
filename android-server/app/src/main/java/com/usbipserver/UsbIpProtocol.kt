package com.usbipserver

import java.nio.ByteBuffer
import java.nio.ByteOrder

/**
 * USB/IP protocol constants and packet structures.
 *
 * Reference: https://www.kernel.org/doc/html/latest/usb/usbip_protocol.html
 * Port: 3240 (IANA assigned)
 */
object UsbIpProtocol {

    const val USBIP_PORT = 3240
    const val USBIP_VERSION: Short = 0x0111  // version 1.1.1

    // Operation codes
    const val OP_REQUEST_DEVLIST: Short = 0x8005.toShort()
    const val OP_REPLY_DEVLIST: Short   = 0x0005
    const val OP_REQUEST_IMPORT: Short  = 0x8003.toShort()
    const val OP_REPLY_IMPORT: Short    = 0x0003

    // USB/IP commands (used after device import)
    const val USBIP_CMD_SUBMIT: Int  = 0x00000001
    const val USBIP_CMD_UNLINK: Int  = 0x00000002
    const val USBIP_RET_SUBMIT: Int  = 0x00000003
    const val USBIP_RET_UNLINK: Int  = 0x00000004

    // Direction
    const val USBIP_DIR_OUT: Int = 0
    const val USBIP_DIR_IN: Int  = 1

    // Status
    const val ST_OK: Int  = 0x00
    const val ST_NA: Int  = 0x01

    /**
     * Common header for OP packets (before IMPORT completes).
     * struct usbip_usb_device (kernel)
     */
    data class OpHeader(
        val version: Short,   // USBIP_VERSION
        val code: Short,      // operation code
        val status: Int       // 0 = OK
    ) {
        fun toBytes(): ByteArray {
            val buf = ByteBuffer.allocate(8).order(ByteOrder.BIG_ENDIAN)
            buf.putShort(version)
            buf.putShort(code)
            buf.putInt(status)
            return buf.array()
        }

        companion object {
            const val SIZE = 8
            fun fromBytes(buf: ByteBuffer): OpHeader {
                return OpHeader(buf.short, buf.short, buf.int)
            }
        }
    }

    /**
     * USB device descriptor as sent in DEVLIST reply.
     * Mirrors struct usbip_usb_device (256 bytes total).
     */
    data class UsbIpDevice(
        val path: String,          // sysfs path, padded to 256 bytes
        val busId: String,         // "N-M" format, padded to 32 bytes
        val busNum: Int,
        val devNum: Int,
        val speed: Int,            // USB speed enum
        val idVendor: Short,
        val idProduct: Short,
        val bcdDevice: Short,
        val bDeviceClass: Byte,
        val bDeviceSubClass: Byte,
        val bDeviceProtocol: Byte,
        val bConfigurationValue: Byte,
        val bNumConfigurations: Byte,
        val bNumInterfaces: Byte
    ) {
        fun toBytes(): ByteArray {
            val buf = ByteBuffer.allocate(DEVICE_SIZE).order(ByteOrder.BIG_ENDIAN)
            // path: 256 bytes
            val pathBytes = path.toByteArray(Charsets.US_ASCII)
            buf.put(pathBytes.copyOf(256))
            // busId: 32 bytes
            val busIdBytes = busId.toByteArray(Charsets.US_ASCII)
            buf.put(busIdBytes.copyOf(32))
            buf.putInt(busNum)
            buf.putInt(devNum)
            buf.putInt(speed)
            buf.putShort(idVendor)
            buf.putShort(idProduct)
            buf.putShort(bcdDevice)
            buf.put(bDeviceClass)
            buf.put(bDeviceSubClass)
            buf.put(bDeviceProtocol)
            buf.put(bConfigurationValue)
            buf.put(bNumConfigurations)
            buf.put(bNumInterfaces)
            return buf.array()
        }

        companion object {
            const val DEVICE_SIZE = 312  // 256 + 32 + 4+4+4+2+2+2+1+1+1+1+1+1 = 312
        }
    }

    /**
     * USB interface descriptor appended after each device in DEVLIST.
     * 4 bytes per interface.
     */
    data class UsbIpInterface(
        val bInterfaceClass: Byte,
        val bInterfaceSubClass: Byte,
        val bInterfaceProtocol: Byte,
        val padding: Byte = 0
    ) {
        fun toBytes(): ByteArray {
            return byteArrayOf(bInterfaceClass, bInterfaceSubClass, bInterfaceProtocol, padding)
        }
    }

    /**
     * Header for CMD/RET packets (after device is imported).
     */
    data class UsbIpHeader(
        val command: Int,
        val seqNum: Int,
        val devIdOrBusNum: Int,   // busnum for submit, devId for unlink
        val direction: Int,
        val ep: Int
    ) {
        companion object {
            const val BASIC_SIZE = 20
            fun fromBytes(buf: ByteBuffer): UsbIpHeader {
                return UsbIpHeader(
                    command = buf.int,
                    seqNum  = buf.int,
                    devIdOrBusNum = buf.int,
                    direction = buf.int,
                    ep = buf.int
                )
            }
        }
    }

    /**
     * CMD_SUBMIT extra fields (after basic header).
     */
    data class CmdSubmitBody(
        val transferFlags: Int,
        val transferBufferLength: Int,
        val startFrame: Int,
        val numberOfPackets: Int,
        val interval: Int,
        val setup: ByteArray = ByteArray(8)
    ) {
        companion object {
            const val SIZE = 28  // 5 ints + 8 bytes setup
            fun fromBytes(buf: ByteBuffer): CmdSubmitBody {
                val flags = buf.int
                val bufLen = buf.int
                val startFr = buf.int
                val numPkts = buf.int
                val interval = buf.int
                val setup = ByteArray(8)
                buf.get(setup)
                return CmdSubmitBody(flags, bufLen, startFr, numPkts, interval, setup)
            }
        }
    }

    /**
     * RET_SUBMIT fields.
     */
    fun buildRetSubmit(
        seqNum: Int,
        devId: Int,
        direction: Int,
        ep: Int,
        status: Int,
        actualLength: Int,
        data: ByteArray
    ): ByteArray {
        val buf = ByteBuffer.allocate(BASIC_SIZE + 28 + actualLength).order(ByteOrder.BIG_ENDIAN)
        buf.putInt(USBIP_RET_SUBMIT)
        buf.putInt(seqNum)
        buf.putInt(devId)
        buf.putInt(direction)
        buf.putInt(ep)
        // ret_submit body
        buf.putInt(status)           // status
        buf.putInt(actualLength)     // actual_length
        buf.putInt(0)                // start_frame
        buf.putInt(0)                // number_of_packets
        buf.putInt(0)                // error_count
        buf.put(ByteArray(8))        // padding
        if (actualLength > 0) buf.put(data, 0, actualLength)
        return buf.array()
    }

    private const val BASIC_SIZE = 20
}
