package org.cgutman.usbip.config;

import org.cgutman.usbip.service.UsbIpService;
import org.cgutman.usbipserverforandroid.R;

import android.Manifest;
import android.app.ActivityManager;
import android.app.ActivityManager.RunningServiceInfo;
import android.content.Context;
import android.content.BroadcastReceiver;
import android.content.IntentFilter;
import android.content.Intent;
import android.content.pm.PackageManager;
import android.os.Build;
import android.os.Bundle;
import android.view.View;
import android.view.View.OnClickListener;
import android.widget.Button;
import android.widget.TextView;
import android.hardware.usb.UsbManager;

import androidx.activity.ComponentActivity;
import androidx.activity.result.ActivityResultLauncher;
import androidx.activity.result.contract.ActivityResultContracts;
import androidx.core.content.ContextCompat;

import android.hardware.usb.UsbDevice;
import android.widget.LinearLayout;
import android.view.LayoutInflater;

public class UsbIpConfig extends ComponentActivity {
        private Button serviceButton;
        private TextView serviceStatus;
        private TextView serviceReadyText;
        private LinearLayout deviceListContainer;
        private UsbManager usbManager;
	
	private boolean running;

	private ActivityResultLauncher<String> requestPermissionLauncher =
			registerForActivityResult(new ActivityResultContracts.RequestPermission(), isGranted -> {
				// We don't actually care if the permission is granted or not. We will launch the service anyway.
				ContextCompat.startForegroundService(UsbIpConfig.this, new Intent(UsbIpConfig.this, UsbIpService.class));
			});
	
	private void updateStatus() {
		if (running) {
			serviceButton.setText("Stop Service");
			serviceStatus.setText("USB/IP Service Running");
			serviceReadyText.setText(R.string.ready_text);
		}
		else {
			serviceButton.setText("Start Service");
			serviceStatus.setText("USB/IP Service Stopped");
			serviceReadyText.setText("");
		}
	}
	
	// Elegant Stack Overflow solution to querying running services
	private boolean isMyServiceRunning(Class<?> serviceClass) {
	    ActivityManager manager = (ActivityManager) getSystemService(Context.ACTIVITY_SERVICE);
	    for (RunningServiceInfo service : manager.getRunningServices(Integer.MAX_VALUE)) {
	        if (serviceClass.getName().equals(service.service.getClassName())) {
	            return true;
	        }
	    }
	    return false;
	}
	
	@Override
	protected void onCreate(Bundle savedInstanceState) {
		super.onCreate(savedInstanceState);
		setContentView(R.layout.activity_usbip_config);

                usbManager = (UsbManager) getSystemService(Context.USB_SERVICE);

		serviceButton = findViewById(R.id.serviceButton);
		serviceStatus = findViewById(R.id.serviceStatus);
		serviceReadyText = findViewById(R.id.serviceReadyText);
                deviceListContainer = findViewById(R.id.deviceListContainer);

                running = isMyServiceRunning(UsbIpService.class);

                updateStatus();
                populateDeviceList();

                serviceButton.setOnClickListener(new OnClickListener() {
                        @Override
                        public void onClick(View v) {
                                if (running) {
                                        stopService(new Intent(UsbIpConfig.this, UsbIpService.class));
                                }
                                else {
                                        if (ContextCompat.checkSelfPermission(UsbIpConfig.this, Manifest.permission.POST_NOTIFICATIONS) == PackageManager.PERMISSION_GRANTED) {
                                                ContextCompat.startForegroundService(UsbIpConfig.this, new Intent(UsbIpConfig.this, UsbIpService.class));
                                        } else {
                                                if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.TIRAMISU) {
                                                        requestPermissionLauncher.launch(Manifest.permission.POST_NOTIFICATIONS);
                                                } else {
                                                        ContextCompat.startForegroundService(UsbIpConfig.this, new Intent(UsbIpConfig.this, UsbIpService.class));
                                                }
                                        }
                                }

                                running = !running;
                                updateStatus();
                        }
                });
        }

        @Override
        protected void onDestroy() {
                super.onDestroy();
        }

        private void populateDeviceList() {
                deviceListContainer.removeAllViews();
                if (usbManager == null) return;
                
                LayoutInflater inflater = LayoutInflater.from(this);
                
                for (UsbDevice device : usbManager.getDeviceList().values()) {
                        View itemView = inflater.inflate(R.layout.device_list_item, deviceListContainer, false);
                        
                        TextView nameText = itemView.findViewById(R.id.deviceName);
                        TextView idText = itemView.findViewById(R.id.deviceIds);
                        Button shareButton = itemView.findViewById(R.id.btnShare);
                        
                        String productName = device.getProductName();
                        if (productName == null || productName.isEmpty()) {
                            productName = "Unknown Device";
                        }
                        nameText.setText(productName);
                        
                        String ids = String.format("VID: %04X  PID: %04X", device.getVendorId(), device.getProductId());
                        idText.setText(ids);
                        
                        shareButton.setText("Exposed");
                        shareButton.setEnabled(false);
                        
                        deviceListContainer.addView(itemView);
                }
        }
}
