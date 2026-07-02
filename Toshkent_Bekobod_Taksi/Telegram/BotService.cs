using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.ReplyMarkups;
using Toshkent_Bekobod_Taksi.Models;
using Toshkent_Bekobod_Taksi.Services;

namespace Toshkent_Bekobod_Taksi.Telegram;

public class BotService
{
    private readonly ITelegramBotClient _bot;
    private readonly OrderManager _orders;
    private readonly UserStore _users;
    private readonly long _groupId;
    private readonly long _viewerGroupId;
    private readonly long _adminId;

    public BotService(IConfiguration config, OrderManager orders, UserStore users)
    {
        var token = config["TelegramBot:Token"] ?? throw new Exception("Token missing");
        _groupId = long.Parse(config["TelegramBot:GroupId"] ?? throw new Exception("GroupId missing"));
        _viewerGroupId = long.Parse(config["TelegramBot:ViewerGroupId"] ?? throw new Exception("ViewerGroupId missing"));
        _adminId = long.Parse(config["TelegramBot:AdminId"] ?? throw new Exception("AdminId missing"));
        _bot = new TelegramBotClient(token);
        _orders = orders;
        _users = users;
    }

    public async Task StartReceiving()
    {
        _bot.StartReceiving(HandleUpdate, HandleError);
        var me = await _bot.GetMeAsync();
        Console.WriteLine($"Bot started: @{me.Username}");
    }

    private Task HandleError(ITelegramBotClient client, Exception ex, CancellationToken ct)
    {
        Console.WriteLine($"Error: {ex.Message}");
        return Task.CompletedTask;
    }

