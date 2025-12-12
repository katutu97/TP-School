using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using TP_School.Data;
using TP_School.Models;
using TP_School.ViewModels;
using System.Globalization;
using System.Linq;

namespace TP_School.Controllers
{
    // Доступ только для пользователей с ролями "Учитель" или "Директор"
    [Authorize(Roles = "Учитель,Директор")]
    public class JournalController : Controller
    {
        private readonly ApplicationDbContext _context;
        public JournalController(ApplicationDbContext context)
        {
            _context = context;
        }

        // Получение ID текущего пользователя из claims токена
        private int GetCurrentUserId()
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            return int.TryParse(userIdClaim, out int userId) ? userId : 0;
        }

        // ОСНОВНОЙ МЕТОД: Отображение журнала успеваемости
        [HttpGet]
        public async Task<IActionResult> Index(int? classId, int? subjectId, DateTime? weekStart)
        {
            var userId = GetCurrentUserId(); // ID текущего пользователя
            var userRole = User.FindFirst(ClaimTypes.Role)?.Value; // Роль пользователя
            bool isDirector = userRole == "Директор"; // Флаг директора

            // Сохраняем параметры в TempData для перенаправлений (чтобы помнить фильтры)
            if (classId.HasValue) TempData["CurrentClassId"] = classId.Value;
            if (subjectId.HasValue) TempData["CurrentSubjectId"] = subjectId.Value;
            if (weekStart.HasValue) TempData["CurrentWeekStart"] = weekStart.Value.ToString("yyyy-MM-dd");

            // 1. Определение текущей недели
            var today = DateTime.Today; // Сегодняшняя дата
            int diff = today.DayOfWeek - DayOfWeek.Monday; // Разница от понедельника
            if (diff < 0) diff += 7; // Если воскресенье, корректируем
            var currentWeekStart = today.AddDays(-diff); // Начало текущей недели (понедельник)

            // Если неделя не указана, используем текущую
            if (!weekStart.HasValue)
            {
                weekStart = currentWeekStart;
            }

            // 2. Расчет начала учебного года
            var academicYearStartDate = new DateTime(today.Year, 9, 1); // Учебный год начинается 1 сентября
            if (today < academicYearStartDate) // Если сейчас до 1 сентября, берем предыдущий год
            {
                academicYearStartDate = academicYearStartDate.AddYears(-1);
            }

            // Определяем первый понедельник учебного года
            DateTime firstWeekOfAcademicYear = academicYearStartDate;
            int startDiff = firstWeekOfAcademicYear.DayOfWeek - DayOfWeek.Monday;
            if (startDiff < 0) startDiff += 7;
            firstWeekOfAcademicYear = firstWeekOfAcademicYear.AddDays(-startDiff);

            // 3. Генерация списка недель для выпадающего списка
            var ruCulture = new CultureInfo("ru-RU"); // Для русских названий дней недели
            var availableWeeksList = new List<SelectListItem>();
            var week = firstWeekOfAcademicYear;

            // Создаем список недель от начала учебного года до текущей недели
            while (week.Date <= currentWeekStart.Date)
            {
                var weekEndDisplay = week.AddDays(6); // Конец недели (воскресенье)

                // Расчет номера недели от начала учебного года
                TimeSpan timeDifference = week.Date - firstWeekOfAcademicYear.Date;
                int weekNumber = (int)(timeDifference.TotalDays / 7) + 1;

                // Форматирование строки: "1 нед. (Пн 02.09 - Вс 08.09)"
                string weekDisplay = $"{weekNumber} нед. ({week.ToString("ddd", ruCulture)} {week:dd.MM} - {weekEndDisplay.ToString("ddd", ruCulture)} {weekEndDisplay:dd.MM})";

                availableWeeksList.Add(new SelectListItem
                {
                    Text = weekDisplay,
                    Value = week.ToString("yyyy-MM-dd"), // Значение для отправки
                    Selected = (week.Date == weekStart.Value.Date) // Выделяем текущую неделю
                });

                week = week.AddDays(7); // Переход к следующей неделе
            }

            // 4. Загрузка доступных классов и предметов (разные для директора и учителя)
            List<SchoolClass> availableClasses;
            List<Subject> availableSubjects;

            if (isDirector)
            {
                // Директор видит все классы и предметы
                availableClasses = await _context.SchoolClasses
                    .OrderBy(c => c.ClassNumber)
                    .ThenBy(c => c.ClassLetter)
                    .ToListAsync();
                availableSubjects = await _context.Subjects
                    .OrderBy(s => s.SubjectName)
                    .ToListAsync();
            }
            else
            {
                // Учитель видит только классы и предметы, которые он ведет
                var relations = await _context.ClassSubjectTeachers
                    .Where(cst => cst.TeacherId == userId) // Фильтр по ID учителя
                    .Include(cst => cst.Class)
                    .Include(cst => cst.Subject)
                    .ToListAsync();

                // Уникальные классы
                availableClasses = relations.Select(r => r.Class)
                    .GroupBy(c => c.ClassId)
                    .Select(g => g.First())
                    .OrderBy(c => c.ClassNumber)
                    .ToList();

                // Уникальные предметы
                availableSubjects = relations.Select(r => r.Subject)
                    .GroupBy(s => s.SubjectId)
                    .Select(g => g.First())
                    .OrderBy(s => s.SubjectName)
                    .ToList();
            }

            // Если нет доступных классов, возвращаем пустую модель
            if (!availableClasses.Any())
            {
                return View(new JournalViewModel
                {
                    Classes = new SelectList(new List<string>()),
                    Subjects = new SelectList(new List<string>()),
                    Weeks = new SelectList(new List<string>()),
                    LessonsForWeek = new List<Schedule>(),
                    Rows = new List<JournalRow>()
                });
            }

            // Определение выбранных ID (или использование первых доступных)
            int selectedClassId = classId ?? availableClasses.First().ClassId;
            int selectedSubjectId = subjectId ?? availableSubjects.FirstOrDefault()?.SubjectId ?? 0;

            // 5. Загрузка данных для журнала
            var weekEndQuery = weekStart.Value.AddDays(7); // Конец недели
            var lessons = await _context.Schedules
                .Where(s => s.ClassId == selectedClassId &&
                            s.SubjectId == selectedSubjectId &&
                            s.Date >= weekStart.Value && s.Date < weekEndQuery) // Уроки в выбранной неделе
                .OrderBy(s => s.Date) // Сортировка по дате
                .ToListAsync();

            // Список учеников в классе (сортировка по фамилии)
            var studentsData = await _context.StudentClasses
                .Where(sc => sc.ClassId == selectedClassId)
                .Include(sc => sc.Student)
                .OrderBy(sc => sc.Student.LastName)
                .Select(sc => sc.Student)
                .ToListAsync();

            var lessonIds = lessons.Select(l => l.LessonId).ToList(); // ID всех уроков

            // Загрузка оценок для этих уроков
            var gradesData = await _context.Grades
                .Where(g => lessonIds.Contains(g.LessonId))
                .ToListAsync();

            // Загрузка посещаемости для этих уроков
            var attendancesData = await _context.Attendances
                .Where(a => lessonIds.Contains(a.LessonId))
                .ToListAsync();

            // 6. Создание модели представления
            var model = new JournalViewModel
            {
                SelectedClassId = selectedClassId,
                SelectedSubjectId = selectedSubjectId,
                WeekStart = weekStart.Value,
                Classes = new SelectList(availableClasses, "ClassId", "ClassName", selectedClassId),
                Subjects = new SelectList(availableSubjects, "SubjectId", "SubjectName", selectedSubjectId),
                Weeks = new SelectList(availableWeeksList, "Value", "Text", weekStart.Value.ToString("yyyy-MM-dd")),
                LessonsForWeek = lessons, // Уроки в выбранной неделе
                Rows = new List<JournalRow>() // Строки с данными учеников
            };

            // 7. Формирование строк журнала (по одному на каждого ученика)
            foreach (var student in studentsData)
            {
                var row = new JournalRow { Student = student };

                // Для каждого урока заполняем ячейку данными
                foreach (var lesson in lessons)
                {
                    // Поиск оценки для этого ученика и урока
                    var grade = gradesData.FirstOrDefault(g =>
                        g.StudentId == student.UserId &&
                        g.LessonId == lesson.LessonId);

                    // Поиск посещаемости для этого ученика и урока
                    var attendance = attendancesData.FirstOrDefault(a =>
                        a.StudentId == student.UserId &&
                        a.LessonId == lesson.LessonId);

                    CellData cellData = null;

                    // Приоритет: посещаемость отображается, если есть
                    if (attendance != null && !string.IsNullOrEmpty(attendance.Status))
                    {
                        cellData = new CellData
                        {
                            Value = attendance.Status,
                            IsAttendance = true // Флаг, что это посещаемость (не оценка)
                        };
                    }
                    else if (grade != null && grade.GradeValue.HasValue)
                    {
                        // Если нет посещаемости, но есть оценка
                        cellData = new CellData
                        {
                            Value = grade.GradeValue.Value.ToString(), // Значение оценки
                            IsAttendance = false,
                            HasComment = !string.IsNullOrEmpty(grade.Comment), // Есть ли комментарий
                            Comment = grade.Comment ?? string.Empty // Текст комментария
                        };
                    }

                    // Добавляем данные ячейки, если они есть
                    if (cellData != null)
                    {
                        row.Cells.Add(lesson.LessonId, cellData); // Ключ - ID урока
                    }
                }
                model.Rows.Add(row);
            }

            // Передаем параметры в ViewBag для формы (чтобы сохранить фильтры)
            ViewBag.CurrentClassId = selectedClassId;
            ViewBag.CurrentSubjectId = selectedSubjectId;
            ViewBag.CurrentWeekStart = weekStart.Value.ToString("yyyy-MM-dd");

            return View(model);
        }

