namespace WebApplication1.Models;
using System.Text.Json.Serialization;

public class RelayStatus
{
    public int Id { get; set; }

    [JsonPropertyName("relay")]
    public int Relay { get; set; }

    [JsonPropertyName("status")]
    public string Status { get; set; } = "";

    public DateTime Timestamp { get; set; }
}