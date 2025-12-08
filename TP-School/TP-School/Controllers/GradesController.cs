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

            if (userRole == "Родитель")
            {
                return await ParentView(quarter);
            }
            else if (userRole == "Ученик")
            {
                return await StudentView(GetCurrentUserId(), quarter, false);
            }
            else if (userRole == "Учитель" || userRole == "Директор")
            {
                return await TeacherView(classId, subjectId, quarter);
            }

            return Forbid();
        }

        // --- МЕТОД ДЛЯ РОДИТЕЛЯ ---
        private async Task<IActionResult> ParentView(string quarter)
        {
            var parentId = GetCurrentUserId();

            // Получаем первого привязанного ребенка родителя
            var childRelation = await _context.StudentParentses
                .Where(ps => ps.ParentId == parentId)
                .Select(ps => new { ps.StudentId, ps.Student.FullName })
                .FirstOrDefaultAsync();

            if (childRelation == null)
            {
                // Если детей нет, показываем сообщение
                return View("NoChild");
            }

            var childId = childRelation.StudentId;
            var childName = childRelation.FullName;

            // Прямой вызов метода StudentView с флагом isParentView = true
            return await StudentView(childId, quarter, true);
        }

        // --- МЕТОД ДЛЯ ПРОСМОТРА УСПЕВАЕМОСТИ (общий для ученика и родителя) ---
        private async Task<IActionResult> StudentView(int studentId, string quarter, bool isParentView)
        {
            var studentData = await _context.Users.FindAsync(studentId);

            if (quarter == "Итоговые оценки")
            {
                return await CalculateYearGrades(studentId, studentData.FullName, "Итоговые оценки", isParentView);
            }

            var (startDate, endDate) = GetQuarterDates(quarter);

            var studentClass = await _context.StudentClasses
                .Include(sc => sc.Class)
                .FirstOrDefaultAsync(sc => sc.StudentId == studentId);

            if (studentClass == null)
            {
                return View("StudentView", new StudentGradesViewModel
                {
                    AvailableQuarters = new List<string> { "Итоговые оценки", "I", "II", "III", "IV" },
                    StudentFullName = studentData.FullName,
                    IsParentView = isParentView
                });
            }

            // Все оценки ученика за период по предметам с детализацией
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
                    AverageGrade = g.Average(gr => gr.GradeValue.Value),
                    AllGrades = g.Select(gr => gr.GradeValue.Value).ToList()
                })
                .ToListAsync();

            // Все пропуски ученика за период по предметам с детализацией по типам
            var absencesData = await _context.Attendances
                .Where(a => a.StudentId == studentId && AbsenceStatuses.Contains(a.Status))
                .Include(a => a.Lesson)
                .Where(a => a.Lesson != null && a.Lesson.Date >= startDate && a.Lesson.Date < endDate.AddDays(1))
                .GroupBy(a => new { a.Lesson.SubjectId, a.Status })
                .Select(g => new
                {
                    SubjectId = g.Key.SubjectId,
                    Status = g.Key.Status,
                    Count = g.Count()
                })
                .ToListAsync();

            // Общее количество уроков по каждому предмету за период
            var totalLessonsData = await _context.Schedules
                .Where(l => l.Date >= startDate && l.Date < endDate.AddDays(1))
                .GroupBy(l => l.SubjectId)
                .Select(g => new
                {
                    SubjectId = g.Key,
                    TotalLessons = g.Count()
                })
                .ToListAsync();

            // Объединение и расчет округленной оценки за четверть
            var subjectItems = gradesData.Select(g => new StudentSubjectItem
            {
                SubjectName = g.SubjectName,
                AverageGrade = g.AverageGrade,
                QuarterFinalGrade = (int)Math.Round((double)g.AverageGrade, 0, MidpointRounding.AwayFromZero),
                TotalAbsences = absencesData.Where(a => a.SubjectId == g.SubjectId).Sum(a => a.Count),
                AbsentTypeH = absencesData.FirstOrDefault(a => a.SubjectId == g.SubjectId && a.Status == "Н")?.Count ?? 0,
                AbsentTypeU = absencesData.FirstOrDefault(a => a.SubjectId == g.SubjectId && a.Status == "У")?.Count ?? 0,
                AbsentTypeB = absencesData.FirstOrDefault(a => a.SubjectId == g.SubjectId && a.Status == "Б")?.Count ?? 0,
                TotalLessonsInPeriod = totalLessonsData.FirstOrDefault(t => t.SubjectId == g.SubjectId)?.TotalLessons ?? 0,
                AllGrades = g.AllGrades.Select(grade => (int)Math.Round((double)grade, 0, MidpointRounding.AwayFromZero)).ToList()
            }).ToList();

            // Добавляем предметы, по которым были только пропуски
            var subjectsWithOnlyAbsences = absencesData
                .GroupBy(a => a.SubjectId)
                .Where(g => !subjectItems.Any(si => si.SubjectName == _context.Subjects.Find(g.Key)?.SubjectName))
                .Select(g => new StudentSubjectItem
                {
                    SubjectName = _context.Subjects.Find(g.Key)?.SubjectName,
                    AverageGrade = 0.0,
                    QuarterFinalGrade = 0,
                    TotalAbsences = g.Sum(x => x.Count),
                    AbsentTypeH = g.FirstOrDefault(x => x.Status == "Н")?.Count ?? 0,
                    AbsentTypeU = g.FirstOrDefault(x => x.Status == "У")?.Count ?? 0,
                    AbsentTypeB = g.FirstOrDefault(x => x.Status == "Б")?.Count ?? 0,
                    TotalLessonsInPeriod = totalLessonsData.FirstOrDefault(t => t.SubjectId == g.Key)?.TotalLessons ?? 0,
                    AllGrades = new List<int>()
                })
                .Where(s => s.SubjectName != null);

            subjectItems.AddRange(subjectsWithOnlyAbsences);

            var viewModel = new StudentGradesViewModel
            {
                StudentFullName = studentData.FullName,
                ClassName = studentClass.Class.ClassName,
                Subjects = subjectItems.OrderBy(s => s.SubjectName).ToList(),
                SelectedQuarter = quarter,
                AvailableQuarters = new List<string> { "Итоговые оценки", "I", "II", "III", "IV" },
                IsParentView = isParentView
            };

            return View("StudentView", viewModel);
        }

        // --- МЕТОД ДЛЯ УЧИТЕЛЯ ---
        private async Task<IActionResult> TeacherView(int? classId, int? subjectId, string quarter)
        {
            var userId = GetCurrentUserId();
            var (startDate, endDate) = GetQuarterDates(quarter);

            // 1. Определение доступных классов и предметов
            var accessibleClasses = await GetAccessibleClasses(userId);
            var accessibleSubjects = await GetAccessibleSubjects(userId, accessibleClasses.Keys.ToList());

            if (!accessibleClasses.Any() || !accessibleSubjects.Any())
            {
                return View("TeacherView", new TeacherClassGradesViewModel
                {
                    AvailableQuarters = new List<string> { "Итоговые оценки", "I", "II", "III", "IV" }
                });
            }

            var currentClassId = classId ?? accessibleClasses.Keys.First();
            var currentSubjectId = subjectId ?? accessibleSubjects.Keys.First();

            var selectedClassName = accessibleClasses.ContainsKey(currentClassId) ? accessibleClasses[currentClassId] : "";
            var selectedSubjectName = accessibleSubjects.ContainsKey(currentSubjectId) ? accessibleSubjects[currentSubjectId] : "";

            // 2. Список ID студентов выбранного класса
            var studentIdsInClass = await _context.StudentClasses
                .Where(sc => sc.ClassId == currentClassId)
                .Select(sc => sc.StudentId)
                .ToListAsync();

            if (!studentIdsInClass.Any())
            {
                return View("TeacherView", new TeacherClassGradesViewModel
                {
                    AvailableClasses = accessibleClasses,
                    AvailableSubjects = accessibleSubjects,
                    AvailableQuarters = new List<string> { "Итоговые оценки", "I", "II", "III", "IV" },
                    SelectedClassId = currentClassId,
                    SelectedSubjectId = currentSubjectId,
                    SelectedQuarter = quarter,
                    SelectedClassName = selectedClassName,
                    SelectedSubjectName = selectedSubjectName
                });
            }

            // 3. Сбор данных об оценках для каждого ученика с детализацией
            var gradesData = await _context.Grades
                .Where(g => studentIdsInClass.Contains(g.StudentId))
                .Where(g => g.Lesson.SubjectId == currentSubjectId)
                .Where(g => g.Date >= startDate && g.Date < endDate.AddDays(1))
                .Where(g => g.GradeValue.HasValue)
                .GroupBy(g => g.StudentId)
                .Select(g => new
                {
                    StudentId = g.Key,
                    AverageGrade = g.Average(gr => gr.GradeValue.Value),
                    AllGrades = g.Select(gr => gr.GradeValue.Value).ToList()
                })
                .ToListAsync();

            // 4. Сбор данных о пропусках для каждого ученика с детализацией по типам
            var absencesData = await _context.Attendances
                .Where(a => studentIdsInClass.Contains(a.StudentId) && AbsenceStatuses.Contains(a.Status))
                .Include(a => a.Lesson)
                .Where(a => a.Lesson != null && a.Lesson.SubjectId == currentSubjectId && a.Lesson.Date >= startDate && a.Lesson.Date < endDate.AddDays(1))
                .GroupBy(a => new { a.StudentId, a.Status })
                .Select(g => new
                {
                    StudentId = g.Key.StudentId,
                    Status = g.Key.Status,
                    Count = g.Count()
                })
                .ToListAsync();

            // 5. Общее количество уроков по предмету за период
            var totalLessons = await _context.Schedules
                .Where(l => l.Date >= startDate && l.Date < endDate.AddDays(1) && l.SubjectId == currentSubjectId)
                .CountAsync();

            // 6. Формирование ViewModel - ИЗМЕНЕНО: сортировка на клиенте
            var students = await _context.Users
                .Where(u => studentIdsInClass.Contains(u.UserId))
                .Select(u => new
                {
                    u.UserId,
                    u.FullName
                })
                .ToListAsync();

            // Сортируем на клиенте
            var sortedStudents = students.OrderBy(s => s.FullName).ToList();

            var studentPerformanceItems = new List<TeacherStudentGradeItem>();

            foreach (var student in sortedStudents)
            {
                var studentGrades = gradesData.FirstOrDefault(g => g.StudentId == student.UserId);
                var studentAbsences = absencesData.Where(a => a.StudentId == student.UserId).ToList();

                var avgGrade = studentGrades?.AverageGrade ?? 0.0;
                var allGrades = studentGrades?.AllGrades.Select(grade => (int)Math.Round((double)grade, 0, MidpointRounding.AwayFromZero)).ToList() ?? new List<int>();

                var item = new TeacherStudentGradeItem
                {
                    StudentId = student.UserId,
                    FullName = student.FullName,
                    AverageGrade = avgGrade,
                    QuarterFinalGrade = (int)Math.Round((double)avgGrade, 0, MidpointRounding.AwayFromZero),
                    TotalAbsences = studentAbsences.Sum(a => a.Count),
                    AbsentTypeH = studentAbsences.FirstOrDefault(a => a.Status == "Н")?.Count ?? 0,
                    AbsentTypeU = studentAbsences.FirstOrDefault(a => a.Status == "У")?.Count ?? 0,
                    AbsentTypeB = studentAbsences.FirstOrDefault(a => a.Status == "Б")?.Count ?? 0,
                    TotalLessonsInPeriod = totalLessons,
                    AllGrades = allGrades
                };

                studentPerformanceItems.Add(item);
            }

            var viewModel = new TeacherClassGradesViewModel
            {
                Students = studentPerformanceItems,
                AvailableClasses = accessibleClasses,
                AvailableSubjects = accessibleSubjects,
                AvailableQuarters = new List<string> { "Итоговые оценки", "I", "II", "III", "IV" },
                SelectedClassId = currentClassId,
                SelectedSubjectId = currentSubjectId,
                SelectedQuarter = quarter,
                SelectedClassName = selectedClassName,
                SelectedSubjectName = selectedSubjectName
            };

            return View("TeacherView", viewModel);
        }

        // --- МЕТОД: Расчет годовых оценок ---
        private async Task<IActionResult> CalculateYearGrades(int studentId, string studentFullName, string quarterName, bool isParentView)
        {
            var allQuarters = new List<string> { "I", "II", "III", "IV" };
            var yearFinalGrades = new Dictionary<string, List<int>>();
            var yearAllGrades = new Dictionary<string, List<int>>();
            var yearAbsences = new Dictionary<string, Dictionary<string, int>>();
            var yearTotalLessons = new Dictionary<string, int>();

            foreach (var quarter in allQuarters)
            {
                var (startDate, endDate) = GetQuarterDates(quarter);

                // 1. Сбор всех оценок за четверть с детализацией
                var gradesBySubject = await _context.Grades
                    .Where(g => g.StudentId == studentId)
                    .Where(g => g.Date >= startDate && g.Date < endDate.AddDays(1))
                    .Include(g => g.Lesson).ThenInclude(l => l.Subject)
                    .Where(g => g.GradeValue.HasValue)
                    .Select(g => new
                    {
                        SubjectName = g.Lesson.Subject.SubjectName,
                        GradeValue = g.GradeValue.Value
                    })
                    .ToListAsync();

                // Группировка оценок по предметам
                var groupedGrades = gradesBySubject
                    .GroupBy(g => g.SubjectName)
                    .Select(g => new
                    {
                        SubjectName = g.Key,
                        QuarterFinalGrade = (int)Math.Round((double)g.Average(x => x.GradeValue), 0, MidpointRounding.AwayFromZero),
                        AllGrades = g.Select(x => (int)Math.Round((double)x.GradeValue, 0, MidpointRounding.AwayFromZero)).ToList()
                    })
                    .ToList();

                foreach (var item in groupedGrades)
                {
                    if (!yearFinalGrades.ContainsKey(item.SubjectName))
                    {
                        yearFinalGrades.Add(item.SubjectName, new List<int>());
                    }
                    yearFinalGrades[item.SubjectName].Add(item.QuarterFinalGrade);

                    if (!yearAllGrades.ContainsKey(item.SubjectName))
                    {
                        yearAllGrades.Add(item.SubjectName, new List<int>());
                    }
                    yearAllGrades[item.SubjectName].AddRange(item.AllGrades);
                }

                // 2. Сбор пропусков за четверть с детализацией
                var absencesInQuarter = await _context.Attendances
                    .Where(a => a.StudentId == studentId && AbsenceStatuses.Contains(a.Status))
                    .Include(a => a.Lesson)
                    .Where(a => a.Lesson != null && a.Lesson.Date >= startDate && a.Lesson.Date < endDate.AddDays(1))
                    .GroupBy(a => new { a.Lesson.Subject.SubjectName, a.Status })
                    .Select(g => new {
                        SubjectName = g.Key.SubjectName,
                        Status = g.Key.Status,
                        Count = g.Count()
                    })
                    .ToListAsync();

                foreach (var item in absencesInQuarter)
                {
                    if (!yearAbsences.ContainsKey(item.SubjectName))
                    {
                        yearAbsences.Add(item.SubjectName, new Dictionary<string, int>
                        {
                            { "Н", 0 },
                            { "У", 0 },
                            { "Б", 0 }
                        });
                    }

                    if (yearAbsences[item.SubjectName].ContainsKey(item.Status))
                    {
                        yearAbsences[item.SubjectName][item.Status] += item.Count;
                    }
                }

                // 3. Сбор общего количества уроков за четверть
                var lessonsInQuarter = await _context.Schedules
                    .Where(l => l.Date >= startDate && l.Date < endDate.AddDays(1))
                    .GroupBy(l => l.Subject.SubjectName)
                    .Select(g => new { SubjectName = g.Key, TotalLessons = g.Count() })
                    .ToListAsync();

                foreach (var item in lessonsInQuarter)
                {
                    if (!yearTotalLessons.ContainsKey(item.SubjectName))
                    {
                        yearTotalLessons.Add(item.SubjectName, 0);
                    }
                    yearTotalLessons[item.SubjectName] += item.TotalLessons;
                }
            }

            // 3. Расчет годовой оценки
            var finalYearItems = yearFinalGrades.Select(kvp => {
                var subjectName = kvp.Key;
                var quarterFinalGrades = kvp.Value;
                var avgOfFinals = quarterFinalGrades.Average();

                var absences = yearAbsences.ContainsKey(subjectName) ? yearAbsences[subjectName] :
                    new Dictionary<string, int> { { "Н", 0 }, { "У", 0 }, { "Б", 0 } };

                var allGrades = yearAllGrades.ContainsKey(subjectName) ? yearAllGrades[subjectName] : new List<int>();

                return new StudentSubjectItem
                {
                    SubjectName = subjectName,
                    AverageGrade = avgOfFinals,
                    QuarterFinalGrade = (int)Math.Round((double)avgOfFinals, 0, MidpointRounding.AwayFromZero),
                    TotalAbsences = absences.Sum(a => a.Value),
                    AbsentTypeH = absences.ContainsKey("Н") ? absences["Н"] : 0,
                    AbsentTypeU = absences.ContainsKey("У") ? absences["У"] : 0,
                    AbsentTypeB = absences.ContainsKey("Б") ? absences["Б"] : 0,
                    TotalLessonsInPeriod = yearTotalLessons.ContainsKey(subjectName) ? yearTotalLessons[subjectName] : 0,
                    AllGrades = allGrades
                };
            }).ToList();

            // Добавляем предметы, по которым были только пропуски
            var subjectsWithOnlyAbsences = yearAbsences
                .Where(kvp => !finalYearItems.Any(f => f.SubjectName == kvp.Key))
                .Select(kvp => new StudentSubjectItem
                {
                    SubjectName = kvp.Key,
                    AverageGrade = 0.0,
                    QuarterFinalGrade = 0,
                    TotalAbsences = kvp.Value.Sum(v => v.Value),
                    AbsentTypeH = kvp.Value.ContainsKey("Н") ? kvp.Value["Н"] : 0,
                    AbsentTypeU = kvp.Value.ContainsKey("У") ? kvp.Value["У"] : 0,
                    AbsentTypeB = kvp.Value.ContainsKey("Б") ? kvp.Value["Б"] : 0,
                    TotalLessonsInPeriod = yearTotalLessons.ContainsKey(kvp.Key) ? yearTotalLessons[kvp.Key] : 0,
                    AllGrades = new List<int>()
                });

            finalYearItems.AddRange(subjectsWithOnlyAbsences);

            var studentClass = await _context.StudentClasses
                .Include(sc => sc.Class)
                .FirstOrDefaultAsync(sc => sc.StudentId == studentId);

            var viewModel = new StudentGradesViewModel
            {
                StudentFullName = studentFullName,
                ClassName = studentClass?.Class.ClassName,
                Subjects = finalYearItems.OrderBy(s => s.SubjectName).ToList(),
                SelectedQuarter = quarterName,
                AvailableQuarters = new List<string> { "Итоговые оценки", "I", "II", "III", "IV" },
                IsParentView = isParentView
            };

            return View("StudentView", viewModel);
        }

        // --- ВСПОМОГАТЕЛЬНЫЕ МЕТОДЫ ---
        private async Task<Dictionary<int, string>> GetAccessibleClasses(int userId)
        {
            var userRole = GetCurrentUserRole();
            var query = _context.SchoolClasses.AsQueryable();

            if (userRole == "Директор")
            {
                // Директор видит все классы
            }
            else if (userRole == "Учитель")
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

        private async Task<Dictionary<int, string>> GetAccessibleSubjects(int userId, List<int> accessibleClassIds)
        {
            var userRole = GetCurrentUserRole();
            var query = _context.Subjects.AsQueryable();

            if (userRole == "Директор")
            {
                // Директор видит все предметы
            }
            else if (userRole == "Учитель")
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

        private string GetCurrentQuarter()
        {
            var today = DateTime.Today;
            var currentYear = today.Month >= 9 ? today.Year : today.Year - 1;

            if (today >= new DateTime(currentYear, 9, 1) && today <= new DateTime(currentYear, 10, 31)) return "I";
            if (today >= new DateTime(currentYear, 11, 1) && today <= new DateTime(currentYear, 12, 31)) return "II";
            if (today >= new DateTime(currentYear + 1, 1, 15) && today <= new DateTime(currentYear + 1, 3, 31)) return "III";
            if (today >= new DateTime(currentYear + 1, 4, 1) && today <= new DateTime(currentYear + 1, 5, 25)) return "IV";

            return "Итоговые оценки";
        }

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
                case "Итоговые оценки":
                default:
                    effectiveEndDate = new DateTime(currentYear + 1, 6, 30);
                    return (startSchoolYear, effectiveEndDate);
            }

            return (startSchoolYear, effectiveEndDate);
        }
    }
}