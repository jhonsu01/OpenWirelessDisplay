package com.openwdisplay.client

import android.content.Context
import android.net.nsd.NsdManager
import android.net.nsd.NsdServiceInfo
import android.util.Log
import java.util.concurrent.ConcurrentHashMap

/**
 * Modulo de Descubrimiento (guia, Fase 5): escucha el servicio mDNS/DNS-SD
 * "_openwdisplay._tcp." en la red local mediante NsdManager y resuelve host/puerto
 * automaticamente, sin que el usuario teclee IPs.
 */
class DiscoveryManager(context: Context) {

    data class Server(val name: String, val host: String, val port: Int)

    private val nsd = context.getSystemService(Context.NSD_SERVICE) as NsdManager
    private val found = ConcurrentHashMap<String, Server>()
    private var discoveryListener: NsdManager.DiscoveryListener? = null

    var onChanged: ((List<Server>) -> Unit)? = null

    fun start() {
        if (discoveryListener != null) return
        val listener = object : NsdManager.DiscoveryListener {
            override fun onStartDiscoveryFailed(serviceType: String, errorCode: Int) {
                Log.e(TAG, "Fallo al iniciar descubrimiento: $errorCode")
            }

            override fun onStopDiscoveryFailed(serviceType: String, errorCode: Int) {}
            override fun onDiscoveryStarted(serviceType: String) {
                Log.i(TAG, "Descubrimiento iniciado")
            }

            override fun onDiscoveryStopped(serviceType: String) {}

            override fun onServiceFound(info: NsdServiceInfo) {
                Log.i(TAG, "Servicio encontrado: ${info.serviceName}")
                resolve(info)
            }

            override fun onServiceLost(info: NsdServiceInfo) {
                found.remove(info.serviceName)
                onChanged?.invoke(found.values.toList())
            }
        }
        discoveryListener = listener
        nsd.discoverServices(WireProtocol.SERVICE_TYPE, NsdManager.PROTOCOL_DNS_SD, listener)
    }

    private fun resolve(info: NsdServiceInfo) {
        @Suppress("DEPRECATION")
        nsd.resolveService(info, object : NsdManager.ResolveListener {
            override fun onResolveFailed(serviceInfo: NsdServiceInfo, errorCode: Int) {
                Log.w(TAG, "Resolucion fallida (${serviceInfo.serviceName}): $errorCode")
            }

            override fun onServiceResolved(serviceInfo: NsdServiceInfo) {
                val host = serviceInfo.host?.hostAddress ?: return
                val server = Server(
                    name = serviceInfo.serviceName ?: host,
                    host = host,
                    port = serviceInfo.port,
                )
                found[server.name] = server
                onChanged?.invoke(found.values.toList())
            }
        })
    }

    fun stop() {
        discoveryListener?.let {
            try { nsd.stopServiceDiscovery(it) } catch (_: Exception) {}
        }
        discoveryListener = null
        found.clear()
    }

    companion object {
        private const val TAG = "DiscoveryManager"
    }
}
