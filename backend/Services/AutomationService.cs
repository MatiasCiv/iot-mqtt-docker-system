using Microsoft.EntityFrameworkCore;
using WebApplication1.Data;
using WebApplication1.Models;

public class AutomationService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;

    public AutomationService(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
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
            Etapa etapaActual = null;

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
            if (etapaActual != null)
            {
                if (lecturas.Humedad < etapaActual.HumedadMin)
                {
                    Console.WriteLine("💧 Riego necesario");

                    // 👉 aquí enviarás MQTT
                }
            }

        }
    }
}