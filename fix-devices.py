import sys

with open('android-server/app/src/main/java/org/cgutman/usbip/service/UsbIpService.java', 'r') as f:
    content = f.read()

import re

old_get = """\t\tfor (UsbDevice dev : usbManager.getDeviceList().values()) {
\t\t\tAttachedDeviceContext context = connections.get(dev.getDeviceId());
\t\t\tUsbDeviceConnection devConn = null;
\t\t\tif (context != null) {
\t\t\t\tdevConn = context.devConn;
\t\t\t}

\t\t\tlist.add(getInfoForDevice(dev, devConn));
\t\t}"""

new_get = """\t\tfor (UsbDevice dev : usbManager.getDeviceList().values()) {
\t\t\tAttachedDeviceContext context = connections.get(dev.getDeviceId());
\t\t\tUsbDeviceConnection devConn = null;
\t\t\tif (context != null) {
\t\t\t\tdevConn = context.devConn;
\t\t\t} else {
\t\t\t\tif (usbManager.hasPermission(dev)) {
\t\t\t\t\ttry {
\t\t\t\t\t\tdevConn = usbManager.openDevice(dev);
\t\t\t\t\t} catch (Exception e) {}
\t\t\t\t} else {
\t\t\t\t\tusbManager.requestPermission(dev, usbPermissionIntent);
\t\t\t\t}
\t\t\t}

\t\t\tlist.add(getInfoForDevice(dev, devConn));

\t\t\tif (context == null && devConn != null) {
\t\t\t\ttry {
\t\t\t\t\tdevConn.close();
\t\t\t\t} catch (Exception e) {}
\t\t\t}
\t\t}"""

content = content.replace(old_get, new_get)

with open('android-server/app/src/main/java/org/cgutman/usbip/service/UsbIpService.java', 'w') as f:
    f.write(content)
