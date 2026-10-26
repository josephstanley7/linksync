package com.linksync.app.service

import android.app.Notification
import android.service.notification.NotificationListenerService
import android.service.notification.StatusBarNotification
import android.util.Log

class LinkSyncNotificationListener : NotificationListenerService() {

    companion object {
        @Volatile
        var instance: LinkSyncNotificationListener? = null
            private set

        fun syncAllActiveNotifications() {
            instance?.syncActiveNotifications()
        }
    }

    override fun onListenerConnected() {
        super.onListenerConnected()
        instance = this
        Log.i("LinkSyncNotif", "Notification listener connected to Android system.")
        syncActiveNotifications()
    }

    override fun onListenerDisconnected() {
        super.onListenerDisconnected()
        if (instance == this) instance = null
        Log.w("LinkSyncNotif", "Notification listener disconnected from Android system.")
    }

    fun syncActiveNotifications() {
        try {
            val active = activeNotifications
            if (active.isNullOrEmpty()) {
                Log.i("LinkSyncNotif", "No active notifications present on device.")
                return
            }
            Log.i("LinkSyncNotif", "Found ${active.size} active notifications. Forwarding to desktop...")
            for (sbn in active) {
                processAndSendNotification(sbn)
            }
        } catch (e: Exception) {
            Log.e("LinkSyncNotif", "Error querying active notifications: ${e.message}")
        }
    }

    override fun onNotificationPosted(sbn: StatusBarNotification?) {
        if (sbn == null) return
        processAndSendNotification(sbn)
    }

    private fun processAndSendNotification(sbn: StatusBarNotification) {
        val pkgName = sbn.packageName ?: return
        // Ignore self notifications to prevent loops
        if (pkgName == packageName) return

        val notification = sbn.notification ?: return

        // Ignore generic ongoing background services (e.g. download managers, persistent foreground banners with no message)
        if (sbn.isOngoing && (notification.flags and Notification.FLAG_FOREGROUND_SERVICE) != 0) {
            val hasText = notification.extras?.getCharSequence(Notification.EXTRA_TEXT)?.isNotEmpty() == true
            val hasTitle = notification.extras?.getCharSequence(Notification.EXTRA_TITLE)?.isNotEmpty() == true
            if (!hasText && !hasTitle) return
        }

        val extras = notification.extras ?: return
        val title = extras.getCharSequence(Notification.EXTRA_TITLE)?.toString()?.trim() ?: ""
        val text = extras.getCharSequence(Notification.EXTRA_BIG_TEXT)?.toString()?.trim()
            ?: extras.getCharSequence(Notification.EXTRA_TEXT)?.toString()?.trim()
            ?: ""

        // If both title and text are blank, ignore
        if (title.isEmpty() && text.isEmpty()) return

        val appName = try {
            val pm = applicationContext.packageManager
            val appInfo = pm.getApplicationInfo(pkgName, 0)
            pm.getApplicationLabel(appInfo).toString()
        } catch (e: Exception) {
            pkgName.substringAfterLast('.')
        }

        val timestamp = if (sbn.postTime > 0) sbn.postTime else System.currentTimeMillis()
        val key = sbn.key ?: "${pkgName}_${sbn.id}"

        Log.i("LinkSyncNotif", "Forwarding notification: [$appName] $title - ${text.take(40)}")
        ForegroundSyncService.sendPhoneNotification(
            key = key,
            packageName = pkgName,
            appName = appName,
            title = title,
            text = text,
            timestamp = timestamp
        )
    }

    override fun onNotificationRemoved(sbn: StatusBarNotification?) {
        // Reserved for dismissal sync
    }
}
