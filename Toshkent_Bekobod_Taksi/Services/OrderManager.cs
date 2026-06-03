using System.Collections.Concurrent;
using System.Text.Json;
using Toshkent_Bekobod_Taksi.Models;

namespace Toshkent_Bekobod_Taksi.Services;

public class OrderManager
{
    private readonly ConcurrentDictionary<Guid, Order> _orders = new();
    private readonly ConcurrentDictionary<long, int> _userStates = new();
    private readonly ConcurrentDictionary<long, Order> _pendingOrders = new();
    private readonly string _filePath;

    public OrderManager()
    {
        _filePath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "Data", "orders.json");
        _filePath = Path.GetFullPath(_filePath);
        var dir = Path.GetDirectoryName(_filePath)!;
        if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
        Load();
    }

    public int GetUserState(long chatId) => _userStates.GetValueOrDefault(chatId, 0);
    public void SetUserState(long chatId, int state) => _userStates[chatId] = state;
    public void RemoveUserState(long chatId) => _userStates.TryRemove(chatId, out _);

    public Order GetOrCreatePendingOrder(long chatId)
        => _pendingOrders.GetOrAdd(chatId, _ => new Order { UserChatId = chatId });

    public Order? GetPendingOrder(long chatId)
    {
        _pendingOrders.TryGetValue(chatId, out var order);
        return order;
    }

    public void AddOrder(Order order)
    {
        _orders[order.Id] = order;
        Save();
    }

    public Order? GetOrder(Guid id) => _orders.GetValueOrDefault(id);

    public List<Order> GetActiveOrders()
        => _orders.Values.Where(o => o.Status == OrderStatus.Active).ToList();

    public bool AcceptOrder(Guid id, long driverTelegramId, string? driverUsername, string? driverName)
    {
        if (_orders.TryGetValue(id, out var order) && order.Status == OrderStatus.Active)
        {
            order.Status = OrderStatus.Accepted;
            order.DriverTelegramId = driverTelegramId;
            order.DriverUsername = driverUsername;
            order.DriverName = driverName;
            order.AcceptedAt = DateTime.UtcNow;
            Save();
            return true;
        }
        return false;
    }

    public List<Order> GetAllOrders() => _orders.Values.OrderByDescending(o => o.CreatedAt).ToList();

    public List<MonthlyStat> GetMonthlyStats()
    {
        return _orders.Values
            .GroupBy(o => $"{o.CreatedAt.Year}-{o.CreatedAt.Month:D2}")
            .OrderBy(g => g.Key)
            .Select(g => new MonthlyStat
            {
                Month = g.Key,
                TotalOrders = g.Count(),
                Orders = g.OrderByDescending(o => o.CreatedAt).ToList()
            })
            .ToList();
    }

    public List<DriverStat> GetDriverStats()
    {
        return _orders.Values
            .Where(o => o.DriverTelegramId != null)
            .GroupBy(o => o.DriverTelegramId!.Value)
            .Select(g =>
            {
                var first = g.First();
                return new DriverStat
                {
                    TelegramId = g.Key,
                    Name = first.DriverName ?? "Noma'lum",
                    Username = first.DriverUsername,
                    TotalOrders = g.Count(),
                    LastOrderAt = g.Max(o => o.AcceptedAt),
                    Orders = g.OrderByDescending(o => o.AcceptedAt).ToList()
                };
            })
            .OrderByDescending(d => d.TotalOrders)
            .ToList();
    }

    private void Save()
    {
        try
        {
            var data = JsonSerializer.Serialize(_orders.Values.ToList(), new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_filePath, data);
        }
        catch { }
    }

    private void Load()
    {
        try
        {
            if (File.Exists(_filePath))
            {
                var data = File.ReadAllText(_filePath);
                var list = JsonSerializer.Deserialize<List<Order>>(data);
                if (list != null)
                    foreach (var o in list)
                        _orders[o.Id] = o;
            }
        }
        catch { }
    }
}

public class MonthlyStat
{
    public string Month { get; set; } = string.Empty;
    public int TotalOrders { get; set; }
    public List<Order> Orders { get; set; } = new();
}

public class DriverStat
{
    public long TelegramId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Username { get; set; }
    public int TotalOrders { get; set; }
    public DateTime? LastOrderAt { get; set; }
    public List<Order> Orders { get; set; } = new();
}
