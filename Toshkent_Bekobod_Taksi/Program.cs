using Toshkent_Bekobod_Taksi.Services;
using Toshkent_Bekobod_Taksi.Telegram;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<OrderManager>();
builder.Services.AddSingleton<UserStore>();
builder.Services.AddSingleton<BotService>();

var app = builder.Build();

var bot = app.Services.GetRequiredService<BotService>();
_ = bot.StartReceiving();


var port = Environment.GetEnvironmentVariable("PORT") ?? "5000";
app.Urls.Clear();
app.Urls.Add($"http://0.0.0.0:{port}");

app.MapGet("/", () => "Toshkent-Bekobod Taksi Bot is running");

app.Run();
