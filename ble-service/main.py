import os
import time
import sys
import signal
import threading
import paho.mqtt.client as mqtt
import serial
import asyncio
import json
import logging
from bleak import BleakScanner

# Configuración
MQTT_BROKER = "mqtt"
MQTT_PORT = 1883
TOPIC_COMMANDS = "ble/commands"
TOPIC_STATUS = "ble/status"
TOPIC_DIAGNOSTICO = "ble/diagnostico"

# Variables de control global y concurrencia
running = True
conectado_hw = False
ser = None
lock_serial = threading.Lock()
client = None  # Instancia global del cliente MQTT para que la use el callback de BLE

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

# Registrar las señales del sistema operativo
signal.signal(signal.SIGTERM, descolgar_servicio)
signal.signal(signal.SIGINT, descolgar_servicio)


def callback_ble(device, advertising_data):
    """Se ejecuta cada vez que la Pi capta un paquete BLE en el aire."""
    global client
    nombre = device.name if device.name else "Dispositivo Desconocido"
    mac = device.address
    rssi = advertising_data.rssi
    
    manu_data = {}
    if advertising_data.manufacturer_data:
        for company_id, data_bytes in advertising_data.manufacturer_data.items():
            manu_data[str(company_id)] = data_bytes.hex()

    payload = {
        "mac": mac,
        "nombre": nombre,
        "rssi": rssi,
        "manufacturer_data": manu_data
    }
    
    try:
        # Corregido: Ahora usa la instancia de cliente correcta
        if client and client.is_connected():
            client.publish(TOPIC_DIAGNOSTICO, json.dumps(payload))
    except Exception as e:
        print(f"Error publicando diagnóstico BLE: {e}")

async def tarea_escaneo_ble():
    """Inicia el escáner continuo de Bleak."""
    print("📡 [BLE] Iniciando escáner BLE Pasivo...")
    scanner = BleakScanner(detection_callback=callback_ble)
    await scanner.start()
    while running:
        await asyncio.sleep(1)

def hilo_asincrono_ble():
    """Hilo dedicado exclusivamente a ejecutar el bucle de eventos asíncronos para BLE."""
    loop = asyncio.new_event_loop()
    asyncio.set_event_loop(loop)
    try:
        loop.run_until_complete(tarea_escaneo_ble())
    except Exception as e:
        print(f"❌ Error en el bucle asíncrono BLE: {e}")


def conectar_dispositivo():
    """Intenta abrir el puerto serial de forma segura capturando estados no enlazados."""
    global ser, conectado_hw
    
    if not os.path.exists("/dev/rfcomm0"):
        return False

    try:
        # Testeo preventivo nativo a bajo nivel
        fd = os.open("/dev/rfcomm0", os.O_RDWR | os.O_NONBLOCK)
        os.close(fd)
    except (OSError, PermissionError):
        if conectado_hw:
            print("📡 [BLE] Enlace de radio ausente (HC-05 apagado o fuera de rango).")
            conectado_hw = False
        return False

    if lock_serial.acquire(timeout=1.0):
        try:
            if ser is None:
                ser = serial.Serial()
                ser.port = "/dev/rfcomm0"
                ser.baudrate = 9600
                ser.timeout = 1
                ser.write_timeout = 1

            if not ser.is_open:
                # Si el HC-05 no está conectado por radio, ser.open() lanzará SerialException (Errno 5)
                ser.open()
                ser.reset_input_buffer()
                ser.reset_output_buffer()
                ser.write(b"\n")
                ser.flush()
                conectado_hw = True
                print("✅ [BLE] Conexión física establecida con Arduino con éxito.")
                return True
        except (serial.SerialException, OSError) as e:
            if conectado_hw:
                print(f"📡 [BLE] Fallo de enlace RF durante la apertura: {e}")
            conectado_hw = False
            if ser and ser.is_open:
                try:
                    ser.close()
                except:
                    pass
            return False
        finally:
            lock_serial.release()
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
def on_connect(client_obj, userdata, flags, rc):
    if rc == 0:
        print("✅ Conectado con éxito al Broker MQTT")
        client_obj.subscribe(TOPIC_COMMANDS)
    else:
        print(f"❌ Fallo de conexión a MQTT. Código de retorno: {rc}")

def on_message(client_obj, userdata, msg):
    try:
        payload = msg.payload.decode('utf-8')
        print(f"📥 Comando recibido desde MQTT: {payload}")
        enviar_bluetooth(payload)
    except Exception as e:
        print(f"❌ Error al procesar mensaje MQTT: {e}")


def hilo_lectura_serial(client_mqtt):
    """Hilo secundario que gestiona la lectura y reporta el estado a MQTT."""
    global conectado_hw, running
    print("🧵 Hilo de lectura serial iniciado.")
    
    ultimo_estado_reportado = None

    while running:
        # Reportar cambio de estado hacia MQTT para que la UI se entere
        if conectado_hw != ultimo_estado_reportado:
            estado_str = "CONNECTED" if conectado_hw else "DISCONNECTED"
            try:
                client_mqtt.publish(TOPIC_STATUS, f"STATUS:{estado_str}", qos=1, retain=True)
                ultimo_estado_reportado = conectado_hw
            except:
                pass

        if not conectado_hw:
            conectar_dispositivo()
            if not conectado_hw:
                time.sleep(5)
                continue

        if lock_serial.acquire(timeout=1.0):
            try:
                if ser and ser.is_open:
                    linea = ser.readline().decode('utf-8', errors='ignore').strip()
                    if linea:
                        print(f"📥 Datos recibidos de Arduino: {linea}")
                        client_mqtt.publish(TOPIC_STATUS, linea, qos=1)
            except (serial.SerialException, OSError) as e:
                conectado_hw = False
                try:
                    ser.close()
                except:
                    pass
            finally:
                lock_serial.release()
                
        time.sleep(0.2)

# --- FLUJO PRINCIPAL ---
def main():
    global client
    print("🚀 Iniciando ble-service en modo producción...")
    
    # Inicialización del cliente MQTT en la instancia global
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

    # Lanzar el hilo encargado de la comunicación por puerto serial (Arduino)
    worker_serial = threading.Thread(target=hilo_lectura_serial, args=(client,), daemon=True)
    worker_serial.start()

    # Lanzar el hilo encargado del escáner BLE Pasivo (ESP32 / Diagnóstico)
    worker_ble = threading.Thread(target=hilo_asincrono_ble, daemon=True)
    worker_ble.start()

    # Bucle de monitorización del hilo principal
    while running:
        time.sleep(1)

if __name__ == "__main__":
    try:
        main()
    except Exception as e:
        print(f"💥 Error crítico no controlado en la aplicación: {e}")
        sys.exit(1)