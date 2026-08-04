package co.cnewb.yammic.ui.dashboard

import android.Manifest
import android.content.Intent
import android.os.Build
import android.os.Bundle
import android.provider.Settings
import android.view.LayoutInflater
import android.view.View
import android.view.ViewGroup
import androidx.activity.result.contract.ActivityResultContracts
import androidx.fragment.app.Fragment
import androidx.fragment.app.activityViewModels
import androidx.navigation.fragment.findNavController
import co.cnewb.yammic.R
import co.cnewb.yammic.bike.BikeDebugState
import co.cnewb.yammic.databinding.FragmentDashboardBinding
import co.cnewb.yammic.ui.BikeViewModel
import co.cnewb.yammic.ui.ConnectButtonState
import co.cnewb.yammic.ui.navigation.describeNavigationState

class DashboardFragment : Fragment() {

    private var _binding: FragmentDashboardBinding? = null
    private val binding get() = _binding!!

    private val viewModel: BikeViewModel by activityViewModels()

    private val permissionLauncher =
        registerForActivityResult(ActivityResultContracts.RequestMultiplePermissions()) { results ->
            viewModel.onPermissionsGranted(results.values.all { it })
        }

    override fun onCreateView(
        inflater: LayoutInflater,
        container: ViewGroup?,
        savedInstanceState: Bundle?,
    ): View {
        _binding = FragmentDashboardBinding.inflate(inflater, container, false)
        return binding.root
    }

    override fun onViewCreated(
        view: View,
        savedInstanceState: Bundle?,
    ) {
        super.onViewCreated(view, savedInstanceState)

        binding.toolbar.setOnMenuItemClickListener { item ->
            if (item.itemId == R.id.action_settings) {
                findNavController().navigate(R.id.action_dashboard_to_settings)
                true
            } else {
                false
            }
        }

        binding.grantNotificationAccessButton.setOnClickListener {
            startActivity(Intent(Settings.ACTION_NOTIFICATION_LISTENER_SETTINGS))
        }

        binding.connectButton.setOnClickListener { requestPermissionsAndConnect() }
        binding.disconnectButton.setOnClickListener { viewModel.disconnect() }
        binding.clearLogsButton.setOnClickListener { viewModel.clearLogs() }
        binding.navigationCard.setOnClickListener {
            findNavController().navigate(R.id.action_dashboard_to_navigationSettings)
        }
        binding.vpbSwitch.setOnCheckedChangeListener { switchView, isChecked ->
            if (switchView.isPressed) viewModel.setVpbEnabled(isChecked)
        }

        observeState()
    }

    private fun observeState() {
        viewModel.connectionState.observe(viewLifecycleOwner) { renderState(it) }
        viewModel.connectButtonState.observe(viewLifecycleOwner) { renderConnectButtons(it) }
        viewModel.vpbEnabled.observe(viewLifecycleOwner) { enabled ->
            if (binding.vpbSwitch.isChecked != enabled) binding.vpbSwitch.isChecked = enabled
        }
        viewModel.vpbStatus.observe(viewLifecycleOwner) { status ->
            binding.vpbStatusText.text = getString(R.string.vpb_status_format, status)
        }
        viewModel.mediaMetadata.observe(viewLifecycleOwner) { metadata ->
            if (metadata == null) {
                binding.mediaTrackText.text = getString(R.string.media_nothing_playing)
                binding.mediaArtistText.text = ""
                binding.mediaStateText.text = ""
            } else {
                binding.mediaTrackText.text = metadata.songName.ifBlank { getString(R.string.media_nothing_playing) }
                binding.mediaArtistText.text = metadata.artistName
                binding.mediaStateText.text = metadata.playbackState.name
            }
        }
        viewModel.navigationState.observe(viewLifecycleOwner) { navState ->
            binding.navigationStatusText.text = describeNavigationState(requireContext(), navState)
        }
    }

    private fun renderState(state: BikeDebugState) {
        binding.bikeNameText.text = state.deviceName
        binding.bleNameChip.text = getString(R.string.ble_name_format, state.deviceName)
        binding.connectionStatusChip.text = getString(R.string.status_format, state.connectionLabel)
        binding.odometerChip.text = getString(R.string.odometer_placeholder)
        if (state.lastError.isNotBlank()) {
            binding.lastErrorText.visibility = View.VISIBLE
            binding.lastErrorText.text = state.lastError
        } else {
            binding.lastErrorText.visibility = View.GONE
        }
        binding.logText.text = state.logs.joinToString("\n")
        binding.logScrollView.post { binding.logScrollView.fullScroll(View.FOCUS_DOWN) }
    }

    private fun renderConnectButtons(buttonState: ConnectButtonState) {
        when (buttonState) {
            ConnectButtonState.IDLE -> {
                binding.connectButton.isEnabled = true
                binding.connectButton.isSelected = true
                binding.disconnectButton.isEnabled = false
            }
            ConnectButtonState.CONNECTING -> {
                binding.connectButton.isEnabled = false
                binding.connectButton.isSelected = false
                binding.disconnectButton.isEnabled = false
            }
            ConnectButtonState.CONNECTED -> {
                binding.connectButton.isEnabled = false
                binding.connectButton.isSelected = false
                binding.disconnectButton.isEnabled = true
            }
        }
    }

    private fun requestPermissionsAndConnect() {
        val permissions =
            buildList {
                add(Manifest.permission.ACCESS_FINE_LOCATION)
                add(Manifest.permission.READ_PHONE_STATE)
                add(Manifest.permission.READ_CONTACTS)
                add(Manifest.permission.ANSWER_PHONE_CALLS)
                if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.S) {
                    add(Manifest.permission.BLUETOOTH_CONNECT)
                    add(Manifest.permission.BLUETOOTH_SCAN)
                }
                if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.TIRAMISU) {
                    add(Manifest.permission.POST_NOTIFICATIONS)
                }
            }.toTypedArray()
        permissionLauncher.launch(permissions)
    }

    override fun onResume() {
        super.onResume()
        binding.notificationAccessCard.visibility = if (isNotificationListenerGranted()) View.GONE else View.VISIBLE
    }

    private fun isNotificationListenerGranted(): Boolean {
        val flat =
            Settings.Secure.getString(requireContext().contentResolver, "enabled_notification_listeners")
                ?: return false
        return flat.split(":").any { it.startsWith("${requireContext().packageName}/") }
    }

    override fun onDestroyView() {
        super.onDestroyView()
        _binding = null
    }
}
