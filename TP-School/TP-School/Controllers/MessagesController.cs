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
    // 🔥 ИСПРАВЛЕНО: Теперь принимаем ViewModel, которая корректно сопоставляется с полями формы.
    public async Task<IActionResult> Create(SendMessageViewModel model)
    {
        // 1. Проверка основных данных
        if (model.RecipientId == 0 || string.IsNullOrWhiteSpace(model.Body))
        {
            // Используем TempData, чтобы передать ошибку на страницу Index, если редирект происходит
            TempData["ErrorMessage"] = "Ошибка отправки: ID получателя или текст сообщения отсутствуют.";
            // Возврат на отправленные сообщения (как запасной вариант)
            return RedirectToAction(nameof(Index), new { filter = "sent" });
        }

        try
        {
            int currentUserId = GetCurrentUserId();

            // 2. Дополнительная проверка, что ID получателя существует
            if (await _context.Users.FindAsync(model.RecipientId) == null)
            {
                TempData["ErrorMessage"] = "Ошибка отправки: Получатель не найден в базе данных.";
                return RedirectToAction(nameof(Index), new { filter = "sent" });
            }

            // 3. Создаем и сохраняем сообщение
            var newMessage = new Message
            {
                FromUserId = currentUserId,
                ToUserId = model.RecipientId,      // Из ViewModel
                MessageText = model.Body,          // Из ViewModel
                SentAt = DateTime.Now,
                // IsRead = false, // Если не нужен статус прочтения, это поле можно удалить или не инициализировать
            };

            _context.Messages.Add(newMessage);
            await _context.SaveChangesAsync();

            // 4. 🔥 Ключевое изменение: Перенаправление на страницу отправленных сообщений
            TempData["SuccessMessage"] = "Сообщение успешно отправлено!";
            return RedirectToAction(nameof(Index), new { filter = "sent" });
        }
        catch (InvalidOperationException)
        {
            // Если GetCurrentUserId() выдал ошибку (нет ID)
            return Unauthorized();
        }
        catch (Exception)
        {
            TempData["ErrorMessage"] = "Произошла ошибка сервера при отправке сообщения.";
            return RedirectToAction(nameof(Index), new { filter = "sent" });
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