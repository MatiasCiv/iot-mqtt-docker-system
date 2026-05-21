using Microsoft.EntityFrameworkCore;
using WebApplication1.Data;
using WebApplication1.Models;
using MQTTnet;
using MQTTnet.Client;
using System.Text;


public class AutomationService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IMqttClient _mqttClient;

    private static Dictionary<int, string> estadoReles = new();

    public AutomationService(IServiceScopeFactory scopeFactory, IMqttClient mqttClient)
    {
        _scopeFactory = scopeFactory;
        _mqttClient = mqttClient;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await EjecutarLogica();

            await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
        }
    }

    private async Task EjecutarLogica()
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var cultivos = await db.Cultivos.ToListAsync();
    
        foreach (var cultivo in cultivos)
        {
            Console.WriteLine($"🌱 Procesando cultivo: {cultivo.Nombre}");

            // Aquí irá la lógica real 👇
            
            var lecturas = await db.Readings
                .Where(r => r.DeviceId == cultivo.SensorId)
                .OrderByDescending(r => r.Timestamp)
                .FirstOrDefaultAsync();
            
            

            if (lecturas == null)
                return;

            // calcular etapa actual
            var etapas = await db.Etapas
                .Where(e => e.CultivoId == cultivo.Id)
                .OrderBy(e => e.Id)
                .ToListAsync();

            var dias = (DateTime.UtcNow - cultivo.FechaInicio).Days;

            int acumulado = 0;
            Etapa? etapaActual = null;
    
            foreach (var e in etapas)
            {
                acumulado += e.DuracionDias;

                if (dias <= acumulado)
                {
                    etapaActual = e;
                    break;
                }
            }

            // validar condiciones
            Console.WriteLine($"📊 Humedad actual: {lecturas.Humedad}");
            Console.WriteLine($"📉 Min etapa: {etapaActual?.HumedadMin}");
            if (etapaActual is not null)
            {
                
                var humedad = lecturas.Humedad;
                
                string estadoActual = estadoReles.ContainsKey(cultivo.Id)
                    ? estadoReles[cultivo.Id]
                    : "OFF";

                // 🔴 ENCENDER (riego)
                if (humedad < etapaActual.HumedadMin && estadoActual != "ON")
                {
                    Console.WriteLine($"💧 ENCENDER riego → Relay {cultivo.Relay}");

                    await EnviarComandoRelay(cultivo.SensorId, cultivo.Relay, "ON");

                    estadoReles[cultivo.Id] = "ON";
                }

                // 🔵 APAGAR (suficiente humedad)
                else if (humedad > etapaActual.HumedadMax && estadoActual != "OFF")
                {
                    Console.WriteLine($"✅ APAGAR riego → Relay {cultivo.Relay}");

                    await EnviarComandoRelay(cultivo.SensorId, cultivo.Relay, "OFF");

                    estadoReles[cultivo.Id] = "OFF";
                }

                // 🟢 zona estable → no hacer nada
                else
                {
                    Console.WriteLine("🟢 Humedad en rango → sin cambios");
                }

            }

        }
    }


    private async Task EnviarComandoRelay(string deviceId, int relay, string action)
    {
        var comando = new
        {
            deviceId = deviceId,
            relay = relay,
            action = action
        };

        var payload = System.Text.Json.JsonSerializer.Serialize(comando);

        Console.WriteLine("📡 MQTT → " + payload);

        var message = new MqttApplicationMessageBuilder()
            .WithTopic("ble/commands")
            .WithPayload(payload)
            .Build();

        await _mqttClient.PublishAsync(message);
    }

}
