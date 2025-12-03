using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using TP_School.Data;
using TP_School.Models;
using TP_School.ViewModels;

[Authorize]
public class ProfileController : Controller
{
    private readonly ApplicationDbContext _context;

    public ProfileController(ApplicationDbContext context)
    {
        _context = context;
    }

    // GET: /Profile
    public async Task<IActionResult> Index()
    {
        var userIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userIdClaim) || !int.TryParse(userIdClaim, out int currentUserId))
        {
            return NotFound("Пользователь не авторизован или ID недействителен.");
        }

        // Загружаем пользователя и все необходимые связи
        var user = await _context.Users
            .Include(u => u.Role)
            .Include(u => u.ClassesAsTeacher) // Для учителя
            .Include(u => u.StudentClasses) // Для ученика
            .Include(u => u.StudentParentsAsParent) // Для родителя (чтобы получить детей)
                .ThenInclude(sp => sp.Student) // Если StudentParents имеет навигацию к Student
            .FirstOrDefaultAsync(u => u.UserId == currentUserId);

        if (user == null)
        {
            return NotFound("Данные профиля не найдены.");
        }

        var model = new ProfileViewModel
        {
            UserId = user.UserId,
            LastName = user.LastName,
            FirstName = user.FirstName,
            MiddleName = user.MiddleName,
            Login = user.Login,
            Email = user.Email,
            Phone = user.Phone,
            BirthDate = user.BirthDate,
            RoleName = user.Role.RoleName,
            Info = user.Info
        };

        // Логика для динамических полей
        if (model.RoleName == "Ученик")
        {
            // Берем SchoolClass из StudentClasses (если это связь многие-ко-многим, берем первый)
            model.ClassInfo = user.StudentClasses.FirstOrDefault()?.Class?.ClassName ?? "Не определен";
        }
        else if (model.RoleName == "Учитель")
        {
            // Классное руководство
            model.ClassInfo = user.ClassesAsTeacher.FirstOrDefault()?.ClassName ?? "Нет классного руководства";
        }
        else if (model.RoleName == "Родитель")
        {
            // Берем имя первого ребенка из StudentParentsAsParent
            model.StudentInfo = user.StudentParentsAsParent.FirstOrDefault()?.Student?.FullName ?? "Нет привязанного ученика";
        }

        // Получение ID Директора для кнопки сообщения
        ViewBag.DirectorId = await GetDirectorIdAsync();

        return View(model);
    }

    // Вспомогательный метод для поиска директора
    private async Task<int?> GetDirectorIdAsync()
    {
        var director = await _context.Users
            .Include(u => u.Role)
            .FirstOrDefaultAsync(u => u.Role.RoleName == "Директор");
        return director?.UserId;
    }

    // POST: /Profile/SendMessageToDirector
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SendMessageToDirector(string body)
    {
        var directorId = await GetDirectorIdAsync();
        var senderIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);

        if (!directorId.HasValue)
        {
            return Json(new { success = false, message = "Директор не найден в системе." });
        }

        if (string.IsNullOrEmpty(body))
        {
            return Json(new { success = false, message = "Сообщение не может быть пустым." });
        }

        if (!int.TryParse(senderIdClaim, out int senderId))
        {
            return Json(new { success = false, message = "Ошибка аутентификации отправителя." });
        }

        var message = new Message
        {
            FromUserId = senderId, // <-- ИСПОЛЬЗУЕМ ВАШЕ СВОЙСТВО
            ToUserId = directorId.Value, // <-- ИСПОЛЬЗУЕМ ВАШЕ СВОЙСТВО
            MessageText = body, // <-- ИСПОЛЬЗУЕМ ВАШЕ СВОЙСТВО
            SentAt = DateTime.Now // <-- ИСПОЛЬЗУЕМ ВАШЕ СВОЙСТВО
        };

        _context.Messages.Add(message);
        await _context.SaveChangesAsync();

        return Json(new { success = true, message = "Сообщение директору успешно отправлено." });
    }
}