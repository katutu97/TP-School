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
using TP_School.Extensions;

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
        public async Task<IActionResult> Index(DateTime? date)
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
                int diff = (7 + (selectedDate.DayOfWeek - DayOfWeek.Monday)) % 7;
                var startOfWeek = selectedDate.AddDays(-diff).Date;
                var endOfWeek = startOfWeek.AddDays(6).Date;

                ViewBag.StartOfWeek = startOfWeek.ToString("yyyy-MM-dd");
                ViewBag.CurrentDate = selectedDate.ToString("yyyy-MM-dd");
                ViewBag.DateDisplay = selectedDate.ToString("dd MMMM yyyy", new CultureInfo("ru-RU"));
                ViewBag.Role = userRole;

                if (userRole == "Ученик" || userRole == "Родитель")
                {
                    return await GetPersonalSchedule(userId, userRole, startOfWeek, endOfWeek, selectedDate);
                }
                else if (userRole == "Учитель" || userRole == "Директор")
                {
                    // Получаем параметры фильтрации из строки запроса
                    var filterType = HttpContext.Request.Query["filterType"].ToString();
                    int? selectedId = HttpContext.Request.Query.ContainsKey("selectedId") && int.TryParse(HttpContext.Request.Query["selectedId"], out int idValue) ? idValue : (int?)null;

                    return await GetAdminSchedule(userId, userRole, startOfWeek, endOfWeek, selectedDate, filterType, selectedId);
                }
                else
                {
                    return Forbid();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка в Index методе: {ex.Message}");
                Console.WriteLine($"StackTrace: {ex.StackTrace}");

                ViewBag.ErrorMessage = "Произошла ошибка при загрузке расписания. Пожалуйста, попробуйте позже.";
                return View("SchedulePersonal", CreateEmptyScheduleModel(DateTime.Today, DateTime.Today, true, false));
            }
        }

        private async Task<IActionResult> GetPersonalSchedule(int userId, string role, DateTime startOfWeek, DateTime endOfWeek, DateTime date)
        {
            try
            {
                int studentIdForGrades = userId; // Для поиска в таблице Grades
                string studentName = "";
                bool isParent = (role == "Родитель");

                ViewBag.IsParent = isParent;

                // Если родитель, находим его ребенка
                if (role == "Родитель")
                {
                    var student = await _context.StudentParentses
                        .Where(sp => sp.ParentId == userId)
                        .Include(sp => sp.Student)
                        .Select(sp => new {
                            sp.StudentId,
                            sp.Student.FullName,
                            sp.Student.UserId
                        })
                        .FirstOrDefaultAsync();

                    if (student == null)
                    {
                        ViewBag.ErrorMessage = "У вас нет привязанных учеников.";
                        return View("SchedulePersonal", CreateEmptyScheduleModel(date, startOfWeek, true, false));
                    }

                    studentIdForGrades = student.UserId;
                    studentName = student.FullName;

                    ViewBag.SelectedChildName = studentName;
                }
                else if (role == "Ученик")
                {
                    var student = await _context.Users
                        .FirstOrDefaultAsync(u => u.UserId == userId);

                    if (student != null)
                    {
                        studentName = student.FullName;
                        ViewBag.SelectedChildName = studentName;
                    }
                }

                // Находим класс ученика
                var studentClassEntry = await _context.StudentClasses
                    .Include(sc => sc.Class)
                    .Include(sc => sc.Student)
                    .FirstOrDefaultAsync(sc =>
                        (role == "Ученик" && sc.Student.UserId == userId) ||
                        (role == "Родитель" && sc.Student.UserId == studentIdForGrades));

                if (studentClassEntry == null)
                {
                    ViewBag.ErrorMessage = role == "Ученик"
                        ? "Вы не назначены в класс."
                        : "Ученик не назначен в класс.";
                    return View("SchedulePersonal", CreateEmptyScheduleModel(date, startOfWeek, true, false));
                }

                var classId = studentClassEntry.ClassId;
                var className = studentClassEntry.Class.ClassName;

                var classTeacher = await _context.Users
                    .FirstOrDefaultAsync(u => u.UserId == studentClassEntry.Class.ClassTeacherId);

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

                // Получаем оценки ученика для этих уроков
                Dictionary<int, (int? GradeValue, string Comment)> studentGrades = new Dictionary<int, (int? GradeValue, string Comment)>();

                if (scheduleEntries.Any())
                {
                    var lessonIds = scheduleEntries.Select(s => s.LessonId).ToList();

                    try
                    {
                        var grades = await _context.Grades
                            .Where(g => g.StudentId == studentIdForGrades && lessonIds.Contains(g.LessonId))
                            .Select(g => new
                            {
                                g.LessonId,
                                g.GradeValue,
                                g.Comment
                            })
                            .ToListAsync();

                        foreach (var grade in grades)
                        {
                            studentGrades[grade.LessonId] = (grade.GradeValue, grade.Comment);
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Ошибка при получении оценок: {ex.Message}");
                    }
                }

                ViewBag.ClassName = className;
                ViewBag.ClassTeacher = classTeacher?.FullName ?? "Не назначен";
                ViewBag.ClassId = classId;
                ViewBag.StudentName = studentName;
                ViewBag.LessonTimes = LessonTimeMap;

                var model = CreateScheduleModel(scheduleEntries, date, startOfWeek, true, false, studentGrades);
                model.SelectedClassId = classId;

                return View("SchedulePersonal", model);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка в GetPersonalSchedule: {ex.Message}");
                Console.WriteLine($"StackTrace: {ex.StackTrace}");

                ViewBag.ErrorMessage = $"Ошибка при загрузке расписания: {ex.Message}";
                return View("SchedulePersonal", CreateEmptyScheduleModel(date, startOfWeek, true, false));
            }
        }

        private async Task<IActionResult> GetAdminSchedule(int userId, string role, DateTime startOfWeek, DateTime endOfWeek, DateTime date, string filterType, int? selectedId)
        {
            try
            {
                // 1. Определение типа фильтрации и ID по умолчанию
                if (string.IsNullOrEmpty(filterType))
                {
                    // По умолчанию отображаем расписание текущего учителя/директора
                    filterType = role == "Учитель" ? "Teacher" : "Class";
                }

                if (selectedId == null)
                {
                    // Если ID не передан, устанавливаем значение по умолчанию
                    if (filterType == "Teacher" && role == "Учитель")
                    {
                        selectedId = userId;
                    }
                    else if (filterType == "Class")
                    {
                        // Для класса берем первый доступный класс (для директора - любой, для учителя - свой)
                        if (role == "Директор")
                        {
                            selectedId = await _context.SchoolClasses.Select(c => (int?)c.ClassId).FirstOrDefaultAsync();
                        }
                        else // Учитель
                        {
                            selectedId = await _context.ClassSubjectTeachers
                                .Where(cst => cst.TeacherId == userId)
                                .Select(cst => (int?)cst.ClassId)
                                .FirstOrDefaultAsync();
                        }
                    }
                }

                // 2. Загрузка справочников для фильтров
                var availableTeachers = await _context.Users
                    .Where(u => u.Role.RoleName == "Учитель" || u.Role.RoleName == "Директор") 
                    .OrderBy(u => u.FullName)
                    .ToListAsync();

                var availableClasses = await _context.SchoolClasses
                    .OrderBy(c => c.ClassNumber)
                    .ThenBy(c => c.ClassLetter)
                    .ToListAsync();

                // 3. Загрузка и объединение данных (Schedule + ScheduleTemplate)
                // Вызов нового вспомогательного метода LoadScheduleItemsAsync
                var scheduleItems = await LoadScheduleItemsAsync(startOfWeek, endOfWeek, filterType, selectedId, LessonTimeMap);

                // 4. Построение ViewModel
                var viewModel = new AdminScheduleViewModel
                {
                    StartOfWeek = startOfWeek,
                    FilterType = filterType,
                    SelectedTeacherId = filterType == "Teacher" ? selectedId : null,
                    SelectedClassId = filterType == "Class" ? selectedId : null,
                    AvailableTeachers = availableTeachers.Where(u => u.Role.RoleName == "Учитель").ToList(), // Показываем только учителей
                    AvailableClasses = availableClasses,
                    ScheduleByDay = scheduleItems
                        .GroupBy(i => i.DayOfWeek)
                        .ToDictionary(g => g.Key, g => g.OrderBy(i => i.LessonNumber).ToList())
                };

                // Добавляем пустые дни недели в словарь
                var days = new[] { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday };
                foreach (var day in days)
                {
                    if (!viewModel.ScheduleByDay.ContainsKey(day))
                    {
                        viewModel.ScheduleByDay.Add(day, new List<AdminScheduleItemViewModel>());
                    }
                }

                return View("ScheduleAdmin", viewModel);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка в GetAdminSchedule: {ex.Message}");
                Console.WriteLine($"StackTrace: {ex.StackTrace}");

                ViewBag.ErrorMessage = $"Произошла ошибка при загрузке расписания: {ex.Message}";

                // Возврат пустой модели для административного представления
                return View("ScheduleAdmin", new AdminScheduleViewModel
                {
                    StartOfWeek = startOfWeek,
                    ScheduleByDay = new Dictionary<DayOfWeek, List<AdminScheduleItemViewModel>>(),
                    AvailableTeachers = new List<User>(),
                    AvailableClasses = new List<SchoolClass>()
                });
            }
        }
        // -----------------------------------------------------------------------
        // --- Вспомогательный метод загрузки и объединения данных (Schedule + Template) ---
        // -----------------------------------------------------------------------
        private async Task<List<AdminScheduleItemViewModel>> LoadScheduleItemsAsync(
            DateTime startOfWeek,
            DateTime endOfWeek,
            string filterType,
            int? selectedId,
            Dictionary<int, string> lessonTimes)
        {
            if (!selectedId.HasValue)
            {
                return new List<AdminScheduleItemViewModel>();
            }

            // 1. Загрузка индивидуальных записей (Schedule) за неделю
            var customLessonsQuery = _context.Schedules
                .Include(s => s.Class)
                .Include(s => s.Subject)
                .Include(s => s.Teacher)
                .Where(s => s.Date >= startOfWeek && s.Date <= endOfWeek);

            // Применяем фильтр к индивидуальным записям
            if (filterType == "Teacher")
            {
                customLessonsQuery = customLessonsQuery.Where(s => s.TeacherId == selectedId.Value);
            }
            else if (filterType == "Class")
            {
                customLessonsQuery = customLessonsQuery.Where(s => s.ClassId == selectedId.Value);
            }

            var customLessons = await customLessonsQuery.ToListAsync();

            // 2. Преобразование кастомных уроков в ViewModel
            var items = customLessons.Select(s => new AdminScheduleItemViewModel
            {
                ScheduleId = s.LessonId,
                DayOfWeek = s.Date.DayOfWeek,
                LessonNumber = s.LessonNumber,
                LessonTime = lessonTimes.GetValueOrDefault(s.LessonNumber, "N/A"),
                ClassId = s.ClassId,
                ClassName = s.Class?.ClassName ?? "—",
                SubjectId = s.SubjectId,
                SubjectName = s.Subject?.SubjectName ?? "—",
                TeacherId = s.TeacherId,
                TeacherFullName = s.Teacher?.FullName ?? "—",
                Classroom = s.Room,
                IsCustomLesson = true // Флаг, что это индивидуальная запись
            }).ToList();

            // 3. Добавление уроков из Шаблона (ScheduleTemplate)
            // Список уже занятых слотов (День недели + Номер урока + Класс/Учитель)
            // Поскольку Schedule может быть изменением, затрагивающим класс ИЛИ учителя, 
            // нам нужно проверить, перекрывает ли индивидуальный урок шаблон.
            var overriddenSlots = customLessons
                .Select(s => new { Day = s.Date.DayOfWeek, s.LessonNumber, s.ClassId, s.TeacherId })
                .ToHashSet();

            for (int i = 0; i < 5; i++) // С Понедельника (0) по Пятницу (4)
            {
                DayOfWeek currentDay = (DayOfWeek)(((int)DayOfWeek.Monday + i) % 7);
                byte currentDayByte = (byte)currentDay;

                // Загрузка шаблонов, соответствующих фильтру
                var templateQuery = _context.ScheduleTemplates
                    .Include(t => t.Class)
                    .Include(t => t.Subject)
                    .Include(t => t.Teacher)
                    .Where(t => t.DayOfWeek == currentDayByte);

                if (filterType == "Teacher")
                {
                    templateQuery = templateQuery.Where(t => t.TeacherId == selectedId.Value);
                }
                else if (filterType == "Class")
                {
                    templateQuery = templateQuery.Where(t => t.ClassId == selectedId.Value);
                }

                var templates = await templateQuery.ToListAsync();

                foreach (var template in templates)
                {
                    // Проверяем, перекрывает ли этот шаблон индивидуальная запись
                    bool isOverridden = overriddenSlots.Any(slot =>
                        slot.Day == currentDay &&
                        slot.LessonNumber == template.LessonNumber &&
                        (filterType == "Teacher" ? slot.TeacherId == selectedId.Value : slot.ClassId == selectedId.Value)
                    );

                    if (!isOverridden)
                    {
                        // Если кастомной записи нет, добавляем шаблон
                        items.Add(new AdminScheduleItemViewModel
                        {
                            ScheduleId = null, // Шаблон
                            DayOfWeek = currentDay,
                            LessonNumber = template.LessonNumber,
                            LessonTime = lessonTimes.GetValueOrDefault(template.LessonNumber, "N/A"),
                            ClassId = template.ClassId,
                            ClassName = template.Class?.ClassName ?? "—",
                            SubjectId = template.SubjectId,
                            SubjectName = template.Subject?.SubjectName ?? "—",
                            TeacherId = template.TeacherId,
                            TeacherFullName = template.Teacher?.FullName ?? "—",
                            Classroom = template.Room,
                            IsCustomLesson = false
                        });
                    }
                }
            }

            return items;
        }
        // -----------------------------------------------------------------------
        // --- МЕТОД ГЕНЕРАЦИИ РАСПИСАНИЯ ДЛЯ АДМИНИСТРАТОРА (ДИРЕКТОРА) ---
        // -----------------------------------------------------------------------
        [HttpPost]
        [Authorize(Roles = "Директор")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> GenerateScheduleFromTemplate(DateTime startDate, DateTime endDate)
        {
            if (startDate > endDate || startDate < DateTime.Today.Date)
            {
                TempData["ErrorMessage"] = "Неверный диапазон дат или дата начала в прошлом.";
                return RedirectToAction(nameof(Index), new { date = startDate.ToString("yyyy-MM-dd") });
            }

            var templates = await _context.ScheduleTemplates.ToListAsync();
            var newLessons = new List<Schedule>();
            int lessonsAdded = 0;

            for (DateTime date = startDate.Date; date <= endDate.Date; date = date.AddDays(1))
            {
                if (date.DayOfWeek == DayOfWeek.Saturday || date.DayOfWeek == DayOfWeek.Sunday)
                    continue;

                byte currentDayOfWeek = (byte)date.DayOfWeek;

                // Получаем существующие уроки (индивидуальные изменения) для этой даты
                var existingLessonsForDay = await _context.Schedules
                    .Where(s => s.Date.Date == date.Date)
                    .Select(s => new { s.LessonNumber, s.ClassId })
                    .ToListAsync();

                var relevantTemplates = templates.Where(t => t.DayOfWeek == currentDayOfWeek);

                foreach (var template in relevantTemplates)
                {
                    // Если урока для этого класса и этого номера УЖЕ НЕТ в базе (т.е. нет индивидуального изменения)
                    if (!existingLessonsForDay.Any(l => l.LessonNumber == template.LessonNumber && l.ClassId == template.ClassId))
                    {
                        newLessons.Add(new Schedule
                        {
                            Date = date,
                            LessonNumber = template.LessonNumber,
                            ClassId = template.ClassId,
                            SubjectId = template.SubjectId,
                            TeacherId = template.TeacherId,
                            Room = template.Room,
                            LessonTopic = null,
                            HomeworkText = null
                        });
                        lessonsAdded++;
                    }
                }
            }

            if (newLessons.Any())
            {
                _context.Schedules.AddRange(newLessons);
                await _context.SaveChangesAsync();
                TempData["SuccessMessage"] = $"Успешно добавлено {lessonsAdded} записей расписания за период с {startDate.ToShortDateString()} по {endDate.ToShortDateString()}.";
            }
            else
            {
                TempData["InfoMessage"] = "В указанном диапазоне не найдено новых записей для генерации (все слоты либо уже заняты, либо нет шаблонов).";
            }

            return RedirectToAction(nameof(Index), new { date = startDate.ToString("yyyy-MM-dd") });
        }

        [HttpPost]
        [Authorize(Roles = "Ученик")] // Только для учеников
        [ValidateAntiForgeryToken]
        [RequestSizeLimit(52428800)]
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

            var safeStudentAnswer = studentAnswer?.Trim() ?? string.Empty;

            if (safeStudentAnswer.Length == 0 && fileData == null)
            {
                return BadRequest(new { success = false, message = "Пожалуйста, введите текст ответа или прикрепите файл." });
            }

            var homeworkEntry = new Homework
            {
                LessonId = lessonId,
                Date = lesson.Date,
                Text = safeStudentAnswer,
                FilePath = fileData ?? new byte[0],
                StudentId = studentId
            };

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
            catch (Microsoft.EntityFrameworkCore.DbUpdateException dbEx)
            {
                var innerMessage = dbEx.InnerException?.Message ?? dbEx.Message;
                return StatusCode(500, new { success = false, message = $"Ошибка базы данных: {innerMessage}" });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = $"Непредвиденная ошибка сервера: {ex.Message}" });
            }
        }

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

        [HttpGet]
        public async Task<IActionResult> GetAllTeachers()
        {
            var teachers = await _context.Users
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

        [HttpGet]
        public async Task<IActionResult> GetLessonById(int id)
        {
            var lesson = await _context.Schedules
                .Where(s => s.LessonId == id)
                .Select(s => new
                {
                    s.LessonId,
                    Date = s.Date.ToString("yyyy-MM-dd"),
                    s.LessonNumber,
                    s.SubjectId,
                    TeacherId = s.TeacherId,
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

        private ScheduleViewModel CreateScheduleModel(
            List<Schedule> entries,
            DateTime selectedDate,
            DateTime startOfWeek,
            bool isPersonal,
            bool isAdmin,
            Dictionary<int, (int? GradeValue, string Comment)> studentGrades = null)
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

            if (entries != null)
            {
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
                            HomeworkText = s.HomeworkText,
                            Grade = studentGrades != null && studentGrades.ContainsKey(s.LessonId)
                                ? studentGrades[s.LessonId].GradeValue
                                : null,
                            GradeComment = studentGrades != null && studentGrades.ContainsKey(s.LessonId)
                                ? studentGrades[s.LessonId].Comment
                                : null
                        }).OrderBy(i => i.LessonNumber).ToList();

                        scheduleByDay[dayOfWeek] = items;
                    }
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