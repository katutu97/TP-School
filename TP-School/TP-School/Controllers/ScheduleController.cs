using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using TP_School.Data;
using TP_School.Models;
using TP_School.ViewModels;
using Microsoft.EntityFrameworkCore;

namespace TP_School.Controllers
{
    [Authorize]
    public class ScheduleController : Controller
    {
        private readonly ApplicationDbContext _context;

        public ScheduleController(ApplicationDbContext context)
        {
            _context = context;
        }

        private int GetCurrentUserId()
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            return int.TryParse(userIdClaim, out int userId) ? userId : 0;
        }

        [HttpGet]
        public async Task<IActionResult> Index(int? classId, DateTime? date, int? childId)
        {
            try
            {
                var userId = GetCurrentUserId();
                var userRole = User.FindFirst(ClaimTypes.Role)?.Value;

                if (userId == 0 || string.IsNullOrEmpty(userRole))
                {
                    return Unauthorized();
                }

                // Определение текущей недели
                var selectedDate = date ?? DateTime.Today;
                // Считаем дни от понедельника (DayOfWeek.Monday)
                int diff = (7 + (selectedDate.DayOfWeek - DayOfWeek.Monday)) % 7;
                var startOfWeek = selectedDate.AddDays(-diff).Date;
                var endOfWeek = startOfWeek.AddDays(6).Date;

                ViewBag.StartOfWeek = startOfWeek.ToString("yyyy-MM-dd");
                ViewBag.CurrentDate = selectedDate.ToString("yyyy-MM-dd");
                ViewBag.DateDisplay = selectedDate.ToString("dd MMMM yyyy", new CultureInfo("ru-RU"));
                ViewBag.Role = userRole;

                if (userRole == "Ученик" || userRole == "Родитель")
                {
                    return await GetPersonalSchedule(userId, userRole, startOfWeek, endOfWeek, selectedDate, childId);
                }
                else if (userRole == "Учитель" || userRole == "Директор")
                {
                    return await GetAdminSchedule(userId, userRole, classId, startOfWeek, endOfWeek, selectedDate);
                }
                else
                {
                    return Forbid();
                }
            }
            catch (Exception ex)
            {
                // Рекомендуется использовать ILogger
                return View("Error");
            }
        }

        // -- ДЛЯ УЧЕНИКОВ И РОДИТЕЛЕЙ (только просмотр) --
        private async Task<IActionResult> GetPersonalSchedule(int userId, string role, DateTime startOfWeek, DateTime endOfWeek, DateTime date, int? childId = null)
        {
            int studentId = userId;
            string studentName = "";
            // Инициализируем ID для навигации
            int selectedContextId = userId;

            // Если это родитель, проверяем, выбран ли конкретный ребенок
            if (role == "Родитель")
            {
                // Используем переданный childId
                int requestedChildId = childId ?? 0;

                // Получаем всех детей родителя
                var children = await _context.StudentParentses
                    .Where(sp => sp.ParentId == userId)
                    .Include(sp => sp.Student)
                    .Select(sp => new {
                        sp.StudentId,
                        sp.Student.FullName,
                        sp.Student.UserId
                    })
                    .ToListAsync();

                if (!children.Any())
                {
                    ViewBag.ErrorMessage = "У вас нет привязанных учеников.";
                    return View("SchedulePersonal", CreateEmptyScheduleModel(date, startOfWeek, true, false));
                }

                // Находим выбранного ребенка, либо берем первого, если requestedChildId недействителен
                var selectedChild = children.FirstOrDefault(c => c.StudentId == requestedChildId) ?? children.First();
                studentId = selectedChild.StudentId;
                studentName = selectedChild.FullName;
                selectedContextId = studentId; // Устанавливаем ID выбранного ребенка

                ViewBag.Children = children;
                ViewBag.SelectedChildId = selectedContextId; // *** КЛЮЧЕВОЕ ИСПРАВЛЕНИЕ: Устанавливаем ID для навигации ***
                ViewBag.SelectedChildName = studentName;
            }
            else if (role == "Ученик")
            {
                // Получаем данные ученика
                var student = await _context.Users
                    .FirstOrDefaultAsync(u => u.UserId == userId);

                if (student != null)
                {
                    studentName = student.FullName;
                    ViewBag.SelectedChildName = studentName;
                }
                ViewBag.SelectedChildId = userId; // *** КЛЮЧЕВОЕ ИСПРАВЛЕНИЕ: Устанавливаем ID для навигации ученика ***
            }

            // Находим класс ученика через таблицу StudentClasses
            var studentClassEntry = await _context.StudentClasses
                .Include(sc => sc.Class)
                .Include(sc => sc.Student)
                .FirstOrDefaultAsync(sc => sc.StudentId == studentId);

            if (studentClassEntry == null)
            {
                ViewBag.ErrorMessage = role == "Ученик"
                    ? "Вы не назначены в класс."
                    : "Ученик не назначен в класс.";
                return View("SchedulePersonal", CreateEmptyScheduleModel(date, startOfWeek, true, false));
            }

            var classId = studentClassEntry.ClassId;
            var className = studentClassEntry.Class.ClassName;

            // Получаем классного руководителя
            var classTeacher = await _context.Users
                .FirstOrDefaultAsync(u => u.UserId == studentClassEntry.Class.ClassTeacherId);

            // Получаем расписание для ЭТОГО КОНКРЕТНОГО КЛАССА
            var scheduleEntries = await _context.Schedules
                .Include(s => s.Subject)
                .Include(s => s.Teacher)
                .Include(s => s.Class)
                .Where(s => s.ClassId == classId &&
                            s.Date >= startOfWeek &&
                            s.Date <= endOfWeek)
                .OrderBy(s => s.Date)
                .ThenBy(s => s.LessonNumber)
                .ToListAsync();

            // Устанавливаем ViewBag для передачи данных в представление
            ViewBag.ClassName = className;
            ViewBag.ClassTeacher = classTeacher?.FullName ?? "Не назначен";
            ViewBag.ClassId = classId;
            ViewBag.IsParent = (role == "Родитель");
            ViewBag.StudentName = studentName;
            ViewBag.IsStudent = (role == "Ученик");
            ViewBag.LessonTimes = LessonTimeMap;


            // Создаем модель с данными конкретного ученика
            var model = CreateScheduleModel(scheduleEntries, date, startOfWeek, true, false);
            model.SelectedClassId = classId;

            return View("SchedulePersonal", model);
        }

