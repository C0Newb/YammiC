package co.cnewb.yammic.ui.settings

import android.os.Bundle
import android.view.LayoutInflater
import android.view.View
import android.view.ViewGroup
import androidx.fragment.app.Fragment
import androidx.fragment.app.activityViewModels
import androidx.navigation.fragment.findNavController
import co.cnewb.yammic.R
import co.cnewb.yammic.databinding.FragmentSettingsBinding
import co.cnewb.yammic.settings.SettingsPreferences
import co.cnewb.yammic.ui.BikeViewModel

class SettingsFragment : Fragment() {

    private var _binding: FragmentSettingsBinding? = null
    private val binding get() = _binding!!

    private val viewModel: BikeViewModel by activityViewModels()
    private lateinit var preferences: SettingsPreferences

    override fun onCreateView(
        inflater: LayoutInflater,
        container: ViewGroup?,
        savedInstanceState: Bundle?,
    ): View {
        _binding = FragmentSettingsBinding.inflate(inflater, container, false)
        return binding.root
    }

    override fun onViewCreated(
        view: View,
        savedInstanceState: Bundle?,
    ) {
        super.onViewCreated(view, savedInstanceState)
        preferences = SettingsPreferences(requireContext())

        binding.toolbar.setNavigationOnClickListener { findNavController().navigateUp() }

        binding.musicMetadataSwitch.isChecked = preferences.musicMetadataEnabled
        binding.musicMetadataSwitch.setOnCheckedChangeListener { _, isChecked ->
            preferences.musicMetadataEnabled = isChecked
        }

        binding.phoneMetadataSwitch.isChecked = preferences.phoneMetadataEnabled
        binding.phoneMetadataSwitch.setOnCheckedChangeListener { _, isChecked ->
            preferences.phoneMetadataEnabled = isChecked
        }

        binding.autoReconnectSwitch.isChecked = preferences.autoReconnectEnabled
        binding.autoReconnectSwitch.setOnCheckedChangeListener { _, isChecked ->
            preferences.autoReconnectEnabled = isChecked
        }

        binding.navigationSettingSwitch.setOnCheckedChangeListener { switchView, isChecked ->
            if (switchView.isPressed) viewModel.setNavigationEnabled(isChecked)
        }
        viewModel.navigationEnabled.observe(viewLifecycleOwner) { enabled ->
            if (binding.navigationSettingSwitch.isChecked != enabled) binding.navigationSettingSwitch.isChecked = enabled
        }

        binding.vpbSettingSwitch.setOnCheckedChangeListener { switchView, isChecked ->
            if (switchView.isPressed) viewModel.setVpbEnabled(isChecked)
        }
        viewModel.vpbEnabled.observe(viewLifecycleOwner) { enabled ->
            if (binding.vpbSettingSwitch.isChecked != enabled) binding.vpbSettingSwitch.isChecked = enabled
        }

        binding.pairingRow.setOnClickListener {
            findNavController().navigate(R.id.action_settings_to_pairing)
        }
    }

    override fun onDestroyView() {
        super.onDestroyView()
        _binding = null
    }
}
