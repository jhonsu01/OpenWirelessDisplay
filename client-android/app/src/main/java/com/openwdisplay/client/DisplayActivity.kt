package com.openwdisplay.client

import android.annotation.SuppressLint
import android.graphics.Bitmap
import android.graphics.Canvas
import android.graphics.Color
import android.graphics.Paint
import android.graphics.Rect
import android.os.Build
import android.os.Bundle
import android.view.MotionEvent
import android.view.SurfaceHolder
import android.view.SurfaceView
import android.view.View
import android.view.WindowInsets
import android.view.WindowInsetsController
import android.view.WindowManager
import androidx.appcompat.app.AppCompatActivity
import com.openwdisplay.client.databinding.ActivityDisplayBinding

/**
 * Modulo de Renderizado + Input (guia, Fases 3 y 4): dibuja los frames recibidos en un
 * SurfaceView y reenvia los toques como coordenadas normalizadas (0..1) al servidor.
 */
class DisplayActivity : AppCompatActivity(), VideoClient.Listener, SurfaceHolder.Callback {

    private lateinit var binding: ActivityDisplayBinding
    private var client: VideoClient? = null
    private var holder: SurfaceHolder? = null
    private val paint = Paint(Paint.FILTER_BITMAP_FLAG)
    @Volatile private var imageRect = Rect()

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        binding = ActivityDisplayBinding.inflate(layoutInflater)
        setContentView(binding.root)
        // Mantener la pantalla encendida mientras se comparte: evita el bloqueo y
        // la desconexion al apagarse la pantalla del dispositivo.
        window.addFlags(WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON)
        hideSystemBars()

        binding.surface.holder.addCallback(this)
        binding.surface.setOnTouchListener { v, event -> handleTouch(v, event) }

        val host = intent.getStringExtra(EXTRA_HOST) ?: return finish()
        val port = intent.getIntExtra(EXTRA_PORT, WireProtocol.DEFAULT_PORT)
        val pin = intent.getStringExtra(EXTRA_PIN).orEmpty()
        val name = intent.getStringExtra(EXTRA_NAME).orEmpty()

        binding.overlay.text = getString(R.string.connecting)
        client = VideoClient(host, port, pin, Build.MODEL).also {
            // La conexion arranca cuando la Surface este lista (surfaceCreated).
            pending = Triple(host, port, name)
        }
    }

    private var pending: Triple<String, Int, String>? = null

    override fun surfaceCreated(h: SurfaceHolder) {
        holder = h
        client?.connect(this)
    }

    override fun surfaceChanged(h: SurfaceHolder, format: Int, width: Int, height: Int) {}
    override fun surfaceDestroyed(h: SurfaceHolder) { holder = null }

    // ---- VideoClient.Listener ----
    override fun onPaired(width: Int, height: Int, fps: Int) {
        runOnUiThread { binding.overlay.text = "${pending?.third}  ${width}x$height @ ${fps}fps" }
        // Oculta el overlay tras 2s.
        binding.overlay.postDelayed({ binding.overlay.visibility = View.GONE }, 2000)
    }

    override fun onPinFailed(reason: String, attemptsLeft: Int) {
        runOnUiThread {
            binding.overlay.visibility = View.VISIBLE
            binding.overlay.text = "$reason (intentos restantes: $attemptsLeft)"
            binding.overlay.postDelayed({ finish() }, 1800)
        }
    }

    override fun onFrame(bitmap: Bitmap) {
        val h = holder ?: return
        val canvas = h.lockCanvas() ?: return
        try {
            canvas.drawColor(Color.BLACK)
            val rect = computeFitRect(bitmap.width, bitmap.height, canvas.width, canvas.height)
            imageRect = rect
            canvas.drawBitmap(bitmap, null, rect, paint)
        } finally {
            h.unlockCanvasAndPost(canvas)
        }
    }

    override fun onError(message: String) {
        runOnUiThread {
            binding.overlay.visibility = View.VISIBLE
            binding.overlay.text = "Error: $message"
        }
    }

    override fun onDisconnected() {
        runOnUiThread {
            if (!isFinishing) {
                binding.overlay.visibility = View.VISIBLE
                binding.overlay.text = "Desconectado"
            }
        }
    }

    // ---- Input ----
    @SuppressLint("ClickableViewAccessibility")
    private fun handleTouch(v: View, event: MotionEvent): Boolean {
        val rect = imageRect
        if (rect.width() <= 0 || rect.height() <= 0) return true
        val nx = ((event.x - rect.left) / rect.width()).coerceIn(0f, 1f)
        val ny = ((event.y - rect.top) / rect.height()).coerceIn(0f, 1f)
        val action = when (event.actionMasked) {
            MotionEvent.ACTION_DOWN -> WireProtocol.INPUT_DOWN
            MotionEvent.ACTION_MOVE -> WireProtocol.INPUT_MOVE
            MotionEvent.ACTION_UP, MotionEvent.ACTION_CANCEL -> WireProtocol.INPUT_UP
            else -> return true
        }
        client?.sendInput(action, nx, ny)
        return true
    }

    private fun computeFitRect(srcW: Int, srcH: Int, dstW: Int, dstH: Int): Rect {
        if (srcW == 0 || srcH == 0) return Rect(0, 0, dstW, dstH)
        val scale = minOf(dstW.toFloat() / srcW, dstH.toFloat() / srcH)
        val w = (srcW * scale).toInt()
        val h = (srcH * scale).toInt()
        val left = (dstW - w) / 2
        val top = (dstH - h) / 2
        return Rect(left, top, left + w, top + h)
    }

    private fun hideSystemBars() {
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.R) {
            window.setDecorFitsSystemWindows(false)
            window.insetsController?.let {
                it.hide(WindowInsets.Type.systemBars())
                it.systemBarsBehavior = WindowInsetsController.BEHAVIOR_SHOW_TRANSIENT_BARS_BY_SWIPE
            }
        } else {
            @Suppress("DEPRECATION")
            window.decorView.systemUiVisibility =
                View.SYSTEM_UI_FLAG_FULLSCREEN or
                View.SYSTEM_UI_FLAG_HIDE_NAVIGATION or
                View.SYSTEM_UI_FLAG_IMMERSIVE_STICKY
        }
    }

    override fun onDestroy() {
        client?.close()
        client = null
        super.onDestroy()
    }

    companion object {
        const val EXTRA_HOST = "host"
        const val EXTRA_PORT = "port"
        const val EXTRA_PIN = "pin"
        const val EXTRA_NAME = "name"
    }
}
