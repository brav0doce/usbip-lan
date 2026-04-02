import sys

with open('android-server/app/src/main/java/org/cgutman/usbip/config/UsbIpConfig.java', 'r') as f:
    content = f.read()

import re

ip_func = """
        private String getIpAddresses() {
                StringBuilder builder = new StringBuilder();
                try {
                        java.util.List<java.net.NetworkInterface> interfaces = java.util.Collections.list(java.net.NetworkInterface.getNetworkInterfaces());
                        for (java.net.NetworkInterface intf : interfaces) {
                                if (intf.getName().startsWith("lo")) continue;
                                java.util.List<java.net.InetAddress> addrs = java.util.Collections.list(intf.getInetAddresses());
                                for (java.net.InetAddress addr : addrs) {
                                        if (!addr.isLoopbackAddress() && addr instanceof java.net.Inet4Address) {
                                                builder.append(addr.getHostAddress()).append(" (").append(intf.getName()).append(")\\n");
                                        }
                                }
                        }
                } catch (Exception ex) {
                        return "Error";
                }
                if (builder.length() == 0) return "No IP";
                return builder.toString();
        }

        private void updateStatus() {
"""

content = content.replace("        private void updateStatus() {", ip_func)

old_status = """                        serviceReadyText.setText(R.string.ready_text);
                }
                else {"""

new_status = """                        serviceReadyText.setText("Servidor escuchando en el puerto 3240.\\n\\nSi usas Punto de Acceso (Tethering), el descubrimiento automático no funciona en Android.\\nAñade manualmente esta IP en el cliente de PC:\\n" + getIpAddresses());
                }
                else {"""

content = content.replace(old_status, new_status)

with open('android-server/app/src/main/java/org/cgutman/usbip/config/UsbIpConfig.java', 'w') as f:
    f.write(content)