        // МЕТОД: Обработка сохранения оценки/посещаемости
        [HttpPost]
        [ValidateAntiForgeryToken] // Защита от CSRF-атак
        public async Task<IActionResult> SaveGrade(
            [FromForm] int studentId,
            [FromForm] int lessonId,
            [FromForm] string? gradeValue,
            [FromForm] string? attendanceStatus,
            [FromForm] string? comment)
        {
            // Валидация входных данных
            if (studentId <= 0 || lessonId <= 0)
            {
                return BadRequest("Неверный ID ученика или урока.");
            }

            // Очистка и нормализация данных
            gradeValue = gradeValue?.Trim();
            attendanceStatus = attendanceStatus?.Trim();
            string? finalComment = string.IsNullOrWhiteSpace(comment) ? null : comment.Trim();

            // Определение типа данных
            int gradeInt = 0;
            bool hasGrade = !string.IsNullOrEmpty(gradeValue) && int.TryParse(gradeValue, out gradeInt);
            bool hasAttendance = !string.IsNullOrEmpty(attendanceStatus);

            // Поиск существующих записей
            var existingGrade = await _context.Grades
                .FirstOrDefaultAsync(g => g.StudentId == studentId && g.LessonId == lessonId);
            var existingAttendance = await _context.Attendances
                .FirstOrDefaultAsync(a => a.StudentId == studentId && a.LessonId == lessonId);

            // 1. Сценарий: Выставлена оценка (имеет приоритет над посещаемостью)
            if (hasGrade)
            {
                // Если есть посещаемость - удаляем ее
                if (existingAttendance != null)
                {
                    _context.Attendances.Remove(existingAttendance);
                }

                // Создание или обновление оценки
                if (existingGrade == null)
                {
                    _context.Grades.Add(new Grade
                    {
                        StudentId = studentId,
                        LessonId = lessonId,
                        GradeValue = gradeInt,
                        Comment = finalComment
                    });
                }
                else
                {
                    existingGrade.GradeValue = gradeInt;
                    existingGrade.Comment = finalComment;
                    _context.Grades.Update(existingGrade);
                }
            }
            // 2. Сценарий: Выставлена посещаемость (оценки нет)
            else if (hasAttendance)
            {
                // Если есть оценка - удаляем ее
                if (existingGrade != null)
                {
                    _context.Grades.Remove(existingGrade);
                }

                // Создание или обновление посещаемости
                if (existingAttendance == null)
                {
                    _context.Attendances.Add(new Attendance
                    {
                        StudentId = studentId,
                        LessonId = lessonId,
                        Status = attendanceStatus
                    });
                }
                else
                {
                    existingAttendance.Status = attendanceStatus;
                    _context.Attendances.Update(existingAttendance);
                }
            }
            // 3. Сценарий: Очистка данных (удаление и оценки, и посещаемости)
            else
            {
                if (existingGrade != null) _context.Grades.Remove(existingGrade);
                if (existingAttendance != null) _context.Attendances.Remove(existingAttendance);
            }

            await _context.SaveChangesAsync(); // Сохранение изменений в БД

            // Получение параметров для возврата (чтобы сохранить фильтры)
            var returnClassId = TempData["CurrentClassId"] as int? ??
                    (Request.Query["classId"].FirstOrDefault() != null ?
                     int.Parse(Request.Query["classId"].FirstOrDefault()) : (int?)null);

            var returnSubjectId = TempData["CurrentSubjectId"] as int? ??
                                  (Request.Query["subjectId"].FirstOrDefault() != null ?
                                   int.Parse(Request.Query["subjectId"].FirstOrDefault()) : (int?)null);
            var returnWeekStart = TempData["CurrentWeekStart"] as string ?? Request.Query["weekStart"].FirstOrDefault();

            // Возврат к журналу с сохранением фильтров
            return RedirectToAction("Index", new
            {
                classId = returnClassId,
                subjectId = returnSubjectId,
                weekStart = returnWeekStart
            });
        }

