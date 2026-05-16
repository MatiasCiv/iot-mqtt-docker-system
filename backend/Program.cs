using MQTTnet;
using MQTTnet.Client;
using System.Text;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

var factory = new MqttFactory();
var mqttClient = factory.CreateMqttClient();

var options = new MqttClientOptionsBuilder()
    .WithTcpServer("mqtt", 1883)
    .Build();

// ✅ FORMA CORRECTA MODERNA
mqttClient.ConnectedAsync += async e =>
{
    Console.WriteLine("✅ Conectado a MQTT");

    await mqttClient.SubscribeAsync("ble/readings");

    Console.WriteLine("✅ Suscrito a ble/readings");
};

mqttClient.ApplicationMessageReceivedAsync += e =>
{
    var payload = Encoding.UTF8.GetString(e.ApplicationMessage.PayloadSegment);

    Console.WriteLine("📥 Mensaje recibido:");
    Console.WriteLine(payload);

    return Task.CompletedTask;
};

// Conectar
await mqttClient.ConnectAsync(options);

app.Run("http://0.0.0.0:5000");