package com.usbipserver

import android.app.PendingIntent
import android.content.BroadcastReceiver
import android.content.Context
import android.content.Intent
import android.content.IntentFilter
import android.hardware.usb.*
import android.os.Build
import android.util.Log
import java.nio.ByteBuffer
import java.nio.ByteOrder
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow

/**
 * Manages USB device enumeration and permissions.
 * Uses Android USB Host API.
 */
class UsbDeviceManager(private val context: Context) {

    private val tag = "UsbDeviceManager"
    private val usbManager = context.getSystemService(Context.USB_SERVICE) as UsbManager

    private val _devices = MutableStateFlow<List<UsbDeviceInfo>>(emptyList())
    val devices: StateFlow<List<UsbDeviceInfo>> = _devices

    private val openDevices = mutableMapOf<Int, UsbDeviceConnection>()

    companion object {
        const val ACTION_USB_PERMISSION = "com.usbipserver.USB_PERMISSION"
    }

    data class UsbDeviceInfo(
        val device: UsbDevice,
        val isShared: Boolean = true,
        val busNum: Int = 1,
        val devNum: Int
    ) {
        val displayName: String get() {
            val name = device.productName ?: "Unknown Device"
            return if (name.isBlank()) "USB Device (${device.vendorId.toHex()}:${device.productId.toHex()})" else name
        }
        val details: String get() =
            "VID:${device.vendorId.toHex()} PID:${device.productId.toHex()}"
        val className: String get() =
            usbClassToString(device.deviceClass)

        private fun Int.toHex(): String = String.format("%04X", this)

        private fun usbClassToString(cls: Int): String = when (cls) {
            UsbConstants.USB_CLASS_AUDIO         -> "Audio"
            UsbConstants.USB_CLASS_CDC_DATA      -> "CDC Data"
            UsbConstants.USB_CLASS_COMM          -> "Communications"
            UsbConstants.USB_CLASS_HID           -> "HID"
            5      -> "Physical"
            UsbConstants.USB_CLASS_STILL_IMAGE   -> "Still Image"
            UsbConstants.USB_CLASS_PRINTER       -> "Printer"
            UsbConstants.USB_CLASS_MASS_STORAGE  -> "Mass Storage"
            UsbConstants.USB_CLASS_HUB           -> "Hub"
            UsbConstants.USB_CLASS_CSCID         -> "Smart Card"
            UsbConstants.USB_CLASS_CONTENT_SEC   -> "Content Security"
            UsbConstants.USB_CLASS_VIDEO         -> "Video"
            UsbConstants.USB_CLASS_WIRELESS_CONTROLLER -> "Wireless"
            UsbConstants.USB_CLASS_MISC          -> "Miscellaneous"
            UsbConstants.USB_CLASS_APP_SPEC      -> "App-Specific"
            UsbConstants.USB_CLASS_VENDOR_SPEC   -> "Vendor-Specific"
            else -> "Class 0x${cls.toString(16).uppercase()}"
        }
    }