    private async Task HandleUpdate(ITelegramBotClient client, Update update, CancellationToken ct)
    {
        try
        {
            if (update.Message is { } msg)
                await HandleMessage(msg);
            else if (update.CallbackQuery is { } cb)
                await HandleCallback(cb);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"HandleUpdate error: {ex.Message}");
        }
    }

    private async Task HandleMessage(Message msg)
    {
        var chatId = msg.Chat.Id;
        var text = msg.Text ?? "";
        var contact = msg.Contact;
        var telegramUsername = msg.From?.Username;

        if (chatId == _adminId)
            await HandleAdminMessage(chatId, text);
        else
            await HandlePassengerMessage(chatId, text, contact, telegramUsername);
    }

    private async Task HandleAdminMessage(long chatId, string text)
    {
        if (text == "/start" || text == "📊 Statistika")
        {
            await ShowMonthlyStats(chatId);
            return;
        }
        if (text == "🚗 Haydovchilar")
        {
            await ShowDriverStats(chatId);
            return;
        }

        var stats = _orders.GetMonthlyStats();
        var driverStats = _orders.GetDriverStats();

        var msg = $"🇺🇿 TOSHKENT – BEKOBOD TAKSI | Admin panel\n\n" +
                  $"👋 Xush kelibsiz! Bugungi tizim ko'rsatkichlari:\n\n" +
                  $"📊 Jami buyurtmalar: {stats.Sum(s => s.TotalOrders)} ta\n" +
                  $"🚗 Faol haydovchilar: {driverStats.Count} ta\n\n" +
                  $"Quyidagi tugmalar orqali statistikani kuzatishingiz mumkin:";

        var keyboard = new ReplyKeyboardMarkup(new[]
        {
            new[] { new KeyboardButton("📊 Statistika"), new KeyboardButton("🚗 Haydovchilar") }
        })
        { ResizeKeyboard = true };

        await _bot.SendTextMessageAsync(chatId, msg, replyMarkup: keyboard);
    }

    private async Task HandlePassengerMessage(long chatId, string text, Contact? contact, string? telegramUsername = null)
    {
        if (text == "/start")
        {
            _orders.ClearPendingOrder(chatId);

            var order = _orders.GetOrCreatePendingOrder(chatId);
            order.UserTelegramUsername = telegramUsername;

            if (_users.Exists(chatId))
            {
                await _bot.SendTextMessageAsync(chatId,
                    "🇺🇿 TOSHKENT – BEKOBOD TAKSI\n" +
                    "Xush kelibsiz! Buyurtma berishni davom ettiramiz.\n\n" +
                    "📍 Qayerdan ketmoqchisiz? (jo'nash manzilini kiriting)");
                _orders.SetUserState(chatId, 3);
            }
            else
            {
                await _bot.SendTextMessageAsync(chatId,
                    "🇺🇿 TOSHKENT – BEKOBOD TAKSI\n" +
                    "📞 Xush kelibsiz! Tez va ishonchli taksi xizmati.\n\n" +
                    "Buyurtma berishni boshlash uchun ismingizni kiriting:");
                _orders.SetUserState(chatId, 1);
            }
            return;
        }

        var state = _orders.GetUserState(chatId);

        if (state == 0)
        {
            await _bot.SendTextMessageAsync(chatId, "Iltimos, /start ni bosing.");
            return;
        }

        if (state == 1)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                await _bot.SendTextMessageAsync(chatId, "Iltimos, ismingizni kiriting:");
                return;
            }

            var order = _orders.GetOrCreatePendingOrder(chatId);
            order.UserName = text.Trim();

            var keyboard = new ReplyKeyboardMarkup(new[]
            {
                new KeyboardButton("📞 Telefon raqamni ulashish") { RequestContact = true }
            })
            { ResizeKeyboard = true, OneTimeKeyboard = true };

            await _bot.SendTextMessageAsync(chatId,
                $"✅ Rahmat, {text.Trim()}!\n\n📞 Iltimos, telefon raqamingizni yuboring. Haydovchi siz bilan bog'lanishi uchun kerak:",
                replyMarkup: keyboard);
            _orders.SetUserState(chatId, 2);
            return;
        }

        if (state == 2)
        {
            string phone;
            if (contact != null)
                phone = contact.PhoneNumber ?? "";
            else if (!string.IsNullOrWhiteSpace(text))
                phone = text.Trim();
            else
            {
                await _bot.SendTextMessageAsync(chatId, "Iltimos, telefon raqamingizni yuboring.");
                return;
            }

            var order = _orders.GetOrCreatePendingOrder(chatId);
            order.Phone = phone;
            order.UserName = order.UserName ?? "Foydalanuvchi";

            _users.SaveUser(chatId, order.UserName, phone);

            await _bot.SendTextMessageAsync(chatId,
                "📍 Qayerdan ketmoqchisiz? (jo'nash manzilini kiriting)");
            _orders.SetUserState(chatId, 3);
            return;
        }

        if (state == 3)
        {
            var user = _users.Get(chatId);
            var order = _orders.GetOrCreatePendingOrder(chatId);
            order.PickupLocation = text;
            if (user != null) order.UserName = user.Name;

            await _bot.SendTextMessageAsync(chatId, "🏁 Qayerga bormoqchisiz? (borish manzilini kiriting)");
            _orders.SetUserState(chatId, 4);
            return;
        }

        if (state == 4)
        {
            var order = _orders.GetOrCreatePendingOrder(chatId);
            order.DropoffLocation = text;

            await _bot.SendTextMessageAsync(chatId, "👥 Necha kishisiz? (raqam bilan yozing, masalan: 2)");
            _orders.SetUserState(chatId, 5);
            return;
        }

        if (state == 5)
        {
            if (!int.TryParse(text, out var count) || count < 1)
            {
                await _bot.SendTextMessageAsync(chatId, "❌ Iltimos, to'g'ri son kiriting (kamida 1):");
                return;
            }

            var order = _orders.GetOrCreatePendingOrder(chatId);
            order.PassengerCount = count;

            var user = _users.Get(chatId);
            if (user != null)
            {
                order.UserName = user.Name;
                order.Phone = user.Phone;
            }

            await SendOrderToGroup(order);

            var doneKeyboard = new ReplyKeyboardMarkup(new[]
            {
                new KeyboardButton("🆕 Yangi buyurtma"),
                new KeyboardButton("📋 Holatimni tekshirish")
            })
            { ResizeKeyboard = true };

            await _bot.SendTextMessageAsync(chatId,
                "✅ Buyurtmangiz qabul qilindi!\n\n" +
                "📌 Buyurtma raqamingiz haydovchilarga yuborildi. Tez orada siz bilan bog'lanadi.",
                replyMarkup: doneKeyboard);

            _orders.ClearPendingOrder(chatId);
            return;
        }

        if (text == "🆕 Yangi buyurtma")
        {
            _orders.ClearPendingOrder(chatId);
            if (_users.Exists(chatId))
            {
                await _bot.SendTextMessageAsync(chatId, "📍 Qayerdan ketmoqchisiz? (jo'nash manzilini kiriting)");
                _orders.SetUserState(chatId, 3);
            }
            else
            {
                await _bot.SendTextMessageAsync(chatId, "Ismingizni kiriting:");
                _orders.SetUserState(chatId, 1);
            }
            return;
        }

        if (text == "📋 Holatimni tekshirish")
        {
            var activeOrders = _orders.GetActiveOrders()
                .Where(o => o.UserChatId == chatId).ToList();

            if (activeOrders.Count == 0)
            {
                await _bot.SendTextMessageAsync(chatId, "Sizda faol buyurtmalar yo'q.");
            }
            else
            {
                foreach (var o in activeOrders)
                {
                    var status = o.Status == OrderStatus.Accepted ? "✅ Haydovchi topildi" : "⏳ Haydovchi kutilmoqda";
                    await _bot.SendTextMessageAsync(chatId,
                        $"#{o.Id.ToString()[..8]}\n" +
                        $"📍 {o.PickupLocation} ➡ {o.DropoffLocation}\n" +
                        $"👤 {o.PassengerCount} kishi\n" +
                        $"📌 {status}");
                }
            }
            return;
        }

        await _bot.SendTextMessageAsync(chatId, "Iltimos, tugmalardan foydalaning yoki /start ni bosing.");
    }

    private async Task ShowMonthlyStats(long chatId)
    {
        var stats = _orders.GetMonthlyStats();
        if (stats.Count == 0)
        {
            await _bot.SendTextMessageAsync(chatId, "Hali buyurtmalar yo'q.");
            return;
        }

        var text = "📊 OYLIK STATISTIKA\n─────────────────\n\n";
        foreach (var s in stats)
        {
            text += $"📅 {s.Month} — {s.TotalOrders} ta buyurtma\n";
            int i = 1;
            foreach (var o in s.Orders)
            {
                var date = o.CreatedAt.ToString("dd.MM HH:mm");
                text += $"  {i}. {o.UserName} — {o.PickupLocation} → {o.DropoffLocation} ({date})\n";
                i++;
            }
            text += "\n";
        }

        var keyboard = new ReplyKeyboardMarkup(new[]
        {
            new[] { new KeyboardButton("📊 Statistika"), new KeyboardButton("🚗 Haydovchilar") }
        })
        { ResizeKeyboard = true };

        await _bot.SendTextMessageAsync(chatId, text, replyMarkup: keyboard);
    }

    private async Task ShowDriverStats(long chatId)
    {
        var stats = _orders.GetDriverStats();
        if (stats.Count == 0)
        {
            await _bot.SendTextMessageAsync(chatId, "Hali hech qanday buyurtma qabul qilinmagan.");
            return;
        }

        var text = "🚗 HAYDOVCHILAR STATISTIKASI\n─────────────────\n\n";
        foreach (var d in stats)
        {
            var name = !string.IsNullOrEmpty(d.Username) ? $"@{d.Username}" : d.Name;
            text += $"👤 {name} — {d.TotalOrders} ta buyurtma\n";
            int i = 1;
            foreach (var o in d.Orders)
            {
                var date = o.AcceptedAt?.ToString("dd.MM HH:mm") ?? "—";
                text += $"  {i}. {o.UserName} — {o.PickupLocation} → {o.DropoffLocation} ({date})\n";
                i++;
            }
            text += "\n";
        }

        var keyboard = new ReplyKeyboardMarkup(new[]
        {
            new[] { new KeyboardButton("📊 Statistika"), new KeyboardButton("🚗 Haydovchilar") }
        })
        { ResizeKeyboard = true };

        await _bot.SendTextMessageAsync(chatId, text, replyMarkup: keyboard);
    }

    private async Task SendOrderToGroup(Order order)
    {
        var contactLine = $"👤 {order.UserName}";
        if (!string.IsNullOrEmpty(order.UserTelegramUsername))
            contactLine += $" | 💬 @{order.UserTelegramUsername}";
        else
            contactLine += $" | 🔗 tg://user?id={order.UserChatId}";

        var msg = await _bot.SendTextMessageAsync(_groupId,
            $"🚖 YANGI BUYURTMA\n" +
            $"─────────────────\n" +
            $"{contactLine}\n" +
            $"📞 {order.Phone}\n" +
            $"📍 {order.PickupLocation} ➡️ {order.DropoffLocation}\n" +
            $"👥 {order.PassengerCount} kishi\n" +
            $"🕐 {order.CreatedAt:HH:mm}\n" +
            $"─────────────────",
            replyMarkup: new InlineKeyboardMarkup(new[]
            {
                new[]
                {
                    InlineKeyboardButton.WithCallbackData("👁 Ko'rish", $"view_{order.Id}"),
                    InlineKeyboardButton.WithCallbackData("✅ Qabul qildim", $"accept_{order.Id}")
                }
            }));

        order.GroupMessageId = msg.MessageId;
        order.GroupChatId = msg.Chat.Id;
        _orders.AddOrder(order);
    }

    private async Task HandleCallback(CallbackQuery cb)
    {
        try
        {
            var data = cb.Data ?? "";
            var msg = cb.Message;
            if (msg == null)
            {
                await _bot.AnswerCallbackQueryAsync(cb.Id);
                return;
            }

            if (data.StartsWith("view_"))
            {
                var id = Guid.Parse(data["view_".Length..]);
                var order = _orders.GetOrder(id);
                if (order == null)
                {
                    await _bot.AnswerCallbackQueryAsync(cb.Id, "Buyurtma topilmadi");
                    return;
                }

                if (order.Status != OrderStatus.Active)
                {
                    await _bot.AnswerCallbackQueryAsync(cb.Id, "Bu buyurtma allaqachon qabul qilingan");
                    return;
                }

                var (result, isNewViewer) = _orders.TryViewOrder(id,
                    cb.From.Id,
                    cb.From.Username,
                    cb.From.FirstName);

                if (result == OrderManager.ViewResult.Ok)
                {
                    var contactLine = $"👤 {order.UserName}\n📞 {order.Phone}";
                    if (!string.IsNullOrEmpty(order.UserTelegramUsername))
                        contactLine += $"\n💬 @{order.UserTelegramUsername}";
                    else
                        contactLine += $"\n🔗 tg://user?id={order.UserChatId}";

                    await _bot.AnswerCallbackQueryAsync(cb.Id, contactLine, showAlert: true);

                    if (isNewViewer)
                    {
                        var driverName = !string.IsNullOrEmpty(cb.From.Username)
                            ? "@" + cb.From.Username
                            : cb.From.FirstName ?? "Noma'lum";

                        await _bot.SendTextMessageAsync(_viewerGroupId,
                            $"👁 {driverName} buyurtmani ko'rdi\n" +
                            $"🆔 #{order.Id.ToString()[..8]}\n" +
                            $"📍 {order.PickupLocation} ➡ {order.DropoffLocation}");
                    }
                }
                else
                {
                    await _bot.AnswerCallbackQueryAsync(cb.Id,
                        "Hozir boshqa haydovchi ko'rmoqda, birozdan so'ng urinib ko'ring",
                        showAlert: true);
                }
                return;
            }

            if (data.StartsWith("accept_"))
            {
                var id = Guid.Parse(data["accept_".Length..]);
                var accepted = _orders.AcceptOrder(id,
                    cb.From.Id,
                    cb.From.Username,
                    cb.From.FirstName);

                if (!accepted)
                {
                    await _bot.AnswerCallbackQueryAsync(cb.Id, "Bu buyurtma allaqachon qabul qilingan", showAlert: true);
                    return;
                }

                var order = _orders.GetOrder(id);

                var driverName = !string.IsNullOrEmpty(order!.DriverUsername)
                    ? "@" + order.DriverUsername
                    : order.DriverName ?? "";

                await _bot.EditMessageTextAsync(msg.Chat.Id, msg.MessageId,
                    $"✅ BUYURTMA QABUL QILINDI\n" +
                    $"─────────────────\n" +
                    $"📍 Yo'nalish: {order.PickupLocation} ➡️ {order.DropoffLocation}\n" +
                    $"👥 Yo'lovchilar: {order.PassengerCount} kishi\n" +
                    $"🚗 Haydovchi: {driverName}\n" +
                    $"🕐 Vaqt: {order.CreatedAt:HH:mm}\n" +
                    $"─────────────────\n\n" +
                    $"ℹ️ Mijoz ma'lumotlari faqat haydovchiga ko'rsatilgan.");

                await _bot.AnswerCallbackQueryAsync(cb.Id, "✅ Buyurtma qabul qilindi! Mijoz bilan bog'lanishingiz mumkin.");

                await _bot.SendTextMessageAsync(_viewerGroupId,
                    $"✅ Buyurtma qabul qilindi\n" +
                    $"🆔 #{order.Id.ToString()[..8]}\n" +
                    $"🚗 Haydovchi: {driverName}\n" +
                    $"📍 {order.PickupLocation} ➡ {order.DropoffLocation}\n" +
                    $"👥 {order.PassengerCount} kishi");

                int viewerCount = order.ViewedByDrivers.Count;
                foreach (var viewer in order.ViewedByDrivers)
                {
                    var viewerName = !string.IsNullOrEmpty(viewer.Username)
                        ? "@" + viewer.Username
                        : viewer.Name ?? "Noma'lum";
                    await _bot.SendTextMessageAsync(_viewerGroupId,
                        $"  👁 {viewerName} — {viewer.ViewedAt:HH:mm}");
                }

                if (viewerCount == 0)
                    await _bot.SendTextMessageAsync(_viewerGroupId, "  (hech kim ko'rmagan)");

                return;
            }

            await _bot.AnswerCallbackQueryAsync(cb.Id);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Callback error: {ex.Message}");
            try { await _bot.AnswerCallbackQueryAsync(cb.Id); } catch { }
        }
    }
}
