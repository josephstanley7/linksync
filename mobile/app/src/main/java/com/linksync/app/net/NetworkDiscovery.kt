package com.linksync.app.net

import android.content.Context
import android.net.wifi.WifiManager
import android.util.Log
import kotlinx.coroutines.*
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import java.io.File
import java.net.*
import java.util.concurrent.ConcurrentHashMap

data class DiscoveredDesktop(
    val ipAddress: String,
    val deviceName: String,
    val port: Int,
    val deviceId: String = ""
)

object NetworkDiscovery {
    private const val TAG = "LinkSyncDiscovery"
    private const val DISCOVERY_PORT = 54885
    private const val DEFAULT_TCP_PORT = 54884

    private val discoveredMap = ConcurrentHashMap<String, DiscoveredDesktop>()
    private val _discoveredDesktops = MutableStateFlow<List<DiscoveredDesktop>>(emptyList())
    val discoveredDesktops: StateFlow<List<DiscoveredDesktop>> = _discoveredDesktops.asStateFlow()

    private var multicastLock: WifiManager.MulticastLock? = null
    private var passiveListenerJob: Job? = null
    private var discoverySocket: DatagramSocket? = null

    /**
     * Starts continuous passive listening on UDP port 54885.
     * Essential for Mobile Hotspot: Laptop sends beacons directly to Phone's Gateway IP on port 54885.
     */
    fun startPassiveListener(context: Context, scope: CoroutineScope) {
        if (passiveListenerJob != null && passiveListenerJob?.isActive == true) return

        try {
            val wifi = context.applicationContext.getSystemService(Context.WIFI_SERVICE) as? WifiManager
            multicastLock = wifi?.createMulticastLock("LinkSyncMulticastLock")?.apply {
                setReferenceCounted(true)
                acquire()
            }
            Log.d(TAG, "MulticastLock acquired: ${multicastLock?.isHeld}")
        } catch (e: Exception) {
            Log.w(TAG, "Could not acquire MulticastLock: ${e.message}")
        }

        passiveListenerJob = scope.launch(Dispatchers.IO) {
            try {
                val socket = DatagramSocket(null).apply {
                    reuseAddress = true
                    broadcast = true
                    bind(InetSocketAddress(DISCOVERY_PORT))
                }
                discoverySocket = socket
                val buffer = ByteArray(2048)
                val packet = DatagramPacket(buffer, buffer.size)

                Log.i(TAG, "Passive discovery listener listening on port $DISCOVERY_PORT")

                while (isActive) {
                    try {
                        socket.receive(packet)
                        val message = String(packet.data, 0, packet.length, Charsets.UTF_8).trim()
                        val senderIp = packet.address.hostAddress ?: ""

                        if (senderIp.isNotEmpty() && !senderIp.startsWith("127.")) {
                            val parts = message.split("|")
                            if (parts.size >= 3 && (parts[0] == "LINKSYNC_DISCOVERY" || parts[0] == "LINKSYNC_PONG")) {
                                val name = parts[1]
                                val port = parts[2].toIntOrNull() ?: DEFAULT_TCP_PORT
                                val devId = if (parts.size >= 4 && parts[3].isNotEmpty()) parts[3] else "PC-$senderIp"

                                val item = DiscoveredDesktop(
                                    ipAddress = senderIp,
                                    deviceName = name,
                                    port = port,
                                    deviceId = devId
                                )
                                discoveredMap[devId] = item
                                _discoveredDesktops.value = discoveredMap.values.toList()
                                Log.i(TAG, "Discovered Desktop: $name ($senderIp:$port) [ID: $devId]")
                            }
                        }
                    } catch (e: Exception) {
                        if (!isActive) break
                    }
                }
            } catch (e: Exception) {
                Log.e(TAG, "Passive listener socket error: ${e.message}")
            } finally {
                try { discoverySocket?.close() } catch (_: Exception) {}
                discoverySocket = null
            }
        }
    }

    fun stopPassiveListener() {
        passiveListenerJob?.cancel()
        passiveListenerJob = null
        try { discoverySocket?.close() } catch (_: Exception) {}
        discoverySocket = null
        try {
            if (multicastLock?.isHeld == true) {
                multicastLock?.release()
            }
        } catch (_: Exception) {}
        multicastLock = null
    }

