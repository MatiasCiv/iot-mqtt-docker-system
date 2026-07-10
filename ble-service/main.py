import os
import time
import sys
import signal
import threading
import paho.mqtt.client as mqtt
import serial

# Configuración
MQTT_BROKER = "mqtt"
MQTT_PORT = 1883
TOPIC_COMMANDS = "ble/commands"
TOPIC_STATUS = "ble/status"

# Variables de control global y concurrencia
running = True
conectado_hw = False
ser = None
lock_serial = threading.Lock()

def descolgar_servicio(signum, frame):
    """Maneja el apagado limpio solicitado por Docker (SIGTERM / SIGINT)."""
    global running, ser
    print("\n🛑 Señal de parada recibida de Docker. Cerrando conexiones...")
    running = False
    try:
        if ser and ser.is_open:
            ser.close()
    except:
        pass
    sys.exit(0)

# Registrar las señales del sistema operativo
signal.signal(signal.SIGTERM, descolgar_servicio)
signal.signal(signal.SIGINT, descolgar_servicio)

def conectar_dispositivo():
    """Intenta abrir el puerto serial de forma segura sin bloquear el hilo indefinidamente."""
    global ser, conectado_hw
    with lock_serial:
        try:
            if not os.path.exists("/dev/rfcomm0"):
                return False
            
            # Configuración limpia del objeto Serial sin abrirlo inmediatamente
            if ser is None:
                ser = serial.Serial()
                ser.port = "/dev/rfcomm0"
                ser.baudrate = 9600
                ser.timeout = 1
                ser.write_timeout = 1

            if not ser.is_open:
                ser.open()
                ser.reset_input_buffer()
                ser.reset_output_buffer()
                # Test de comunicación inicial
                ser.write(b"\n")
                ser.flush()
                conectado_hw = True
                print("✅ Puerto RFComm completamente operativo.")
                return True
        except (serial.SerialException, OSError) as e:
            print(f"⏳ Puerto detectado pero no está listo (Arduino desconectado/fuera de rango): {e}")
            conectado_hw = False
            if ser and ser.is_open:
                ser.close()
        except Exception as e:
            print(f"❌ Error inesperado al abrir puerto: {e}")
            conectado_hw = False
        return False

def enviar_bluetooth(mensaje):
    """Envía datos al puerto serial de forma segura garantizando la exclusión mutua."""
    global conectado_hw
    if not conectado_hw:
        print("⚠️ Ignorando comando: El dispositivo Bluetooth no está conectado.")
        return

    with lock_serial:
        try:
            print(f"📡 [BLE] Enviando a Arduino: {mensaje}")
            ser.write((mensaje + "\n").encode('utf-8'))
            ser.flush()
        except Exception as e:
            print(f"❌ Fallo al enviar por Bluetooth: {e}")
            conectado_hw = False

# --- CALLBACKS MQTT ---
def on_connect(client, userdata, flags, rc):
    if rc == 0:
        print("✅ Conectado con éxito al Broker MQTT")
        client.subscribe(TOPIC_COMMANDS)
    else:
        print(f"❌ Fallo de conexión a MQTT. Código de retorno: {rc}")

def on_message(client, userdata, msg):
    try:
        payload = msg.payload.decode('utf-8')
        print(f"📥 Comando recibido desde MQTT: {payload}")
        enviar_bluetooth(payload)
    except Exception as e:
        print(f"❌ Error al procesar mensaje MQTT: {e}")

def hilo_lectura_serial(client):
    """Hilo secundario dedicado exclusivamente a recibir datos del Arduino."""
    global conectado_hw, running
    print("🧵 Hilo de lectura serial iniciado.")
    
    while running:
        if not conectado_hw:
            conectar_dispositivo()
            if not conectado_hw:
                time.sleep(4) # Espera prudencial antes del siguiente intento
                continue

        try:
            # Comprobación de datos en espera de forma no bloqueante
            if ser and ser.is_open and ser.in_waiting > 0:
                linea = ser.readline().decode('utf-8', errors='ignore').strip()
                if linea:
                    print(f"📥 Datos recibidos de Arduino: {linea}")
                    if client.is_connected():
                        client.publish(TOPIC_STATUS, linea, qos=1)
        except (serial.SerialException, OSError) as e:
            print(f"🔌 Desconexión del dispositivo detectada durante la lectura: {e}")
            conectado_hw = False
            with lock_serial:
                try:
                    ser.close()
                except:
                    pass
        except Exception as e:
            print(f"⚠️ Error inesperado en el hilo serial: {e}")
            time.sleep(1)
            
        time.sleep(0.1) # Evita la saturación de la CPU

# --- FLUJO PRINCIPAL ---
def main():
    print("🚀 Iniciando ble-service en modo producción...")
    
    # Inicialización del cliente MQTT
    client = mqtt.Client(client_id="ble-service-client")
    client.on_connect = on_connect
    client.on_message = on_message

    # Intento de conexión inicial al broker con reintentos automáticos
    mqtt_conectado = False
    while not mqtt_conectado and running:
        try:
            client.connect(MQTT_BROKER, MQTT_PORT, keepalive=30)
            mqtt_conectado = True
        except Exception:
            print("⏳ Esperando al Broker MQTT...")
            time.sleep(2)

    # Iniciar procesamiento de la red MQTT en un hilo secundario gestionado por Paho
    client.loop_start()

    # Lanzar el hilo encargado de la comunicación por puerto serial
    worker = threading.Thread(target=hilo_lectura_serial, args=(client,), daemon=True)
    worker.start()

    # Bucle de monitorización del hilo principal
    while running:
        time.sleep(1)

if __name__ == "__main__":
    try:
        main()
    except Exception as e:
        print(f"💥 Error crítico no controlado en la aplicación: {e}")
        sys.exit(1) # Permite a Docker identificar el fallo y reiniciar el contenedor