package com.usbipserver

import android.content.Context
import android.net.nsd.NsdManager
import android.net.nsd.NsdServiceInfo
import android.util.Log

/**
 * Advertises the USB/IP server via mDNS (NSD) so that Windows clients
 * can auto-discover it on the LAN without manual IP configuration.
 *
 * Service type: _usbip._tcp  (port 3240)
 */
class MdnsAdvertiser(private val context: Context) {

    private val tag = "MdnsAdvertiser"
    private val nsdManager = context.getSystemService(Context.NSD_SERVICE) as NsdManager
    private var registrationListener: NsdManager.RegistrationListener? = null

    fun start(deviceCount: Int) {
        if (registrationListener != null) return   // already advertising

        val serviceInfo = NsdServiceInfo().apply {
            serviceName = "USBIPServer-${android.os.Build.MODEL.replace(" ", "-")}"
            serviceType = "_usbip._tcp."
            port = UsbIpProtocol.USBIP_PORT
            setAttribute("devices", deviceCount.toString())
            setAttribute("version", "1.1.1")
        }

        registrationListener = object : NsdManager.RegistrationListener {
            override fun onRegistrationFailed(info: NsdServiceInfo, code: Int) {
                Log.e(tag, "mDNS registration failed: $code")
                registrationListener = null
            }
            override fun onUnregistrationFailed(info: NsdServiceInfo, code: Int) {
                Log.e(tag, "mDNS unregistration failed: $code")
            }
            override fun onServiceRegistered(info: NsdServiceInfo) {
                Log.i(tag, "mDNS registered: ${info.serviceName}")
            }
            override fun onServiceUnregistered(info: NsdServiceInfo) {
                Log.i(tag, "mDNS unregistered: ${info.serviceName}")
                registrationListener = null
            }
        }

        nsdManager.registerService(serviceInfo, NsdManager.PROTOCOL_DNS_SD, registrationListener)
        Log.d(tag, "mDNS advertising started")
    }

    fun stop() {
        registrationListener?.let {
            try {
                nsdManager.unregisterService(it)
            } catch (e: Exception) {
                Log.w(tag, "mDNS stop error: ${e.message}")
            }
        }
        registrationListener = null
    }

    fun updateDeviceCount(count: Int) {
        // Re-register with updated attributes
        stop()
        start(count)
    }
}
