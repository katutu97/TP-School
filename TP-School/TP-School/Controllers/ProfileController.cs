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

        
        var user = await _context.Users
            .Include(u => u.Role)
            .Include(u => u.ClassesAsTeacher)
            // Для ученика: StudentClasses -> Class (для ClassInfo)
            .Include(u => u.StudentClasses)
                .ThenInclude(sc => sc.Class)
            // Для родителя: StudentParentsAsParent
            .Include(u => u.StudentParentsAsParent)
            .FirstOrDefaultAsync(u => u.UserId == currentUserId);

        if (user == null) { return NotFound("Данные профиля не найдены."); }

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

        // Строка, в которой будет храниться ФИО классного руководителя
        string homeroomTeacherName = null;

        // Логика для динамических полей и поиска классного руководителя 

        if (model.RoleName == "Ученик")
        {
            var studentClass = user.StudentClasses.FirstOrDefault();
            model.ClassInfo = studentClass?.Class?.ClassName ?? "Не определен";

            
            if (studentClass?.Class != null)
            {
                
                var teacher = await _context.Users
                    .FirstOrDefaultAsync(u => u.UserId == studentClass.Class.ClassTeacherId);
                homeroomTeacherName = teacher?.FullName;
            }
        }
        else if (model.RoleName == "Учитель")
        {
            model.ClassInfo = user.ClassesAsTeacher.FirstOrDefault()?.ClassName ?? "Нет классного руководства";
        }
        else if (model.RoleName == "Родитель")
        {
            var studentParent = user.StudentParentsAsParent.FirstOrDefault();

            if (studentParent != null)
            {
                int studentId = studentParent.StudentId;

                
                var studentClass = await _context.StudentClasses
                    .Where(sc => sc.StudentId == studentId)
                    .Include(sc => sc.Class) 
                    .FirstOrDefaultAsync();

                if (studentClass?.Class != null)
                {
                    
                    var teacher = await _context.Users
                        .FirstOrDefaultAsync(u => u.UserId == studentClass.Class.ClassTeacherId);
                    homeroomTeacherName = teacher?.FullName;

                    
                    var studentUser = await _context.Users.FirstOrDefaultAsync(u => u.UserId == studentId);
                    model.StudentInfo = studentUser?.FullName ?? "Ученик не найден";
                }
                else
                {
                    model.StudentInfo = "Нет привязанного ученика";
                }
            }
        }

        //  ИМЯ КЛАССНОГО РУКОВОДИТЕЛЯ ЧЕРЕЗ VIEWBAG
        ViewBag.HomeroomTeacher = homeroomTeacherName;

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
            FromUserId = senderId, 
            ToUserId = directorId.Value,
            MessageText = body, 
            SentAt = DateTime.Now 
        };

        _context.Messages.Add(message);
        await _context.SaveChangesAsync();

        return Json(new { success = true, message = "Сообщение директору успешно отправлено." });
    }
}