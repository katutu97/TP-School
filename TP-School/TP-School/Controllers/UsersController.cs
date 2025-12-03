using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using TP_School.Models;
using TP_School.Data;
using TP_School.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace TP_School.Controllers
{
    [Authorize]
    public class UsersController : Controller
    {
        private readonly ApplicationDbContext _context;

        public UsersController(ApplicationDbContext context)
        {
            _context = context;
        }


        // -------------------------------------------------------------
        // GET: Users/Index (Отображение списка пользователей)
        // -------------------------------------------------------------
        public async Task<IActionResult> Index(string roleFilter, int? classFilter, string searchTerm)
        {
            string currentAuthUserId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? string.Empty;
            ViewBag.CurrentAuthUserId = currentAuthUserId;

            IQueryable<User> query = _context.Users
                .Include(u => u.Role)
                .Include(u => u.StudentClasses)
                .Include(u => u.ClassesAsTeacher);

            // 1. Фильтрация по роли
            if (!string.IsNullOrEmpty(roleFilter) && roleFilter != "Все роли")
            {
                query = query.Where(u => u.Role.RoleName == roleFilter);
            }

            // 2. Фильтрация по классу
            if (classFilter.HasValue)
            {
                query = query.Where(u =>
                    (u.Role.RoleName == "Ученик" && u.StudentClasses.Any(sc => sc.ClassId == classFilter.Value)) ||
                    (u.Role.RoleName == "Учитель" && u.ClassesAsTeacher.Any(c => c.ClassId == classFilter.Value))
                );
            }

            // 3. Поиск по ФИО
            if (!string.IsNullOrEmpty(searchTerm))
            {
                query = query.Where(u =>
                    u.LastName.Contains(searchTerm) ||
                    u.FirstName.Contains(searchTerm) ||
                    u.MiddleName.Contains(searchTerm)
                );
            }

            // Сортировка
            query = query.OrderBy(u => u.LastName).ThenBy(u => u.FirstName);

            var users = await query.ToListAsync();

            // Передача данных для фильтров в ViewBags
            ViewBag.AvailableRoles = await _context.Roles.ToListAsync();
            ViewBag.SchoolClasses = await _context.SchoolClasses.ToListAsync();

            ViewBag.RoleFilter = roleFilter;
            ViewBag.ClassFilter = classFilter;
            ViewBag.SearchTerm = searchTerm;

            return View(users);
        }

        // -------------------------------------------------------------
        // GET: Users/GetUserDetails (AJAX)
        // -------------------------------------------------------------
        public async Task<IActionResult> GetUserDetails(string id)
        {
            if (!int.TryParse(id, out int userIdInt))
            {
                return BadRequest("Некорректный формат ID пользователя.");
            }

            var user = await _context.Users
                .Include(u => u.Role)
                .Include(u => u.StudentClasses).ThenInclude(sc => sc.Class)
                .Include(u => u.StudentParentsAsStudent).ThenInclude(sp => sp.Parent)
                .Include(u => u.StudentParentsAsParent).ThenInclude(sp => sp.Student)
                .Include(u => u.ClassesAsTeacher)
                .FirstOrDefaultAsync(u => u.UserId == userIdInt);

            if (user == null)
            {
                return NotFound();
            }

            return PartialView("_UserDetailsPartial", user);
        }

        // =============================================================
        //               МЕТОДЫ ДЛЯ СОЗДАНИЯ ПОЛЬЗОВАТЕЛЯ (ДИРЕКТОР)
        // =============================================================

        // -------------------------------------------------------------
        // GET: Users/Create (Отображение формы)
        // -------------------------------------------------------------
        [Authorize(Roles = "Директор")]
        public async Task<IActionResult> Create()
        {
            var model = new UserCreateViewModel();
            await LoadSelectLists(model);
            return View(model);
        }

        [HttpPost]
        [Authorize(Roles = "Директор")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(UserCreateViewModel model)
        {
            ModelState.Remove(nameof(model.SchoolClasses));
            ModelState.Remove(nameof(model.AllStudents));

            if (!ModelState.IsValid)
            {
                await LoadSelectLists(model);
                return View(model);
            }

            var role = await _context.Roles.FindAsync(model.RoleId);
            if (role == null)
            {
                ModelState.AddModelError(nameof(model.RoleId), "Выбранная роль не существует.");
                await LoadSelectLists(model);
                return View(model);
            }

            string generatedPassword = GenerateRandomPassword(6);

            try
            {
                var newUser = new User
                {
                    RoleId = model.RoleId,
                    LastName = model.LastName,
                    FirstName = model.FirstName,
                    MiddleName = model.MiddleName,
                    BirthDate = model.BirthDate,
                    Phone = model.Phone,
                    Email = model.Email,
                    Login = model.Login,
                    Password = generatedPassword,
                    Info = model.Info,
                    Role = role,
                    StudentClasses = new List<StudentClass>(),
                    StudentParentsAsParent = new List<StudentParents>()
                };

                _context.Users.Add(newUser);

                if (role.RoleName == "Ученик" && model.ClassId.HasValue)
                {
                    newUser.StudentClasses.Add(new StudentClass
                    {
                        ClassId = model.ClassId.Value
                    });
                }
                else if (role.RoleName == "Родитель" && model.StudentIdForParent.HasValue)
                {
                    newUser.StudentParentsAsParent.Add(new StudentParents
                    {
                        StudentId = model.StudentIdForParent.Value
                    });
                }

                await _context.SaveChangesAsync();

                TempData["SuccessMessage"] = $"Пользователь {newUser.FullName} успешно создан с ролью **{role.RoleName}**. Временный пароль: {generatedPassword}";
                return RedirectToAction(nameof(Index));
            }
            catch (Exception ex)
            {
                string details = ex.InnerException != null ? ex.InnerException.Message : ex.Message;

                if (ex is Microsoft.EntityFrameworkCore.DbUpdateException dbEx && dbEx.InnerException != null)
                {
                    details = $"Ошибка БД: {dbEx.InnerException.Message}";
                }

                ModelState.AddModelError("", "Ошибка сохранения: " + details);
                await LoadSelectLists(model);
                return View(model);
            }
        }

        private string GenerateRandomPassword(int length = 6)
        {
            const string validChars = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ1234567890";
            var random = new Random();
            var password = new char[length];

            for (int i = 0; i < length; i++)
            {
                password[i] = validChars[random.Next(validChars.Length)];
            }

            return new string(password);
        }

        // =============================================================
        //          МЕТОДЫ ДЛЯ РЕДАКТИРОВАНИЯ ПОЛЬЗОВАТЕЛЯ (ДИРЕКТОР)
        // =============================================================

        // ------------------------------------------------------------------
        // GET: Users/EditUserPartial/5 (Загрузка формы редактирования для модального окна)
        // ------------------------------------------------------------------
        [HttpGet]
        [Authorize(Roles = "Директор")]
        public async Task<IActionResult> EditUserPartial(int id)
        {
            try
            {
                var user = await _context.Users
                    .Include(u => u.Role)
                    .Include(u => u.StudentClasses)
                    .Include(u => u.StudentParentsAsParent)
                    .Include(u => u.ClassesAsTeacher)
                    .FirstOrDefaultAsync(u => u.UserId == id);

                if (user == null)
                {
                    return NotFound();
                }

                var model = new UserCreateViewModel
                {
                    UserId = user.UserId,
                    RoleId = user.RoleId,
                    LastName = user.LastName,
                    FirstName = user.FirstName,
                    MiddleName = user.MiddleName,
                    BirthDate = user.BirthDate,
                    Phone = user.Phone,
                    Email = user.Email,
                    Login = user.Login,
                    Info = user.Info,
                    // Класс для ученика
                    ClassId = user.Role.RoleName == "Ученик"
                        ? user.StudentClasses.FirstOrDefault()?.ClassId
                        : null,
                    // Ученик для родителя
                    StudentIdForParent = user.Role.RoleName == "Родитель"
                        ? user.StudentParentsAsParent.FirstOrDefault()?.StudentId
                        : null
                    // Убираем TeacherClassId из модели - будем использовать ViewBag
                };

                await LoadSelectListsForEdit(model);

                // Для учителей: получаем список классов без классных руководителей или где этот учитель уже руководитель
                // Сохраняем выбранный класс в ViewBag
                if (user.Role.RoleName == "Учитель")
                {
                    var teacherClassId = user.ClassesAsTeacher.FirstOrDefault()?.ClassId;
                    ViewBag.TeacherClassId = teacherClassId; // Сохраняем в ViewBag

                    var availableClassesForTeacher = await _context.SchoolClasses
                        .Where(c => c.ClassTeacherId == null || c.ClassTeacherId == user.UserId)
                        .OrderBy(c => c.ClassNumber)
                        .ThenBy(c => c.ClassLetter)
                        .Select(c => new SelectListItem
                        {
                            Value = c.ClassId.ToString(),
                            Text = c.ClassName,
                            Selected = c.ClassId == teacherClassId // Выделяем текущий класс
                        })
                        .ToListAsync();

                    ViewBag.AvailableClassesForTeacher = availableClassesForTeacher;
                }

                return PartialView("_EditUserFormPartial", model);
            }
            catch (Exception ex)
            {
                return Content($"<div class='text-red-500 p-4'>Ошибка загрузки: {ex.Message}</div>");
            }
        }

        // ------------------------------------------------------------------
        // POST: Users/EditUser (Сохранение изменений из модального окна)
        // ------------------------------------------------------------------
        [HttpPost]
        [Authorize(Roles = "Директор")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditUser(UserCreateViewModel model)
        {
            // Получаем TeacherClassId из формы
            var teacherClassIdStr = Request.Form["TeacherClassId"];
            int? teacherClassId = null;
            if (!string.IsNullOrEmpty(teacherClassIdStr) && int.TryParse(teacherClassIdStr, out int tempId))
            {
                teacherClassId = tempId;
            }

            if (!ModelState.IsValid)
            {
                await LoadSelectListsForEdit(model);

                // Загружаем данные учителя для ViewBag
                if (model.RoleId != 0) // Проверяем на 0 вместо HasValue
                {
                    var role = await _context.Roles.FindAsync(model.RoleId);
                    if (role?.RoleName == "Учитель")
                    {
                        ViewBag.TeacherClassId = teacherClassId;

                        var availableClassesForTeacher = await _context.SchoolClasses
                            .Where(c => c.ClassTeacherId == null || c.ClassTeacherId == model.UserId)
                            .OrderBy(c => c.ClassNumber)
                            .ThenBy(c => c.ClassLetter)
                            .Select(c => new SelectListItem
                            {
                                Value = c.ClassId.ToString(),
                                Text = c.ClassName,
                                Selected = c.ClassId == teacherClassId
                            })
                            .ToListAsync();

                        ViewBag.AvailableClassesForTeacher = availableClassesForTeacher;
                    }
                }

                return PartialView("_EditUserFormPartial", model);
            }

            try
            {
                var userToUpdate = await _context.Users
                    .Include(u => u.Role)
                    .Include(u => u.StudentClasses)
                    .Include(u => u.StudentParentsAsParent)
                    .Include(u => u.ClassesAsTeacher)
                    .FirstOrDefaultAsync(u => u.UserId == model.UserId);

                if (userToUpdate == null)
                {
                    return NotFound();
                }

                // Обновление основных полей (роль не меняем!)
                userToUpdate.LastName = model.LastName ?? "";
                userToUpdate.FirstName = model.FirstName ?? "";
                userToUpdate.MiddleName = model.MiddleName ?? "";
                userToUpdate.BirthDate = model.BirthDate;
                userToUpdate.Phone = model.Phone ?? "";
                userToUpdate.Email = model.Email ?? "";
                userToUpdate.Info = model.Info ?? "";

                // Обработка в зависимости от роли
                if (userToUpdate.Role.RoleName == "Ученик")
                {
                    // Обновление класса ученика
                    await UpdateStudentClass(userToUpdate, model.ClassId);
                }
                else if (userToUpdate.Role.RoleName == "Родитель")
                {
                    // Обновление связи родитель-ученик
                    await UpdateParentStudent(userToUpdate, model.StudentIdForParent);
                }
                else if (userToUpdate.Role.RoleName == "Учитель")
                {
                    // Обновление классного руководства
                    await UpdateTeacherClass(userToUpdate, teacherClassId);
                }

                await _context.SaveChangesAsync();

                return Json(new
                {
                    success = true,
                    message = $"Данные пользователя {userToUpdate.FullName} успешно обновлены."
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = $"Ошибка: {ex.Message}" });
            }
        }

        // =============================================================
        //          ВСПОМОГАТЕЛЬНЫЕ МЕТОДЫ
        // =============================================================

        /// <summary>
        /// Обновляет связь ученика с классом (StudentClass).
        /// </summary>
        private async Task UpdateStudentClass(User student, int? newClassId)
        {
            var existingLink = student.StudentClasses.FirstOrDefault();
            int? currentClassId = existingLink?.ClassId;

            if (currentClassId == newClassId)
            {
                return;
            }

            if (existingLink != null)
            {
                _context.StudentClasses.Remove(existingLink);
            }

            if (newClassId.HasValue)
            {
                _context.StudentClasses.Add(new StudentClass { StudentId = student.UserId, ClassId = newClassId.Value });
            }
        }

        // Вспомогательный метод для обновления классного руководства учителя
        private async Task UpdateTeacherClass(User teacher, int? newClassId)
        {
            var currentClass = teacher.ClassesAsTeacher.FirstOrDefault();
            var currentClassId = currentClass?.ClassId;

            if (currentClassId == newClassId)
            {
                return;
            }

            if (currentClass != null)
            {
                currentClass.ClassTeacherId = 0;
                teacher.ClassesAsTeacher.Remove(currentClass);
            }

            if (newClassId != 0)
            {
                var newClass = await _context.SchoolClasses
                    .FirstOrDefaultAsync(c => c.ClassId == newClassId.Value);

                if (newClass != null)
                {
                    if (newClass.ClassTeacherId != 0)
                    {
                        var previousTeacher = await _context.Users
                            .Include(u => u.ClassesAsTeacher)
                            .FirstOrDefaultAsync(u => u.UserId == newClass.ClassTeacherId);

                        if (previousTeacher != null)
                        {
                            var previousTeacherClass = previousTeacher.ClassesAsTeacher
                                .FirstOrDefault(c => c.ClassId == newClassId.Value);

                            if (previousTeacherClass != null)
                            {
                                previousTeacher.ClassesAsTeacher.Remove(previousTeacherClass);
                            }
                        }
                    }

                    newClass.ClassTeacherId = teacher.UserId;
                    teacher.ClassesAsTeacher.Add(newClass);
                }
            }
        }

        // Вспомогательный метод для обновления связи родитель-ученик
        private async Task UpdateParentStudent(User parent, int? newStudentId)
        {
            var currentLink = parent.StudentParentsAsParent.FirstOrDefault();
            var currentStudentId = currentLink?.StudentId;

            if (currentStudentId == newStudentId)
            {
                return;
            }

            if (currentLink != null)
            {
                _context.StudentParentses.Remove(currentLink);
            }

            if (newStudentId.HasValue)
            {
                _context.StudentParentses.Add(new StudentParents
                {
                    ParentId = parent.UserId,
                    StudentId = newStudentId.Value
                });
            }
        }

        // -------------------------------------------------------------
        // ВСПОМОГАТЕЛЬНЫЙ МЕТОД: Загрузка списков для ViewModel
        // -------------------------------------------------------------
        private async Task LoadSelectLists(UserCreateViewModel model)
        {
            // Роли
            model.AvailableRoles = await _context.Roles
                 .Select(r => new SelectListItem { Value = r.RoleId.ToString(), Text = r.RoleName })
                 .OrderBy(item => item.Text)
                 .ToListAsync();

            // Классы
            var schoolClassesList = await _context.SchoolClasses
                   .OrderBy(c => c.ClassNumber)
                   .ThenBy(c => c.ClassLetter)
                   .ToListAsync();

            model.SchoolClasses = new SelectList(
                schoolClassesList,
                "ClassId",
                "ClassName",
                model.ClassId
            );

            // Ученики
            var allStudentsList = await _context.Users
                   .Include(u => u.Role)
                   .Where(u => u.Role.RoleName == "Ученик")
                   .OrderBy(u => u.LastName)
                   .ThenBy(u => u.FirstName)
                   .ToListAsync();

            model.AllStudents = new SelectList(
                allStudentsList,
                "UserId",
                "FullName",
                null
            );
        }

        // Вспомогательный метод для загрузки списков для редактирования
        private async Task LoadSelectListsForEdit(UserCreateViewModel model)
        {
            // Роли
            model.AvailableRoles = await _context.Roles
                .Select(r => new SelectListItem
                {
                    Value = r.RoleId.ToString(),
                    Text = r.RoleName
                })
                .OrderBy(item => item.Text)
                .ToListAsync();

            // Классы
            var schoolClassesList = await _context.SchoolClasses
                .OrderBy(c => c.ClassNumber)
                .ThenBy(c => c.ClassLetter)
                .ToListAsync();

            model.SchoolClasses = new SelectList(
                schoolClassesList,
                "ClassId",
                "ClassName",
                model.ClassId
            );

            // Ученики
            var allStudentsList = await _context.Users
                .Include(u => u.Role)
                .Where(u => u.Role.RoleName == "Ученик")
                .OrderBy(u => u.LastName)
                .ThenBy(u => u.FirstName)
                .ThenBy(u => u.MiddleName)
                .Select(u => new SelectListItem
                {
                    Value = u.UserId.ToString(),
                    Text = $"{u.LastName} {u.FirstName} {u.MiddleName}".Trim()
                })
                .ToListAsync();

            model.AllStudents = new SelectList(
                allStudentsList,
                "Value",
                "Text",
                model.StudentIdForParent?.ToString()
            );
        }

        // =============================================================
        //          МЕТОДЫ ДЛЯ УПРАВЛЕНИЯ КЛАССАМИ И ПРЕДМЕТАМИ
        // =============================================================

        // -------------------------------------------------------------
        // ЧАСТИЧНОЕ ПРЕДСТАВЛЕНИЕ: Управление классами (для модального окна)
        // -------------------------------------------------------------
        [HttpGet]
        [Authorize(Roles = "Директор")]
        public async Task<IActionResult> ManageClassesPartial()
        {
            var classes = await _context.SchoolClasses
                .Include(c => c.ClassTeacher)
                .OrderBy(c => c.ClassNumber)
                .ThenBy(c => c.ClassLetter)
                .ToListAsync();

            // Загрузка списка учителей для выбора классного руководителя
            var availableTeachers = await _context.Users
                .Include(u => u.Role)
                .Where(u => u.Role.RoleName == "Учитель")
                .OrderBy(u => u.LastName)
                .ThenBy(u => u.FirstName)
                .ThenBy(u => u.MiddleName)
                .Select(u => new SelectListItem
                {
                    Value = u.UserId.ToString(),
                    Text = $"{u.LastName} {u.FirstName} {u.MiddleName}".Trim()
                })
                .ToListAsync();

            ViewBag.AvailableTeachers = availableTeachers;

            return PartialView("_ManageClassesPartial", classes);
        }

        // -------------------------------------------------------------
        // POST: Добавление нового класса
        // -------------------------------------------------------------
        [HttpPost]
        [Authorize(Roles = "Директор")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AddClass(int classNumber, string classLetter, int classTeacherId)
        {
            if (classNumber <= 0 || string.IsNullOrWhiteSpace(classLetter) || classTeacherId <= 0)
            {
                return BadRequest("Некорректные данные класса или не выбран классный руководитель.");
            }

            bool exists = await _context.SchoolClasses.AnyAsync(c => c.ClassNumber == classNumber && c.ClassLetter.ToUpper() == classLetter.ToUpper());
            if (exists)
            {
                return BadRequest($"Класс {classNumber}{classLetter.ToUpper()} уже существует.");
            }

            bool isAlreadyTeacher = await _context.SchoolClasses.AnyAsync(c => c.ClassTeacherId == classTeacherId);
            if (isAlreadyTeacher)
            {
                return BadRequest("Этот учитель уже является классным руководителем другого класса.");
            }

            var newClass = new SchoolClass
            {
                ClassNumber = classNumber,
                ClassLetter = classLetter.ToUpper(),
                ClassTeacherId = classTeacherId
            };

            _context.SchoolClasses.Add(newClass);
            await _context.SaveChangesAsync();

            var teacherName = await _context.Users.Where(u => u.UserId == classTeacherId).Select(u => u.FullName).FirstOrDefaultAsync();

            return Ok(new { success = true, className = newClass.ClassName, classTeacher = teacherName });
        }

        // -------------------------------------------------------------
        // POST: Удаление класса
        // -------------------------------------------------------------
        [HttpPost]
        [Authorize(Roles = "Директор")]
        public async Task<IActionResult> DeleteClass(int classId)
        {
            var schoolClass = await _context.SchoolClasses.FindAsync(classId);

            if (schoolClass == null)
            {
                return NotFound("Класс не найден.");
            }

            bool hasStudents = await _context.StudentClasses.AnyAsync(sc => sc.ClassId == classId);
            if (hasStudents)
            {
                return BadRequest($"Невозможно удалить класс {schoolClass.ClassName}. В нем числятся ученики. Сначала переведите их.");
            }

            try
            {
                _context.SchoolClasses.Remove(schoolClass);
                await _context.SaveChangesAsync();
                return Ok(new { success = true, message = $"Класс {schoolClass.ClassName} успешно удален." });
            }
            catch (DbUpdateException ex)
            {
                return StatusCode(500, new { success = false, message = "Ошибка базы данных при удалении класса. Убедитесь, что нет других связанных записей (например, расписания)." });
            }
        }

        // -------------------------------------------------------------
        // ЧАСТИЧНОЕ ПРЕДСТАВЛЕНИЕ: Управление предметами
        // -------------------------------------------------------------
        [HttpGet]
        [Authorize(Roles = "Директор")]
        public async Task<IActionResult> ManageSubjectsPartial()
        {
            var subjects = await _context.Subjects
                .OrderBy(s => s.SubjectName)
                .ToListAsync();

            return PartialView("_ManageSubjectsPartial", subjects);
        }

        // -------------------------------------------------------------
        // POST: Добавление нового предмета
        // -------------------------------------------------------------
        [HttpPost]
        [Authorize(Roles = "Директор")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AddSubject(string subjectName)
        {
            if (string.IsNullOrWhiteSpace(subjectName))
            {
                return BadRequest("Имя предмета не может быть пустым.");
            }

            bool exists = await _context.Subjects.AnyAsync(s => s.SubjectName.ToLower() == subjectName.ToLower());
            if (exists)
            {
                return BadRequest($"Предмет '{subjectName}' уже существует.");
            }

            var newSubject = new Subject
            {
                SubjectName = subjectName
            };

            _context.Subjects.Add(newSubject);
            await _context.SaveChangesAsync();

            return Ok(new { success = true, subjectName = newSubject.SubjectName, subjectId = newSubject.SubjectId });
        }

        // -------------------------------------------------------------
        // POST: Удаление предмета
        // -------------------------------------------------------------
        [HttpPost]
        [Authorize(Roles = "Директор")]
        public async Task<IActionResult> DeleteSubject(int subjectId)
        {
            var subject = await _context.Subjects.FindAsync(subjectId);

            if (subject == null)
            {
                return NotFound("Предмет не найден.");
            }

            _context.Subjects.Remove(subject);
            await _context.SaveChangesAsync();

            return Ok(new { success = true, message = $"Предмет '{subject.SubjectName}' успешно удален." });
        }

        [HttpPost]
        [Authorize(Roles = "Директор")]
        public async Task<IActionResult> Delete(int id)
        {
            var user = await _context.Users
                .Include(u => u.StudentClasses)
                .Include(u => u.StudentParentsAsParent)
                .Include(u => u.StudentParentsAsStudent)
                .FirstOrDefaultAsync(u => u.UserId == id);

            if (user == null)
                return NotFound();

            if (user.StudentClasses.Any())
                _context.StudentClasses.RemoveRange(user.StudentClasses);

            if (user.StudentParentsAsParent.Any())
                _context.StudentParentses.RemoveRange(user.StudentParentsAsParent);

            if (user.StudentParentsAsStudent.Any())
                _context.StudentParentses.RemoveRange(user.StudentParentsAsStudent);

            _context.Users.Remove(user);
            await _context.SaveChangesAsync();

            return Json(new { success = true });
        }
    }
}