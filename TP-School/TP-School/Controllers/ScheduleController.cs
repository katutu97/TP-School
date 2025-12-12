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
    // Доступ только для авторизованных пользователей
    [Authorize]
    public class ScheduleController : Controller
    {
        private readonly ApplicationDbContext _context;

        public ScheduleController(ApplicationDbContext context)
        {
            _context = context;
        }

        // Получение ID текущего пользователя 
        private int GetCurrentUserId()
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            return int.TryParse(userIdClaim, out int userId) ? userId : 0;
        }

        // ОСНОВНОЙ МЕТОД: Отображение расписания
        [HttpGet]
        public async Task<IActionResult> Index(DateTime? date)
        {
            try
            {
                var userId = GetCurrentUserId(); // ID текущего пользователя
                var userRole = User.FindFirst(ClaimTypes.Role)?.Value; // Роль пользователя

                // Проверка авторизации
                if (userId == 0 || string.IsNullOrEmpty(userRole))
                {
                    return Unauthorized();
                }

                // Определение выбранной даты (или сегодняшней)
                var selectedDate = date ?? DateTime.Today;

                // Расчет начала недели (понедельник)
                int diff = (7 + (selectedDate.DayOfWeek - DayOfWeek.Monday)) % 7;
                var startOfWeek = selectedDate.AddDays(-diff).Date;
                var endOfWeek = startOfWeek.AddDays(6).Date;

                // Передача данных в ViewBag для представления
                ViewBag.StartOfWeek = startOfWeek.ToString("yyyy-MM-dd");
                ViewBag.CurrentDate = selectedDate.ToString("yyyy-MM-dd");
                ViewBag.DateDisplay = selectedDate.ToString("dd MMMM yyyy", new CultureInfo("ru-RU"));
                ViewBag.Role = userRole;

                // Маршрутизация в зависимости от роли пользователя
                if (userRole == "Ученик" || userRole == "Родитель")
                {
                    // Личное расписание для учеников и родителей
                    return await GetPersonalSchedule(userId, userRole, startOfWeek, endOfWeek, selectedDate);
                }
                else if (userRole == "Учитель" || userRole == "Директор")
                {
                    // Административное расписание для учителей и директоров
                    var filterType = HttpContext.Request.Query["filterType"].ToString(); // Тип фильтра
                    int? selectedId = HttpContext.Request.Query.ContainsKey("selectedId") &&
                                      int.TryParse(HttpContext.Request.Query["selectedId"], out int idValue)
                                      ? idValue : (int?)null; // ID выбранного элемента

                    return await GetAdminSchedule(userId, userRole, startOfWeek, endOfWeek, selectedDate, filterType, selectedId);
                }
                else
                {
                    return Forbid(); // Доступ запрещен для других ролей
                }
            }
            catch (Exception ex)
            {
                // Логирование ошибок
                Console.WriteLine($"Ошибка в Index методе: {ex.Message}");
                Console.WriteLine($"StackTrace: {ex.StackTrace}");

                ViewBag.ErrorMessage = "Произошла ошибка при загрузке расписания. Пожалуйста, попробуйте позже.";

                // Возврат пустой модели в случае ошибки
                return View("SchedulePersonal", CreateEmptyScheduleModel(DateTime.Today, DateTime.Today, true, false));
            }
        }


        // МЕТОД: Получение личного расписания для ученика/родителя
        private async Task<IActionResult> GetPersonalSchedule(int userId, string role, DateTime startOfWeek, DateTime endOfWeek, DateTime date)
        {
            try
            {
                int studentUserId = 0; // ID ученика
                string studentName = ""; // Имя ученика
                bool isParent = (role == "Родитель"); // Флаг родителя

                ViewBag.IsParent = isParent;

                // 1. Определение ученика (для родителей - привязанный ребенок)
                if (role == "Родитель")
                {
                    var studentParent = await _context.StudentParentses
                        .Include(sp => sp.Student) // Включаем данные ученика
                        .FirstOrDefaultAsync(sp => sp.ParentId == userId);

                    if (studentParent == null)
                    {
                        ViewBag.ErrorMessage = "У вас нет привязанных учеников.";
                        return View("SchedulePersonal", CreateEmptyScheduleModel(date, startOfWeek, true, false));
                    }

                    studentUserId = studentParent.StudentId;
                    studentName = studentParent.Student?.FullName ?? "Неизвестно";
                    ViewBag.SelectedChildName = studentName;
                }
                else if (role == "Ученик")
                {
                    studentUserId = userId; // Сам ученик

                    var student = await _context.Users
                        .FirstOrDefaultAsync(u => u.UserId == userId);

                    if (student != null)
                    {
                        studentName = student.FullName;
                        ViewBag.SelectedChildName = studentName;
                    }
                }

                if (studentUserId == 0)
                {
                    ViewBag.ErrorMessage = "Не удалось определить ученика.";
                    return View("SchedulePersonal", CreateEmptyScheduleModel(date, startOfWeek, true, false));
                }

                // 2. Получение класса ученика
                var studentClass = await _context.StudentClasses
                    .Include(sc => sc.Class) // Включаем данные класса
                    .FirstOrDefaultAsync(sc => sc.StudentId == studentUserId);

                if (studentClass == null)
                {
                    ViewBag.ErrorMessage = "Ученик не назначен в класс.";
                    return View("SchedulePersonal", CreateEmptyScheduleModel(date, startOfWeek, true, false));
                }

                var classId = studentClass.ClassId;
                var className = studentClass.Class?.ClassName ?? "Неизвестный класс";

                // 3. Получение классного руководителя
                var classTeacher = await _context.Users
                    .FirstOrDefaultAsync(u => u.UserId == studentClass.Class.ClassTeacherId);

                // 4. Загрузка расписания на неделю
                var scheduleEntries = await _context.Schedules
                    .Include(s => s.Subject) // Включаем предмет
                    .Include(s => s.Teacher) // Включаем учителя
                    .Include(s => s.Class) // Включаем класс
                    .Where(s => s.ClassId == classId &&
                                s.Date >= startOfWeek &&
                                s.Date <= endOfWeek) // Фильтр по датам недели
                    .OrderBy(s => s.Date)
                    .ThenBy(s => s.LessonNumber) // Сортировка по дате и номеру урока
                    .ToListAsync();

                // 5. Загрузка оценок ученика за эти уроки
                Dictionary<int, (int? GradeValue, string Comment)> studentGrades = new Dictionary<int, (int? GradeValue, string Comment)>();

                if (scheduleEntries.Any())
                {
                    var lessonIds = scheduleEntries.Select(s => s.LessonId).ToList();

                    var grades = await _context.Grades
                        .Where(g => g.StudentId == studentUserId && lessonIds.Contains(g.LessonId))
                        .Select(g => new { g.LessonId, g.GradeValue, g.Comment })
                        .ToListAsync();

                    // Сохранение оценок в словарь для быстрого доступа
                    foreach (var grade in grades)
                    {
                        studentGrades[grade.LessonId] = (grade.GradeValue, grade.Comment);
                    }
                }

                // 6. Передача данных в ViewBag
                ViewBag.ClassName = className;
                ViewBag.ClassTeacher = classTeacher?.FullName ?? "Не назначен";
                ViewBag.ClassId = classId;
                ViewBag.StudentName = studentName;
                ViewBag.LessonTimes = LessonTimeMap; // Время уроков

                // 7. Создание модели представления
                var model = CreateScheduleModel(scheduleEntries, date, startOfWeek, true, false, studentGrades);
                model.SelectedClassId = classId;

                return View("SchedulePersonal", model);
            }
            catch (Exception ex)
            {
                // Обработка ошибок
                Console.WriteLine($"Ошибка в GetPersonalSchedule: {ex.Message}");
                Console.WriteLine($"StackTrace: {ex.StackTrace}");

                ViewBag.ErrorMessage = $"Ошибка при загрузке расписания: {ex.Message}";
                return View("SchedulePersonal", CreateEmptyScheduleModel(date, startOfWeek, true, false));
            }
        }

        // МЕТОД: Получение административного расписания для учителя/директора
        private async Task<IActionResult> GetAdminSchedule(int userId, string role, DateTime startOfWeek, DateTime endOfWeek, DateTime date, string filterType, int? selectedId)
        {
            try
            {
                // 1. Определение типа фильтра по умолчанию
                if (string.IsNullOrEmpty(filterType))
                {
                    filterType = role == "Учитель" ? "Teacher" : "Class";
                }

                // 2. Определение выбранного ID по умолчанию
                if (selectedId == null)
                {
                    if (filterType == "Teacher" && role == "Учитель")
                    {
                        selectedId = userId; // Учитель видит свое расписание
                    }
                    else if (filterType == "Class")
                    {
                        if (role == "Директор")
                        {
                            // Директор видит первый класс по умолчанию
                            selectedId = await _context.SchoolClasses.Select(c => (int?)c.ClassId).FirstOrDefaultAsync();
                        }
                        else
                        {
                            // Учитель видит первый доступный ему класс
                            selectedId = await _context.ClassSubjectTeachers
                                .Where(cst => cst.TeacherId == userId)
                                .Select(cst => (int?)cst.ClassId)
                                .FirstOrDefaultAsync();
                        }
                    }
                }

                // 3. Загрузка доступных учителей и классов
                var availableTeachers = await _context.Users
                    .Where(u => u.Role.RoleName == "Учитель" || u.Role.RoleName == "Директор")
                    .OrderBy(u => u.FullName)
                    .ToListAsync();

                var availableClasses = await _context.SchoolClasses
                    .OrderBy(c => c.ClassNumber)
                    .ThenBy(c => c.ClassLetter)
                    .ToListAsync();

                // 4. Загрузка элементов расписания
                var scheduleItems = await LoadScheduleItemsAsync(startOfWeek, endOfWeek, filterType, selectedId, LessonTimeMap);

                // 5. Создание модели представления
                var viewModel = new AdminScheduleViewModel
                {
                    StartOfWeek = startOfWeek,
                    FilterType = filterType,
                    SelectedTeacherId = filterType == "Teacher" ? selectedId : null,
                    SelectedClassId = filterType == "Class" ? selectedId : null,
                    AvailableTeachers = availableTeachers.Where(u => u.Role.RoleName == "Учитель").ToList(),
                    AvailableClasses = availableClasses,
                    // Группировка расписания по дням недели
                    ScheduleByDay = scheduleItems
                        .GroupBy(i => i.DayOfWeek)
                        .ToDictionary(g => g.Key, g => g.OrderBy(i => i.LessonNumber).ToList())
                };

                // 6. Добавление пустых дней для полной структуры
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
                // Обработка ошибок
                Console.WriteLine($"Ошибка в GetAdminSchedule: {ex.Message}");
                Console.WriteLine($"StackTrace: {ex.StackTrace}");

                ViewBag.ErrorMessage = $"Произошла ошибка при загрузке расписания: {ex.Message}";

                // Возврат пустой модели
                return View("ScheduleAdmin", new AdminScheduleViewModel
                {
                    StartOfWeek = startOfWeek,
                    ScheduleByDay = new Dictionary<DayOfWeek, List<AdminScheduleItemViewModel>>(),
                    AvailableTeachers = new List<User>(),
                    AvailableClasses = new List<SchoolClass>()
                });
            }
        }

        // МЕТОД: Загрузка элементов расписания 
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

            // 1. Загрузка кастомных уроков (измененные расписания)
            var customLessonsQuery = _context.Schedules
                .Include(s => s.Class)
                .Include(s => s.Subject)
                .Include(s => s.Teacher)
                .Where(s => s.Date >= startOfWeek && s.Date <= endOfWeek); // Фильтр по неделе

            // Применение фильтрации
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
                LessonTime = lessonTimes.GetValueOrDefault(s.LessonNumber, "N/A"), // Время из маппинга
                ClassId = s.ClassId,
                ClassName = s.Class?.ClassName ?? "—",
                SubjectId = s.SubjectId,
                SubjectName = s.Subject?.SubjectName ?? "—",
                TeacherId = s.TeacherId,
                TeacherFullName = s.Teacher?.FullName ?? "—",
                Classroom = s.Room,
                IsCustomLesson = true // Флаг кастомного урока
            }).ToList();

            // 3. Определение переопределенных слотов (чтобы не дублировать шаблоны)
            var overriddenSlots = customLessons
                .Select(s => new { Day = s.Date.DayOfWeek, s.LessonNumber, s.ClassId, s.TeacherId })
                .ToHashSet();

            // 4. Загрузка шаблонов расписания (для незаполненных слотов)
            for (int i = 0; i < 5; i++) // Только рабочие дни (пн-пт)
            {
                DayOfWeek currentDay = (DayOfWeek)(((int)DayOfWeek.Monday + i) % 7);
                byte currentDayByte = (byte)currentDay;

                var templateQuery = _context.ScheduleTemplates
                    .Include(t => t.Class)
                    .Include(t => t.Subject)
                    .Include(t => t.Teacher)
                    .Where(t => t.DayOfWeek == currentDayByte); // Фильтр по дню недели

                // Применение фильтрации к шаблонам
                if (filterType == "Teacher")
                {
                    templateQuery = templateQuery.Where(t => t.TeacherId == selectedId.Value);
                }
                else if (filterType == "Class")
                {
                    templateQuery = templateQuery.Where(t => t.ClassId == selectedId.Value);
                }

                var templates = await templateQuery.ToListAsync();

                // 5. Добавление шаблонных уроков, которые не переопределены
                foreach (var template in templates)
                {
                    bool isOverridden = overriddenSlots.Any(slot =>
                        slot.Day == currentDay &&
                        slot.LessonNumber == template.LessonNumber &&
                        (filterType == "Teacher" ? slot.TeacherId == selectedId.Value : slot.ClassId == selectedId.Value)
                    );

                    // Если слот не переопределен, добавляем шаблон
                    if (!isOverridden)
                    {
                        items.Add(new AdminScheduleItemViewModel
                        {
                            ScheduleId = null, // У шаблонов нет ScheduleId
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
                            IsCustomLesson = false // Флаг шаблонного урока
                        });
                    }
                }
            }

            return items;
        }

        // МЕТОД: Генерация расписания из шаблонов (только для директора)
        [HttpPost]
        [Authorize(Roles = "Директор")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> GenerateScheduleFromTemplate(DateTime startDate, DateTime endDate)
        {
            // Валидация дат
            if (startDate > endDate || startDate < DateTime.Today.Date)
            {
                TempData["ErrorMessage"] = "Неверный диапазон дат или дата начала в прошлом.";
                return RedirectToAction(nameof(Index), new { date = startDate.ToString("yyyy-MM-dd") });
            }

            // 1. Загрузка всех шаблонов
            var templates = await _context.ScheduleTemplates.ToListAsync();
            var newLessons = new List<Schedule>();
            int lessonsAdded = 0;

            // 2. Перебор всех дней в диапазоне
            for (DateTime date = startDate.Date; date <= endDate.Date; date = date.AddDays(1))
            {
                // Пропуск выходных
                if (date.DayOfWeek == DayOfWeek.Saturday || date.DayOfWeek == DayOfWeek.Sunday)
                    continue;

                byte currentDayOfWeek = (byte)date.DayOfWeek;

                // 3. Проверка существующих уроков на этот день
                var existingLessonsForDay = await _context.Schedules
                    .Where(s => s.Date.Date == date.Date)
                    .Select(s => new { s.LessonNumber, s.ClassId })
                    .ToListAsync();

                // 4. Фильтрация шаблонов для текущего дня
                var relevantTemplates = templates.Where(t => t.DayOfWeek == currentDayOfWeek);

                // 5. Создание новых уроков на основе шаблонов
                foreach (var template in relevantTemplates)
                {
                    // Проверка, нет ли уже урока в этом слоте
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
                            LessonTopic = null, // Тема урока не заполняется при генерации
                            HomeworkText = null // ДЗ не заполняется при генерации
                        });
                        lessonsAdded++;
                    }
                }
            }

            // 6. Сохранение новых уроков в БД
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

        // МЕТОД: Отправка домашнего задания учеником
        [HttpPost]
        [Authorize(Roles = "Ученик")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SubmitHomework(int lessonId, string studentAnswer)
        {
            var studentId = GetCurrentUserId();

            // Валидация входных данных
            if (studentId == 0 || lessonId == 0)
            {
                return BadRequest(new { success = false, message = "Не удалось определить ученика или урок." });
            }

            // Поиск урока
            var lesson = await _context.Schedules.FirstOrDefaultAsync(s => s.LessonId == lessonId);
            if (lesson == null)
            {
                return NotFound(new { success = false, message = "Урок не найден." });
            }

            // Очистка и проверка ответа
            var safeStudentAnswer = studentAnswer?.Trim() ?? string.Empty;

            if (safeStudentAnswer.Length == 0)
            {
                return BadRequest(new { success = false, message = "Пожалуйста, введите текст ответа." });
            }

            // Создание объекта домашнего задания
            var homeworkEntry = new Homework
            {
                LessonId = lessonId,
                Date = lesson.Date, // Дата урока
                Text = safeStudentAnswer, // Ответ ученика
                StudentId = studentId
            };

            try
            {
                // Проверка существующего домашнего задания
                var existingHomework = await _context.Homeworks
                    .FirstOrDefaultAsync(h => h.LessonId == lessonId && h.StudentId == studentId);

                if (existingHomework != null)
                {
                    // Обновление существующего
                    existingHomework.Text = homeworkEntry.Text;
                    _context.Homeworks.Update(existingHomework);
                }
                else
                {
                    // Создание нового
                    _context.Homeworks.Add(homeworkEntry);
                }

                await _context.SaveChangesAsync();

                return Ok(new { success = true, message = "Домашнее задание успешно отправлено!" });
            }
            catch (Microsoft.EntityFrameworkCore.DbUpdateException dbEx)
            {
                // Обработка ошибок БД
                var innerMessage = dbEx.InnerException?.Message ?? dbEx.Message;
                return StatusCode(500, new { success = false, message = $"Ошибка базы данных: {innerMessage}" });
            }
            catch (Exception ex)
            {
                // Общая обработка ошибок
                return StatusCode(500, new { success = false, message = $"Непредвиденная ошибка сервера: {ex.Message}" });
            }
        }


        // Вспомогательные методы для получения данных
        // Получение всех предметов для выпадающих списков
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

        // Получение всех учителей для выпадающих списков
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

        // Получение данных конкретного урока по ID
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


        // ВСПОМОГАТЕЛЬНЫЕ МЕТОДЫ: Создание моделей

        // Создание полной модели расписания
        private ScheduleViewModel CreateScheduleModel(
            List<Schedule> entries,
            DateTime selectedDate,
            DateTime startOfWeek,
            bool isPersonal,
            bool isAdmin,
            Dictionary<int, (int? GradeValue, string Comment)> studentGrades = null)
        {
            // Инициализация словаря для группировки по дням недели
            var scheduleByDay = new Dictionary<DayOfWeek, List<ScheduleItemViewModel>>();

            // Создание структуры для всех рабочих дней
            for (int i = 0; i < 7; i++)
            {
                var dayOfWeek = (DayOfWeek)(((int)DayOfWeek.Monday + i) % 7);
                if (dayOfWeek != DayOfWeek.Sunday) // Пропускаем воскресенье
                {
                    scheduleByDay.Add(dayOfWeek, new List<ScheduleItemViewModel>());
                }
            }

            // Заполнение расписания данными
            if (entries != null)
            {
                foreach (var dayGroup in entries.GroupBy(s => s.Date.DayOfWeek))
                {
                    var dayOfWeek = dayGroup.Key;
                    if (scheduleByDay.ContainsKey(dayOfWeek))
                    {
                        // Преобразование каждого урока в ViewModel
                        var items = dayGroup.Select(s => new ScheduleItemViewModel
                        {
                            ScheduleId = s.LessonId,
                            LessonNumber = s.LessonNumber,
                            LessonTime = GetLessonTime(s.LessonNumber), // Время из маппинга
                            Date = s.Date,
                            SubjectId = s.SubjectId,
                            SubjectName = s.Subject?.SubjectName ?? "Неизвестный предмет",
                            TeacherId = s.TeacherId,
                            TeacherFullName = s.Teacher?.FullName ?? "Неизвестный учитель",
                            Classroom = s.Room,
                            LessonTopic = s.LessonTopic,
                            HomeworkText = s.HomeworkText,
                            // Получение оценки ученика (если есть)
                            Grade = studentGrades != null && studentGrades.ContainsKey(s.LessonId)
                                ? studentGrades[s.LessonId].GradeValue
                                : null,
                            // Получение комментария к оценке (если есть)
                            GradeComment = studentGrades != null && studentGrades.ContainsKey(s.LessonId)
                                ? studentGrades[s.LessonId].Comment
                                : null
                        }).OrderBy(i => i.LessonNumber).ToList();

                        scheduleByDay[dayOfWeek] = items;
                    }
                }
            }

            // Создание итоговой модели
            return new ScheduleViewModel
            {
                ScheduleByDay = scheduleByDay,
                SelectedDate = selectedDate,
                StartOfWeek = startOfWeek,
                IsPersonalView = isPersonal,
                IsAdminView = isAdmin
            };
        }

        // Создание пустой модели расписания (для обработки ошибок)
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


        // ВСПОМОГАТЕЛЬНЫЕ МЕТОДЫ: Утилиты
        // Получение времени урока по его номеру
        public static string GetLessonTime(int lessonNumber)
        {
            if (LessonTimeMap.ContainsKey(lessonNumber))
            {
                return LessonTimeMap[lessonNumber];
            }
            return "";
        }

        // Статический словарь соответствия номеров уроков и времени
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