        // МЕТОД: Отображение формы урока (для модального окна)
        [HttpGet]
        public async Task<IActionResult> LessonForm(int lessonId)
        {
            // Загрузка данных урока с включением связанных данных
            var lesson = await _context.Schedules
                .Include(s => s.Class)
                .Include(s => s.Subject)
                .FirstOrDefaultAsync(s => s.LessonId == lessonId);

            if (lesson == null)
            {
                return NotFound();
            }

            // Передача данных в ViewBag для отображения в форме
            ViewBag.ClassName = lesson.Class?.ClassName ?? "Класс";
            ViewBag.SubjectName = lesson.Subject?.SubjectName ?? "Предмет";
            ViewBag.LessonDate = lesson.Date.ToString("dd.MM.yyyy");
            ViewBag.LessonId = lessonId;

            // Получаем текущие параметры фильтров из Query для возврата
            ViewBag.CurrentClassId = Request.Query["classId"].FirstOrDefault();
            ViewBag.CurrentSubjectId = Request.Query["subjectId"].FirstOrDefault();
            ViewBag.CurrentWeekStart = Request.Query["weekStart"].FirstOrDefault();

            return PartialView("_LessonFormPartial", lesson); // Возвращаем частичное представление
        }

        // МЕТОД: Сохранение формы урока (тема и домашнее задание)
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SaveLesson(
            [FromForm] int lessonId,
            [FromForm] string lessonTopic,
            [FromForm] string homeworkText,
            [FromQuery] int? classId,
            [FromQuery] int? subjectId,
            [FromQuery] string weekStart)
        {
            try
            {
                // Поиск урока в БД
                var lesson = await _context.Schedules.FindAsync(lessonId);
                if (lesson == null)
                {
                    TempData["ErrorMessage"] = "Урок не найден";
                    return RedirectToAction("Index", new { classId, subjectId, weekStart });
                }

                // Обновление данных урока
                lesson.LessonTopic = lessonTopic?.Trim();
                lesson.HomeworkText = homeworkText?.Trim();

                _context.Schedules.Update(lesson);
                await _context.SaveChangesAsync();

                TempData["SuccessMessage"] = "Данные урока успешно сохранены!";
                return RedirectToAction("Index", new { classId, subjectId, weekStart });
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = $"Ошибка при сохранении: {ex.Message}";
                return RedirectToAction("Index", new { classId, subjectId, weekStart });
            }
        }
    }
}