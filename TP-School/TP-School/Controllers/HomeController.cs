using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore; // Для работы с EF
using System.Diagnostics;
using System.Linq;
using System.Security.Claims; 
using TP_School.Data;
using TP_School.Models; 
using TP_School.ViewModels;


[Authorize]
public class HomeController : Controller
{
    private readonly ILogger<HomeController> _logger;
    private readonly ApplicationDbContext _context; 

    
    public HomeController(ILogger<HomeController> logger, ApplicationDbContext context)
    {
        _logger = logger;
        _context = context;
    }

    // Вспомогательный метод для получения ID текущего пользователя
    private int GetCurrentUserId()
    {
        // В ClaimTypes.NameIdentifier обычно хранится ID пользователя после аутентификации
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (int.TryParse(userIdClaim, out int userId))
        {
            return userId;
        }
        // В реальном приложении это должно быть исключение или обработка ошибки
        throw new InvalidOperationException("Не удалось получить ID текущего пользователя.");
    }

    // Вспомогательный метод для получения ClassId текущего учителя
    // Учитель может быть классным руководителем только одного класса.
    private async Task<int?> GetClassIdForCurrentUserAsync()
    {
        int userId = GetCurrentUserId();

        if (User.IsInRole("Учитель"))
        {
            // Логика для Учителя (Классный руководитель): ищем класс, где он является ClassTeacherId
            var schoolClass = await _context.SchoolClasses
                .Where(c => c.ClassTeacherId == userId)
                .Select(c => (int?)c.ClassId)
                .FirstOrDefaultAsync();
            return schoolClass;
        }
        else if (User.IsInRole("Ученик"))
        {
            // Логика для Ученика: ищем его ClassId через таблицу StudentClass (многие ко многим)
            var studentClass = await _context.StudentClasses
                .Where(sc => sc.StudentId == userId)
                .Select(sc => (int?)sc.ClassId)
                .FirstOrDefaultAsync(); // Берем ClassId первого найденного класса
            return studentClass;
        }
        else if (User.IsInRole("Родитель"))
        {
            // Логика для Родителя:
            // 1. Ищем ID всех детей, привязанных к этому родителю через StudentParents
            var studentIds = await _context.StudentParentses
                .Where(sp => sp.ParentId == userId)
                .Select(sp => sp.StudentId)
                .ToListAsync();

            if (studentIds.Any())
            {
                // 2. Ищем ClassId, к которому привязан один из этих студентов (если их несколько, берем первый)
                var classIdFromStudent = await _context.StudentClasses
                    .Where(sc => studentIds.Contains(sc.StudentId))
                    .Select(sc => (int?)sc.ClassId)
                    .FirstOrDefaultAsync();
                return classIdFromStudent;
            }

            return null;
        }

        // Для других ролей или не привязанных пользователей
        return null;
    }

    //---------------------------------------------------------

    // GET: Главная страница портала
    public async Task<IActionResult> Index()
    {
        ViewBag.FullName = User.Identity.Name ?? "Пользователь";

        // 0.Определяем граничную дату: ровно 7 дней назад
        // Объявления, опубликованные ДО этого момента, будут исключены.
        DateTime cutoffDate = DateTime.Now.AddDays(-7);

        // 1. Получаем ID класса для текущего учителя
        var classId = await GetClassIdForCurrentUserAsync();

        // 2. Загружаем объявления только для этого класса
        if (classId.HasValue)
        {
            var announcements = await _context.Announcements
                .Where(a => 
                    a.ClassId == classId.Value && // Фильтр по классу
                    a.CreatedAt >= cutoffDate)    // ДОБАВЛЯЕМ ФИЛЬТР ПО ДАТЕ:
                                              // Дата создания (CreatedAt) должна быть новее или равна дате 7 дней назад

                .OrderByDescending(a => a.CreatedAt)
                .ToListAsync();

            ViewBag.Announcements = announcements;
            ViewBag.ClassId = classId.Value; // Передаем ID класса во View для использования
        }
        else
        {
            ViewBag.Announcements = new List<Announcement>();
            ViewBag.ClassId = null;
        }

        return View();
    }

    //---------------------------------------------------------

    // POST: Обработка создания нового объявления
    [HttpPost]
    [Authorize(Roles = "Учитель")] // Убедитесь, что роли соответствуют вашим
    [ValidateAntiForgeryToken] // Защита от CSRF
    public async Task<IActionResult> CreateAnnouncement([FromForm] AnnouncementViewModel model)
    {
        // Проверяем ViewModel на ошибки (например, пустое поле)
        if (!ModelState.IsValid)
        {
            // Если данные некорректны, можно вернуть ошибку или модальное окно с ошибками
            return BadRequest(new { success = false, message = "Некорректные данные формы." });
        }

        try
        {
            // 1. Получаем ID класса, к которому привязано объявление
            var classId = await GetClassIdForCurrentUserAsync();

            if (!classId.HasValue)
            {
                // Учитель не является классным руководителем, ему нельзя добавлять объявления
                return Forbid();
            }

            // 2. Создаем новую модель БД
            var newAnnouncement = new Announcement
            {
                Title = model.Title,
                Text = model.Text,
                ClassId = classId.Value, // Привязываем к классу учителя
                CreatedAt = DateTime.Now // Устанавливаем текущую дату и время
            };

            // 3. Сохраняем в БД
            _context.Announcements.Add(newAnnouncement);
            await _context.SaveChangesAsync();

            // 4. Возвращаем успешный ответ (например, JSON, чтобы обновить страницу через JS)
            return Json(new { success = true, message = "Объявление успешно опубликовано." });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка при создании объявления.");
            return StatusCode(500, new { success = false, message = "Произошла ошибка сервера при сохранении." });
        }
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }
}