package co.cnewb.yammic.ui.settings

import android.os.Bundle
import android.view.LayoutInflater
import android.view.View
import android.view.ViewGroup
import androidx.fragment.app.Fragment
import androidx.fragment.app.activityViewModels
import androidx.navigation.fragment.findNavController
import co.cnewb.yammic.databinding.FragmentPairingBinding
import co.cnewb.yammic.ui.BikeViewModel

class PairingFragment : Fragment() {

    private var _binding: FragmentPairingBinding? = null
    private val binding get() = _binding!!

    private val viewModel: BikeViewModel by activityViewModels()

    // True until the fields have been pre-filled once from the current state, so later state
    // emissions (e.g. a log update) don't clobber text the user is actively editing.
    private var prefilled = false

    override fun onCreateView(
        inflater: LayoutInflater,
        container: ViewGroup?,
        savedInstanceState: Bundle?,
    ): View {
        _binding = FragmentPairingBinding.inflate(inflater, container, false)
        return binding.root
    }

    override fun onViewCreated(
        view: View,
        savedInstanceState: Bundle?,
    ) {
        super.onViewCreated(view, savedInstanceState)

        binding.toolbar.setNavigationOnClickListener { findNavController().navigateUp() }

        viewModel.connectionState.observe(viewLifecycleOwner) { state ->
            if (!prefilled) {
                binding.ccuIdInput.setText(state.ccuId)
                binding.passKeyInput.setText(state.passKey)
                binding.deviceNameInput.setText(state.deviceName)
                prefilled = true
            }
        }

        binding.saveButton.setOnClickListener {
            viewModel.updateCredentials(
                binding.ccuIdInput.text?.toString().orEmpty(),
                binding.passKeyInput.text?.toString().orEmpty(),
            )
            viewModel.updateDeviceName(binding.deviceNameInput.text?.toString().orEmpty())
            findNavController().navigateUp()
        }
    }

    override fun onDestroyView() {
        super.onDestroyView()
        _binding = null
    }
}
