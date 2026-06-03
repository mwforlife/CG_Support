const express = require('express');
const http = require('http');
const { Server } = require('socket.io');
const dgram = require('dgram');
const os = require('os');

const app = express();
const server = http.createServer(app);

// Configurar Socket.io con límites de carga aumentados para transmisión de frames
const io = new Server(server, {
    cors: {
        origin: "*",
        methods: ["GET", "POST"]
    },
    maxHttpBufferSize: 1e7 // 10MB para frames grandes si es necesario
});

const PORT = 3000;

// Almacenamiento en memoria para el estado de la clase
let tutorSocket = null;
const connectedStudents = new Map(); // socketId -> Datos del estudiante
const studentHistory = new Map();   // macAddress -> Datos de persistencia (ej: MAC, hostname, etc.)

// --- MÓDULO WAKE-ON-LAN (WOL) ---
function sendWakeOnLan(macAddress) {
    const cleanMac = macAddress.replace(/[^0-9a-fA-F]/g, '');
    if (cleanMac.length !== 12) {
        console.error(`[WOL] Dirección MAC inválida: ${macAddress}`);
        return false;
    }

    const packet = Buffer.alloc(102);
    packet.fill(0xff, 0, 6);

    const macBuffer = Buffer.from(cleanMac, 'hex');
    for (let i = 0; i < 16; i++) {
        macBuffer.copy(packet, 6 + (i * 6));
    }

    const socket = dgram.createSocket('udp4');
    socket.once('listening', () => {
        socket.setBroadcast(true);
    });

    socket.send(packet, 0, packet.length, 9, '255.255.255.255', (err) => {
        if (err) {
            console.error(`[WOL] Error al enviar Magic Packet a ${macAddress}:`, err);
        } else {
            console.log(`[WOL] Magic Packet enviado exitosamente a la MAC: ${macAddress}`);
        }
        socket.close();
    });
    return true;
}

