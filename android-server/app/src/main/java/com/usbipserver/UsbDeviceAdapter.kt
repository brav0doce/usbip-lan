package com.usbipserver

import android.view.LayoutInflater
import android.view.ViewGroup
import androidx.recyclerview.widget.DiffUtil
import androidx.recyclerview.widget.ListAdapter
import androidx.recyclerview.widget.RecyclerView
import com.usbipserver.databinding.ItemUsbDeviceBinding

/**
 * RecyclerView adapter for showing USB devices and their share toggle.
 */
class UsbDeviceAdapter(
    private val onShareChanged: (deviceId: Int, shared: Boolean) -> Unit
) : ListAdapter<UsbDeviceManager.UsbDeviceInfo, UsbDeviceAdapter.ViewHolder>(DiffCallback) {

    inner class ViewHolder(private val binding: ItemUsbDeviceBinding) :
        RecyclerView.ViewHolder(binding.root) {

        fun bind(item: UsbDeviceManager.UsbDeviceInfo) {
            binding.tvDeviceName.text    = item.displayName
            binding.tvDeviceDetails.text = item.details
            binding.tvDeviceClass.text   = item.className

            // Update switch without triggering listener
            binding.switchShare.setOnCheckedChangeListener(null)
            binding.switchShare.isChecked = item.isShared
            binding.switchShare.setOnCheckedChangeListener { _, checked ->
                onShareChanged(item.device.deviceId, checked)
            }
        }
    }

    override fun onCreateViewHolder(parent: ViewGroup, viewType: Int): ViewHolder {
        val binding = ItemUsbDeviceBinding.inflate(
            LayoutInflater.from(parent.context), parent, false
        )
        return ViewHolder(binding)
    }

    override fun onBindViewHolder(holder: ViewHolder, position: Int) {
        holder.bind(getItem(position))
    }

    object DiffCallback : DiffUtil.ItemCallback<UsbDeviceManager.UsbDeviceInfo>() {
        override fun areItemsTheSame(
            old: UsbDeviceManager.UsbDeviceInfo,
            new: UsbDeviceManager.UsbDeviceInfo
        ) = old.device.deviceId == new.device.deviceId

        override fun areContentsTheSame(
            old: UsbDeviceManager.UsbDeviceInfo,
            new: UsbDeviceManager.UsbDeviceInfo
        ) = old == new
    }
}
