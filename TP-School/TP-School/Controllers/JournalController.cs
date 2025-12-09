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
    [Authorize(Roles = "Учитель,Директор")]
    public class JournalController : Controller
    {
        private readonly ApplicationDbContext _context;

        public JournalController(ApplicationDbContext context)
        {
            _context = context;
        }

        private int GetCurrentUserId()
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            return int.TryParse(userIdClaim, out int userId) ? userId : 0;
        }

        [HttpGet]
        public async Task<IActionResult> Index(int? classId, int? subjectId, DateTime? weekStart)
        {
            var userId = GetCurrentUserId();
            var userRole = User.FindFirst(ClaimTypes.Role)?.Value;
            bool isDirector = userRole == "Директор";

            // Сохраняем параметры в TempData для перенаправлений
            if (classId.HasValue) TempData["CurrentClassId"] = classId.Value;
            if (subjectId.HasValue) TempData["CurrentSubjectId"] = subjectId.Value;
            if (weekStart.HasValue) TempData["CurrentWeekStart"] = weekStart.Value.ToString("yyyy-MM-dd");

            // 1. Определение текущей недели
            var today = DateTime.Today;
            int diff = today.DayOfWeek - DayOfWeek.Monday;
            if (diff < 0) diff += 7;
            var currentWeekStart = today.AddDays(-diff);

            if (!weekStart.HasValue)
            {
                weekStart = currentWeekStart;
            }

            // 2. Расчет начала учебного года
            var academicYearStartDate = new DateTime(today.Year, 9, 1);
            if (today < academicYearStartDate)
            {
                academicYearStartDate = academicYearStartDate.AddYears(-1);
            }

            DateTime firstWeekOfAcademicYear = academicYearStartDate;
            int startDiff = firstWeekOfAcademicYear.DayOfWeek - DayOfWeek.Monday;
            if (startDiff < 0) startDiff += 7;
            firstWeekOfAcademicYear = firstWeekOfAcademicYear.AddDays(-startDiff);

            // 3. Генерация списка недель
            var ruCulture = new CultureInfo("ru-RU");
            var availableWeeksList = new List<SelectListItem>();
            var week = firstWeekOfAcademicYear;

            while (week.Date <= currentWeekStart.Date)
            {
                var weekEndDisplay = week.AddDays(6);
                TimeSpan timeDifference = week.Date - firstWeekOfAcademicYear.Date;
                int weekNumber = (int)(timeDifference.TotalDays / 7) + 1;
                string weekDisplay = $"{weekNumber} нед. ({week.ToString("ddd", ruCulture)} {week:dd.MM} - {weekEndDisplay.ToString("ddd", ruCulture)} {weekEndDisplay:dd.MM})";

                availableWeeksList.Add(new SelectListItem
                {
                    Text = weekDisplay,
                    Value = week.ToString("yyyy-MM-dd"),
                    Selected = (week.Date == weekStart.Value.Date)
                });

                week = week.AddDays(7);
            }

            // 4. Загрузка доступных классов и предметов
            List<SchoolClass> availableClasses;
            List<Subject> availableSubjects;

            if (isDirector)
            {
                availableClasses = await _context.SchoolClasses.OrderBy(c => c.ClassNumber).ThenBy(c => c.ClassLetter).ToListAsync();
                availableSubjects = await _context.Subjects.OrderBy(s => s.SubjectName).ToListAsync();
            }
            else
            {
                var relations = await _context.ClassSubjectTeachers
                    .Where(cst => cst.TeacherId == userId)
                    .Include(cst => cst.Class)
                    .Include(cst => cst.Subject)
                    .ToListAsync();

                availableClasses = relations.Select(r => r.Class)
                    .GroupBy(c => c.ClassId)
                    .Select(g => g.First())
                    .OrderBy(c => c.ClassNumber)
                    .ToList();
                availableSubjects = relations.Select(r => r.Subject)
                    .GroupBy(s => s.SubjectId)
                    .Select(g => g.First())
                    .OrderBy(s => s.SubjectName)
                    .ToList();
            }

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

            int selectedClassId = classId ?? availableClasses.First().ClassId;
            int selectedSubjectId = subjectId ?? availableSubjects.FirstOrDefault()?.SubjectId ?? 0;

            // 5. Загрузка данных для журнала
            var weekEndQuery = weekStart.Value.AddDays(7);
            var lessons = await _context.Schedules
                .Where(s => s.ClassId == selectedClassId &&
                            s.SubjectId == selectedSubjectId &&
                            s.Date >= weekStart.Value && s.Date < weekEndQuery)
                .OrderBy(s => s.Date)
                .ToListAsync();

            var studentsData = await _context.StudentClasses
                .Where(sc => sc.ClassId == selectedClassId)
                .Include(sc => sc.Student)
                .OrderBy(sc => sc.Student.LastName)
                .Select(sc => sc.Student)
                .ToListAsync();

            var lessonIds = lessons.Select(l => l.LessonId).ToList();

            var gradesData = await _context.Grades
                .Where(g => lessonIds.Contains(g.LessonId))
                .ToListAsync();

            var attendancesData = await _context.Attendances
                .Where(a => lessonIds.Contains(a.LessonId))
                .ToListAsync();


            var model = new JournalViewModel
            {
                SelectedClassId = selectedClassId,
                SelectedSubjectId = selectedSubjectId,
                WeekStart = weekStart.Value,
                Classes = new SelectList(availableClasses, "ClassId", "ClassName", selectedClassId),
                Subjects = new SelectList(availableSubjects, "SubjectId", "SubjectName", selectedSubjectId),
                Weeks = new SelectList(availableWeeksList, "Value", "Text", weekStart.Value.ToString("yyyy-MM-dd")),
                LessonsForWeek = lessons,
                Rows = new List<JournalRow>()
            };

            // 6. Формирование строк журнала
            foreach (var student in studentsData)
            {
                var row = new JournalRow { Student = student };

                foreach (var lesson in lessons)
                {
                    var grade = gradesData.FirstOrDefault(g => g.StudentId == student.UserId && g.LessonId == lesson.LessonId);
                    var attendance = attendancesData.FirstOrDefault(a => a.StudentId == student.UserId && a.LessonId == lesson.LessonId);

                    CellData cellData = null;

                    if (attendance != null && !string.IsNullOrEmpty(attendance.Status))
                    {
                        cellData = new CellData { Value = attendance.Status, IsAttendance = true };
                    }
                    else if (grade != null)
                    {
                        if (grade.GradeValue.HasValue)
                        {
                            cellData = new CellData
                            {
                                Value = grade.GradeValue.Value.ToString(),
                                IsAttendance = false,
                                HasComment = !string.IsNullOrEmpty(grade.Comment),
                                Comment = grade.Comment ?? string.Empty
                            };
                        }
                    }

                    if (cellData != null)
                    {
                        row.Cells.Add(lesson.LessonId, cellData);
                    }
                }
                model.Rows.Add(row);
            }

            // Передаем параметры в ViewBag для формы
            ViewBag.CurrentClassId = selectedClassId;
            ViewBag.CurrentSubjectId = selectedSubjectId;
            ViewBag.CurrentWeekStart = weekStart.Value.ToString("yyyy-MM-dd");

            return View(model);
        }

        // =========================================================================
        // МЕТОД: Обработка сохранения оценки/посещаемости
        // =========================================================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SaveGrade([FromForm] int studentId, [FromForm] int lessonId, [FromForm] string? gradeValue, [FromForm] string? attendanceStatus, [FromForm] string? comment)
        {
            if (studentId <= 0 || lessonId <= 0)
            {
                return BadRequest("Неверный ID ученика или урока.");
            }

            gradeValue = gradeValue?.Trim();
            attendanceStatus = attendanceStatus?.Trim();

            string? finalComment = string.IsNullOrWhiteSpace(comment) ? null : comment.Trim();

            int gradeInt = 0;
            bool hasGrade = !string.IsNullOrEmpty(gradeValue) && int.TryParse(gradeValue, out gradeInt);
            bool hasAttendance = !string.IsNullOrEmpty(attendanceStatus);

            var existingGrade = await _context.Grades
                .FirstOrDefaultAsync(g => g.StudentId == studentId && g.LessonId == lessonId);
            var existingAttendance = await _context.Attendances
                .FirstOrDefaultAsync(a => a.StudentId == studentId && a.LessonId == lessonId);

            // 1. Сценарий: Выставлена оценка (Приоритет)
            if (hasGrade)
            {
                if (existingAttendance != null)
                {
                    _context.Attendances.Remove(existingAttendance);
                }

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
            // 2. Сценарий: Выставлена посещаемость (Оценки нет)
            else if (hasAttendance)
            {
                if (existingGrade != null)
                {
                    _context.Grades.Remove(existingGrade);
                }

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
            // 3. Сценарий: Очистка 
            else
            {
                if (existingGrade != null)
                {
                    _context.Grades.Remove(existingGrade);
                }
                if (existingAttendance != null)
                {
                    _context.Attendances.Remove(existingAttendance);
                }
            }

            await _context.SaveChangesAsync();

            // Получаем параметры из TempData или Query
            var returnClassId = TempData["CurrentClassId"] as int? ??
                    (Request.Query["classId"].FirstOrDefault() != null ?
                     int.Parse(Request.Query["classId"].FirstOrDefault()) : (int?)null);

            var returnSubjectId = TempData["CurrentSubjectId"] as int? ??
                                  (Request.Query["subjectId"].FirstOrDefault() != null ?
                                   int.Parse(Request.Query["subjectId"].FirstOrDefault()) : (int?)null);
            var returnWeekStart = TempData["CurrentWeekStart"] as string ?? Request.Query["weekStart"].FirstOrDefault();

            return RedirectToAction("Index", new
            {
                classId = returnClassId,
                subjectId = returnSubjectId,
                weekStart = returnWeekStart
            });
        }

        // =========================================================================
        // МЕТОД: Отображение формы урока (модальное окно)
        // =========================================================================
        [HttpGet]
        public async Task<IActionResult> LessonForm(int lessonId)
        {
            var lesson = await _context.Schedules
                .Include(s => s.Class)
                .Include(s => s.Subject)
                .FirstOrDefaultAsync(s => s.LessonId == lessonId);

            if (lesson == null)
            {
                return NotFound();
            }

            ViewBag.ClassName = lesson.Class?.ClassName ?? "Класс";
            ViewBag.SubjectName = lesson.Subject?.SubjectName ?? "Предмет";
            ViewBag.LessonDate = lesson.Date.ToString("dd.MM.yyyy");
            ViewBag.LessonId = lessonId;

            // Получаем текущие параметры фильтров из Query
            ViewBag.CurrentClassId = Request.Query["classId"].FirstOrDefault();
            ViewBag.CurrentSubjectId = Request.Query["subjectId"].FirstOrDefault();
            ViewBag.CurrentWeekStart = Request.Query["weekStart"].FirstOrDefault();

            return PartialView("_LessonFormPartial", lesson);
        }

        // =========================================================================
        // МЕТОД: Сохранение формы урока (ОБЫЧНАЯ ФОРМА, не AJAX)
        // =========================================================================
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
                var lesson = await _context.Schedules.FindAsync(lessonId);
                if (lesson == null)
                {
                    TempData["ErrorMessage"] = "Урок не найден";
                    return RedirectToAction("Index", new { classId, subjectId, weekStart });
                }

                // Обновляем данные
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