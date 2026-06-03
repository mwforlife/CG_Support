# Walkthrough de Implementación: CG_Support

Este documento describe la arquitectura final implementada para **CG_Support**, detallando los componentes nativos en C# (WPF) y el broker Node.js en segundo plano, así como las instrucciones de compilación y prueba.

---

## 🏗️ Resumen de Componentes Desarrollados

Hemos desarrollado exitosamente la arquitectura en 3 partes, adaptada 100% para Windows, segura contra cierres y autoarrancable:

### 1. Servidor / Broker de Comunicaciones (Node.js)
Ubicación: `c:\xampp\htdocs\CG_Support\Server`
* **Tecnología**: Node.js, Express y Socket.io.
* **Función**: Corre de forma invisible en segundo plano, administrando las conexiones del Tutor y los Estudiantes.
* **WOL (Wake-on-LAN)**: Construye y envía Magic Packets (UDP broadcast al puerto 9) para encender computadoras de forma física remota.

### 2. Consola del Tutor (C# - WPF)
Ubicación: `c:\xampp\htdocs\CG_Support\Client\CG_Support.Tutor`
* **Estética**: Interfaz oscura moderna basada en tonos Obsidian, con grilla dinámica de computadoras e indicadores de estado LED.
* **Control de Procesos**: Inicia silenciosamente el servidor Node.js al arrancar y mata el proceso Node.js limpiamente al cerrarse.
* **Renderizado**: Dibuja en tiempo real las capturas de pantalla de los estudiantes directamente desde flujos binarios Socket.io en componentes WPF, eliminando la necesidad de navegadores web.
* **Control Remoto**: Permite hacer doble clic en cualquier PC de la grilla para abrir una ventana nativa de control remoto interactivo (capturando clics, movimientos relativos y pulsaciones de teclado).

### 3. Cliente / Agente Estudiante (C# - Servicio + Agente WPF)
Ubicación: `c:\xampp\htdocs\CG_Support\Client\CG_Support.Service` y `CG_Support.Agent`
* **`StudentService` (Servicio de Windows)**:
  * Inmune a intentos de cierre por parte del estudiante desde el Administrador de Tareas (ejecutándose bajo la cuenta local de máxima seguridad `SYSTEM`).
  * Conexión WebSocket persistente al broker.
  * Servidor IPC (Named Pipes) local para comunicarse con el agente de sesión del usuario.
  * **Watchdog de seguridad**: Monitorea el estado del agente y, si es cerrado, lo vuelve a levantar instantáneamente usando la API `CreateProcessAsUser` (evitando el aislamiento de Session 0).
* **`StudentAgent` (Agente de Sesión WPF)**:
  * Corre de forma invisible en la sesión de usuario activa del estudiante.
  * Captura la pantalla a alta velocidad y la envía al servicio mediante la tubería Named Pipe.
  * Intercepta la navegación web activa en Chrome, Edge y Firefox mediante **UI Automation** de Windows y bloquea URLs restringidas simulando un cierre de pestaña en menos de 500ms.
  * **Locks/Bloqueos**: Despliega una pantalla de bloqueo a pantalla completa, topmost y sin bordes.
  * **Hooks de Escape**: Utiliza un gancho de teclado de bajo nivel (`SetWindowsHookEx`) para deshabilitar atajos de escape comunes como Alt+Tab, Alt+F4, tecla Windows y Ctrl+Esc.
  * **Control Remoto Directo**: Utiliza la API nativa de Win32 `BlockInput` para congelar el mouse y teclado físico del estudiante cuando el tutor toma control, permitiendo únicamente las acciones del profesor.

---

## 📂 Archivos Creados y Modificados

