package co.cnewb.yammic

import android.os.Bundle
import android.util.Log
import androidx.activity.viewModels
import androidx.appcompat.app.AppCompatActivity
import co.cnewb.yammic.databinding.ActivityMainBinding
import co.cnewb.yammic.ui.BikeViewModel

class MainActivity : AppCompatActivity() {

    private lateinit var binding: ActivityMainBinding
    private val bikeViewModel: BikeViewModel by viewModels()

    private var previousUncaughtHandler: Thread.UncaughtExceptionHandler? = null

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)

        previousUncaughtHandler = Thread.getDefaultUncaughtExceptionHandler()
        Thread.setDefaultUncaughtExceptionHandler { thread, throwable ->
            try {
                bikeViewModel.logThrowable("uncaught:${thread.name}", throwable)
            } catch (_: Throwable) {
                Log.e("YammiC", "Uncaught exception", throwable)
            }
            previousUncaughtHandler?.uncaughtException(thread, throwable)
        }

        binding = ActivityMainBinding.inflate(layoutInflater)
        setContentView(binding.root)
    }

    override fun onDestroy() {
        Thread.setDefaultUncaughtExceptionHandler(previousUncaughtHandler)
        super.onDestroy()
    }
}