    private val usbReceiver = object : BroadcastReceiver() {
        override fun onReceive(ctx: Context, intent: Intent) {
            when (intent.action) {
                ACTION_USB_PERMISSION -> {
                    val device = if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.TIRAMISU) {
                        intent.getParcelableExtra(UsbManager.EXTRA_DEVICE, UsbDevice::class.java)
                    } else {
                        @Suppress("DEPRECATION")
                        intent.getParcelableExtra(UsbManager.EXTRA_DEVICE)
                    }
                    val granted = intent.getBooleanExtra(UsbManager.EXTRA_PERMISSION_GRANTED, false)
                    Log.d(tag, "Permission for ${device?.deviceName}: $granted")
                    if (granted && device != null) refreshDevices()
                }
                UsbManager.ACTION_USB_DEVICE_ATTACHED -> {
                    Log.d(tag, "USB device attached")
                    refreshDevices()
                }
                UsbManager.ACTION_USB_DEVICE_DETACHED -> {
                    val device = if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.TIRAMISU) {
                        intent.getParcelableExtra(UsbManager.EXTRA_DEVICE, UsbDevice::class.java)
                    } else {
                        @Suppress("DEPRECATION")
                        intent.getParcelableExtra(UsbManager.EXTRA_DEVICE)
                    }
                    device?.let {
                        openDevices.remove(it.deviceId)?.close()
                        Log.d(tag, "USB device detached: ${it.deviceName}")
                    }
                    refreshDevices()
                }
            }
        }
    }

    fun start() {
        val filter = IntentFilter().apply {
            addAction(ACTION_USB_PERMISSION)
            addAction(UsbManager.ACTION_USB_DEVICE_ATTACHED)
            addAction(UsbManager.ACTION_USB_DEVICE_DETACHED)
        }
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.TIRAMISU) {
            context.registerReceiver(usbReceiver, filter, Context.RECEIVER_NOT_EXPORTED)
        } else {
            context.registerReceiver(usbReceiver, filter)
        }
        refreshDevices()
    }

    fun stop() {
        try { context.unregisterReceiver(usbReceiver) } catch (_: Exception) {}
        openDevices.values.forEach { it.close() }
        openDevices.clear()
    }

    fun refreshDevices() {
        val attached = usbManager.deviceList.values.toList()
        val devInfos = attached.mapIndexed { index, device ->
            val existing = _devices.value.find { it.device.deviceId == device.deviceId }
            existing?.copy(device = device)
                ?: UsbDeviceInfo(device, isShared = true, devNum = index + 1)
        }
        _devices.value = devInfos
        Log.d(tag, "Found ${devInfos.size} USB device(s)")

        // Request permissions for any device we don't have permission for yet
        devInfos.forEach { info ->
            if (!usbManager.hasPermission(info.device)) {
                requestPermission(info.device)
            }
        }
    }

    fun setShared(deviceId: Int, shared: Boolean) {
        _devices.value = _devices.value.map {
            if (it.device.deviceId == deviceId) it.copy(isShared = shared) else it
        }
    }

    fun setAllShared(shared: Boolean) {
        _devices.value = _devices.value.map { it.copy(isShared = shared) }
    }

    /** Open a USB device connection for data transfer. */
    fun openDevice(deviceId: Int): UsbDeviceConnection? {
        val info = _devices.value.find { it.device.deviceId == deviceId } ?: return null
        if (!usbManager.hasPermission(info.device)) {
            requestPermission(info.device)
            return null
        }
        return openDevices.getOrPut(deviceId) {
            usbManager.openDevice(info.device)
        }
    }

    fun controlTransfer(
        deviceId: Int,
        requestType: Int,
        request: Int,
        value: Int,
        index: Int,
        buffer: ByteArray?,
        length: Int,
        timeout: Int
    ): Int {
        val conn = openDevice(deviceId) ?: return -1
        return conn.controlTransfer(requestType, request, value, index, buffer, length, timeout)
    }

    fun bulkTransfer(deviceId: Int, endpoint: UsbEndpoint, buffer: ByteArray, length: Int, timeout: Int): Int {
        val conn = openDevice(deviceId) ?: return -1
        return conn.bulkTransfer(endpoint, buffer, length, timeout)
    }

    fun getDeviceDescriptor(info: UsbDeviceInfo): ByteArray {
        val device = info.device
        val buf = ByteBuffer.allocate(18).order(ByteOrder.LITTLE_ENDIAN)
        buf.put(18)                                // bLength
        buf.put(0x01)                              // bDescriptorType = DEVICE
        buf.putShort(0x0200.toShort())             // bcdUSB = USB 2.0
        buf.put(device.deviceClass.toByte())
        buf.put(device.deviceSubclass.toByte())
        buf.put(device.deviceProtocol.toByte())
        buf.put(64)                                // bMaxPacketSize0
        buf.putShort(device.vendorId.toShort())
        buf.putShort(device.productId.toShort())
        buf.putShort(0x0100.toShort())             // bcdDevice
        buf.put(1)                                 // iManufacturer
        buf.put(2)                                 // iProduct
        buf.put(3)                                 // iSerialNumber
        buf.put(device.configurationCount.toByte())
        return buf.array()
    }

    private fun requestPermission(device: UsbDevice) {
        val flags = if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.S)
            PendingIntent.FLAG_MUTABLE else 0
        val permIntent = PendingIntent.getBroadcast(
            context, 0,
            Intent(ACTION_USB_PERMISSION),
            flags
        )
        usbManager.requestPermission(device, permIntent)
    }
}
