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

namespace TP_School.Controllers
{
    // Доступ только для Учителя и Директора
    [Authorize(Roles = "Учитель, Директор")]
    public class HomeworkController : Controller
    {
        private readonly ApplicationDbContext _context;

        public HomeworkController(ApplicationDbContext context)
        {
            _context = context;
        }

        private int GetCurrentUserId()
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            return int.TryParse(userIdClaim, out int userId) ? userId : 0;
        }

        // --- 1. DISPLAY HOMEWORK SUBMISSIONS ---
        [HttpGet]
        public async Task<IActionResult> Review(int? classId)
        {
            var userId = GetCurrentUserId();
            var userRole = User.FindFirst(ClaimTypes.Role)?.Value;

            // 1. Определение доступных классов (с учетом роли)
            var availableClasses = new List<SchoolClass>();

            if (userRole == "Директор")
            {
                // Директор видит все классы
                availableClasses = await _context.SchoolClasses
                    .OrderBy(c => c.ClassNumber).ThenBy(c => c.ClassLetter)
                    .ToListAsync();
            }
            else // Учитель
            {
                // Учитель видит классы, в которых он ведет предметы
                var classIds = await _context.ClassSubjectTeachers
                    .Where(cst => cst.TeacherId == userId)
                    .Select(cst => cst.ClassId)
                    .Distinct()
                    .ToListAsync();

                availableClasses = await _context.SchoolClasses
                    .Where(c => classIds.Contains(c.ClassId))
                    .OrderBy(c => c.ClassNumber).ThenBy(c => c.ClassLetter)
                    .ToListAsync();
            }

            if (!availableClasses.Any())
            {
                ViewBag.ErrorMessage = "Нет доступных классов для проверки домашнего задания.";
                return View(new HomeworkReviewViewModel { Submissions = new List<HomeworkReviewItem>(), AvailableClasses = new Dictionary<int, string>() });
            }

            // 2. Выбор класса для отображения
            int selectedClassId = classId ?? availableClasses.First().ClassId;
            var selectedClass = availableClasses.FirstOrDefault(c => c.ClassId == selectedClassId);

            // 3. Загрузка данных
            var submissionsQuery = _context.Homeworks
                .Include(h => h.Student)
                .Include(h => h.Lesson) // Lesson имеет тип Schedule
                    .ThenInclude(l => l.Class)
                .Include(h => h.Lesson)
                    .ThenInclude(l => l.Subject)
                // LEFT JOIN с таблицей Grade для получения текущей оценки/комментария
                .GroupJoin(_context.Grades.Where(g => g.HomeworkId != null),
                    h => h.HomeworkId,
                    g => g.HomeworkId,
                    (h, gradeGroup) => new { Homework = h, Grade = gradeGroup.OrderByDescending(g => g.Date).FirstOrDefault() })
                .Where(x => x.Homework.Lesson.ClassId == selectedClassId)
                .AsQueryable();

            // Дополнительный фильтр для роли Учитель (только задания по предметам, которые он ведет)
            if (userRole == "Учитель")
            {
                var teacherSubjectsInClass = await _context.ClassSubjectTeachers
                    .Where(cst => cst.TeacherId == userId && cst.ClassId == selectedClassId)
                    .Select(cst => cst.SubjectId)
                    .ToListAsync();

                submissionsQuery = submissionsQuery
                    // Строгая фильтрация: Учитель видит только ДЗ по предметам, которые он ведет в этом классе
                    .Where(x => teacherSubjectsInClass.Contains(x.Homework.Lesson.SubjectId));
            }

            var submissions = await submissionsQuery
                // Сортировка по SubmissionDate
                .OrderByDescending(x => x.Homework.Date)
                .Select(x => new HomeworkReviewItem
                {
                    HomeworkId = x.Homework.HomeworkId,
                    StudentId = x.Homework.StudentId,
                    StudentFullName = x.Homework.Student.FullName,
                    ClassId = x.Homework.Lesson.ClassId,
                    ClassName = x.Homework.Lesson.Class.ClassName,
                    SubjectName = x.Homework.Lesson.Subject.SubjectName,
                    LessonDate = x.Homework.Lesson.Date,
                    LessonNumber = x.Homework.Lesson.LessonNumber,
                    // Использование SubmissionDate
                    SubmissionDate = x.Homework.Date,
                    StudentAnswer = x.Homework.Text,
                    HasFile = x.Homework.FilePath != null && x.Homework.FilePath.Length > 0,
                    StatusId = x.Homework.Status,

                    // 🛠️ ИСПРАВЛЕНИЕ: Используем CurrentTeacherComment для временного хранения текста задания с урока.
                    // Теперь мы точно знаем поле: HomeworkText из модели Schedule.
                    CurrentTeacherComment = x.Homework.Lesson.HomeworkText,

                    // Данные из Grade
                    GradeId = x.Grade != null ? x.Grade.GradeId : (int?)null,
                    CurrentGradeValue = x.Grade != null ? x.Grade.GradeValue : (int?)null,
                    // Мы не можем использовать x.Grade.Comment здесь, так как CurrentTeacherComment занят текстом задания.
                })
                .ToListAsync();

            // 4. Формирование ViewModel
            var classDict = availableClasses.ToDictionary(c => c.ClassId, c => c.ClassName);

            var viewModel = new HomeworkReviewViewModel
            {
                Submissions = submissions,
                AvailableClasses = classDict,
                SelectedClassId = selectedClassId,
                SelectedClassName = selectedClass?.ClassName ?? "Класс не найден"
            };

            return View(viewModel);
        }

