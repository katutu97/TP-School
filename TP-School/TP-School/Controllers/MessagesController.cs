using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using TP_School.Data;
using TP_School.Models;
using TP_School.ViewModels; 

[Authorize] // Доступ только для аутентифицированных
public class MessagesController : Controller
{
    private readonly ApplicationDbContext _context;

    public MessagesController(ApplicationDbContext context)
    {
        _context = context;
    }

    // Вспомогательный метод (как в HomeController)
    private int GetCurrentUserId()
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (int.TryParse(userIdClaim, out int userId))
        {
            return userId;
        }
        throw new InvalidOperationException("Не удалось получить ID текущего пользователя.");
    }

    // GET: /Messages/Index (Главная страница сообщений)
    public async Task<IActionResult> Index(string filter = "inbox")
    {
        int currentUserId = GetCurrentUserId();

        IQueryable<Message> messagesQuery;

        if (filter.ToLower() == "inbox")
        {
            // Входящие: сообщения, где ToUserId - это текущий пользователь
            messagesQuery = _context.Messages
                .Where(m => m.ToUserId == currentUserId)
                .OrderByDescending(m => m.SentAt);
        }
        else if (filter.ToLower() == "sent")
        {
            // Отправленные: сообщения, где FromUserId - это текущий пользователь
            messagesQuery = _context.Messages
                .Where(m => m.FromUserId == currentUserId)
                .OrderByDescending(m => m.SentAt);
        }
        else
        {
            // По умолчанию показываем входящие
            return RedirectToAction(nameof(Index), new { filter = "inbox" });
        }

        // Загружаем данные отправителя/получателя для отображения ФИО
        var messages = await messagesQuery
            .Include(m => m.FromUser)
            .Include(m => m.ToUser)
            .ToListAsync();

        ViewBag.Filter = filter;
        return View(messages);
    }

    // GET: /Messages/SearchUser?fullName=Иванов Иван Иванович
    [HttpGet]
    public async Task<IActionResult> SearchUser(string fullName)
    {
        if (string.IsNullOrWhiteSpace(fullName))
        {
            return Json(new { success = false, message = "ФИО не может быть пустым." });
        }

        // Предполагаем, что FullName хранится в вашей модели User
        // Ищем точное совпадение
        var user = await _context.Users
            .Where(u => u.FullName == fullName) // Используйте ваше реальное поле для ФИО
            .FirstOrDefaultAsync();

        if (user == null)
        {
            // ИСКЛЮЧЕНИЕ: Пользователь не найден в БД
            return Json(new { success = false, message = "Пользователь не найден." });
        }

        // Пользователь найден, возвращаем ID и подтвержденное имя
        return Json(new
        {
            success = true,
            userId = user.UserId,
            fullName = user.FullName
        });
    }

    // Файл: Controllers/MessagesController.cs (Добавьте в MessagesController)

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(int toUserId, string messageText)
    {
        // 1. Простая проверка
        if (toUserId == 0 || string.IsNullOrWhiteSpace(messageText))
        {
            return BadRequest(new { success = false, message = "Некорректные данные." });
        }

        try
        {
            int currentUserId = GetCurrentUserId();

            // 2. Дополнительная проверка, что ID существует (хотя JS уже проверил)
            if (await _context.Users.FindAsync(toUserId) == null)
            {
                return NotFound(new { success = false, message = "Получатель не найден в базе данных." });
            }

            // 3. Создаем сообщение
            var newMessage = new Message
            {
                FromUserId = currentUserId,
                ToUserId = toUserId,
                MessageText = messageText,
                SentAt = DateTime.Now,
                
            };

            _context.Messages.Add(newMessage);
            await _context.SaveChangesAsync();

            // 4. Возвращаем успешный ответ и перенаправляем на входящие
            return Json(new { success = true, message = "Сообщение успешно отправлено." });
            // Или перенаправьте пользователя на страницу Index: return RedirectToAction(nameof(Index), new { filter = "sent" });
        }
        catch (Exception ex)
        {
            // Логирование ошибки
            return StatusCode(500, new { success = false, message = "Ошибка сервера при отправке сообщения." });
        }
    }

    // Добавьте сюда методы Read (для прочтения), Delete (для удаления) и Create (для отправки нового сообщения)
}