### Backend (Node.js)
* 📄 [Server/package.json](file:///c:/xampp/htdocs/CG_Support/Server/package.json): Inicialización de dependencias.
* 📄 [Server/server.js](file:///c:/xampp/htdocs/CG_Support/Server/server.js): Broker Socket.io, enrutador de streams de pantalla, eventos de control y módulo Wake-on-LAN UDP.

### Consola del Tutor (C# - WPF)
* 📄 [Client/CG_Support.Tutor/StudentModel.cs](file:///c:/xampp/htdocs/Client/CG_Support.Tutor/StudentModel.cs): Datos de enlace del alumno.
* 📄 [Client/CG_Support.Tutor/Converters.cs](file:///c:/xampp/htdocs/Client/CG_Support.Tutor/Converters.cs): Convertidores XAML (Null/Bool a Visibilidad).
* 📄 [Client/CG_Support.Tutor/MainWindow.xaml](file:///c:/xampp/htdocs/Client/CG_Support.Tutor/MainWindow.xaml): Interfaz de grilla oscura.
* 📄 [Client/CG_Support.Tutor/MainWindow.xaml.cs](file:///c:/xampp/htdocs/Client/CG_Support.Tutor/MainWindow.xaml.cs): Lanzador del servidor Node, conexión WebSocket, handlers de eventos.
* 📄 [Client/CG_Support.Tutor/RemoteControlWindow.xaml](file:///c:/xampp/htdocs/Client/CG_Support.Tutor/RemoteControlWindow.xaml): Ventana de monitorización individual.
* 📄 [Client/CG_Support.Tutor/RemoteControlWindow.xaml.cs](file:///c:/xampp/htdocs/Client/CG_Support.Tutor/RemoteControlWindow.xaml.cs): Mapeo de coordenadas relativas de ratón y teclado a Socket.io.

### Agente Estudiante (C#)
* 📄 [Client/CG_Support.Service/Win32Helper.cs](file:///c:/xampp/htdocs/CG_Support/Client/CG_Support.Service/Win32Helper.cs): Query de tokens de sesión activa y llamadas a procesos de usuario desde Session 0.
* 📄 [Client/CG_Support.Service/Worker.cs](file:///c:/xampp/htdocs/CG_Support/Client/CG_Support.Service/Worker.cs): Conexión principal del socket, servidor de pipe IPC, Watchdog.
* 📄 [Client/CG_Support.Agent/KeyboardHook.cs](file:///c:/xampp/htdocs/CG_Support/Client/CG_Support.Agent/KeyboardHook.cs): Gancho global de bajo nivel para bloquear Alt+Tab, Alt+F4 y tecla de Windows.
* 📄 [Client/CG_Support.Agent/LockWindow.xaml](file:///c:/xampp/htdocs/CG_Support/Client/CG_Support.Agent/LockWindow.xaml) y [LockWindow.xaml.cs](file:///c:/xampp/htdocs/CG_Support/Client/CG_Support.Agent/LockWindow.xaml.cs): Interfaz de bloqueo y bloqueo de eventos.
* 📄 [Client/CG_Support.Agent/InputSimulator.cs](file:///c:/xampp/htdocs/CG_Support/Client/CG_Support.Agent/InputSimulator.cs): Llamadas a SendInput y BlockInput de Windows.
* 📄 [Client/CG_Support.Agent/ScreenCapturer.cs](file:///c:/xampp/htdocs/CG_Support/Client/CG_Support.Agent/ScreenCapturer.cs): Capturador GDI+ y compresión JPEG.
* 📄 [Client/CG_Support.Agent/BrowserMonitor.cs](file:///c:/xampp/htdocs/CG_Support/Client/CG_Support.Agent/BrowserMonitor.cs): Lector UI Automation de URLs del navegador.
* 📄 [Client/CG_Support.Agent/App.xaml](file:///c:/xampp/htdocs/CG_Support/Client/CG_Support.Agent/App.xaml) y [App.xaml.cs](file:///c:/xampp/htdocs/CG_Support/Client/CG_Support.Agent/App.xaml.cs): Conexión al pipe local, bucles de streaming de pantalla y monitoreo de URL/Ventana.

---

## 🛠️ Guía de Compilación e Instalación

### Compilación (Visual Studio / dotnet CLI)
1. Sitúate en el directorio de la solución: `c:\xampp\htdocs\CG_Support\Client`
2. Compila la solución entera en modo Release:
   ```bash
   dotnet build -c Release CG_Support.sln
   ```

### Despliegue en el Computador del Estudiante
1. Copia la carpeta compilada del servicio y del agente al PC destino (ej. `C:\Program Files\CG_Support`).
2. Abre CMD como Administrador e instala el servicio del sistema:
   ```cmd
   sc.exe create CG_SupportService binPath= "C:\Program Files\CG_Support\CG_Support.Service.exe" start= auto
   sc.exe start CG_SupportService
   ```
3. Registra el agente `CG_Support.Agent.exe` para ejecutarse en el perfil de usuario (puedes meter un acceso directo en `Inicio` / `Startup` o usar la clave de registro `Run`).

Una vez levantado, el servicio detectará el inicio de sesión del alumno, cargará el agente visual de manera invisible, y la PC del estudiante se conectará al Tutor al instante de forma permanente.
