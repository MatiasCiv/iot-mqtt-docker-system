namespace WebApplication1.Models;

public class Cultivo
{
    public int Id { get; set; }

    public string Nombre { get; set; } = "";

    public string SensorId { get; set; } = "";

    public DateTime FechaInicio { get; set; } = DateTime.UtcNow;

    public bool Activo { get; set; } = true;

    public List<Etapa> Etapas { get; set; } = new();
}