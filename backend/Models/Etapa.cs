namespace WebApplication1.Models;

public class Etapa
{
    public int Id { get; set; }

    public int CultivoId { get; set; }

    public string Nombre { get; set; } = "";

    public int DuracionDias { get; set; }

    public double HumedadMin { get; set; }
    public double HumedadMax { get; set; }

    public double TemperaturaMin { get; set; }
    public double TemperaturaMax { get; set; }

    public bool RiegoActivo { get; set; } = true;
}