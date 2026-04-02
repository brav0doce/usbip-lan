import sys

with open('android-server/app/src/main/java/org/cgutman/usbip/service/UsbIpService.java', 'r') as f:
    content = f.read()

# Edit 1: Receiver Attachment block
import re
new_receiver = """
                                synchronized (dev) {
                                        permission.put(dev.getDeviceId(), intent.getBooleanExtra(UsbManager.EXTRA_PERMISSION_GRANTED, false));
                                        dev.notifyAll();
                                }
                        } else if (UsbManager.ACTION_USB_DEVICE_ATTACHED.equals(action)) {
                                UsbDevice dev = (UsbDevice)intent.getParcelableExtra(UsbManager.EXTRA_DEVICE);
                                if (dev != null && !usbManager.hasPermission(dev)) {
                                        usbManager.requestPermission(dev, usbPermissionIntent);
                                }
                        }
                }
        };"""

content = re.sub(
    r'(\s+)synchronized \(dev\) \{\s+permission.put\(dev.getDeviceId\(\), intent.getBooleanExtra\(UsbManager.EXTRA_PERMISSION_GRANTED, false\)\);\s+dev.notifyAll\(\);\s+\}\s+\}\s+\}\s+};',
    new_receiver, content)

# Edit 2: Register filter
content = content.replace(
    'IntentFilter filter = new IntentFilter(ACTION_USB_PERMISSION);',
    'IntentFilter filter = new IntentFilter();\n\t\tfilter.addAction(ACTION_USB_PERMISSION);\n\t\tfilter.addAction(UsbManager.ACTION_USB_DEVICE_ATTACHED);'
)

# Edit 3: Auto-claim all devices at startup
content = content.replace(
    '\t\tupdateNotification();\n\t}',
    '\t\tupdateNotification();\n\n\t\tfor (UsbDevice dev : usbManager.getDeviceList().values()) {\n\t\t\tif (!usbManager.hasPermission(dev)) {\n\t\t\t\tusbManager.requestPermission(dev, usbPermissionIntent);\n\t\t\t}\n\t\t}\n\t}'
)

# Edit 4: Fix bcdDevice descriptor
content = content.replace(
    'ipDev.bcdDevice = -1;',
    'ipDev.bcdDevice = 0x0200;'
)

# Edit 5: Get device desc
old_desc = """\t\tAttachedDeviceContext context = connections.get(dev.getDeviceId());
\t\tUsbDeviceDescriptor devDesc = null;
\t\tif (context != null) {
\t\t\t// Since we're attached already, we can directly query the USB descriptors
\t\t\t// to fill some information that Android's USB API doesn't expose
\t\t\tdevDesc = UsbControlHelper.readDeviceDescriptor(context.devConn);
\t\t\tif (devDesc != null) {
\t\t\t\tipDev.bcdDevice = devDesc.bcdDevice;
\t\t\t}
\t\t}"""

new_desc = """\t\tAttachedDeviceContext context = connections.get(dev.getDeviceId());
\t\tUsbDeviceDescriptor devDesc = null;
\t\tif (devConn != null) {
\t\t\tdevDesc = UsbControlHelper.readDeviceDescriptor(devConn);
\t\t\tif (devDesc != null) {
\t\t\t\tipDev.bDeviceClass = devDesc.bDeviceClass;
\t\t\t\tipDev.bDeviceSubClass = devDesc.bDeviceSubClass;
\t\t\t\tipDev.bDeviceProtocol = devDesc.bDeviceProtocol;
\t\t\t\tipDev.bcdDevice = devDesc.bcdDevice;
\t\t\t}
\t\t}"""
content = content.replace(old_desc, new_desc)

# Edit 6: getDevices logic
old_get_dev = """\t\t\tAttachedDeviceContext context = connections.get(dev.getDeviceId());
\t\t\tUsbDeviceConnection devConn = null;
\t\t\tif (context != null) {
\t\t\t\tdevConn = context.devConn;
\t\t\t}

\t\t\tlist.add(getInfoForDevice(dev, devConn));"""

new_get_dev = """\t\t\tAttachedDeviceContext context = connections.get(dev.getDeviceId());
\t\t\tUsbDeviceConnection devConn = null;
\t\t\tif (context != null) {
\t\t\t\tdevConn = context.devConn;
\t\t\t} else {
\t\t\t\tif (!usbManager.hasPermission(dev)) {
\t\t\t\t\tusbManager.requestPermission(dev, usbPermissionIntent);
\t\t\t\t} else {
\t\t\t\t\ttry {
\t\t\t\t\t\tdevConn = usbManager.openDevice(dev);
\t\t\t\t\t} catch (Exception e) {}
\t\t\t\t}
\t\t\t}

\t\t\tlist.add(getInfoForDevice(dev, devConn));

\t\t\tif (context == null && devConn != null) {
\t\t\t\ttry {
\t\t\t\t\tdevConn.close();
\t\t\t\t} catch (Exception e) {}
\t\t\t}"""
content = content.replace(old_get_dev, new_get_dev)

with open('android-server/app/src/main/java/org/cgutman/usbip/service/UsbIpService.java', 'w') as f:
    f.write(content)
