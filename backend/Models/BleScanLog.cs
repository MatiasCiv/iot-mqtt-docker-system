public class BleScanLog
{
    public int Id { get; set; }
    public string Mac { get; set; } = string.Empty;
    public string Nombre { get; set; } = string.Empty;
    public int Rssi { get; set; }
    public string ManufacturerData { get; set; } = string.Empty;
    public DateTime FechaCaptura { get; set; } = DateTime.UtcNow;
}