        // --- 2. SAVE GRADE AND COMMENT (AJAX POST) ---
        public class HomeworkReviewRequest
        {
            public int homeworkId { get; set; }
            public int? gradeValue { get; set; }
            public string comment { get; set; }
            public int? existingGradeId { get; set; }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SaveReview([FromBody] HomeworkReviewRequest request)
        {
            var userId = GetCurrentUserId();
            var userRole = User.FindFirst(ClaimTypes.Role)?.Value;

            if (request == null)
            {
                return BadRequest(new { success = false, message = "Некорректный запрос." });
            }

            var homework = await _context.Homeworks
                .Include(h => h.Lesson)
                    .ThenInclude(l => l.Subject)
                .FirstOrDefaultAsync(h => h.HomeworkId == request.homeworkId);

            if (homework == null)
            {
                return NotFound(new { success = false, message = "Задание не найдено." });
            }

            // Создание или Обновление записи Grade
            Grade gradeEntry = null;

            if (request.existingGradeId.HasValue && request.existingGradeId.Value > 0)
            {
                gradeEntry = await _context.Grades.FirstOrDefaultAsync(g => g.GradeId == request.existingGradeId);
            }

            if (gradeEntry == null)
            {
                // Создание новой записи
                gradeEntry = new Grade
                {
                    StudentId = homework.StudentId,
                    HomeworkId = homework.HomeworkId,
                    LessonId = homework.LessonId,
                    Date = DateTime.Now,
                };
                _context.Grades.Add(gradeEntry);
            }

            // Обновление полей
            gradeEntry.GradeValue = request.gradeValue;
            gradeEntry.Comment = request.comment?.Trim();
            gradeEntry.Date = DateTime.Now;

            // Если запись Grade была создана/обновлена, значит, задание проверено.
            if (homework.Status != 2)
            {
                homework.Status = 2; // Устанавливаем статус "Проверено"
            }

            try
            {
                await _context.SaveChangesAsync();

                // Возвращаем ID для обновления в представлении
                return Ok(new { success = true, message = "Оценка и комментарий сохранены.", newGradeId = gradeEntry.GradeId });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = $"Ошибка сохранения: {ex.Message}" });
            }
        }

        // --- 3. DOWNLOAD HOMEWORK FILE ---
        [HttpGet]
        public async Task<IActionResult> DownloadFile(int homeworkId)
        {
            var homework = await _context.Homeworks
                .Include(h => h.Student)
                .Include(h => h.Lesson)
                    .ThenInclude(l => l.Subject)
                .FirstOrDefaultAsync(h => h.HomeworkId == homeworkId);

            if (homework == null || homework.FilePath == null || homework.FilePath.Length == 0)
            {
                return NotFound("Файл не найден.");
            }

            // Проверка прав (повторяем для безопасности)
            var userId = GetCurrentUserId();
            var userRole = User.FindFirst(ClaimTypes.Role)?.Value;

            if (userRole == "Учитель")
            {
                var isTeacherForSubject = await _context.ClassSubjectTeachers
                    .AnyAsync(cst => cst.TeacherId == userId &&
                                     cst.ClassId == homework.Lesson.ClassId &&
                                     cst.SubjectId == homework.Lesson.SubjectId);

                if (!isTeacherForSubject)
                {
                    return Forbid("У вас нет прав на скачивание этого файла.");
                }
            }

            var fileName = $"ДЗ_{homework.Lesson.Subject.SubjectName}_{homework.Student.FullName}_{homework.Lesson.Date:yyyyMMdd}.zip";

            return File(homework.FilePath, "application/octet-stream", fileName);
        }
    }
}