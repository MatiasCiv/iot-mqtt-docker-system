import time
import random
import json
import paho.mqtt.client as mqtt

MQTT_BROKER = "mqtt"
MQTT_PORT = 1883
TOPIC = "ble/readings"


def generar_dato():
    return {
        "deviceId": "ESP32_SIM",
        "temperatura": round(random.uniform(20, 30), 2),
        "humedad": round(random.uniform(40, 80), 2)
    }


client = mqtt.Client()

# Conectar al broker
client.connect(MQTT_BROKER, MQTT_PORT, 60)

client.loop_start()

print("🚀 MODO MQTT ACTIVO")

while True:
    data = generar_dato()

    payload = json.dumps(data)

    print("📡 Publicando:", payload)

    client.publish(TOPIC, payload)

    time.sleep(5)
