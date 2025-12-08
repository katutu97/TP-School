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
        // Определяем, какие сообщения показывать
        if (filter.ToLower() == "inbox")
        {
            messagesQuery = _context.Messages
                .Where(m => m.ToUserId == currentUserId && m.Status != MessageStatus.Archived)
                .OrderBy(m => m.Status)
                .ThenByDescending(m => m.SentAt);
        }
        else if (filter.ToLower() == "sent")
        {
            // Отправленные: сообщения, где FromUserId - это текущий пользователь
            messagesQuery = _context.Messages
                .Where(m => m.FromUserId == currentUserId)
                .OrderByDescending(m => m.SentAt);
        }
        else if (filter.ToLower() == "archive")
        {
            // Архив: сообщения, где ToUserId - текущий пользователь И в архиве
            messagesQuery = _context.Messages
                .Where(m => m.ToUserId == currentUserId && m.Status == MessageStatus.Archived)
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

        // Получаем роли для модального окна
        var roles = await _context.Roles
            .Select(r => r.RoleName)
            .OrderBy(name => name)
            .ToListAsync();

        ViewBag.RecipientRoles = roles;
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
                Status = MessageStatus.New,
                
            };

            _context.Messages.Add(newMessage);
            await _context.SaveChangesAsync();

            // 4. Перенаправление на страницу отправленных сообщений
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

        // Удалить можно ТОЛЬКО из ОТПРАВЛЕННЫХ, И ТОЛЬКО если статус "Новое"
        if (message.FromUserId == currentUserId && message.Status == MessageStatus.New)
        {
            try
            {
                _context.Messages.Remove(message);
                await _context.SaveChangesAsync();
                return RedirectToAction(nameof(Index), new { filter = "sent" });
            }
            catch (Exception)
            {
                TempData["ErrorMessage"] = "Произошла ошибка при удалении сообщения.";
                return RedirectToAction(nameof(Index), new { filter = "sent" });
            }
        }
        else if (message.FromUserId == currentUserId)
        {
            // Если не новое, но отправитель
            TempData["ErrorMessage"] = "Можно удалить только отправленное сообщение со статусом 'Новое'.";
            return RedirectToAction(nameof(Index), new { filter = "sent" });
        }

        // Если пользователь не является отправителем (а значит получатель), или не соответствует условиям
        return Forbid();
    }
    // -------------------------------------------------------------
    // POST: /Messages/MarkAsRead/{id} (Пометить как прочитанное) - ВОССТАНОВЛЕНО
    // -------------------------------------------------------------
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> MarkAsRead(int id)
    {
        int currentUserId = GetCurrentUserId();

        var message = await _context.Messages
            .FirstOrDefaultAsync(m => m.MessageId == id && m.ToUserId == currentUserId && m.Status == MessageStatus.New);

        if (message == null)
            return NotFound();

        message.Status = MessageStatus.Read;

        await _context.SaveChangesAsync();

        TempData["SuccessMessage"] = "Сообщение помечено как прочитанное.";
        return RedirectToAction(nameof(Index), new { filter = "inbox" });
    }
    // -------------------------------------------------------------
    // POST: /Messages/Archive/{id} (Архивация входящего сообщения) 
    // -------------------------------------------------------------
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Archive(int id)
    {
        int currentUserId = GetCurrentUserId();

        // Ищем сообщение, которое адресовано текущему пользователю И не в архиве
        var message = await _context.Messages
            .FirstOrDefaultAsync(m => m.MessageId == id && m.ToUserId == currentUserId && m.Status != MessageStatus.Archived);

        if (message == null)
            return NotFound();

        // Если было New, оно становится Read, прежде чем архивироваться. 
        // Но так как кнопка появляется только для Read, достаточно просто установить Archived.
        message.Status = MessageStatus.Archived;

        await _context.SaveChangesAsync();

        TempData["SuccessMessage"] = "Сообщение архивировано.";
        return RedirectToAction(nameof(Index), new { filter = "inbox" });
    }

    // -------------------------------------------------------------
    // POST: /Messages/Restore/{id} (Восстановление из архива)
    // -------------------------------------------------------------
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Restore(int id)
    {
        int currentUserId = GetCurrentUserId();

        // Ищем сообщение, которое адресовано текущему пользователю И в архиве
        var message = await _context.Messages
            .FirstOrDefaultAsync(m => m.MessageId == id && m.ToUserId == currentUserId && m.Status == MessageStatus.Archived);

        if (message == null)
            return NotFound();

        // Возвращаем из архива в статус Прочитано
        message.Status = MessageStatus.Read;

        await _context.SaveChangesAsync();

        TempData["SuccessMessage"] = "Сообщение восстановлено из архива.";
        return RedirectToAction(nameof(Index), new { filter = "inbox" });
    }

}