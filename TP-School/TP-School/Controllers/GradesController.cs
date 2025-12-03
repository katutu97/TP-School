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
    [Authorize]
    public class GradesController : Controller
    {
        private readonly ApplicationDbContext _context;

        // Статусы пропусков в БД, которые нужно считать ("Н" - не был, "Б" - болен/уважительная причина)
        private List<string> AbsenceStatuses { get; } = new List<string> { "Н", "Б", "У" };

        public GradesController(ApplicationDbContext context)
        {
            _context = context;
        }

        private int GetCurrentUserId()
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            return int.TryParse(userIdClaim, out int userId) ? userId : 0;
        }

        private string GetCurrentUserRole()
        {
            return User.FindFirst(ClaimTypes.Role)?.Value ?? "Гость";
        }

        // --- ГЛАВНАЯ ТОЧКА ВХОДА ---
        [HttpGet]
        public async Task<IActionResult> Index(int? classId, int? subjectId, string quarter)
        {
            var userRole = GetCurrentUserRole();

            if (string.IsNullOrEmpty(quarter))
            {
                quarter = GetCurrentQuarter();
            }

            if (userRole == "Ученик")
            {
                return await StudentView(quarter);
            }
            else if (userRole == "Учитель" || userRole == "Директор")
            {
                // В представлении учителя/директора режим "Итоговые оценки" пока не реализован
                return await TeacherView(classId, subjectId, quarter);
            }

            return Forbid();
        }

        // --- МЕТОД ДЛЯ УЧЕНИКА ---
        private async Task<IActionResult> StudentView(string quarter)
        {
            var studentId = GetCurrentUserId();
            var studentData = await _context.Users.FindAsync(studentId);

            // 1. Обработка нового режима "Итоговые оценки" (Годовая оценка)
            if (quarter == "Итоговые оценки")
            {
                // Вызываем расчет годовых оценок
                return await CalculateYearGrades(studentId, studentData.FullName, "Итоговые оценки");
            }

            // 2. Обработка обычной четверти
            var (startDate, endDate) = GetQuarterDates(quarter);

            var studentClass = await _context.StudentClasses
                .Include(sc => sc.Class)
                .FirstOrDefaultAsync(sc => sc.StudentId == studentId);

            if (studentClass == null)
            {
                // Используем ViewModel с обновленным списком четвертей
                return View("StudentView", new StudentGradesViewModel
                {
                    AvailableQuarters = new List<string> { "Итоговые оценки", "I", "II", "III", "IV" }
                });
            }

            // Все оценки ученика за период по предметам
            var gradesData = await _context.Grades
                .Where(g => g.StudentId == studentId)
                .Where(g => g.Date >= startDate && g.Date < endDate.AddDays(1))
                .Include(g => g.Lesson)
                .ThenInclude(l => l.Subject)
                .Where(g => g.GradeValue.HasValue)
                .GroupBy(g => g.Lesson.Subject)
                .Select(g => new
                {
                    SubjectId = g.Key.SubjectId,
                    SubjectName = g.Key.SubjectName,
                    AverageGrade = g.Average(gr => gr.GradeValue.Value)
                })
                .ToListAsync();



            // Все пропуски ученика за период по предметам
            var absencesData = await _context.Attendances
                .Where(a => a.StudentId == studentId && AbsenceStatuses.Contains(a.Status))
                .Include(a => a.Lesson)
                .Where(a => a.Lesson != null && a.Lesson.Date >= startDate && a.Lesson.Date < endDate.AddDays(1))
                .GroupBy(a => a.Lesson.SubjectId)
                .Select(g => new
                {
                    SubjectId = g.Key,
                    TotalAbsences = g.Count()
                })
                .ToListAsync();

            // 3. Объединение и расчет округленной оценки за четверть
            var subjectItems = gradesData.Select(g => new SubjectGradesItem
            {
                SubjectName = g.SubjectName,
                AverageGrade = g.AverageGrade,
                // Итоговая оценка: округленный средний балл за период (четверть)
                QuarterFinalGrade = (int)Math.Round(g.AverageGrade, MidpointRounding.AwayFromZero),
                TotalAbsences = absencesData.FirstOrDefault(a => a.SubjectId == g.SubjectId)?.TotalAbsences ?? 0
            }).ToList();

            // Добавляем предметы, по которым были только пропуски
            var subjectsWithOnlyAbsences = absencesData
                .Where(a => !subjectItems.Any(si => si.SubjectName == _context.Subjects.Find(a.SubjectId)?.SubjectName))
                .Select(a => new SubjectGradesItem
                {
                    SubjectName = _context.Subjects.Find(a.SubjectId)?.SubjectName,
                    AverageGrade = 0.0,
                    QuarterFinalGrade = 0,
                    TotalAbsences = a.TotalAbsences
                })
                .Where(s => s.SubjectName != null);

            subjectItems.AddRange(subjectsWithOnlyAbsences);

            var viewModel = new StudentGradesViewModel
            {
                StudentFullName = studentData.FullName,
                ClassName = studentClass.Class.ClassName,
                Subjects = subjectItems.OrderBy(s => s.SubjectName).ToList(),
                SelectedQuarter = quarter,
                AvailableQuarters = new List<string> { "Итоговые оценки", "I", "II", "III", "IV" }
            };

            return View("StudentView", viewModel);
        }

        // --- МЕТОД: Расчет годовых оценок (используется для режима "Итоговые оценки") ---
        private async Task<IActionResult> CalculateYearGrades(int studentId, string studentFullName, string quarterName)
        {
            var allQuarters = new List<string> { "I", "II", "III", "IV" };
            var yearFinalGrades = new Dictionary<string, List<int>>();
            var yearAbsences = new Dictionary<string, int>();

            foreach (var quarter in allQuarters)
            {
                var (startDate, endDate) = GetQuarterDates(quarter);

                // 1. Сбор итоговых оценок за четверть
                var gradesBySubject = await _context.Grades
                    .Where(g => g.StudentId == studentId)
                    .Where(g => g.Date >= startDate && g.Date < endDate.AddDays(1))
                    .Include(g => g.Lesson).ThenInclude(l => l.Subject)
                    .Where(g => g.GradeValue.HasValue)
                    .GroupBy(g => g.Lesson.Subject.SubjectName)
                    .Select(g => new
                    {
                        SubjectName = g.Key,
                        // Расчет итоговой оценки за четверть (округленное среднее)
                        QuarterFinalGrade = (int)Math.Round(g.Average(gr => gr.GradeValue.Value), MidpointRounding.AwayFromZero)
                    })
                    .ToListAsync();

                foreach (var item in gradesBySubject)
                {
                    if (!yearFinalGrades.ContainsKey(item.SubjectName))
                    {
                        yearFinalGrades.Add(item.SubjectName, new List<int>());
                    }
                    yearFinalGrades[item.SubjectName].Add(item.QuarterFinalGrade);
                }

                // 2. Сбор пропусков за четверть (суммируем за весь год)
                var absencesInQuarter = await _context.Attendances
                    .Where(a => a.StudentId == studentId && AbsenceStatuses.Contains(a.Status))
                    .Include(a => a.Lesson)
                    .Where(a => a.Lesson != null && a.Lesson.Date >= startDate && a.Lesson.Date < endDate.AddDays(1))
                    .GroupBy(a => a.Lesson.Subject.SubjectName)
                    .Select(g => new { SubjectName = g.Key, TotalAbsences = g.Count() })
                    .ToListAsync();

                foreach (var item in absencesInQuarter)
                {
                    if (!yearAbsences.ContainsKey(item.SubjectName))
                    {
                        yearAbsences.Add(item.SubjectName, 0);
                    }
                    yearAbsences[item.SubjectName] += item.TotalAbsences;
                }
            }

            // 3. Расчет годовой оценки
            var finalYearItems = yearFinalGrades.Select(kvp => {
                var subjectName = kvp.Key;
                var quarterFinalGrades = kvp.Value;

                var avgOfFinals = quarterFinalGrades.Average();

                return new SubjectGradesItem
                {
                    SubjectName = subjectName,
                    AverageGrade = avgOfFinals,
                    // Итоговая годовая оценка (округленное среднее из четвертных оценок)
                    QuarterFinalGrade = (int)Math.Round(avgOfFinals, MidpointRounding.AwayFromZero),
                    TotalAbsences = yearAbsences.ContainsKey(subjectName) ? yearAbsences[subjectName] : 0
                };
            }).ToList();

            var studentClass = await _context.StudentClasses
                .Include(sc => sc.Class)
                .FirstOrDefaultAsync(sc => sc.StudentId == studentId);

            var viewModel = new StudentGradesViewModel
            {
                StudentFullName = studentFullName,
                ClassName = studentClass?.Class.ClassName,
                Subjects = finalYearItems.OrderBy(s => s.SubjectName).ToList(),
                SelectedQuarter = quarterName,
                AvailableQuarters = new List<string> { "Итоговые оценки", "I", "II", "III", "IV" }
            };

            return View("StudentView", viewModel);
        }

        // Вспомогательный метод для определения текущей четверти
        private string GetCurrentQuarter()
        {
            var today = DateTime.Today;
            var currentYear = today.Month >= 9 ? today.Year : today.Year - 1;

            if (today >= new DateTime(currentYear, 9, 1) && today <= new DateTime(currentYear, 10, 31)) return "I";
            if (today >= new DateTime(currentYear, 11, 1) && today <= new DateTime(currentYear, 12, 31)) return "II";
            if (today >= new DateTime(currentYear + 1, 1, 15) && today <= new DateTime(currentYear + 1, 3, 31)) return "III";
            if (today >= new DateTime(currentYear + 1, 4, 1) && today <= new DateTime(currentYear + 1, 5, 25)) return "IV";

            // Если дата не попадает в четверти, по умолчанию выбираем Итоговые оценки
            return "Итоговые оценки";
        }

        // Вспомогательный метод для получения дат четвертей
        private (DateTime startDate, DateTime endDate) GetQuarterDates(string quarter)
        {
            int currentYear = DateTime.Today.Year;
            if (DateTime.Today.Month >= 9)
            {
                currentYear = DateTime.Today.Year;
            }
            else
            {
                currentYear = DateTime.Today.Year - 1;
            }

            DateTime startSchoolYear = new DateTime(currentYear, 9, 1);
            DateTime effectiveEndDate;

            switch (quarter)
            {
                case "I":
                    effectiveEndDate = new DateTime(currentYear, 10, 31);
                    break;
                case "II":
                    effectiveEndDate = new DateTime(currentYear, 12, 31);
                    return (new DateTime(currentYear, 11, 1), effectiveEndDate);
                case "III":
                    effectiveEndDate = new DateTime(currentYear + 1, 3, 31);
                    return (new DateTime(currentYear + 1, 1, 15), effectiveEndDate);
                case "IV":
                    effectiveEndDate = new DateTime(currentYear + 1, 5, 25);
                    return (new DateTime(currentYear + 1, 4, 1), effectiveEndDate);
                // "Итоговые оценки" (бывший "Общий")
                case "Итоговые оценки":
                default:
                    effectiveEndDate = new DateTime(currentYear + 1, 6, 30);
                    return (startSchoolYear, effectiveEndDate);
            }

            return (startSchoolYear, effectiveEndDate);
        }

        // ... (Методы TeacherView и вспомогательные методы для учителя/директора без изменений) ...
        private async Task<IActionResult> TeacherView(int? classId, int? subjectId, string quarter)
        {
            var userId = GetCurrentUserId();
            var userRole = GetCurrentUserRole();
            // Здесь мы используем GetQuarterDates, который вернет даты для "Итоговые оценки"
            // как для всего года (с 1 сентября по 30 июня)
            var (startDate, endDate) = GetQuarterDates(quarter);

            // 1. Определение доступных классов и предметов
            var accessibleClasses = await GetAccessibleClasses(userId, userRole);
            var accessibleSubjects = await GetAccessibleSubjects(userId, userRole, accessibleClasses.Keys.ToList());

            if (!accessibleClasses.Any() || !accessibleSubjects.Any())
            {
                return View("TeacherView", new TeacherGradesViewModel());
            }

            var currentClassId = classId ?? accessibleClasses.Keys.First();
            var currentSubjectId = subjectId ?? accessibleSubjects.Keys.First();

            // 2. Список ID студентов выбранного класса
            var studentIdsInClass = await _context.StudentClasses
                .Where(sc => sc.ClassId == currentClassId)
                .Select(sc => sc.StudentId)
                .ToListAsync();

            // 3. Предварительная загрузка всех оценок и пропусков для класса/предмета/четверти

            // Получаем средний балл (AvgGrade) для каждого ученика
            var gradesAverages = await _context.Grades
                .Where(g => studentIdsInClass.Contains(g.StudentId))
                .Where(g => g.Lesson.SubjectId == currentSubjectId)
                // Фильтрация по датам, включая режим "Итоговые оценки" (весь год)
                .Where(g => g.Date >= startDate && g.Date < endDate.AddDays(1))
                .Where(g => g.GradeValue.HasValue)
                .GroupBy(g => g.StudentId)
                .Select(g => new { StudentId = g.Key, AverageGrade = g.Average(gr => gr.GradeValue.Value) })
                .ToDictionaryAsync(x => x.StudentId, x => x.AverageGrade);

            // Получаем количество пропусков (TotalAbsences) для каждого ученика
            var absencesCounts = await _context.Attendances
                .Where(a => studentIdsInClass.Contains(a.StudentId) && AbsenceStatuses.Contains(a.Status))
                .Include(a => a.Lesson)
                .Where(a => a.Lesson != null && a.Lesson.SubjectId == currentSubjectId && a.Lesson.Date >= startDate && a.Lesson.Date < endDate.AddDays(1))
                .GroupBy(a => a.StudentId)
                .Select(g => new { StudentId = g.Key, TotalAbsences = g.Count() })
                .ToDictionaryAsync(x => x.StudentId, x => x.TotalAbsences);


            // 4. Формирование ViewModel
            var studentPerformance = await _context.Users
                .Where(u => studentIdsInClass.Contains(u.UserId))
                .Select(u => new StudentPerformanceItem
                {
                    StudentId = u.UserId,
                    FullName = u.FullName,
                    AverageGrade = gradesAverages.ContainsKey(u.UserId) ? gradesAverages[u.UserId] : 0.0,
                    TotalAbsences = absencesCounts.ContainsKey(u.UserId) ? absencesCounts[u.UserId] : 0
                })
                .OrderBy(s => s.FullName)
                .ToListAsync();

            var viewModel = new TeacherGradesViewModel
            {
                Students = studentPerformance,
                AvailableClasses = accessibleClasses,
                AvailableSubjects = accessibleSubjects,
                SelectedClassId = currentClassId,
                SelectedSubjectId = currentSubjectId,
                SelectedQuarter = quarter
            };

            return View("TeacherView", viewModel);
        }

        // ... (остальные вспомогательные методы GetAccessibleClasses, GetAccessibleSubjects без изменений) ...

        private async Task<Dictionary<int, string>> GetAccessibleClasses(int userId, string role)
        {
            var query = _context.SchoolClasses.AsQueryable();

            if (role == "Директор")
            {
                // Директор видит все классы
            }
            else if (role == "Учитель")
            {
                var teachingClassIds = await _context.ClassSubjectTeachers
                    .Where(cst => cst.TeacherId == userId)
                    .Select(cst => cst.ClassId)
                    .Distinct()
                    .ToListAsync();

                var curatingClassIds = await _context.SchoolClasses
                    .Where(sc => sc.ClassTeacherId == userId)
                    .Select(sc => sc.ClassId)
                    .Distinct()
                    .ToListAsync();

                var classIds = teachingClassIds.Union(curatingClassIds).ToList();

                query = query.Where(sc => classIds.Contains(sc.ClassId));
            }
            else
            {
                return new Dictionary<int, string>();
            }

            return await query
                .OrderBy(sc => sc.ClassNumber)
                .ThenBy(sc => sc.ClassLetter)
                .ToDictionaryAsync(sc => sc.ClassId, sc => sc.ClassName);
        }

        private async Task<Dictionary<int, string>> GetAccessibleSubjects(int userId, string role, List<int> accessibleClassIds)
        {
            var query = _context.Subjects.AsQueryable();

            if (role == "Директор")
            {
                // Директор видит все предметы
            }
            else if (role == "Учитель")
            {
                var subjectIds = await _context.ClassSubjectTeachers
                    .Where(cst => cst.TeacherId == userId && accessibleClassIds.Contains(cst.ClassId))
                    .Select(cst => cst.SubjectId)
                    .Distinct()
                    .ToListAsync();

                query = query.Where(s => subjectIds.Contains(s.SubjectId));
            }

            return await query
                .OrderBy(s => s.SubjectName)
                .ToDictionaryAsync(s => s.SubjectId, s => s.SubjectName);
        }
    }
}