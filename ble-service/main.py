import time
import random
import json
import paho.mqtt.client as mqtt
import serial

MQTT_BROKER = "mqtt"
MQTT_PORT = 1883

TOPIC_READINGS = "ble/readings"
TOPIC_COMMANDS = "ble/commands"
TOPIC_STATUS = "ble/status"



# ----------------------
# ✅ SERIAL HC-05
# ----------------------

ser = serial.Serial("/dev/rfcomm0", 9600, timeout=1)
print("✅ Conectado a /dev/rfcomm0")



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
    
    print("📡 [BLE] Enviando a Arduino:", mensaje)

    ser.write((mensaje + "\n").encode())




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


    # ✅ esperar respuesta Arduino
    respuesta = ser.readline().decode().strip()

    if respuesta:
        print("📥 Respuesta Arduino:", respuesta)
        client.publish(TOPIC_STATUS, respuesta)
    else:
        print("⚠️ Sin respuesta del Arduino")



# ----------------------
# ✅ SETUP MQTT
# ----------------------

client = mqtt.Client(client_id="ble-service-client")

client.on_connect = on_connect
client.on_message = on_message

client.connect(MQTT_BROKER, MQTT_PORT, 60)

client.loop_start()


print("🚀 MODO MQTT ACTIVO")


# ----------------------
# ✅ LOOP PRINCIPAL
# ----------------------

while True:
    # ✅ seguir publicando sensores
    data = generar_dato()
    payload = json.dumps(data)

    print("📡 Publicando sensor:", payload)

    client.publish(TOPIC_READINGS, payload)

    time.sleep(5)