        // --- ДЛЯ УЧИТЕЛЕЙ И ДИРЕКТОРОВ (администрирование) ---
        private async Task<IActionResult> GetAdminSchedule(int userId, string role, int? selectedClassId, DateTime startOfWeek, DateTime endOfWeek, DateTime date)
        {
            List<SchoolClass> availableClasses;

            if (role == "Директор")
            {
                availableClasses = await _context.SchoolClasses
                    .OrderBy(c => c.ClassNumber)
                    .ThenBy(c => c.ClassLetter)
                    .ToListAsync();
            }
            else // Учитель
            {
                availableClasses = await _context.ClassSubjectTeachers
                    .Where(cst => cst.TeacherId == userId)
                    .Include(cst => cst.Class)
                    .Select(cst => cst.Class)
                    .Distinct()
                    .OrderBy(c => c.ClassNumber)
                    .ThenBy(c => c.ClassLetter)
                    .ToListAsync();
            }

            if (!availableClasses.Any())
            {
                ViewBag.ErrorMessage = "Нет доступных классов.";
                return View("ScheduleAdmin", CreateEmptyScheduleModel(date, startOfWeek, false, true));
            }

            // *** Убеждаемся, что classId установлен и доступен для навигации ***
            int classId = selectedClassId ?? availableClasses.First().ClassId;
            ViewBag.SelectedClassId = classId;
            // ********************************************************************

            ViewBag.AvailableClasses = availableClasses;
            ViewBag.IsDirector = (role == "Директор");

            // Извлекаем информацию о выбранном классе (включая классного руководителя)
            var selectedClass = await _context.SchoolClasses
                .Include(c => c.ClassTeacher)
                .FirstOrDefaultAsync(c => c.ClassId == classId);

            if (selectedClass != null)
            {
                ViewBag.ClassTeacherFullName = selectedClass.ClassTeacher?.FullName;
                ViewBag.ClassName = selectedClass.ClassName;
            }

            // Загрузка расписания
            var scheduleEntries = await _context.Schedules
                .Include(s => s.Subject)
                .Include(s => s.Teacher)
                .Where(s => s.ClassId == classId && s.Date >= startOfWeek && s.Date <= endOfWeek)
                .OrderBy(s => s.Date)
                .ThenBy(s => s.LessonNumber)
                .ToListAsync();

            ViewBag.LessonTimes = LessonTimeMap;
            var model = CreateScheduleModel(scheduleEntries, date, startOfWeek, false, true);
            model.SelectedClassId = classId;

            return View("ScheduleAdmin", model);
        }