    /**
     * Broadcasts discovery ping to 255.255.255.255, directed subnet broadcasts,
     * hotspot client IPs parsed from /proc/net/arp, and any cached IP.
     */
    suspend fun sendDiscoveryPing(cachedIp: String? = null) = withContext(Dispatchers.IO) {
        var tempSocket: DatagramSocket? = null
        try {
            val socket = discoverySocket ?: DatagramSocket().also { tempSocket = it }
            socket.broadcast = true

            val pingBytes = "LINKSYNC_PING".toByteArray(Charsets.UTF_8)
            val targets = getDiscoveryTargetAddresses(cachedIp)

            for (target in targets) {
                try {
                    val packet = DatagramPacket(pingBytes, pingBytes.size, target, DISCOVERY_PORT)
                    socket.send(packet)
                    Log.d(TAG, "Sent discovery ping to $target:$DISCOVERY_PORT")
                } catch (e: Exception) {
                    Log.d(TAG, "Error sending ping to $target: ${e.message}")
                }
            }
        } catch (e: Exception) {
            Log.e(TAG, "sendDiscoveryPing error: ${e.message}")
        } finally {
            try { tempSocket?.close() } catch (_: Exception) {}
        }
    }

    /**
     * One-shot discovery function for backward compatibility.
     */
    suspend fun discoverLaptop(timeoutMs: Int = 1500, cachedIp: String? = null): DiscoveredDesktop? = withContext(Dispatchers.IO) {
        // If already discovered recently, return the first one
        val currentList = _discoveredDesktops.value
        if (currentList.isNotEmpty()) {
            return@withContext currentList.first()
        }

        sendDiscoveryPing(cachedIp)

        // Wait up to timeoutMs for any discovered desktop
        val deadline = System.currentTimeMillis() + timeoutMs
        while (System.currentTimeMillis() < deadline) {
            val list = _discoveredDesktops.value
            if (list.isNotEmpty()) {
                return@withContext list.first()
            }
            delay(100)
        }
        null
    }

    fun getDiscoveryTargetAddresses(cachedIp: String? = null): List<InetAddress> {
        val targets = mutableListOf<InetAddress>()
        try {
            targets.add(InetAddress.getByName("255.255.255.255"))

            // 1. Cached IP if known
            if (!cachedIp.isNullOrEmpty()) {
                try {
                    targets.add(InetAddress.getByName(cachedIp))
                } catch (_: Exception) {}
            }

            // 2. Subnet directed broadcasts from NetworkInterface
            val interfaces = NetworkInterface.getNetworkInterfaces()
            while (interfaces.hasMoreElements()) {
                val netIf = interfaces.nextElement()
                if (netIf.isLoopback || !netIf.isUp) continue

                for (ifAddr in netIf.interfaceAddresses) {
                    val bcast = ifAddr.broadcast
                    if (bcast != null && !targets.contains(bcast)) {
                        targets.add(bcast)
                    }

                    // Calculate manual subnet directed broadcast (e.g. 10.253.25.255)
                    val addr = ifAddr.address
                    if (addr is Inet4Address && !addr.isLoopbackAddress) {
                        val ip = addr.address
                        val prefix = ifAddr.networkPrefixLength.toInt()
                        if (prefix in 8..30) {
                            val mask = -1 shl (32 - prefix)
                            val ipInt = ((ip[0].toInt() and 0xFF) shl 24) or
                                        ((ip[1].toInt() and 0xFF) shl 16) or
                                        ((ip[2].toInt() and 0xFF) shl 8) or
                                        (ip[3].toInt() and 0xFF)
                            val broadcastInt = ipInt or mask.inv()
                            val bcastBytes = byteArrayOf(
                                ((broadcastInt ushr 24) and 0xFF).toByte(),
                                ((broadcastInt ushr 16) and 0xFF).toByte(),
                                ((broadcastInt ushr 8) and 0xFF).toByte(),
                                (broadcastInt and 0xFF).toByte()
                            )
                            try {
                                val manualBcast = InetAddress.getByAddress(bcastBytes)
                                if (!targets.contains(manualBcast)) {
                                    targets.add(manualBcast)
                                }
                            } catch (_: Exception) {}
                        }
                    }
                }
            }

            // 3. Check /proc/net/arp for connected hotspot clients
            try {
                val arpFile = File("/proc/net/arp")
                if (arpFile.exists() && arpFile.canRead()) {
                    arpFile.forEachLine { line ->
                        val tokens = line.trim().split(Regex("\\s+"))
                        if (tokens.size >= 4 && tokens[0] != "IP") {
                            val ip = tokens[0]
                            val flags = tokens[2]
                            if (flags == "0x2" && ip.matches(Regex("\\d+\\.\\d+\\.\\d+\\.\\d+"))) {
                                try {
                                    val clientAddr = InetAddress.getByName(ip)
                                    if (!targets.contains(clientAddr)) {
                                        targets.add(clientAddr)
                                    }
                                } catch (_: Exception) {}
                            }
                        }
                    }
                }
            } catch (_: Exception) {}

        } catch (e: Exception) {
            Log.d(TAG, "Error resolving discovery targets: ${e.message}")
        }
        return targets
    }
}