// --- LOGICA DE WEBSOCKETS ---
io.on('connection', (socket) => {
    console.log(`[CONEXIÓN] Cliente conectado: ${socket.id}`);

    // 1. REGISTRO DEL TUTOR
    socket.on('tutor_join', () => {
        tutorSocket = socket;
        console.log(`[TUTOR] El tutor se ha conectado en el socket: ${socket.id}`);
        
        // Enviar lista actual de estudiantes al tutor recién conectado
        const studentsList = Array.from(connectedStudents.values());
        socket.emit('students_list', studentsList);
    });

    // 2. REGISTRO DEL ESTUDIANTE
    socket.on('student_join', (data) => {
        // data: { hostname, ip, mac, userName, isLocked, activeUrl, activeWindow }
        const studentInfo = {
            socketId: socket.id,
            hostname: data.hostname || 'Desconocido',
            ip: socket.handshake.address.replace('::ffff:', '') || data.ip || '0.0.0.0',
            mac: data.mac || '00:00:00:00:00:00',
            userName: data.userName || 'System',
            isLocked: data.isLocked || false,
            activeUrl: data.activeUrl || '',
            activeWindow: data.activeWindow || 'Pantalla de Bloqueo / Desconectado',
            screenStreaming: false
        };

        connectedStudents.set(socket.id, studentInfo);
        studentHistory.set(studentInfo.mac, { mac: studentInfo.mac, hostname: studentInfo.hostname });

        console.log(`[ESTUDIANTE] Registrado: ${studentInfo.hostname} (${studentInfo.ip}) | MAC: ${studentInfo.mac}`);

        // Notificar al tutor de la nueva conexión
        if (tutorSocket) {
            tutorSocket.emit('student_connected', studentInfo);
        }
    });

    // 3. RETRANSMISIÓN DE FRAMES DE PANTALLA
    socket.on('screen_frame', (frameData) => {
        // frameData: Buffer binario o { imageBytes: Buffer, width, height }
        if (tutorSocket) {
            // Forward al tutor con el id del estudiante emisor
            tutorSocket.emit('screen_frame_received', {
                socketId: socket.id,
                imageBytes: frameData
            });
        }
    });

    // 4. ACTUALIZACIONES DE ESTADO (URL, Título de Ventana, Impresión)
    socket.on('status_update', (update) => {
        const student = connectedStudents.get(socket.id);
        if (student) {
            Object.assign(student, update);
            if (tutorSocket) {
                tutorSocket.emit('student_updated', student);
            }
        }
    });

    // 5. ENRUTAR EVENTOS DEL TUTOR A ESTUDIANTES
    // Comando global o específico (Bloquear, Apagar, Ejecutar CMD, etc.)
    socket.on('tutor_command', (payload) => {
        // payload: { targetSocketId, command, value }
        // targetSocketId: 'all' para broadcast, o un socketId específico
        console.log(`[COMANDO TUTOR] Para ${payload.targetSocketId}: ${payload.command} -> ${JSON.stringify(payload.value)}`);

        if (payload.targetSocketId === 'all') {
            socket.broadcast.emit('student_command', {
                command: payload.command,
                value: payload.value
            });
        } else {
            io.to(payload.targetSocketId).emit('student_command', {
                command: payload.command,
                value: payload.value
            });
        }
    });

    // Control Remoto: Mouse y Teclado
    socket.on('tutor_mouse_event', (payload) => {
        // payload: { targetSocketId, eventType, x, y, button }
        io.to(payload.targetSocketId).emit('student_mouse_event', payload);
    });

    socket.on('tutor_keyboard_event', (payload) => {
        // payload: { targetSocketId, eventType, keyCode, keyChar, modifiers }
        io.to(payload.targetSocketId).emit('student_keyboard_event', payload);
    });

    // Enviar Wake-on-LAN
    socket.on('tutor_wol_request', (payload) => {
        // payload: { macAddress }
        console.log(`[WOL REQUEST] Petición de encendido para MAC: ${payload.macAddress}`);
        sendWakeOnLan(payload.macAddress);
    });

    // 6. MANEJO DE DESCONEXIÓN
    socket.on('disconnect', () => {
        if (socket.id === tutorSocket?.id) {
            console.log('[TUTOR] El tutor se ha desconectado.');
            tutorSocket = null;
            // Detener transmisiones de pantalla de todos para ahorrar ancho de banda
            io.emit('student_command', { command: 'stop_screen_stream', value: null });
            for (let student of connectedStudents.values()) {
                student.screenStreaming = false;
            }
        } else if (connectedStudents.has(socket.id)) {
            const student = connectedStudents.get(socket.id);
            console.log(`[ESTUDIANTE] Desconectado: ${student.hostname} (${student.ip})`);
            connectedStudents.delete(socket.id);

            if (tutorSocket) {
                tutorSocket.emit('student_disconnected', { socketId: socket.id });
            }
        } else {
            console.log(`[DESCONEXIÓN] Socket no identificado cerrado: ${socket.id}`);
        }
    });
});

// Obtener IPs locales del Servidor para facilitar la conexión
function getLocalIPs() {
    const interfaces = os.networkInterfaces();
    const addresses = [];
    for (const k in interfaces) {
        for (const k2 in interfaces[k]) {
            const address = interfaces[k][k2];
            if (address.family === 'IPv4' && !address.internal) {
                addresses.push(address.address);
            }
        }
    }
    return addresses;
}

// Iniciar el servidor local
server.listen(PORT, '0.0.0.0', () => {
    console.log(`==================================================`);
    console.log(`   SERVIDOR CG_SUPPORT INICIADO EXITOSAMENTE`);
    console.log(`   Puerto de Escucha: ${PORT}`);
    console.log(`   IPs de Conexión en Red Local:`);
    getLocalIPs().forEach(ip => console.log(`   - http://${ip}:${PORT}`));
    console.log(`==================================================`);
});
