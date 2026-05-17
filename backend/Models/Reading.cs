using System.Text.Json.Serialization;
namespace WebApplication1.Models;

public class Reading
{
    public int Id { get; set; }

    [JsonPropertyName("deviceId")]
    public string DeviceId { get; set; } = "";

    [JsonPropertyName("temperatura")]
    public double Temperatura { get; set; }

    [JsonPropertyName("humedad")]
    public double Humedad { get; set; }

    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}

