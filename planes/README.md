# Documentación y Manual de Arquitectura - CG_Support

Este directorio contiene los planes de diseño, diagramas de arquitectura, especificaciones técnicas y guías de uso de **CG_Support**, un sistema de control y monitoreo de laboratorios de computación en red local.

## 📂 Archivos en esta Carpeta

1. **[implementation_plan.md](file:///c:/xampp/htdocs/CG_Support/planes/implementation_plan.md)**: El plan de desarrollo detallado con la arquitectura híbrida (Node.js + Windows Service + WPF Agent C#).
2. **[README.md](file:///c:/xampp/htdocs/CG_Support/planes/README.md)**: Este documento de instrucciones y manual de inicio rápido.

---

## 🛠️ Arquitectura de Seguridad del Sistema

### 1. Inmunidad contra el Cierre de Procesos (Anti-Task Manager)
Para evitar que los estudiantes desactiven el software desde el Administrador de Tareas:
* **Servicio Permanente (`StudentService`)**: Funciona en segundo plano con permisos `LocalSystem`. Windows deniega los intentos de terminación por parte de usuarios estándar.
* **Watchdog de Doble Vía**: Si el proceso visual `StudentAgent` es terminado, el Servicio de Windows lo detecta e inicia inmediatamente una nueva instancia.

### 2. Bloqueo de Periféricos (BlockInput)
Durante el control remoto, el sistema bloquea los periféricos locales del estudiante mediante llamadas a la API nativa de Windows `BlockInput` para asegurar que el Tutor tenga control absoluto sin interferencias.

### 3. Encendido Remoto (Wake-on-LAN)
El servidor Node.js envía "Magic Packets" UDP a través del puerto 9 para despertar y encender los equipos del laboratorio que soporten WOL.

---

## 🚀 Manual de Inicio Rápido (Despliegue)

### Paso 1: Servidor Node.js
El servidor coordina toda la red local.
1. Instalar dependencias del servidor en `c:\xampp\htdocs\CG_Support`:
   ```bash
   npm install
   ```
2. Ejecutar el servidor de desarrollo:
   ```bash
   npm run dev
   ```

### Paso 2: Compilar el Cliente C# (Visual Studio)
El cliente consta de dos proyectos:
1. Abrir la solución `CG_Support_Client.sln` en **Visual Studio**.
2. Compilar en modo `Release`.
3. Instalar el servicio de Windows ejecutando en una consola de Administrador:
   ```cmd
   sc.exe create CG_SupportService binPath= "C:\Ruta\Al\Servicio\CG_Support.Service.exe" start= auto
   sc.exe start CG_SupportService
   ```
4. Configurar el agente visual `CG_Support.Agent.exe` (WPF) para iniciarse con el perfil de usuario (por ejemplo, agregando un acceso directo en la carpeta de `Inicio` o en el registro `HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Run`).
