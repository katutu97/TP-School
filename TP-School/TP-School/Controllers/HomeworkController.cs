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

        // Получение ID текущего пользователя 
        private int GetCurrentUserId()
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            return int.TryParse(userIdClaim, out int userId) ? userId : 0;
        }

        // --- 1. ОТОБРАЖЕНИЕ СДАННЫХ ДОМАШНИХ ЗАДАНИЙ ---
        [HttpGet]
        public async Task<IActionResult> Review(int? classId)
        {
            var userId = GetCurrentUserId(); // ID текущего пользователя
            var userRole = User.FindFirst(ClaimTypes.Role)?.Value; // Роль пользователя

            // 1. Определение доступных классов (с учетом роли)
            var availableClasses = new List<SchoolClass>();

            if (userRole == "Директор")
            {
                // Директор видит все классы
                availableClasses = await _context.SchoolClasses
                    .OrderBy(c => c.ClassNumber).ThenBy(c => c.ClassLetter) // Сортировка по номеру, затем по букве класса
                    .ToListAsync();
            }
            else // Учитель
            {
                // Учитель видит классы, в которых он ведет предметы
                var classIds = await _context.ClassSubjectTeachers
                    .Where(cst => cst.TeacherId == userId) // Фильтр по ID учителя
                    .Select(cst => cst.ClassId) // Выбор ID классов
                    .Distinct() // Уникальные ID
                    .ToListAsync();

                availableClasses = await _context.SchoolClasses
                    .Where(c => classIds.Contains(c.ClassId)) // Фильтр по доступным классам
                    .OrderBy(c => c.ClassNumber).ThenBy(c => c.ClassLetter) // Сортировка
                    .ToListAsync();
            }

            // Если нет доступных классов, возвращаем сообщение об ошибке
            if (!availableClasses.Any())
            {
                ViewBag.ErrorMessage = "Нет доступных классов для проверки домашнего задания.";
                return View(new HomeworkReviewViewModel
                {
                    Submissions = new List<HomeworkReviewItem>(),
                    AvailableClasses = new Dictionary<int, string>()
                });
            }

            // 2. Выбор класса для отображения
            // Если classId не указан, берем первый доступный класс
            int selectedClassId = classId ?? availableClasses.First().ClassId;
            var selectedClass = availableClasses.FirstOrDefault(c => c.ClassId == selectedClassId);

            // 3. Загрузка данных домашних заданий
            var submissionsQuery = _context.Homeworks
                .Include(h => h.Student) // Включаем данные студента
                .Include(h => h.Lesson) // Включаем данные урока (расписание)
                    .ThenInclude(l => l.Class) // Включаем данные класса
                .Include(h => h.Lesson)
                    .ThenInclude(l => l.Subject) // Включаем данные предмета
                                                 // LEFT JOIN с таблицей Grade для получения текущей оценки/комментария
                                                 // Соединяем по HomeworkId и выбираем последнюю оценку
                .GroupJoin(_context.Grades.Where(g => g.HomeworkId != null),
                    h => h.HomeworkId,
                    g => g.HomeworkId,
                    (h, gradeGroup) => new { Homework = h, Grade = gradeGroup.OrderByDescending(g => g.Date).FirstOrDefault() })
                .Where(x => x.Homework.Lesson.ClassId == selectedClassId) // Фильтр по выбранному классу
                .AsQueryable(); // Преобразуем в IQueryable для дальнейшей фильтрации

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

            // Выполнение запроса и преобразование в DTO
            var submissions = await submissionsQuery
                .OrderByDescending(x => x.Homework.Date) // Сортировка по дате сдачи (последние первые)
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
                    // Использование SubmissionDate (дата сдачи задания)
                    SubmissionDate = x.Homework.Date,
                    StudentAnswer = x.Homework.Text, // Ответ студента
                    StatusId = x.Homework.Status, // Статус задания (0-2)

                    // Используем CurrentTeacherComment для временного хранения текста задания с урока.
                    CurrentTeacherComment = x.Homework.Lesson.HomeworkText,

                    // Данные из Grade (если есть)
                    GradeId = x.Grade != null ? x.Grade.GradeId : (int?)null,
                    CurrentGradeValue = x.Grade != null ? x.Grade.GradeValue : (int?)null,
                })
                .ToListAsync();

            // 4. Формирование ViewModel
            var classDict = availableClasses.ToDictionary(c => c.ClassId, c => c.ClassName);

            var viewModel = new HomeworkReviewViewModel
            {
                Submissions = submissions, // Список заданий для проверки
                AvailableClasses = classDict, // Доступные классы для выбора
                SelectedClassId = selectedClassId, // Выбранный класс
                SelectedClassName = selectedClass?.ClassName ?? "Класс не найден" // Название выбранного класса
            };

            return View(viewModel);
        }

        // --- 2. СОХРАНЕНИЕ ОЦЕНКИ И КОММЕНТАРИЯ ---

        // Модель для запроса на сохранение проверки задания
        public class HomeworkReviewRequest
        {
            public int homeworkId { get; set; } // ID домашнего задания
            public int? gradeValue { get; set; } // Значение оценки (может быть null)
            public string comment { get; set; } // Комментарий учителя
            public int? existingGradeId { get; set; } // ID существующей оценки (для обновления)
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SaveReview([FromBody] HomeworkReviewRequest request)
        {
            var userId = GetCurrentUserId();
            var userRole = User.FindFirst(ClaimTypes.Role)?.Value;

            // Проверка валидности запроса
            if (request == null)
            {
                return BadRequest(new { success = false, message = "Некорректный запрос." });
            }

            // Поиск домашнего задания в БД
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

            // Проверяем, существует ли уже оценка для этого задания
            if (request.existingGradeId.HasValue && request.existingGradeId.Value > 0)
            {
                gradeEntry = await _context.Grades.FirstOrDefaultAsync(g => g.GradeId == request.existingGradeId);
            }

            if (gradeEntry == null)
            {
                // Создание новой записи Grade
                gradeEntry = new Grade
                {
                    StudentId = homework.StudentId, // ID студента
                    HomeworkId = homework.HomeworkId, // Связь с домашним заданием
                    LessonId = homework.LessonId, // ID урока
                    Date = DateTime.Now, // Дата выставления оценки
                };
                _context.Grades.Add(gradeEntry); // Добавление в контекст
            }

            // Обновление полей оценки
            gradeEntry.GradeValue = request.gradeValue; // Значение оценки
            gradeEntry.Comment = request.comment?.Trim(); // Комментарий (убираем пробелы)
            gradeEntry.Date = DateTime.Now; // Обновляем дату

            // Если запись Grade была создана/обновлена, значит, задание проверено.
            if (homework.Status != 2) // 2 = "Проверено"
            {
                homework.Status = 2; // Устанавливаем статус "Проверено"
            }

            try
            {
                await _context.SaveChangesAsync(); // Сохранение изменений в БД

                // Возвращаем успешный результат с ID новой/обновленной оценки
                return Ok(new
                {
                    success = true,
                    message = "Оценка и комментарий сохранены.",
                    newGradeId = gradeEntry.GradeId
                });
            }
            catch (Exception ex)
            {
                // Обработка ошибок сохранения
                return StatusCode(500, new
                {
                    success = false,
                    message = $"Ошибка сохранения: {ex.Message}"
                });
            }
        }
    }
}