using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore; // Для работы с EF
using System.Diagnostics;
using System.Linq;
using System.Security.Claims; // Для работы с User Claims
using TP_School.Data;
using TP_School.Models; // Ваши модели
using TP_School.ViewModels;

// Контроллер главной страницы. Доступ только для аутентифицированных пользователей.
[Authorize]
public class HomeController : Controller
{
    private readonly ILogger<HomeController> _logger;
    private readonly ApplicationDbContext _context; // Предполагается, что это ваш контекст БД

    // Обновленный конструктор с инжекцией DbContext
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
    private async Task<int?> GetClassIdForCurrentTeacherAsync()
    {
        int teacherId = GetCurrentUserId();

        // Ищем класс, где текущий пользователь является ClassTeacherId
        var schoolClass = await _context.SchoolClasses
            .Where(c => c.ClassTeacherId == teacherId)
            .FirstOrDefaultAsync();

        return schoolClass?.ClassId;
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
        var classId = await GetClassIdForCurrentTeacherAsync();

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
            var classId = await GetClassIdForCurrentTeacherAsync();

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