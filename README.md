# USB/IP LAN

**Comparte dispositivos USB conectados a tu Android por la red Wi-Fi → conectalos en Windows como si estuvieran físicamente enchufados.**

```
  [Dispositivo USB] ──── [Android] ────── Wi-Fi LAN ──────── [Windows 10/11]
       teclado/ratón                                             apk servidor
       pendrive                                                  exe cliente
       Arduino / DAQ
       etc.
```

> **Servidor**: App Android que detecta los USB, los publica en la red LAN y los envía al cliente con mínima latencia imitando una conexión física.
> **Cliente**: Aplicación WPF para Windows que hace una búsqueda automática de servidores en la red y presenta los dispositivos listos para conectar con un solo clic. Utiliza el driver de kernel [usbip-win2](https://github.com/vadimgrn/usbip-win2) para la integración en el sistema.

---

## Índice

1. [Requisitos](#requisitos)
2. [Instalación rápida (5 minutos)](#instalación-rápida)
3. [Uso paso a paso](#uso-paso-a-paso)
4. [Arquitectura técnica](#arquitectura-técnica)
5. [Compilar desde el código fuente](#compilar-desde-el-código-fuente)
6. [Solución de problemas](#solución-de-problemas)
7. [Preguntas frecuentes](#preguntas-frecuentes)
8. [Contribuir](#contribuir)
9. [Licencia](#licencia)

---

## Requisitos

### Android (servidor)

| Requisito | Mínimo |
|-----------|--------|
| Android   | 8.0 Oreo (API 26) |
| USB Host  | Sí (casi todos los teléfonos/tablets modernos) |
| Wi-Fi     | Misma red que el PC |
| Permisos  | USB, Red, Notificaciones |

### Windows (cliente)

| Requisito | Mínimo |
|-----------|--------|
| Windows   | 10 versión 1903 (Build 18362) |
| .NET      | 6.0 Runtime |
| Driver    | usbip-win2 (se instala automáticamente) |
| Wi-Fi/LAN | Misma red que el Android |
| Privilegios | Administrador (solo para instalar el driver) |

---

## Instalación rápida

### Opción A – Usar los builds compilados (recomendado)

#### 1. Android – Instalar el APK del servidor

1. Descarga `USBIPServer.apk` de la sección [Releases](../../releases/latest).
2. En el Android, activa **"Instalar apps de fuentes desconocidas"**:
   - *Ajustes → Seguridad → Instalar apps desconocidas* (varía según fabricante).
3. Copia el APK al teléfono (cable USB, Bluetooth, WhatsApp…).
4. Toca el archivo `.apk` y acepta la instalación.
5. Concede los permisos que pida la app (USB, Red).

#### 2. Windows – Instalar el cliente

1. Descarga `USBIPClient.exe` y `Install.ps1` de la sección [Releases](../../releases/latest).
2. Coloca ambos archivos en la misma carpeta.
3. Haz clic derecho sobre `Install.ps1` → **"Ejecutar con PowerShell"**.
   - Se pedirá elevación UAC → acepta.
   - El script descarga e instala automáticamente el driver usbip-win2.
   - Crea un acceso directo en el escritorio.
4. ¡Listo! Usa el acceso directo **"USB-IP LAN Client"** del escritorio.

> **Sin conexión a Internet**: Descarga manualmente el ZIP de [usbip-win2 Releases](https://github.com/vadimgrn/usbip-win2/releases) y extrae `usbip.exe`, `*.sys`, `*.inf` y `*.cat` en la misma carpeta que `USBIPClient.exe` antes de ejecutar `Install.ps1`.

---

## Uso paso a paso

### En el Android

1. Conecta los dispositivos USB al teléfono (usa un hub OTG si tienes varios).
2. Abre la app **USB/IP Server**.
3. La app detecta automáticamente todos los USB conectados y los muestra en la lista.
4. Activa el interruptor **"Servidor"** (parte superior).
   - El servidor arranca en el puerto `3240`.
   - La app se anuncia en la red vía **mDNS** (protocolo DNS-SD) para ser detectada automáticamente.
   - Se muestra la IP local del teléfono, p.ej. `192.168.1.50`.
5. Usa el toggle de cada dispositivo para decidir cuáles compartir.
   - Por defecto todos están activados.
   - También puedes usar **"Compartir todos"** / **"Dejar de compartir todos"**.
6. La app puede ir al fondo; el servidor sigue corriendo como servicio.

```
┌─────────────────────────────┐
│  USB/IP Server              │
│  ● Servidor activo  ◐ ──── │
│  IP: 192.168.1.50           │
│  Clientes: 1                │
│─────────────────────────────│
│  Dispositivos USB           │
│  🔌 Arduino Uno     [ ON ]  │
│     VID:2341 PID:0043       │
│  🔌 Kingston USB3   [ ON ]  │
│     VID:0951 PID:1666       │
└─────────────────────────────┘
```

### En Windows

1. Abre **"USB-IP LAN Client"** (acceso directo del escritorio o `USBIPClient.exe`).
2. La aplicación busca automáticamente servidores en la red local vía mDNS.
   - Si el servidor Android aparece en la lista izquierda, selecciónalo.
   - Si no aparece: pulsa **"Buscar Servidores"** (escaneo de subred completo).
   - Para añadir un servidor manualmente: escribe la IP del Android y pulsa **➕**.
3. Al seleccionar un servidor, la lista de dispositivos se carga automáticamente.
4. Selecciona el dispositivo que quieres usar y pulsa **"▶ Conectar Dispositivo"**.
   - Windows instala el dispositivo como si estuviera físicamente conectado.
   - Aparecerá en el **Administrador de dispositivos** de Windows.
5. Usa el dispositivo normalmente (teclado, ratón, pendrive, Arduino IDE, etc.).
6. Para desconectar: selecciona el dispositivo y pulsa **"⏹ Desconectar"**.

```
┌────────────────────────────────────────────────────────────────────────┐
│  🔌 USB/IP LAN Client                                       v1.0       │
│────────────────────────────────────────────────────────────────────────│
│  SERVIDORES               │  DISPOSITIVOS USB DISPONIBLES              │
│  ─────────────────        │  Bus-ID  VID:PID  Clase   Velocidad Estado │
│  📱 Pixel-7               │  1-1     2341:0043  CDC   HS(480)  Disponible│
│     192.168.1.50          │  1-2     0951:1666  Mass  HS(480)  Conectado │
│     2 dispositivos        │                                             │
│                           │                                             │
│  [+ 192.168.1.___ ] [➕]  │  [ ▶ Conectar ]  [ ⏹ Desconectar ]        │
└────────────────────────────────────────────────────────────────────────┘
│  Servidor encontrado: Pixel-7 (192.168.1.50:3240)                      │
└────────────────────────────────────────────────────────────────────────┘
```

---

## Arquitectura técnica

### Protocolo USB/IP

El protocolo estándar **USB/IP** (puerto TCP **3240**, asignado por IANA) virtualiza los dispositivos USB sobre TCP/IP.

```
Android Server                             Windows Client
──────────────                             ──────────────
TCP :3240
   ← OP_REQ_DEVLIST ─────────────────────────────────────
   → OP_REP_DEVLIST (lista de dispositivos) ─────────────
   ← OP_REQ_IMPORT (busId "1-1") ──────────────────────
   → OP_REP_IMPORT (OK + descriptor) ─────────────────
   ← USBIP_CMD_SUBMIT (URB) ───────────────────────────
   → USBIP_RET_SUBMIT (datos) ─────────────────────────
```

### Descubrimiento automático (mDNS / DNS-SD)

- **Android**: Usa la API `NsdManager` de Android para anunciarse como `_usbip._tcp` en la red local.
- **Windows**: Usa la librería `Makaretu.Dns.Multicast` para escuchar los anuncios mDNS.
- **Fallback**: Si mDNS no funciona (redes que bloquean multicast), el cliente hace un escaneo TCP del segmento `/24` en paralelo.

### Componentes Android

| Archivo | Descripción |
|---------|-------------|
| `MainActivity.kt` | UI principal: lista de USB, toggle servidor, IP, clientes |
| `UsbIpService.kt` | Servicio foreground que mantiene el servidor activo en background |
| `UsbIpServer.kt` | Servidor TCP en puerto 3240, implementa protocolo USB/IP |
| `UsbDeviceManager.kt` | Enumera USB via Android USB Host API, gestiona permisos |
| `MdnsAdvertiser.kt` | Anuncia el servidor en la red LAN via DNS-SD |
| `UsbIpProtocol.kt` | Constantes y estructuras del protocolo USB/IP |
| `UsbDeviceAdapter.kt` | Adapter RecyclerView para la lista de dispositivos |

### Componentes Windows

| Archivo | Descripción |
|---------|-------------|
| `MainWindow.xaml(.cs)` | Interfaz WPF: lista de servidores y dispositivos |
| `UsbIpClient.cs` | Protocolo USB/IP cliente (OP_REQ_DEVLIST, OP_REQ_IMPORT) |
| `MdnsDiscovery.cs` | Descubrimiento automático mDNS + escaneo de subred |
| `Models/UsbDevice.cs` | Modelos de datos: UsbDevice, UsbIpServer |
| `Install.ps1` | Script de instalación automática (driver + EXE) |

### Driver de kernel (usbip-win2)

El cliente Windows delega el binding de nivel kernel al proyecto **[usbip-win2](https://github.com/vadimgrn/usbip-win2)** de Vadim Grn. Este driver:
- Es un fork/reimplementación moderna del cliente USBIP original para Windows.
- Soporta Windows 10/11 con firma de driver actualizada.
- `usbip.exe attach` conecta el URB forwarder del kernel al servidor TCP.

---

## Compilar desde el código fuente

### APK Android

**Requisitos**: Android Studio 2023.x o superior / JDK 17 / Android SDK API 34.

```bash
# Clonar el repositorio
git clone https://github.com/brav0doce/usbip-lan.git
cd usbip-lan

# Compilar APK de debug
cd android-server
./gradlew assembleDebug

# El APK estará en:
# android-server/app/build/outputs/apk/debug/app-debug.apk

# Instalar directamente en un dispositivo conectado por USB:
adb install -r app/build/outputs/apk/debug/app-debug.apk
```

Para APK de release con firma propia:
```bash
./gradlew assembleRelease
# Luego firma con jarsigner o Android Studio
```

### EXE Windows

**Requisitos**: [.NET 6 SDK](https://dotnet.microsoft.com/download/dotnet/6.0) / Windows 10+.

```powershell
# Desde la raíz del repositorio
cd windows-client

# Restaurar paquetes NuGet
dotnet restore

# Compilar en modo Release → genera USBIPClient.exe en dist\windows\
dotnet publish UsbIpClient.csproj `
    --configuration Release `
    --runtime win-x64 `
    --self-contained false `
    --output ..\dist\windows `
    /p:PublishSingleFile=true
```

O usa el script de un paso:
```powershell
.\build-windows.ps1
```

---

## Solución de problemas

### El servidor Android no aparece en la lista del cliente Windows

1. Verifica que Android y Windows están en la **misma red Wi-Fi** (mismo router/SSID).
2. Comprueba que el servidor Android está **activo** (indicador verde + mensaje "Servidor activo en puerto 3240").
3. Algunos routers bloquean el tráfico multicast. Usa el botón **"Buscar Servidores"** para el escaneo TCP alternativo.
4. Comprueba que el **firewall de Windows** no bloquea el puerto 3240:
   ```powershell
   netsh advfirewall firewall add rule name="USB/IP Client" dir=in action=allow protocol=TCP localport=3240
   ```
5. Añade el servidor manualmente con su IP (campo "Añadir manualmente" en la app).

### Error "usbip.exe not found"

El driver usbip-win2 no está instalado. Soluciones:
- Ejecuta `Install.ps1` como administrador.
- O descarga manualmente el ZIP de [usbip-win2 Releases](https://github.com/vadimgrn/usbip-win2/releases) y extrae `usbip.exe` en la misma carpeta que `USBIPClient.exe`.

### El dispositivo se conecta pero Windows no lo reconoce

1. Abre el **Administrador de dispositivos** (`devmgmt.msc`).
2. Busca el dispositivo en "Otros dispositivos" o "Controladores de bus serie universal".
3. Si aparece con ! amarillo, intenta actualizar los controladores.
4. Para HID (teclado/ratón): suelen funcionar automáticamente.
5. Para dispositivos especializados (Arduino, DAQ, etc.): instala el driver específico del fabricante.

### La app Android se cierra al mover al segundo plano

- La app usa un **servicio foreground** para permanecer activa. Si el sistema la termina por falta de memoria:
  - Ve a *Ajustes → Batería → USB/IP Server → Sin restricciones*.
  - En Xiaomi/Huawei: desactiva la "optimización de batería" para la app.

### Latencia alta en la transferencia

- Usa **Wi-Fi 5GHz** en lugar de 2.4GHz para menor latencia.
- Sitúa el teléfono cerca del router.
- Evita el uso de VPN durante la sesión USB/IP.
- Para dispositivos que requieren baja latencia (ratón, teclado de gaming): considera usar un cable USB directamente.

### Error de permisos USB en Android

La primera vez que conectas un USB nuevo, Android pide permiso. Si rechazaste:
1. Desconecta y vuelve a conectar el dispositivo USB.
2. Pulsa **"Actualizar"** en la app.
3. Acepta el cuadro de diálogo de permisos.

---

## Preguntas frecuentes

**¿Qué tipos de dispositivos USB funcionan?**
Funcionan todos los dispositivos que Android puede acceder mediante USB Host:
- Teclados, ratones, gamepads (HID)
- Memorias USB, discos duros (Mass Storage) ⚠️ limitado en Android
- Arduinos, microcontroladores (CDC/Serial)
- Cámaras, escáneres (UVC, PTP)
- Impresoras
- Dongles Bluetooth/Wi-Fi (USB)

**¿Necesito root en el Android?**
No. La app usa la API estándar USB Host de Android (disponible sin root desde Android 3.1).

**¿Cuántos dispositivos puedo compartir a la vez?**
Técnicamente no hay límite. En la práctica depende del ancho de banda Wi-Fi. Con Wi-Fi 5GHz (802.11ac) puedes compartir 3–5 dispositivos cómodamente.

**¿Funciona con hotspot (punto de acceso) del propio Android?**
Sí, siempre que el Windows se conecte al hotspot del Android. La IP del servidor será la del gateway del hotspot (normalmente `192.168.43.1`).

**¿Es seguro? ¿Pueden acceder otros a mis USB?**
El servidor acepta conexiones de cualquier IP en la red local. Para uso doméstico es suficientemente seguro. En redes corporativas o públicas, considera usar una **red privada** o un **firewall** que limite el acceso al puerto 3240.

---

## Contribuir

Las contribuciones son bienvenidas:

1. Fork el repositorio.
2. Crea una rama: `git checkout -b feature/mi-mejora`.
3. Realiza los cambios y asegúrate de que compila.
4. Abre un Pull Request describiendo el cambio.

**Áreas donde se necesita ayuda:**
- 🔐 Autenticación/cifrado TLS en el protocolo USB/IP.
- 📱 Soporte iOS (requiere jailbreak o enfoques alternativos).
- 🧪 Tests unitarios para el protocolo USB/IP.
- 🌐 Traducciones de la interfaz.

---

## Licencia

Este proyecto se distribuye bajo la licencia **MIT**. Ver [LICENSE](LICENSE) para detalles.

El componente [usbip-win2](https://github.com/vadimgrn/usbip-win2) de Vadim Grn se distribuye bajo su propia licencia (GPL-2.0). Consulta su repositorio para detalles.

---

<div align="center">
  Hecho con ❤️ para compartir USB por la red LAN de forma sencilla.<br>
  Si te es útil, dale una ⭐ al repositorio.
</div>
