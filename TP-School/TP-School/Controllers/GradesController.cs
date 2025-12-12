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
        private List<string> AbsenceStatuses { get; } = new List<string> { "Н", "Б", "У" }; // Статусы пропусков

        public GradesController(ApplicationDbContext context)
        {
            _context = context;
        }

        // Получение ID текущего пользователя 
        private int GetCurrentUserId()
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            return int.TryParse(userIdClaim, out int userId) ? userId : 0; // Возвращает 0, если не удалось найти
        }

        // Получение роли текущего пользователя
        private string GetCurrentUserRole()
        {
            return User.FindFirst(ClaimTypes.Role)?.Value ?? "Гость"; // Если роль не указана, возвращает "Гость"
        }

        // --- ГЛАВНАЯ ТОЧКА ВХОДА ---
        // Основной метод для отображения страницы успеваемости
        [HttpGet]
        public async Task<IActionResult> Index(int? classId, int? subjectId, string quarter)
        {
            var userRole = GetCurrentUserRole(); // Определение роли пользователя

            // Если четверть не указана, определяем текущую
            if (string.IsNullOrEmpty(quarter))
            {
                quarter = GetCurrentQuarter();
            }

            // Маршрутизация в зависимости от роли пользователя
            if (userRole == "Родитель")
            {
                return await ParentView(quarter); // Родитель видит успеваемость своего ребенка
            }
            else if (userRole == "Ученик")
            {
                // Ученик видит свою собственную успеваемость
                return await StudentView(GetCurrentUserId(), quarter, false);
            }
            else if (userRole == "Учитель" || userRole == "Директор")
            {
                // Учитель и директор видят успеваемость выбранного класса по выбранному предмету
                return await TeacherView(classId, subjectId, quarter);
            }

            return Forbid(); // Запрет доступа для других ролей
        }

        // --- МЕТОД ДЛЯ РОДИТЕЛЯ ---
        // Отображение успеваемости ребенка родителем
        private async Task<IActionResult> ParentView(string quarter)
        {
            var parentId = GetCurrentUserId(); // ID текущего родителя

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

            var childId = childRelation.StudentId; // ID ребенка
            var childName = childRelation.FullName; // Имя ребенка

            // Прямой вызов метода 
            return await StudentView(childId, quarter, true);
        }

        // --- МЕТОД ДЛЯ ПРОСМОТРА УСПЕВАЕМОСТИ (общий для ученика и родителя) ---
        // Отображение детальной успеваемости ученика за выбранную четверть
        private async Task<IActionResult> StudentView(int studentId, string quarter, bool isParentView)
        {
            var studentData = await _context.Users.FindAsync(studentId); // Получение данных ученика

            // Если выбраны итоговые оценки, рассчитываем годовую успеваемость
            if (quarter == "Итоговые оценки")
            {
                return await CalculateYearGrades(studentId, studentData.FullName, "Итоговые оценки", isParentView);
            }

            // Получение дат начала и конца выбранной четверти
            var (startDate, endDate) = GetQuarterDates(quarter);

            // Получение класса, в котором учится ученик
            var studentClass = await _context.StudentClasses
                .Include(sc => sc.Class)
                .FirstOrDefaultAsync(sc => sc.StudentId == studentId);

            if (studentClass == null)
            {
                // Если ученик не привязан к классу, возвращаем пустую модель
                return View("StudentView", new StudentGradesViewModel
                {
                    AvailableQuarters = new List<string> { "Итоговые оценки", "I", "II", "III", "IV" },
                    StudentFullName = studentData.FullName,
                    IsParentView = isParentView
                });
            }

            // Все оценки ученика за период по предметам с детализацией
            var gradesData = await _context.Grades
                .Where(g => g.StudentId == studentId) // Фильтр по ученику
                .Where(g => g.Date >= startDate && g.Date < endDate.AddDays(1)) // Фильтр по дате
                .Include(g => g.Lesson) // Включаем данные урока
                .ThenInclude(l => l.Subject) // Включаем данные предмета
                .Where(g => g.GradeValue.HasValue) // Только оценки (не пропуски)
                .GroupBy(g => g.Lesson.Subject) // Группировка по предмету
                .Select(g => new
                {
                    SubjectId = g.Key.SubjectId,
                    SubjectName = g.Key.SubjectName,
                    AverageGrade = g.Average(gr => gr.GradeValue.Value), // Средняя оценка
                    AllGrades = g.Select(gr => gr.GradeValue.Value).ToList() // Все оценки по предмету
                })
                .ToListAsync();

            // Все пропуски ученика за период по предметам с детализацией по типам
            var absencesData = await _context.Attendances
                .Where(a => a.StudentId == studentId && AbsenceStatuses.Contains(a.Status)) // Фильтр по ученику и статусам пропусков
                .Include(a => a.Lesson) // Включаем данные урока
                .Where(a => a.Lesson != null && a.Lesson.Date >= startDate && a.Lesson.Date < endDate.AddDays(1)) // Фильтр по дате
                .GroupBy(a => new { a.Lesson.SubjectId, a.Status }) // Группировка по предмету и типу пропуска
                .Select(g => new
                {
                    SubjectId = g.Key.SubjectId,
                    Status = g.Key.Status,
                    Count = g.Count() // Количество пропусков каждого типа
                })
                .ToListAsync();

            // Общее количество уроков по каждому предмету за период
            var totalLessonsData = await _context.Schedules
                .Where(l => l.Date >= startDate && l.Date < endDate.AddDays(1)) // Фильтр по дате
                .GroupBy(l => l.SubjectId) // Группировка по предмету
                .Select(g => new
                {
                    SubjectId = g.Key,
                    TotalLessons = g.Count() // Общее количество уроков
                })
                .ToListAsync();

            // Объединение и расчет округленной оценки за четверть
            var subjectItems = gradesData.Select(g => new StudentSubjectItem
            {
                SubjectName = g.SubjectName,
                AverageGrade = g.AverageGrade,
                QuarterFinalGrade = (int)Math.Round((double)g.AverageGrade, 0, MidpointRounding.AwayFromZero), // Итоговая оценка за четверть
                TotalAbsences = absencesData.Where(a => a.SubjectId == g.SubjectId).Sum(a => a.Count), // Общее количество пропусков
                AbsentTypeH = absencesData.FirstOrDefault(a => a.SubjectId == g.SubjectId && a.Status == "Н")?.Count ?? 0, // Пропуски по болезни
                AbsentTypeU = absencesData.FirstOrDefault(a => a.SubjectId == g.SubjectId && a.Status == "У")?.Count ?? 0, // Уважительные пропуски
                AbsentTypeB = absencesData.FirstOrDefault(a => a.SubjectId == g.SubjectId && a.Status == "Б")?.Count ?? 0, // Неуважительные пропуски
                TotalLessonsInPeriod = totalLessonsData.FirstOrDefault(t => t.SubjectId == g.SubjectId)?.TotalLessons ?? 0, // Всего уроков за период
                AllGrades = g.AllGrades.Select(grade => (int)Math.Round((double)grade, 0, MidpointRounding.AwayFromZero)).ToList() // Все оценки
            }).ToList();

            // Добавление предметов, по которым были только пропуски (без оценок)
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
                .Where(s => s.SubjectName != null); // Исключаем null

            subjectItems.AddRange(subjectsWithOnlyAbsences);

            // Формирование модели представления
            var viewModel = new StudentGradesViewModel
            {
                StudentFullName = studentData.FullName,
                ClassName = studentClass.Class.ClassName,
                Subjects = subjectItems.OrderBy(s => s.SubjectName).ToList(), // Сортировка по названию предмета
                SelectedQuarter = quarter,
                AvailableQuarters = new List<string> { "Итоговые оценки", "I", "II", "III", "IV" },
                IsParentView = isParentView
            };

            return View("StudentView", viewModel);
        }

        // --- МЕТОД ДЛЯ УЧИТЕЛЯ ---
        // Отображение успеваемости класса по предмету для учителя/директора
        private async Task<IActionResult> TeacherView(int? classId, int? subjectId, string quarter)
        {
            var userId = GetCurrentUserId();
            var (startDate, endDate) = GetQuarterDates(quarter); // Даты выбранной четверти

            // 1. Определение доступных классов и предметов для текущего пользователя
            var accessibleClasses = await GetAccessibleClasses(userId);
            var accessibleSubjects = await GetAccessibleSubjects(userId, accessibleClasses.Keys.ToList());

            if (!accessibleClasses.Any() || !accessibleSubjects.Any())
            {
                // Если нет доступных классов или предметов, возвращаем пустую модель
                return View("TeacherView", new TeacherClassGradesViewModel
                {
                    AvailableQuarters = new List<string> { "Итоговые оценки", "I", "II", "III", "IV" }
                });
            }

            // Установка значений по умолчанию, если параметры не указаны
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
                // Если в классе нет учеников, возвращаем пустую модель
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
                .Where(g => studentIdsInClass.Contains(g.StudentId)) // Фильтр по ученикам класса
                .Where(g => g.Lesson.SubjectId == currentSubjectId) // Фильтр по предмету
                .Where(g => g.Date >= startDate && g.Date < endDate.AddDays(1)) // Фильтр по дате
                .Where(g => g.GradeValue.HasValue) // Только оценки
                .GroupBy(g => g.StudentId) // Группировка по ученику
                .Select(g => new
                {
                    StudentId = g.Key,
                    AverageGrade = g.Average(gr => gr.GradeValue.Value), // Средняя оценка
                    AllGrades = g.Select(gr => gr.GradeValue.Value).ToList() // Все оценки ученика
                })
                .ToListAsync();

            // 4. Сбор данных о пропусках для каждого ученика с детализацией по типам
            var absencesData = await _context.Attendances
                .Where(a => studentIdsInClass.Contains(a.StudentId) && AbsenceStatuses.Contains(a.Status)) // Фильтр по ученикам и статусам
                .Include(a => a.Lesson) // Включаем данные урока
                .Where(a => a.Lesson != null && a.Lesson.SubjectId == currentSubjectId && a.Lesson.Date >= startDate && a.Lesson.Date < endDate.AddDays(1)) // Фильтр по предмету и дате
                .GroupBy(a => new { a.StudentId, a.Status }) // Группировка по ученику и типу пропуска
                .Select(g => new
                {
                    StudentId = g.Key.StudentId,
                    Status = g.Key.Status,
                    Count = g.Count() // Количество пропусков
                })
                .ToListAsync();

            // 5. Общее количество уроков по предмету за период
            var totalLessons = await _context.Schedules
                .Where(l => l.Date >= startDate && l.Date < endDate.AddDays(1) && l.SubjectId == currentSubjectId)
                .CountAsync();

            // 6. Формирование ViewModel 
            var students = await _context.Users
                .Where(u => studentIdsInClass.Contains(u.UserId)) // Фильтр по ID учеников
                .Select(u => new
                {
                    u.UserId,
                    u.FullName
                })
                .ToListAsync();

            // Сортируем на клиенте по алфавиту
            var sortedStudents = students.OrderBy(s => s.FullName).ToList();

            var studentPerformanceItems = new List<TeacherStudentGradeItem>();

            // Создание элементов для каждого ученика
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
                    QuarterFinalGrade = (int)Math.Round((double)avgGrade, 0, MidpointRounding.AwayFromZero), // Итоговая оценка за четверть
                    TotalAbsences = studentAbsences.Sum(a => a.Count), // Всего пропусков
                    AbsentTypeH = studentAbsences.FirstOrDefault(a => a.Status == "Н")?.Count ?? 0,
                    AbsentTypeU = studentAbsences.FirstOrDefault(a => a.Status == "У")?.Count ?? 0,
                    AbsentTypeB = studentAbsences.FirstOrDefault(a => a.Status == "Б")?.Count ?? 0,
                    TotalLessonsInPeriod = totalLessons, // Всего уроков за период
                    AllGrades = allGrades // Все оценки
                };

                studentPerformanceItems.Add(item);
            }

            // Формирование модели представления для учителя
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
        // Расчет и отображение итоговых годовых оценок ученика
        private async Task<IActionResult> CalculateYearGrades(int studentId, string studentFullName, string quarterName, bool isParentView)
        {
            var allQuarters = new List<string> { "I", "II", "III", "IV" };
            var yearFinalGrades = new Dictionary<string, List<int>>(); // Итоговые оценки по четвертям
            var yearAllGrades = new Dictionary<string, List<int>>(); // Все оценки за год
            var yearAbsences = new Dictionary<string, Dictionary<string, int>>(); // Пропуски за год
            var yearTotalLessons = new Dictionary<string, int>(); // Всего уроков за год

            // Сбор данных за каждую четверть
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
                        QuarterFinalGrade = (int)Math.Round((double)g.Average(x => x.GradeValue), 0, MidpointRounding.AwayFromZero), // Итог за четверть
                        AllGrades = g.Select(x => (int)Math.Round((double)x.GradeValue, 0, MidpointRounding.AwayFromZero)).ToList() // Все оценки
                    })
                    .ToList();

                // Сохранение данных по предметам
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

                // Сохранение данных о пропусках
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

                // Сохранение данных об уроках
                foreach (var item in lessonsInQuarter)
                {
                    if (!yearTotalLessons.ContainsKey(item.SubjectName))
                    {
                        yearTotalLessons.Add(item.SubjectName, 0);
                    }
                    yearTotalLessons[item.SubjectName] += item.TotalLessons;
                }
            }

            // 3. Расчет годовой оценки (среднее арифметическое четвертных оценок)
            var finalYearItems = yearFinalGrades.Select(kvp => {
                var subjectName = kvp.Key;
                var quarterFinalGrades = kvp.Value;
                var avgOfFinals = quarterFinalGrades.Average(); // Среднее за год

                var absences = yearAbsences.ContainsKey(subjectName) ? yearAbsences[subjectName] :
                    new Dictionary<string, int> { { "Н", 0 }, { "У", 0 }, { "Б", 0 } };

                var allGrades = yearAllGrades.ContainsKey(subjectName) ? yearAllGrades[subjectName] : new List<int>();

                return new StudentSubjectItem
                {
                    SubjectName = subjectName,
                    AverageGrade = avgOfFinals,
                    QuarterFinalGrade = (int)Math.Round((double)avgOfFinals, 0, MidpointRounding.AwayFromZero), // Итоговая годовая оценка
                    TotalAbsences = absences.Sum(a => a.Value), // Всего пропусков за год
                    AbsentTypeH = absences.ContainsKey("Н") ? absences["Н"] : 0,
                    AbsentTypeU = absences.ContainsKey("У") ? absences["У"] : 0,
                    AbsentTypeB = absences.ContainsKey("Б") ? absences["Б"] : 0,
                    TotalLessonsInPeriod = yearTotalLessons.ContainsKey(subjectName) ? yearTotalLessons[subjectName] : 0, // Всего уроков за год
                    AllGrades = allGrades // Все оценки за год
                };
            }).ToList();

            // Добавляем предметы, по которым были только пропуски (без оценок)
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

            // Получение класса ученика
            var studentClass = await _context.StudentClasses
                .Include(sc => sc.Class)
                .FirstOrDefaultAsync(sc => sc.StudentId == studentId);

            // Формирование модели представления для итоговых оценок
            var viewModel = new StudentGradesViewModel
            {
                StudentFullName = studentFullName,
                ClassName = studentClass?.Class.ClassName,
                Subjects = finalYearItems.OrderBy(s => s.SubjectName).ToList(), // Сортировка по алфавиту
                SelectedQuarter = quarterName,
                AvailableQuarters = new List<string> { "Итоговые оценки", "I", "II", "III", "IV" },
                IsParentView = isParentView
            };

            return View("StudentView", viewModel);
        }

        // --- ВСПОМОГАТЕЛЬНЫЕ МЕТОДЫ ---

        // Получение доступных классов для текущего пользователя (учитель/директор)
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
                // Учитель видит классы, в которых преподает или является классным руководителем
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

                var classIds = teachingClassIds.Union(curatingClassIds).ToList(); // Объединение списков

                query = query.Where(sc => classIds.Contains(sc.ClassId)); // Фильтрация классов
            }
            else
            {
                return new Dictionary<int, string>(); // Для других ролей - пустой список
            }

            // Возврат словаря: ID класса -> Название класса
            return await query
                .OrderBy(sc => sc.ClassNumber) // Сортировка по номеру
                .ThenBy(sc => sc.ClassLetter) // Затем по букве
                .ToDictionaryAsync(sc => sc.ClassId, sc => sc.ClassName);
        }

        // Получение доступных предметов для текущего пользователя (учитель/директор)
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
                // Учитель видит предметы, которые преподает в доступных классах
                var subjectIds = await _context.ClassSubjectTeachers
                    .Where(cst => cst.TeacherId == userId && accessibleClassIds.Contains(cst.ClassId))
                    .Select(cst => cst.SubjectId)
                    .Distinct()
                    .ToListAsync();

                query = query.Where(s => subjectIds.Contains(s.SubjectId)); // Фильтрация предметов
            }

            // Возврат словаря: ID предмета -> Название предмета
            return await query
                .OrderBy(s => s.SubjectName) // Сортировка по названию
                .ToDictionaryAsync(s => s.SubjectId, s => s.SubjectName);
        }

        // Определение текущей четверти на основе текущей даты
        private string GetCurrentQuarter()
        {
            var today = DateTime.Today;
            var currentYear = today.Month >= 9 ? today.Year : today.Year - 1; // Учебный год начинается в сентябре

            // Логика определения четверти
            if (today >= new DateTime(currentYear, 9, 1) && today <= new DateTime(currentYear, 10, 31)) return "I";
            if (today >= new DateTime(currentYear, 11, 1) && today <= new DateTime(currentYear, 12, 31)) return "II";
            if (today >= new DateTime(currentYear + 1, 1, 15) && today <= new DateTime(currentYear + 1, 3, 31)) return "III";
            if (today >= new DateTime(currentYear + 1, 4, 1) && today <= new DateTime(currentYear + 1, 5, 25)) return "IV";

            return "Итоговые оценки"; // Если дата вне четвертей
        }

        // Получение дат начала и конца выбранной четверти
        private (DateTime startDate, DateTime endDate) GetQuarterDates(string quarter)
        {
            int currentYear = DateTime.Today.Year;
            if (DateTime.Today.Month >= 9)
            {
                // Если месяц сентябрь или позже, учебный год текущий
                currentYear = DateTime.Today.Year;
            }
            else
            {
                // Если месяц раньше сентября, учебный год предыдущий
                currentYear = DateTime.Today.Year - 1;
            }

            DateTime startSchoolYear = new DateTime(currentYear, 9, 1); // Начало учебного года
            DateTime effectiveEndDate;

            // Определение дат в зависимости от четверти
            switch (quarter)
            {
                case "I":
                    effectiveEndDate = new DateTime(currentYear, 10, 31); // Конец I четверти
                    break;
                case "II":
                    effectiveEndDate = new DateTime(currentYear, 12, 31); // Конец II четверти
                    return (new DateTime(currentYear, 11, 1), effectiveEndDate); // Начало II четверти
                case "III":
                    effectiveEndDate = new DateTime(currentYear + 1, 3, 31); // Конец III четверти
                    return (new DateTime(currentYear + 1, 1, 15), effectiveEndDate); // Начало III четверти
                case "IV":
                    effectiveEndDate = new DateTime(currentYear + 1, 5, 25); // Конец IV четверти
                    return (new DateTime(currentYear + 1, 4, 1), effectiveEndDate); // Начало IV четверти
                case "Итоговые оценки":
                default:
                    effectiveEndDate = new DateTime(currentYear + 1, 6, 30); // Конец учебного года
                    return (startSchoolYear, effectiveEndDate); // Весь учебный год
            }

            return (startSchoolYear, effectiveEndDate); // Для I четверти
        }
    }
}