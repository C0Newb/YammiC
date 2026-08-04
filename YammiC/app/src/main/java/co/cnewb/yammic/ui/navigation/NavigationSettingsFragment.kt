package co.cnewb.yammic.ui.navigation

import android.os.Bundle
import android.view.LayoutInflater
import android.view.View
import android.view.ViewGroup
import androidx.fragment.app.Fragment
import androidx.fragment.app.activityViewModels
import androidx.navigation.fragment.findNavController
import co.cnewb.yammic.R
import co.cnewb.yammic.databinding.FragmentNavigationSettingsBinding
import co.cnewb.yammic.ui.BikeViewModel

class NavigationSettingsFragment : Fragment() {

    private var _binding: FragmentNavigationSettingsBinding? = null
    private val binding get() = _binding!!

    private val viewModel: BikeViewModel by activityViewModels()

    override fun onCreateView(
        inflater: LayoutInflater,
        container: ViewGroup?,
        savedInstanceState: Bundle?,
    ): View {
        _binding = FragmentNavigationSettingsBinding.inflate(inflater, container, false)
        return binding.root
    }

    override fun onViewCreated(
        view: View,
        savedInstanceState: Bundle?,
    ) {
        super.onViewCreated(view, savedInstanceState)

        binding.toolbar.setNavigationOnClickListener { findNavController().navigateUp() }

        binding.navigationSwitch.setOnCheckedChangeListener { switchView, isChecked ->
            if (switchView.isPressed) viewModel.setNavigationEnabled(isChecked)
        }
        viewModel.navigationEnabled.observe(viewLifecycleOwner) { enabled ->
            if (binding.navigationSwitch.isChecked != enabled) binding.navigationSwitch.isChecked = enabled
        }
        viewModel.navigationState.observe(viewLifecycleOwner) { state ->
            binding.navigationStatusText.text =
                getString(R.string.navigation_settings_status_format, describeNavigationState(requireContext(), state))
        }
    }

    override fun onDestroyView() {
        super.onDestroyView()
        _binding = null
    }
}
