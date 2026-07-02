namespace Toshkent_Bekobod_Taksi.Models;

public enum OrderStatus { Active, Accepted }

public class DriverViewer
{
    public long TelegramId { get; set; }
    public string? Username { get; set; }
    public string? Name { get; set; }
    public DateTime ViewedAt { get; set; }
}

public class Order
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public long UserChatId { get; set; }
    public string UserName { get; set; } = string.Empty;
    public string? UserTelegramUsername { get; set; }
    public string Phone { get; set; } = string.Empty;
    public string PickupLocation { get; set; } = string.Empty;
    public string DropoffLocation { get; set; } = string.Empty;
    public int PassengerCount { get; set; } = 1;
    public OrderStatus Status { get; set; } = OrderStatus.Active;
    public int GroupMessageId { get; set; }
    public long GroupChatId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public long? DriverTelegramId { get; set; }
    public string? DriverUsername { get; set; }
    public string? DriverName { get; set; }
    public DateTime? AcceptedAt { get; set; }

    public List<DriverViewer> ViewedByDrivers { get; set; } = new();
}
