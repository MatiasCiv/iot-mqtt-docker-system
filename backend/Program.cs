using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ValueGeneration.Internal;
using MQTTnet;
using MQTTnet.Client;
using System.Text;
using System.Text.Json;
using WebApplication1.Data;
using WebApplication1.Models;


var builder = WebApplication.CreateBuilder(args);

// ✅ Configurar SQLite
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite("Data Source=data/readings.db")
                        .EnableSensitiveDataLogging());

var app = builder.Build();

// ✅ Crear DB automáticamente
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.EnsureCreated();
    db.Database.ExecuteSqlRaw("PRAGMA journal_mode=WAL;");
}

// ✅ MQTT setup
var factory = new MqttFactory();
var mqttClient = factory.CreateMqttClient();

var options = new MqttClientOptionsBuilder()
    .WithTcpServer("mqtt", 1883)
    .Build();

// ✅ Suscripción al conectar
mqttClient.ConnectedAsync += async e =>
{
    Console.WriteLine("✅ Conectado a MQTT");

    await mqttClient.SubscribeAsync("ble/readings");

    Console.WriteLine("✅ Suscrito a ble/readings");
};

// ✅ Recibir y guardar
mqttClient.ApplicationMessageReceivedAsync += async e =>
{
    var payload = Encoding.UTF8.GetString(e.ApplicationMessage.PayloadSegment);

    Console.WriteLine("📥 Mensaje recibido:");
    Console.WriteLine(payload);

    try
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
    catch (Exception ex)
    {
        Console.WriteLine("❌ Error guardando: " + ex.Message);
    }
};

// ✅ Conectar
await mqttClient.ConnectAsync(options);



// TODOS LOS DATOS
app.MapGet("/readings", async (AppDbContext db) =>
{
    var data = await db.Readings
        .OrderByDescending(r => r.Timestamp)
        .Take(50)
        .ToListAsync();

    return Results.Ok(data);
});

// DATOS POR DEVICE
app.MapGet("/readings/{deviceId}", async (string deviceId, AppDbContext db) =>
{
    var data = await db.Readings
        .Where(r => r.DeviceId == deviceId)
        .OrderByDescending(r => r.Timestamp)
        .Take(50)
        .ToListAsync();

    return Results.Ok(data);
});

// CREAR CULTIVOS

app.MapPost("/cultivos", async (Cultivo cultivo, AppDbContext db) =>
{
    db.Cultivos.Add(cultivo);
    await db.SaveChangesAsync();

    return Results.Ok(cultivo);
});

// CREAR ETAPAS 

app.MapPost("/etapas", async (Etapa etapa, AppDbContext db) =>
{
    db.Etapas.Add(etapa);
    await db.SaveChangesAsync();

    return Results.Ok(etapa);
});



// OBTENER CULTIVOS
app.MapGet("/cultivos", async (AppDbContext db) =>
{
    var lista = await db.Cultivos.ToListAsync();
    return Results.Ok(lista);
});

//OBTENER ETAPAS

app.MapGet("/etapas/{cultivoId}", async (int cultivoId, AppDbContext db) =>
{
    var lista = await db.Etapas
        .Where(e => e.CultivoId == cultivoId)
        .ToListAsync();

    return Results.Ok(lista);
});

//ELIMINAR CULTIVOS

app.MapDelete("/cultivos/{id}", async (int id, AppDbContext db) =>
{
    var cultivo = await db.Cultivos.FindAsync(id);

    if (cultivo == null)
        return Results.NotFound();

    db.Cultivos.Remove(cultivo);
    await db.SaveChangesAsync();

    return Results.Ok();
});
// ELIMINAR ETAPA
app.MapDelete("/etapas/{id}", async (int id, AppDbContext db) =>
{
    var etapa = await db.Etapas.FindAsync(id);

    if (etapa == null)
        return Results.NotFound();

    db.Etapas.Remove(etapa);
    await db.SaveChangesAsync();

    return Results.Ok();
});


app.UseDefaultFiles();
app.UseStaticFiles();
app.Run("http://0.0.0.0:5000");




