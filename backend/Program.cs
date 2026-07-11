using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ValueGeneration.Internal;
using MQTTnet;
using MQTTnet.Client;
using System.Text;
using System.Text.Json;
using WebApplication1.Data;
using WebApplication1.Models;

var builder = WebApplication.CreateBuilder(args);

// 🔥 FORZAR A KESTREL A ESCUCHAR EN TODAS LAS INTERFACES INTERNAS EN EL PUERTO 5000
builder.WebHost.ConfigureKestrel(options =>
{
    options.ListenAnyIP(5000); 
});

// 1. CONFIGURACIÓN DE SERVICIOS (Dependency Injection primero)
var factory = new MqttFactory();
var mqttClient = factory.CreateMqttClient();

// Registrar en DI antes de conectar para que los controladores puedan usarlo
builder.Services.AddSingleton<IMqttClient>(mqttClient);
builder.Services.AddHostedService<AutomationService>();

// Configurar SQLite
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite("Data Source=data/readings.db")
           .EnableSensitiveDataLogging());

var app = builder.Build();

// 2. INICIALIZAR BASE DE DATOS CON CONTROL DE ERRORES (Resistente a Docker)
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    int retries = 3;
    while (retries > 0)
    {
        try
        {
            db.Database.EnsureCreated();
            db.Database.ExecuteSqlRaw("PRAGMA journal_mode=WAL;");
            Console.WriteLine("💾 Base de datos SQLite inicializada correctamente.");
            break;
        }
        catch (Exception ex)
        {
            retries--;
            Console.WriteLine($"⚠️ Error al inicializar la BD. Reintentando... ({retries} intentos restantes). Error: {ex.Message}");
            if (retries == 0) throw;
            await Task.Delay(2000); // Esperar 2 segundos antes de reintentar
        }
    }
}

// 3. CONEXIÓN ASÍNCRONA A MQTT (Una vez que la App ya está construida)
var options = new MqttClientOptionsBuilder()
    .WithClientId("backend-client")
    .WithTcpServer("mqtt", 1883)
    .Build();

mqttClient.ConnectedAsync += async e =>
{
    Console.WriteLine("✅ Conectado a MQTT");
    await mqttClient.SubscribeAsync("ble/readings");
    await mqttClient.SubscribeAsync("ble/status");  
    Console.WriteLine("✅ Suscrito a topics");
};

