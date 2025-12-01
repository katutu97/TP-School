using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
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

    // Вспомогательный метод
    private int GetCurrentUserId()
    {
        // В ClaimTypes.NameIdentifier обычно хранится ID пользователя после аутентификации
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (int.TryParse(userIdClaim, out int userId))
        {
            return userId;
        }
        // В реальном приложении это должно быть исключение
        throw new InvalidOperationException("Не удалось получить ID текущего пользователя.");
    }

    // -------------------------------------------------------------
    // GET: /Messages/Index (Главная страница сообщений)
    // -------------------------------------------------------------
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
            return RedirectToAction(nameof(Index), new { filter = "inbox" });
        }

        // Загружаем данные отправителя/получателя для отображения ФИО
        var messages = await messagesQuery
            .Include(m => m.FromUser)
            .Include(m => m.ToUser)
            .ToListAsync();

        // --- ДОБАВЛЕНИЕ ЛОГИКИ ПОЛУЧЕНИЯ РОЛЕЙ ИЗ БД ---
        var roles = await _context.Roles
            .Select(r => r.RoleName)
            .OrderBy(name => name)
            .ToListAsync();

        ViewBag.RecipientRoles = roles;
        // ---------------------------------------------

        ViewBag.Filter = filter;
        return View(messages);
    }

    // -------------------------------------------------------------
    // GET: /Messages/SearchUser (Автодополнение по роли и ФИО)
    // -------------------------------------------------------------
    [HttpGet]
    public async Task<IActionResult> SearchUser(string fullName, string role)
    {
        // Проверка минимальной длины ввода
        if (string.IsNullOrWhiteSpace(fullName) || fullName.Length < 1)
        {
            return Json(new List<object>());
        }

        // 1. Ищем RoleId по RoleName
        var targetRole = await _context.Roles
                                       .FirstOrDefaultAsync(r => r.RoleName == role);

        if (targetRole == null)
        {
            return Json(new List<object>());
        }

        // 2. Подготовка поисковых частей (нижний регистр для регистронезависимого поиска)
        var parts = fullName.Trim().ToLower().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

        var query = _context.Users
            .Where(u => u.RoleId == targetRole.RoleId); // Фильтрация по RoleId

        // 3. Добавляем гибкий поиск по частям ФИО
        foreach (var part in parts)
        {
            // Ищем в Фамилии, Имени или Отчестве (нижний регистр)
            query = query.Where(u => u.LastName.ToLower().Contains(part) ||
                                     u.FirstName.ToLower().Contains(part) ||
                                     (u.MiddleName != null && u.MiddleName.ToLower().Contains(part)));
        }

        // 4. Выбираем данные, сортируем по алфавиту и ограничиваем результат
        var foundUsers = await query
            .OrderBy(u => u.LastName) // Сортировка по фамилии
            .Take(10) // Ограничение на количество подсказок
            .Select(u => new
            {
                userId = u.UserId,
                fullName = u.FullName
            })
            .ToListAsync();

        return Json(foundUsers);
    }

    // -------------------------------------------------------------
    // POST: /Messages/Create (Отправка нового сообщения)
    // -------------------------------------------------------------
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(int toUserId, string messageText)
    {
        // 1. Простая проверка
        if (toUserId == 0 || string.IsNullOrWhiteSpace(messageText))
        {
            return BadRequest(new { success = false, message = "Некорректные данные: ID получателя или текст сообщения отсутствуют." });
        }

        try
        {
            int currentUserId = GetCurrentUserId();

            // 2. Дополнительная проверка, что ID получателя существует
            if (await _context.Users.FindAsync(toUserId) == null)
            {
                return NotFound(new { success = false, message = "Получатель не найден в базе данных." });
            }

            // 3. Создаем и сохраняем сообщение
            var newMessage = new Message
            {
                FromUserId = currentUserId,
                ToUserId = toUserId,
                MessageText = messageText,
                SentAt = DateTime.Now,
            };

            _context.Messages.Add(newMessage);
            await _context.SaveChangesAsync();

            // 4. Возвращаем успешный ответ (для обновления страницы через JS)
            return Json(new { success = true, message = "Сообщение успешно отправлено." });
        }
        catch (Exception ex)
        {
            // Логирование ошибки
            // _logger.LogError(ex, "Ошибка при отправке сообщения."); // Если бы был инжектирован ILogger
            return StatusCode(500, new { success = false, message = "Ошибка сервера при отправке сообщения." });
        }
    }

    // -------------------------------------------------------------
    // POST: /Messages/Delete/{id} (Удаление сообщения)
    // -------------------------------------------------------------
    [HttpPost, ActionName("Delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteConfirmed(int id)
    {
        var message = await _context.Messages.FindAsync(id);

        if (message == null)
        {
            return NotFound();
        }

        int currentUserId = GetCurrentUserId();

        // Проверка: сообщение может удалить только отправитель (sent) или получатель (inbox)
        if (message.FromUserId != currentUserId && message.ToUserId != currentUserId)
        {
            return Forbid(); // Запрещаем удаление, если пользователь не имеет отношения к сообщению
        }

        try
        {
            _context.Messages.Remove(message);
            await _context.SaveChangesAsync();

            // Определяем, откуда удалено сообщение, чтобы перенаправить пользователя
            string filter = (message.ToUserId == currentUserId) ? "inbox" : "sent";

            // Перенаправляем обратно на страницу сообщений
            return RedirectToAction(nameof(Index), new { filter = filter });
        }
        catch (Exception ex)
        {
            // Логирование
            return StatusCode(500, "Произошла ошибка при удалении сообщения.");
        }
    }
}