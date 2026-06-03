using System.Collections.Concurrent;
using System.Text.Json;

namespace Toshkent_Bekobod_Taksi.Services;

public class UserInfo
{
    public long TelegramId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public DateTime FirstOrderAt { get; set; } = DateTime.UtcNow;
}

public class UserStore
{
    private readonly ConcurrentDictionary<long, UserInfo> _users = new();
    private readonly string _filePath;

    public UserStore()
    {
        _filePath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "Data", "users.json"));
        var dir = Path.GetDirectoryName(_filePath)!;
        if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
        Load();
    }

    public bool Exists(long telegramId) => _users.ContainsKey(telegramId);

    public UserInfo? Get(long telegramId)
    {
        _users.TryGetValue(telegramId, out var user);
        return user;
    }

    public void SaveUser(long telegramId, string name, string phone)
    {
        _users[telegramId] = new UserInfo
        {
            TelegramId = telegramId,
            Name = name,
            Phone = phone,
            FirstOrderAt = _users.TryGetValue(telegramId, out var existing) ? existing.FirstOrderAt : DateTime.UtcNow
        };
        Persist();
    }

    private void Persist()
    {
        try
        {
            var data = JsonSerializer.Serialize(_users.Values.ToList(), new JsonSerializerOptions { WriteIndented = true });
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
                var list = JsonSerializer.Deserialize<List<UserInfo>>(data);
                if (list != null)
                    foreach (var u in list)
                        _users[u.TelegramId] = u;
            }
        }
        catch { }
    }
}