// Recibir y guardar mensajes MQTT
mqttClient.ApplicationMessageReceivedAsync += async e =>
{
    var payload = Encoding.UTF8.GetString(e.ApplicationMessage.PayloadSegment);
    Console.WriteLine($"📥 Mensaje recibido: {payload}");

    try
    {
        if (payload.Contains("temperatura") && payload.Contains("humedad"))
        {
            var data = JsonSerializer.Deserialize<Reading>(payload);
            if (data != null)
            {
                using var scope = app.Services.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                data.Timestamp = DateTime.UtcNow;
                db.Readings.Add(data);
                await db.SaveChangesAsync();
                Console.WriteLine("💾 Guardado en DB");
            }
        }
        else if (payload.Contains("relay") && payload.Contains("status"))
        {
            var estado = JsonSerializer.Deserialize<RelayStatus>(payload);
            if (estado != null)
            {
                using var scope = app.Services.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                estado.Timestamp = DateTime.UtcNow;
                db.RelayStatuses.Add(estado);
                await db.SaveChangesAsync();
                Console.WriteLine("🔌 Estado relé guardado");
            }
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine("❌ Error guardando mensaje MQTT: " + ex.Message);
    }
};

// Conectar de fondo de forma segura
try 
{
    await mqttClient.ConnectAsync(options);
    Console.WriteLine("🚀 Conexión inicial enviada a Mosquitto");
}
catch (Exception ex)
{
    Console.WriteLine($"⚠️ No se pudo conectar a MQTT al iniciar, el cliente reintentará solo: {ex.Message}");
}

// 4. DEFINICIÓN DE ENDPOINTS (Mapeos HTTP)
app.MapGet("/readings", async (AppDbContext db) =>
{
    var data = await db.Readings.OrderByDescending(r => r.Timestamp).Take(50).ToListAsync();
    return Results.Ok(data);
});

app.MapGet("/readings/{deviceId}", async (string deviceId, AppDbContext db) =>
{
    var data = await db.Readings.Where(r => r.DeviceId == deviceId).OrderByDescending(r => r.Timestamp).Take(50).ToListAsync();
    return Results.Ok(data);
});

app.MapPost("/cultivos", async (Cultivo cultivo, AppDbContext db) =>
{
    db.Cultivos.Add(cultivo);
    await db.SaveChangesAsync();
    return Results.Ok(cultivo);
});

app.MapPost("/cultivos/{id}/iniciar", async (int id, AppDbContext db) =>
{
    var cultivo = await db.Cultivos.FindAsync(id);
    if (cultivo == null) return Results.NotFound();
    cultivo.FechaInicio = DateTime.UtcNow;
    await db.SaveChangesAsync();
    return Results.Ok(cultivo);
});

app.MapPost("/etapas", async (Etapa etapa, AppDbContext db) =>
{
    db.Etapas.Add(etapa);
    await db.SaveChangesAsync();
    return Results.Ok(etapa);
});

app.MapGet("/cultivos", async (AppDbContext db) =>
{
    var lista = await db.Cultivos.ToListAsync();
    return Results.Ok(lista);
});

app.MapGet("/etapas/{cultivoId}", async (int cultivoId, AppDbContext db) =>
{
    var lista = await db.Etapas.Where(e => e.CultivoId == cultivoId).ToListAsync();
    return Results.Ok(lista);
});

app.MapGet("/relay-status", async (AppDbContext db) =>
{
    var estado = await db.RelayStatuses
        .GroupBy(r => r.Relay)
        .Select(g => g.OrderByDescending(x => x.Timestamp).First())
        .ToListAsync();
    return Results.Ok(estado);
});

app.MapDelete("/cultivos/{id}", async (int id, AppDbContext db) =>
{
    var cultivo = await db.Cultivos.FindAsync(id);
    if (cultivo == null) return Results.NotFound();
    db.Cultivos.Remove(cultivo);
    await db.SaveChangesAsync();
    return Results.Ok();
});

app.MapDelete("/etapas/{id}", async (int id, AppDbContext db) =>
{
    var etapa = await db.Etapas.FindAsync(id);
    if (etapa == null) return Results.NotFound();
    db.Etapas.Remove(etapa);
    await db.SaveChangesAsync();
    return Results.Ok();
});

app.MapGet("/cultivos/{id}/etapa-actual", async (int id, AppDbContext db) =>
{
    var cultivo = await db.Cultivos.FindAsync(id);
    if (cultivo == null) return Results.NotFound();

    var etapas = await db.Etapas.Where(e => e.CultivoId == id).OrderBy(e => e.Id).ToListAsync();
    var dias = (DateTime.UtcNow - cultivo.FechaInicio).Days;
    int acumulado = 0;

    foreach (var etapa in etapas)
    {
        acumulado += etapa.DuracionDias;
        if (dias <= acumulado)
        {
            return Results.Ok(new { etapaActual = etapa.Nombre, diasTranscurridos = dias });
        }
    }
    return Results.Ok(new { etapaActual = "Finalizado", diasTranscurridos = dias });
});

app.MapPost("/modo", async (ModoRequest data, AppDbContext db) =>
{
    var cultivo = await db.Cultivos.FindAsync(data.CultivoId);
    if (cultivo == null) return Results.NotFound();
    cultivo.Modo = data.Modo;
    await db.SaveChangesAsync();
    return Results.Ok(new { mensaje = "Modo actualizado" });
});

app.MapPost("/relay-control", async (IMqttClient mqttClient, RelayRequest data) =>
{
    int relay = data.Relay;
    string action = data.Action;
    var mensaje = new { relay = relay, action = action };
    var payload = JsonSerializer.Serialize(mensaje);

    var mqttMessage = new MqttApplicationMessageBuilder()
        .WithTopic("ble/commands")
        .WithPayload(payload)
        .Build();

    await mqttClient.PublishAsync(mqttMessage);
    Console.WriteLine($"📡 Enviado a Arduino: {payload}");
    return Results.Ok(new { mensaje = "Comando enviado" });
});

//app.MapGet("/", () => Results.Text("<h1>🚀 Backend IoT operativo en .NET 8</h1>", "text/html"));

app.UseDefaultFiles();
app.UseStaticFiles();

// 5. INICIAR EL SERVIDOR WEB
app.Run();
