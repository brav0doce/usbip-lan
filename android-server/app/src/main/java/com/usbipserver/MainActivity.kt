package com.usbipserver

import android.content.*
import android.net.wifi.WifiManager
import android.os.Build
import android.os.Bundle
import android.os.IBinder
import android.view.View
import android.widget.Toast
import androidx.appcompat.app.AppCompatActivity
import androidx.lifecycle.lifecycleScope
import com.usbipserver.databinding.ActivityMainBinding
import kotlinx.coroutines.flow.collectLatest
import kotlinx.coroutines.launch

/**
 * Main screen – shows server status, IP address, connected clients
 * and the list of USB devices with share toggles.
 */
class MainActivity : AppCompatActivity() {

    private lateinit var binding: ActivityMainBinding
    private lateinit var adapter: UsbDeviceAdapter

    private var usbIpService: UsbIpService? = null
    private var serviceBound = false

    private val serviceConnection = object : ServiceConnection {
        override fun onServiceConnected(name: ComponentName, binder: IBinder) {
            val localBinder = binder as UsbIpService.LocalBinder
            usbIpService = localBinder.getService()
            serviceBound = true
            observeDevices()
        }
        override fun onServiceDisconnected(name: ComponentName) {
            usbIpService = null
            serviceBound = false
        }
    }

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        binding = ActivityMainBinding.inflate(layoutInflater)
        setContentView(binding.root)
        setSupportActionBar(binding.toolbar)

        adapter = UsbDeviceAdapter { deviceId, shared ->
            usbIpService?.deviceManager?.setShared(deviceId, shared)
        }
        binding.recyclerDevices.adapter = adapter

        setupServerSwitch()
        setupButtons()
    }

    override fun onStart() {
        super.onStart()
        val intent = Intent(this, UsbIpService::class.java)
        bindService(intent, serviceConnection, Context.BIND_AUTO_CREATE)
    }

    override fun onStop() {
        super.onStop()
        if (serviceBound) {
            unbindService(serviceConnection)
            serviceBound = false
        }
    }

    // ──────────────────────────────────────────────────────────────────────────
    // UI setup
    // ──────────────────────────────────────────────────────────────────────────

    private fun setupServerSwitch() {
        binding.switchServer.setOnCheckedChangeListener { _, checked ->
            if (checked) startServer() else stopServer()
        }
    }

    private fun setupButtons() {
        binding.btnShareAll.setOnClickListener {
            usbIpService?.deviceManager?.setAllShared(true)
        }
        binding.btnUnshareAll.setOnClickListener {
            usbIpService?.deviceManager?.setAllShared(false)
        }
    }

    private fun startServer() {
        val intent = Intent(this, UsbIpService::class.java)
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O) {
            startForegroundService(intent)
        } else {
            startService(intent)
        }
        updateStatusUI(running = true)
    }

    private fun stopServer() {
        usbIpService?.stopServer()
        updateStatusUI(running = false)
    }

    private fun updateStatusUI(running: Boolean) {
        if (running) {
            binding.tvServerStatus.setText(R.string.server_running)
            binding.statusIndicator.backgroundTintList =
                android.content.res.ColorStateList.valueOf(
                    getColor(R.color.status_running)
                )
            binding.tvIpAddress.text    = getString(R.string.ip_address, getLocalIpAddress())
            binding.tvIpAddress.visibility    = View.VISIBLE
            binding.tvConnectedClients.visibility = View.VISIBLE
        } else {
            binding.tvServerStatus.setText(R.string.server_stopped)
            binding.statusIndicator.backgroundTintList =
                android.content.res.ColorStateList.valueOf(
                    getColor(R.color.status_stopped)
                )
            binding.tvIpAddress.visibility    = View.GONE
            binding.tvConnectedClients.visibility = View.GONE
        }
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Observe device list
    // ──────────────────────────────────────────────────────────────────────────

    private fun observeDevices() {
        val service = usbIpService ?: return
        lifecycleScope.launch {
            service.deviceManager.devices.collectLatest { devices ->
                adapter.submitList(devices)
                val empty = devices.isEmpty()
                binding.recyclerDevices.visibility = if (empty) View.GONE else View.VISIBLE
                binding.layoutEmpty.visibility     = if (empty) View.VISIBLE else View.GONE

                // Update mDNS advertisement with current shared count
                val sharedCount = devices.count { it.isShared }
                service.mdnsAdvertiser.updateDeviceCount(sharedCount)

                // Update clients label
                binding.tvConnectedClients.text =
                    getString(R.string.connected_clients, 0)
            }
        }
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Helpers
    // ──────────────────────────────────────────────────────────────────────────

    private fun getLocalIpAddress(): String {
        return try {
            val interfaces = java.net.NetworkInterface.getNetworkInterfaces()
            for (intf in interfaces) {
                if (intf.isUp && !intf.isLoopback) {
                    for (addr in intf.inetAddresses) {
                        if (!addr.isLoopbackAddress && addr is java.net.Inet4Address) {
                            return addr.hostAddress ?: "N/A"
                        }
                    }
                }
            }
            "N/A"
        } catch (e: Exception) {
            "N/A"
        }
    }
}
