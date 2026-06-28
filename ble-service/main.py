import time
import random
import json
import paho.mqtt.client as mqtt
import serial
import os





MQTT_BROKER = "mqtt"
MQTT_PORT = 1883

TOPIC_READINGS = "ble/readings"
TOPIC_COMMANDS = "ble/commands"
TOPIC_STATUS = "ble/status"


# ----------------------
# ✅ SERIAL HC-05
# ----------------------

while not os.path.exists("/dev/rfcomm0"):
    print("⏳ esperando /dev/rfcomm0...")
    time.sleep(2)

while True:
    try:
        ser = serial.Serial("/dev/rfcomm0", 9600, timeout=1)
        print("✅ Conectado a RFComm")
        break
    except Exception as e:
        print("⏳ esperando conexión RFComm...", e)
        time.sleep(2)



import threading

def leer_serial():
    global ser

    while True:
        try:
            if ser.in_waiting > 0:
                respuesta = ser.readline().decode(errors="ignore").strip()

                if respuesta:
                    print("📥 Respuesta Arduino:", respuesta)

                    if client.is_connected():
                        client.publish(TOPIC_STATUS, respuesta)

        except Exception as e:
            print("⚠️ Error serial:", e)

            # 🔥 RECONEXIÓN AUTOMÁTICA
            try:
                ser.close()
            except:
                pass

            reintentando = True

            while reintentando:
                try:
                    print("🔄 reconectando RFComm...")
                    ser = serial.Serial("/dev/rfcomm0", 9600, timeout=1)
                    print("✅ reconectado")
                    reintentando = False
                except Exception as e:
                    print("⏳ esperando reconexión...", e)
                    time.sleep(3)   # 🔥 MÁS LENTO todavía

        time.sleep(0.2)


# ----------------------
# ✅ SIMULACIÓN SENSOR
# ----------------------

def generar_dato():
    return {
        "deviceId": "ESP32_SIM",
        "temperatura": round(random.uniform(20, 30), 2),
        "humedad": round(random.uniform(40, 80), 2)
    }


# ----------------------
# ✅ GESTION BLUETOOTH
# ----------------------


def enviar_bluetooth(mensaje):
    try:
        print("📡 [BLE] Enviando a Arduino:", mensaje)
        ser.write((mensaje + "\n").encode())
        ser.flush()
    except Exception as e:
        print("❌ Error enviando por Bluetooth:", e)




# ----------------------
# ✅ MQTT CALLBACKS
# ----------------------

def on_connect(client, userdata, flags, rc):
    print("✅ Conectado a MQTT")

    client.subscribe(TOPIC_COMMANDS)
    print("✅ Suscrito a ble/commands")


def on_message(client, userdata, msg):
    payload = msg.payload.decode()

    print("📥 Comando recibido MQTT:", payload)

    # ✅ enviar a "Arduino"
    enviar_bluetooth(payload)





# ----------------------
# ✅ SETUP MQTT
# ----------------------

client = mqtt.Client(client_id="ble-service-client")

client.on_connect = on_connect
client.on_message = on_message

client.connect(MQTT_BROKER, MQTT_PORT, 60)

client.loop_start()
threading.Thread(target=leer_serial, daemon=True).start()

print("🚀 MODO MQTT ACTIVO")



while True:
    time.sleep(1)


