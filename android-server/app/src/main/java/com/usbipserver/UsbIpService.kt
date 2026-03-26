package com.usbipserver

import android.app.*
import android.content.Intent
import android.os.Binder
import android.os.Build
import android.os.IBinder
import android.util.Log
import androidx.core.app.NotificationCompat
import kotlinx.coroutines.*

/**
 * Foreground service that keeps the USB/IP server running while the app
 * is in the background.  Bound by MainActivity.
 */
class UsbIpService : Service() {

    private val tag = "UsbIpService"
    private val binder = LocalBinder()
    private val scope = CoroutineScope(Dispatchers.Main + SupervisorJob())

    lateinit var deviceManager: UsbDeviceManager
        private set

    lateinit var usbIpServer: UsbIpServer
        private set

    lateinit var mdnsAdvertiser: MdnsAdvertiser
        private set

    private var connectedClients = 0

    inner class LocalBinder : Binder() {
        fun getService(): UsbIpService = this@UsbIpService
    }

    companion object {
        private const val NOTIFICATION_ID = 1001
        private const val CHANNEL_ID = "usbip_server_channel"
    }

    override fun onCreate() {
        super.onCreate()
        createNotificationChannel()

        deviceManager = UsbDeviceManager(this)
        usbIpServer   = UsbIpServer(deviceManager) { clients ->
            connectedClients = clients
            updateNotification()
        }
        mdnsAdvertiser = MdnsAdvertiser(this)

        deviceManager.start()
        Log.i(tag, "UsbIpService created")
    }

    override fun onStartCommand(intent: Intent?, flags: Int, startId: Int): Int {
        startForeground(NOTIFICATION_ID, buildNotification())
        usbIpServer.start()
        val sharedCount = deviceManager.devices.value.count { it.isShared }
        mdnsAdvertiser.start(sharedCount)
        Log.i(tag, "USB/IP server started")
        return START_STICKY
    }

    override fun onBind(intent: Intent): IBinder = binder

    override fun onDestroy() {
        usbIpServer.stop()
        mdnsAdvertiser.stop()
        deviceManager.stop()
        scope.cancel()
        super.onDestroy()
        Log.i(tag, "UsbIpService destroyed")
    }

    fun stopServer() {
        usbIpServer.stop()
        mdnsAdvertiser.stop()
        stopForeground(STOP_FOREGROUND_REMOVE)
        stopSelf()
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Notification helpers
    // ──────────────────────────────────────────────────────────────────────────

    private fun createNotificationChannel() {
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O) {
            val channel = NotificationChannel(
                CHANNEL_ID,
                getString(R.string.notification_channel_name),
                NotificationManager.IMPORTANCE_LOW
            )
            getSystemService(NotificationManager::class.java)
                ?.createNotificationChannel(channel)
        }
    }

    private fun buildNotification(): Notification {
        val sharedCount = deviceManager.devices.value.count { it.isShared }
        val openIntent = Intent(this, MainActivity::class.java).apply {
            flags = Intent.FLAG_ACTIVITY_SINGLE_TOP
        }
        val pendingFlags = if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.S)
            PendingIntent.FLAG_IMMUTABLE else 0
        val pendingOpen = PendingIntent.getActivity(this, 0, openIntent, pendingFlags)

        return NotificationCompat.Builder(this, CHANNEL_ID)
            .setContentTitle(getString(R.string.notification_title))
            .setContentText(getString(R.string.notification_text, sharedCount))
            .setSmallIcon(android.R.drawable.ic_dialog_info)
            .setContentIntent(pendingOpen)
            .setOngoing(true)
            .build()
    }

    private fun updateNotification() {
        val nm = getSystemService(NotificationManager::class.java)
        nm?.notify(NOTIFICATION_ID, buildNotification())
    }
}
