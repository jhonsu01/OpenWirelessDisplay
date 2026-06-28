package com.openwdisplay.client

import android.content.Context
import android.content.Intent
import android.net.wifi.WifiManager
import android.os.Bundle
import android.view.Gravity
import android.view.View
import android.widget.LinearLayout
import android.widget.TextView
import androidx.appcompat.app.AppCompatActivity
import com.google.android.material.button.MaterialButton
import com.google.android.material.dialog.MaterialAlertDialogBuilder
import com.openwdisplay.client.databinding.ActivityMainBinding
import com.openwdisplay.client.databinding.DialogManualBinding
import com.openwdisplay.client.databinding.DialogPinBinding

/**
 * Pantalla principal: descubre servidores en la LAN (mDNS) y permite emparejar por PIN.
 */
class MainActivity : AppCompatActivity() {

    private lateinit var binding: ActivityMainBinding
    private lateinit var discovery: DiscoveryManager
    private var multicastLock: WifiManager.MulticastLock? = null

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        binding = ActivityMainBinding.inflate(layoutInflater)
        setContentView(binding.root)

        discovery = DiscoveryManager(this)
        discovery.onChanged = { servers -> runOnUiThread { renderServers(servers) } }

        binding.manualButton.setOnClickListener { showManualDialog() }
    }

    private fun showManualDialog() {
        val dialogBinding = DialogManualBinding.inflate(layoutInflater)
        MaterialAlertDialogBuilder(this)
            .setTitle(R.string.connect_manual)
            .setView(dialogBinding.root)
            .setPositiveButton(R.string.connect) { _, _ ->
                val ip = dialogBinding.ipInput.text?.toString()?.trim().orEmpty()
                val pin = dialogBinding.pinInput.text?.toString()?.trim().orEmpty()
                if (ip.isNotEmpty()) {
                    launchDisplay(DiscoveryManager.Server("Manual ($ip)", ip, WireProtocol.DEFAULT_PORT), pin)
                }
            }
            .setNegativeButton(R.string.cancel, null)
            .show()
    }

    override fun onResume() {
        super.onResume()
        acquireMulticastLock()
        binding.statusText.text = getString(R.string.searching)
        binding.progress.visibility = View.VISIBLE
        discovery.start()
    }

    override fun onPause() {
        super.onPause()
        discovery.stop()
        releaseMulticastLock()
    }

    private fun renderServers(servers: List<DiscoveryManager.Server>) {
        binding.serverList.removeAllViews()
        if (servers.isEmpty()) {
            binding.statusText.text = getString(R.string.no_servers)
            return
        }
        binding.statusText.text = "${servers.size} servidor(es) encontrado(s)"
        binding.progress.visibility = View.GONE

        for (server in servers) {
            val btn = MaterialButton(this).apply {
                text = "${server.name}\n${server.host}:${server.port}"
                gravity = Gravity.START or Gravity.CENTER_VERTICAL
                isAllCaps = false
                cornerRadius = 24
                setPadding(40, 32, 40, 32)
                val lp = LinearLayout.LayoutParams(
                    LinearLayout.LayoutParams.MATCH_PARENT,
                    LinearLayout.LayoutParams.WRAP_CONTENT,
                )
                lp.bottomMargin = 24
                layoutParams = lp
                setOnClickListener { showPinDialog(server) }
            }
            binding.serverList.addView(btn)
        }
    }

    private fun showPinDialog(server: DiscoveryManager.Server) {
        val dialogBinding = DialogPinBinding.inflate(layoutInflater)
        MaterialAlertDialogBuilder(this)
            .setTitle(server.name)
            .setView(dialogBinding.root)
            .setPositiveButton(R.string.pair) { _, _ ->
                val pin = dialogBinding.pinInput.text?.toString()?.trim().orEmpty()
                launchDisplay(server, pin)
            }
            .setNegativeButton(R.string.cancel, null)
            .show()
    }

    private fun launchDisplay(server: DiscoveryManager.Server, pin: String) {
        val intent = Intent(this, DisplayActivity::class.java).apply {
            putExtra(DisplayActivity.EXTRA_HOST, server.host)
            putExtra(DisplayActivity.EXTRA_PORT, server.port)
            putExtra(DisplayActivity.EXTRA_PIN, pin)
            putExtra(DisplayActivity.EXTRA_NAME, server.name)
        }
        startActivity(intent)
    }

    private fun acquireMulticastLock() {
        val wifi = applicationContext.getSystemService(Context.WIFI_SERVICE) as WifiManager
        multicastLock = wifi.createMulticastLock("owd-mdns").apply {
            setReferenceCounted(true)
            acquire()
        }
    }

    private fun releaseMulticastLock() {
        multicastLock?.let { if (it.isHeld) it.release() }
        multicastLock = null
    }
}