        // --- МЕТОД ДЛЯ ОТПРАВКИ ДОМАШНЕГО ЗАДАНИЯ ---

        [HttpPost]
        [Authorize(Roles = "Ученик, Родитель")]
        [ValidateAntiForgeryToken]
        [Microsoft.AspNetCore.Mvc.RequestSizeLimit(52428800)]
        public async Task<IActionResult> SubmitHomework(int lessonId, string studentAnswer, IFormFileCollection homeworkFiles)
        {
            var studentId = GetCurrentUserId();

            if (studentId == 0 || lessonId == 0)
            {
                return BadRequest(new { success = false, message = "Не удалось определить ученика или урок." });
            }

            var lesson = await _context.Schedules.FirstOrDefaultAsync(s => s.LessonId == lessonId);
            if (lesson == null)
            {
                return NotFound(new { success = false, message = "Урок не найден." });
            }

            // 1. Обработка файла
            byte[] fileData = null;
            var file = homeworkFiles?.FirstOrDefault();

            if (file != null && file.Length > 0)
            {
                if (file.Length > 10 * 1024 * 1024)
                {
                    return BadRequest(new { success = false, message = $"Файл '{file.FileName}' слишком большой (макс. 10MB)." });
                }

                using (var memoryStream = new System.IO.MemoryStream())
                {
                    await file.CopyToAsync(memoryStream);
                    fileData = memoryStream.ToArray();
                }
            }

            // 2. Безопасное получение текстового ответа (обрезка и замена NULL на string.Empty)
            var safeStudentAnswer = studentAnswer?.Trim() ?? string.Empty;


            // Проверка, что отправлен хотя бы текст или файл
            if (safeStudentAnswer.Length == 0 && fileData == null)
            {
                return BadRequest(new { success = false, message = "Пожалуйста, введите текст ответа или прикрепите файл." });
            }

            // 3. Создание записи с гарантией NOT NULL
            var homeworkEntry = new Homework
            {
                LessonId = lessonId,
                Date = lesson.Date,
                // ИСПРАВЛЕНИЕ: Передаем безопасную строку
                Text = safeStudentAnswer,

                // КЛЮЧЕВОЕ ИСПРАВЛЕНИЕ: Передаем пустой массив, если файл отсутствует (null)
                FilePath = fileData ?? new byte[0],

                StudentId = studentId
            };

            // 4. Сохранение в базу данных
            try
            {
                var existingHomework = await _context.Homeworks
                    .FirstOrDefaultAsync(h => h.LessonId == lessonId && h.StudentId == studentId);

                if (existingHomework != null)
                {
                    existingHomework.Text = homeworkEntry.Text;
                    existingHomework.FilePath = homeworkEntry.FilePath;
                    _context.Homeworks.Update(existingHomework);
                }
                else
                {
                    _context.Homeworks.Add(homeworkEntry);
                }

                await _context.SaveChangesAsync();

                return Ok(new { success = true, message = "Домашнее задание успешно отправлено!" });
            }
            // Используем DbUpdateException для получения детальной ошибки от MySQL
            catch (Microsoft.EntityFrameworkCore.DbUpdateException dbEx)
            {
                // Для отладки
                var innerMessage = dbEx.InnerException?.Message ?? dbEx.Message;
                return StatusCode(500, new { success = false, message = $"Ошибка базы данных: {innerMessage}" });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = $"Непредвиденная ошибка сервера: {ex.Message}" });
            }
        }
        // 1. Метод для получения всех предметов (для выпадающего списка)
        [HttpGet]
        public async Task<IActionResult> GetAllSubjects()
        {
            var subjects = await _context.Subjects
                .Select(s => new {
                    SubjectId = s.SubjectId,
                    SubjectName = s.SubjectName
                })
                .OrderBy(s => s.SubjectName)
                .ToListAsync();

            return Json(subjects);
        }

        // 2. Метод для получения всех учителей (для выпадающего списка)
        [HttpGet]
        public async Task<IActionResult> GetAllTeachers()
        {
            var teachers = await _context.Users
                // ИСПРАВЛЕНИЕ: Сравниваем Role.RoleName со строкой
                .Where(u => u.Role.RoleName == "Teacher")
                .Select(u => new
                {
                    TeacherId = u.UserId,
                    FullName = u.FullName,
                    
                })
                .OrderBy(u => u.FullName)
                .ToListAsync();

            return Json(teachers);
        }


        // 3. Метод для получения данных конкретного урока (для редактирования)
        [HttpGet]
        public async Task<IActionResult> GetLessonById(int id)
        {
            var lesson = await _context.Schedules
                .Where(s => s.LessonId == id)
                .Select(s => new
                {
                    s.LessonId,
                    Date = s.Date.ToString("yyyy-MM-dd"), // Форматируем для input type="date"
                    s.LessonNumber,
                    s.SubjectId,
                    TeacherId = s.TeacherId, // Учитель может быть Nullable, если его нет
                    ClassId = s.ClassId,
                    Classroom = s.Class,
                    LessonTopic = s.LessonTopic,
                    HomeworkText = s.HomeworkText
                })
                .FirstOrDefaultAsync();

            if (lesson == null)
            {
                return NotFound();
            }

            return Json(lesson);
        }

        // --- ВСПОМОГАТЕЛЬНЫЕ МЕТОДЫ ---
        private ScheduleViewModel CreateScheduleModel(List<Schedule> entries, DateTime selectedDate, DateTime startOfWeek, bool isPersonal, bool isAdmin)
        {
            var scheduleByDay = new Dictionary<DayOfWeek, List<ScheduleItemViewModel>>();

            for (int i = 0; i < 7; i++)
            {
                var dayOfWeek = (DayOfWeek)(((int)DayOfWeek.Monday + i) % 7);
                if (dayOfWeek != DayOfWeek.Sunday) // Исключаем воскресенье
                {
                    scheduleByDay.Add(dayOfWeek, new List<ScheduleItemViewModel>());
                }
            }

            foreach (var dayGroup in entries.GroupBy(s => s.Date.DayOfWeek))
            {
                var dayOfWeek = dayGroup.Key;
                if (scheduleByDay.ContainsKey(dayOfWeek))
                {
                    var items = dayGroup.Select(s => new ScheduleItemViewModel
                    {
                        ScheduleId = s.LessonId,
                        LessonNumber = s.LessonNumber,
                        LessonTime = GetLessonTime(s.LessonNumber),
                        Date = s.Date,
                        SubjectId = s.SubjectId,
                        SubjectName = s.Subject?.SubjectName ?? "Неизвестный предмет",
                        TeacherId = s.TeacherId,
                        TeacherFullName = s.Teacher?.FullName ?? "Неизвестный учитель",
                        Classroom = s.Room,
                        LessonTopic = s.LessonTopic,
                        HomeworkText = s.HomeworkText
                    }).OrderBy(i => i.LessonNumber).ToList();

                    scheduleByDay[dayOfWeek] = items;
                }
            }

            return new ScheduleViewModel
            {
                ScheduleByDay = scheduleByDay,
                SelectedDate = selectedDate,
                StartOfWeek = startOfWeek,
                IsPersonalView = isPersonal,
                IsAdminView = isAdmin
            };
        }

        private ScheduleViewModel CreateEmptyScheduleModel(DateTime selectedDate, DateTime startOfWeek, bool isPersonal, bool isAdmin)
        {
            var scheduleByDay = new Dictionary<DayOfWeek, List<ScheduleItemViewModel>>();
            for (int i = 0; i < 7; i++)
            {
                var dayOfWeek = (DayOfWeek)(((int)DayOfWeek.Monday + i) % 7);
                if (dayOfWeek != DayOfWeek.Sunday)
                {
                    scheduleByDay.Add(dayOfWeek, new List<ScheduleItemViewModel>());
                }
            }
            return new ScheduleViewModel
            {
                ScheduleByDay = scheduleByDay,
                SelectedDate = selectedDate,
                StartOfWeek = startOfWeek,
                IsPersonalView = isPersonal,
                IsAdminView = isAdmin
            };
        }

        public static string GetLessonTime(int lessonNumber)
        {
            if (LessonTimeMap.ContainsKey(lessonNumber))
            {
                return LessonTimeMap[lessonNumber];
            }
            return "";
        }

        // Время начала и конца уроков
        public static readonly Dictionary<int, string> LessonTimeMap = new Dictionary<int, string>
        {
            { 1, "08:30 - 10:00" },
            { 2, "10:10 - 11:40" },
            { 3, "11:50 - 13:20" },
            { 4, "14:00 - 15:30" },
            { 5, "15:40 - 17:10" },
            { 6, "17:20 - 18:50" }
        };
    }
}