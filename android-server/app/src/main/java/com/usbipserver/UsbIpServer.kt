package com.usbipserver

import android.hardware.usb.UsbConstants
import android.hardware.usb.UsbDevice
import android.hardware.usb.UsbEndpoint
import android.hardware.usb.UsbInterface
import android.util.Log
import kotlinx.coroutines.*
import java.io.*
import java.net.ServerSocket
import java.net.Socket
import java.nio.ByteBuffer
import java.nio.ByteOrder
import java.util.concurrent.atomic.AtomicInteger

/**
 * USB/IP TCP server (port 3240).
 *
 * Implements the USB/IP protocol so Windows clients (usbip-win2) can:
 *  1. List exported USB devices  (OP_REQ_DEVLIST / OP_REP_DEVLIST)
 *  2. Attach to a device         (OP_REQ_IMPORT  / OP_REP_IMPORT)
 *  3. Forward USB URBs           (USBIP_CMD_SUBMIT / USBIP_RET_SUBMIT)
 */
class UsbIpServer(
    private val deviceManager: UsbDeviceManager,
    private val onClientCountChanged: (Int) -> Unit
) {
    private val tag = "UsbIpServer"
    private val clientCount = AtomicInteger(0)
    private var serverJob: Job? = null
    private var serverSocket: ServerSocket? = null
    private val scope = CoroutineScope(Dispatchers.IO + SupervisorJob())

    fun start() {
        if (serverJob?.isActive == true) return
        serverJob = scope.launch {
            try {
                serverSocket = ServerSocket(UsbIpProtocol.USBIP_PORT)
                Log.i(tag, "USB/IP server listening on port ${UsbIpProtocol.USBIP_PORT}")
                while (isActive) {
                    val client = serverSocket!!.accept()
                    Log.i(tag, "Client connected: ${client.inetAddress.hostAddress}")
                    launch { handleClient(client) }
                }
            } catch (e: Exception) {
                if (isActive) Log.e(tag, "Server error: ${e.message}")
            } finally {
                serverSocket?.close()
            }
        }
    }

    fun stop() {
        serverJob?.cancel()
        serverSocket?.close()
        serverSocket = null
        Log.i(tag, "USB/IP server stopped")
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Client session
    // ─────────────────────────────────────────────────────────────────────────

    private suspend fun handleClient(socket: Socket) {
        val count = clientCount.incrementAndGet()
        withContext(Dispatchers.Main) { onClientCountChanged(count) }

        try {
            socket.tcpNoDelay = true
            socket.keepAlive  = true
            socket.soTimeout  = 15_000  // 15-second timeout for initial OP negotiation

            val input  = DataInputStream(socket.inputStream.buffered())
            val output = DataOutputStream(socket.outputStream.buffered())

            // Read the OP header to determine what the client wants
            val headerBuf = ByteArray(UsbIpProtocol.OpHeader.SIZE)
            input.readFully(headerBuf)
            val header = UsbIpProtocol.OpHeader.fromBytes(
                ByteBuffer.wrap(headerBuf).order(ByteOrder.BIG_ENDIAN)
            )

            when (header.code) {
                UsbIpProtocol.OP_REQUEST_DEVLIST -> handleDevList(input, output)
                UsbIpProtocol.OP_REQUEST_IMPORT  -> handleImport(input, output, socket)
                else -> Log.w(tag, "Unknown OP code: ${header.code}")
            }
        } catch (e: Exception) {
            Log.e(tag, "Client error: ${e.message}")
        } finally {
            socket.close()
            val remaining = clientCount.decrementAndGet()
            withContext(Dispatchers.Main) { onClientCountChanged(remaining) }
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // OP_REP_DEVLIST
    // ─────────────────────────────────────────────────────────────────────────

    private fun handleDevList(input: DataInputStream, output: DataOutputStream) {
        val sharedDevices = deviceManager.devices.value.filter { it.isShared }
        Log.d(tag, "Sending device list: ${sharedDevices.size} device(s)")

        // Reply header
        val replyBuf = ByteBuffer.allocate(16).order(ByteOrder.BIG_ENDIAN)
        replyBuf.putShort(UsbIpProtocol.USBIP_VERSION)
        replyBuf.putShort(UsbIpProtocol.OP_REPLY_DEVLIST)
        replyBuf.putInt(UsbIpProtocol.ST_OK)
        replyBuf.putInt(sharedDevices.size)
        output.write(replyBuf.array())

        // Write each device + its interfaces
        sharedDevices.forEach { info ->
            output.write(buildDeviceEntry(info))
        }
        output.flush()
    }

    private fun buildDeviceEntry(info: UsbDeviceManager.UsbDeviceInfo): ByteArray {
        val dev = info.device
        val usbipDev = UsbIpProtocol.UsbIpDevice(
            path     = "/sys/devices/pci0000:00/0000:00:01.2/usb${info.busNum}/${info.busNum}-${info.devNum}",
            busId    = "${info.busNum}-${info.devNum}",
            busNum   = info.busNum,
            devNum   = info.devNum,
            speed    = 3,             // USB_SPEED_HIGH (480Mbps)
            idVendor  = dev.vendorId.toShort(),
            idProduct = dev.productId.toShort(),
            bcdDevice = 0x0100,
            bDeviceClass       = dev.deviceClass.toByte(),
            bDeviceSubClass    = dev.deviceSubclass.toByte(),
            bDeviceProtocol    = dev.deviceProtocol.toByte(),
            bConfigurationValue = 1,
            bNumConfigurations  = dev.configurationCount.toByte(),
            bNumInterfaces      = dev.interfaceCount.toByte()
        )

        val deviceBytes = usbipDev.toBytes()
        val ifaceBytes  = buildInterfaceEntries(dev)
        return deviceBytes + ifaceBytes
    }

    private fun buildInterfaceEntries(dev: UsbDevice): ByteArray {
        val buf = ByteBuffer.allocate(dev.interfaceCount * 4).order(ByteOrder.BIG_ENDIAN)
        for (i in 0 until dev.interfaceCount) {
            val iface: UsbInterface = dev.getInterface(i)
            val entry = UsbIpProtocol.UsbIpInterface(
                bInterfaceClass    = iface.interfaceClass.toByte(),
                bInterfaceSubClass = iface.interfaceSubclass.toByte(),
                bInterfaceProtocol = iface.interfaceProtocol.toByte()
            )
            buf.put(entry.toBytes())
        }
        return buf.array()
    }

    // ─────────────────────────────────────────────────────────────────────────
    // OP_REP_IMPORT  (client attaches to a specific device)
    // ─────────────────────────────────────────────────────────────────────────

    private suspend fun handleImport(
        input: DataInputStream,
        output: DataOutputStream,
        socket: Socket
    ) {
        // Read the 32-byte busId string
        val busIdBytes = ByteArray(32)
        input.readFully(busIdBytes)
        val busId = String(busIdBytes, Charsets.US_ASCII).trimEnd('\u0000')
        Log.d(tag, "Client requesting import of busId: $busId")

        val info = deviceManager.devices.value
            .filter { it.isShared }
            .find { "${it.busNum}-${it.devNum}" == busId }

        if (info == null) {
            Log.w(tag, "Device not found: $busId")
            val errBuf = ByteBuffer.allocate(8).order(ByteOrder.BIG_ENDIAN)
            errBuf.putShort(UsbIpProtocol.USBIP_VERSION)
            errBuf.putShort(UsbIpProtocol.OP_REPLY_IMPORT)
            errBuf.putInt(UsbIpProtocol.ST_NA)
            output.write(errBuf.array())
            output.flush()
            return
        }

        // Reply header (OK)
        val headerBuf = ByteBuffer.allocate(8).order(ByteOrder.BIG_ENDIAN)
        headerBuf.putShort(UsbIpProtocol.USBIP_VERSION)
        headerBuf.putShort(UsbIpProtocol.OP_REPLY_IMPORT)
        headerBuf.putInt(UsbIpProtocol.ST_OK)
        output.write(headerBuf.array())

        // Device descriptor
        output.write(buildDeviceEntry(info))
        output.flush()

        Log.i(tag, "Device imported: $busId – entering URB forwarding loop")
        // Increase timeout for the active URB forwarding phase.
        // 5 minutes covers idle devices (e.g., a keyboard with no keystrokes).
        // TCP keepalive will detect dead connections within OS-level intervals.
        socket.soTimeout = 300_000
        forwardUrbs(input, output, info)
    }

    // ─────────────────────────────────────────────────────────────────────────
    // URB forwarding loop
    // ─────────────────────────────────────────────────────────────────────────

    private suspend fun forwardUrbs(
        input: DataInputStream,
        output: DataOutputStream,
        info: UsbDeviceManager.UsbDeviceInfo
    ) {
        val headerBuf = ByteArray(UsbIpProtocol.UsbIpHeader.BASIC_SIZE)
        while (currentCoroutineContext().isActive) {
            try {
                input.readFully(headerBuf)
                val hdr = UsbIpProtocol.UsbIpHeader.fromBytes(
                    ByteBuffer.wrap(headerBuf).order(ByteOrder.BIG_ENDIAN)
                )

                when (hdr.command) {
                    UsbIpProtocol.USBIP_CMD_SUBMIT -> handleCmdSubmit(hdr, input, output, info)
                    UsbIpProtocol.USBIP_CMD_UNLINK -> handleCmdUnlink(hdr, input, output)
                    else -> Log.w(tag, "Unknown command: 0x${hdr.command.toString(16)}")
                }
            } catch (e: EOFException) {
                Log.i(tag, "Client disconnected (EOF)")
                break
            } catch (e: Exception) {
                Log.e(tag, "URB loop error: ${e.message}")
                break
            }
        }
    }

    private suspend fun handleCmdSubmit(
        hdr: UsbIpProtocol.UsbIpHeader,
        input: DataInputStream,
        output: DataOutputStream,
        info: UsbDeviceManager.UsbDeviceInfo
    ) {
        // Read CMD_SUBMIT body
        val bodyBuf = ByteArray(UsbIpProtocol.CmdSubmitBody.SIZE)
        input.readFully(bodyBuf)
        val body = UsbIpProtocol.CmdSubmitBody.fromBytes(
            ByteBuffer.wrap(bodyBuf).order(ByteOrder.BIG_ENDIAN)
        )

        // Read outgoing data (for OUT transfers)
        val transferData = if (hdr.direction == UsbIpProtocol.USBIP_DIR_OUT && body.transferBufferLength > 0) {
            ByteArray(body.transferBufferLength).also { input.readFully(it) }
        } else ByteArray(0)

        // Perform the USB transfer
        val (status, actualLen, resultData) = withContext(Dispatchers.IO) {
            performUsbTransfer(hdr, body, transferData, info)
        }

        // Send RET_SUBMIT
        val retPacket = UsbIpProtocol.buildRetSubmit(
            seqNum = hdr.seqNum,
            devId  = hdr.devIdOrBusNum,
            direction = hdr.direction,
            ep = hdr.ep,
            status = status,
            actualLength = actualLen,
            data = resultData
        )
        synchronized(output) {
            output.write(retPacket)
            output.flush()
        }
    }

    private fun performUsbTransfer(
        hdr: UsbIpProtocol.UsbIpHeader,
        body: UsbIpProtocol.CmdSubmitBody,
        outData: ByteArray,
        info: UsbDeviceManager.UsbDeviceInfo
    ): Triple<Int, Int, ByteArray> {
        val device = info.device

        return try {
            if (hdr.ep == 0) {
                // Control transfer – decode setup packet
                val setup = body.setup
                val bmRequestType = setup[0].toInt() and 0xFF
                val bRequest      = setup[1].toInt() and 0xFF
                val wValue        = ((setup[3].toInt() and 0xFF) shl 8) or (setup[2].toInt() and 0xFF)
                val wIndex        = ((setup[5].toInt() and 0xFF) shl 8) or (setup[4].toInt() and 0xFF)
                val wLength       = ((setup[7].toInt() and 0xFF) shl 8) or (setup[6].toInt() and 0xFF)

                val buffer = if (wLength > 0) ByteArray(wLength) else null
                if (hdr.direction == UsbIpProtocol.USBIP_DIR_OUT && outData.isNotEmpty()) {
                    outData.copyInto(buffer ?: ByteArray(0))
                }

                val ret = deviceManager.controlTransfer(
                    device.deviceId,
                    bmRequestType, bRequest, wValue, wIndex,
                    buffer, wLength, 5000
                )
                if (ret < 0) Triple(1, 0, ByteArray(0))
                else Triple(0, ret, buffer?.copyOf(ret) ?: ByteArray(0))
            } else {
                // Bulk/interrupt transfer
                val endpoint = findEndpoint(device, hdr.ep, hdr.direction)
                    ?: return Triple(1, 0, ByteArray(0))

                val bufSize = maxOf(body.transferBufferLength, endpoint.maxPacketSize)
                val buffer = if (hdr.direction == UsbIpProtocol.USBIP_DIR_OUT) outData else ByteArray(bufSize)
                val ret = deviceManager.bulkTransfer(device.deviceId, endpoint, buffer, buffer.size, 5000)

                if (ret < 0) Triple(1, 0, ByteArray(0))
                else Triple(0, ret, if (hdr.direction == UsbIpProtocol.USBIP_DIR_IN) buffer.copyOf(ret) else ByteArray(0))
            }
        } catch (e: Exception) {
            Log.e(tag, "USB transfer error: ${e.message}")
            Triple(1, 0, ByteArray(0))
        }
    }

    private fun findEndpoint(device: UsbDevice, epAddr: Int, direction: Int): UsbEndpoint? {
        for (i in 0 until device.interfaceCount) {
            val iface = device.getInterface(i)
            for (j in 0 until iface.endpointCount) {
                val ep = iface.getEndpoint(j)
                val epNum = ep.address and 0x0F
                val epDir = if ((ep.address and 0x80) != 0) UsbIpProtocol.USBIP_DIR_IN
                            else UsbIpProtocol.USBIP_DIR_OUT
                if (epNum == epAddr && epDir == direction) return ep
            }
        }
        return null
    }

    private fun handleCmdUnlink(
        hdr: UsbIpProtocol.UsbIpHeader,
        input: DataInputStream,
        output: DataOutputStream
    ) {
        // Read unlink body (24 bytes: seqNum + 20 padding)
        val body = ByteArray(24)
        input.readFully(body)

        // Reply with success
        val buf = ByteBuffer.allocate(UsbIpProtocol.UsbIpHeader.BASIC_SIZE + 24)
            .order(ByteOrder.BIG_ENDIAN)
        buf.putInt(UsbIpProtocol.USBIP_RET_UNLINK)
        buf.putInt(hdr.seqNum)
        buf.putInt(hdr.devIdOrBusNum)
        buf.putInt(0)
        buf.putInt(0)
        buf.putInt(0)  // status = OK
        buf.put(ByteArray(20))
        synchronized(output) {
            output.write(buf.array())
            output.flush()
        }
    }
}
