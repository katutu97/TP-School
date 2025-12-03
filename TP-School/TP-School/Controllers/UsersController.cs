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
    // Методы Create, ManageClasses/Subjects доступны только директору
    // Но сам контроллер доступен всем, кто может видеть Index
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
            // 0. Валидация модели
            // 🔥 ХАК для очистки ошибок SelectList, если они возникают (необязательно, если ViewModel исправлена)
            ModelState.Remove(nameof(model.SchoolClasses));
            ModelState.Remove(nameof(model.AllStudents));

            if (!ModelState.IsValid)
            {
                await LoadSelectLists(model);
                return View(model);
            }

            // Получаем имя роли
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
                // 1. Создание базового пользователя
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
                    // 🔥 Предполагается, что вы используете метод для хеширования, например:
                    // PasswordHash = _passwordHasher.HashPassword(generatedPassword),
                    Password = generatedPassword, // Если вы храните нехешированный пароль (не рекомендуется)
                    Info = model.Info,
                    Role = role,
                    // 🔥 ВАЖНО: Инициализируем коллекции для связи
                    StudentClasses = new List<StudentClass>(),
                    StudentParentsAsParent = new List<StudentParents>()
                };

                _context.Users.Add(newUser); // Добавляем пользователя.

                // 2. Логика, зависящая от роли (работаем через НАВИГАЦИОННЫЕ СВОЙСТВА)

                if (role.RoleName == "Ученик" && model.ClassId.HasValue)
                {
                    // 🔥 ИСПРАВЛЕНО: Добавляем связанную сущность в коллекцию самого пользователя
                    newUser.StudentClasses.Add(new StudentClass
                    {
                        // StudentId теперь устанавливается автоматически EF Core
                        ClassId = model.ClassId.Value
                    });
                    // НЕ НУЖНО _context.StudentClasses.Add(...)
                }
                else if (role.RoleName == "Родитель" && model.StudentIdForParent.HasValue)
                {
                    // 🔥 ИСПРАВЛЕНО: Добавляем связанную сущность в коллекцию самого пользователя
                    newUser.StudentParentsAsParent.Add(new StudentParents
                    {
                        // ParentId (пользователь) устанавливается автоматически EF Core
                        StudentId = model.StudentIdForParent.Value
                    });
                    // НЕ НУЖНО _context.StudentParentses.Add(...)
                }

                // 3. Сохраняем все изменения ОДНИМ вызовом!
                await _context.SaveChangesAsync();

                // 4. Успех
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

                // Повторная загрузка данных SelectList
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

        // ------------------------------------------------------------------
        // GET: Users/EditPartial/5 (Загрузка формы редактирования)
        // ------------------------------------------------------------------
        [HttpGet]
        [Authorize(Roles = "Директор")]
        public async Task<IActionResult> EditPartial(int id)
        {
            var user = await _context.Users
                .Include(u => u.Role)
                .Include(u => u.StudentClasses) // Для Ученика: получить связь с классом
                    .ThenInclude(sc => sc.Class)
                .Include(u => u.StudentParentsAsParent) // Для Родителя: получить связи с детьми
                    .ThenInclude(sp => sp.Student)
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

                // Заполнение ClassId для Ученика
                ClassId = user.Role.RoleName == "Ученик"
                    ? user.StudentClasses.FirstOrDefault()?.ClassId
                    : null,

                // Заполнение StudentIdsForParent для Родителя
                StudentIdsForParent = user.Role.RoleName == "Родитель"
                    ? user.StudentParentsAsParent.Select(sp => sp.StudentId).ToList()
                    : new List<int>()
            };

            await LoadSelectLists(model);
            return PartialView("_EditUserPartial", model);
        }

        // ------------------------------------------------------------------
        // POST: Users/Edit (Сохранение изменений)
        // ------------------------------------------------------------------
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Директор")]
        public async Task<IActionResult> Edit(UserCreateViewModel model)
        {
            // 0. Валидация модели
            if (!ModelState.IsValid)
            {
                await LoadSelectLists(model);
                return PartialView("_EditUserPartial", model);
            }

            try
            {
                // Загружаем пользователя со всеми необходимыми связями
                var userToUpdate = await _context.Users
                    .Include(u => u.Role)
                    .Include(u => u.StudentClasses)
                    .Include(u => u.StudentParentsAsParent)
                    .FirstOrDefaultAsync(u => u.UserId == model.UserId);

                if (userToUpdate == null)
                {
                    return NotFound();
                }

                // Сохраняем старую роль до обновления RoleId
                int oldRoleId = userToUpdate.RoleId;
                string oldRoleName = userToUpdate.Role.RoleName;

                // 1. Обновление основных полей
                userToUpdate.RoleId = model.RoleId;
                userToUpdate.LastName = model.LastName ?? "";
                userToUpdate.FirstName = model.FirstName ?? "";
                userToUpdate.MiddleName = model.MiddleName ?? "";
                userToUpdate.BirthDate = model.BirthDate;
                userToUpdate.Phone = model.Phone ?? "";
                userToUpdate.Email = model.Email ?? "";
                userToUpdate.Info = model.Info ?? "";

                // 2. Логика смены роли и обновления/очистки связанных сущностей

                // 🔥 Сначала находим новую роль, чтобы знать, что делать дальше
                var newRole = await _context.Roles.FindAsync(model.RoleId);
                if (newRole == null)
                {
                    ModelState.AddModelError(nameof(model.RoleId), "Выбранная роль не существует.");
                    await LoadSelectLists(model);
                    return PartialView("_EditUserPartial", model);
                }
                string newRoleName = newRole.RoleName;
                userToUpdate.Role = newRole; // Обновляем объект роли для дальнейших проверок

                // --- A. Очистка старых связей, если роль поменялась или класс/дети удалены ---

                // Если старая роль была "Ученик", а новая не "Ученик" (или ClassId стал null)
                if (oldRoleName == "Ученик" && (newRoleName != "Ученик" || !model.ClassId.HasValue))
                {
                    if (userToUpdate.StudentClasses.Any())
                    {
                        _context.StudentClasses.RemoveRange(userToUpdate.StudentClasses);
                    }
                }

                // Если старая роль была "Родитель", а новая не "Родитель" (или список детей пуст)
                if (oldRoleName == "Родитель" && newRoleName != "Родитель")
                {
                    if (userToUpdate.StudentParentsAsParent.Any())
                    {
                        _context.StudentParentses.RemoveRange(userToUpdate.StudentParentsAsParent);
                    }
                }

                // --- B. Создание/Обновление новых связей ---

                if (newRoleName == "Ученик")
                {
                    // Обновляем/создаем связь ученика с классом (UpdateStudentClass умеет это)
                    await UpdateStudentClass(userToUpdate, model.ClassId);
                }
                // else if (newRoleName == "Родитель") 
                // {
                //     // Логика UpdateParentStudents(userToUpdate, model.StudentIdsForParent)
                //     // для работы с несколькими детьми
                // }


                // 3. Сохранить все изменения
                await _context.SaveChangesAsync();

                TempData["SuccessMessage"] = $"Данные пользователя {userToUpdate.FullName} успешно обновлены. Новая роль: **{newRoleName}**.";
                return Json(new { success = true, message = TempData["SuccessMessage"] });
            }
            catch (Exception ex)
            {
                ModelState.AddModelError("", "Ошибка сохранения: " + (ex.InnerException?.Message ?? ex.Message));
                await LoadSelectLists(model);
                return PartialView("_EditUserPartial", model);
            }
        }

        // --- ВСПОМОГАТЕЛЬНЫЕ МЕТОДЫ EF CORE ДЛЯ СВЯЗЕЙ ---

        /// <summary>
        /// Обновляет связь ученика с классом (StudentClass).
        /// </summary>
        private async Task UpdateStudentClass(User student, int? newClassId)
        {
            // 1. Текущая связь (может быть null)
            var existingLink = student.StudentClasses.FirstOrDefault();

            // 2. ID текущего класса (если есть)
            int? currentClassId = existingLink?.ClassId;

            // Ничего не изменилось: текущий и новый ID совпадают (оба null или оба равны)
            if (currentClassId == newClassId)
            {
                return;
            }

            // Если старая связь существует, удаляем её
            if (existingLink != null)
            {
                _context.StudentClasses.Remove(existingLink);
            }

            // Если есть новый ClassId, создаем новую связь
            if (newClassId.HasValue)
            {
                // Мы должны убедиться, что класс существует, но для простоты опустим проверку.
                _context.StudentClasses.Add(new StudentClass { StudentId = student.UserId, ClassId = newClassId.Value });
            }

            // Внимание: SaveChangesAsync будет вызван позже в методе Edit
        }


        // -------------------------------------------------------------
        // ВСПОМОГАТЕЛЬНЫЙ МЕТОД: Загрузка списков для ViewModel (Исправлены типы)
        // -------------------------------------------------------------
        private async Task LoadSelectLists(UserCreateViewModel model)
        {
            // Роли (List<SelectListItem>)
            model.AvailableRoles = await _context.Roles
                 .Select(r => new SelectListItem { Value = r.RoleId.ToString(), Text = r.RoleName })
                 .OrderBy(item => item.Text)
                 .ToListAsync();

            // Классы (SelectList)
            var schoolClassesList = await _context.SchoolClasses
                   .OrderBy(c => c.ClassNumber)
                   .ThenBy(c => c.ClassLetter)
                   .ToListAsync();

            model.SchoolClasses = new SelectList(
                schoolClassesList,
                "ClassId",
                "ClassName",
                model.ClassId // Используем предвыбранный ID
            );

            object? selectedValue = null;
            if (model.StudentIdsForParent != null && model.StudentIdsForParent.Any())
            {
                selectedValue = model.StudentIdsForParent;
            }

            // Ученики (SelectList)
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
                null // Предвыбранные ID для родителя обрабатываются отдельно
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
                    Text = $"{u.LastName} {u.FirstName} {u.MiddleName}".Trim() // Формируем FullName вручную
                })
                .ToListAsync();

            // Создаем ViewModel или используем ViewBag
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

            // Проверка на наличие связанных учеников
            bool hasStudents = await _context.StudentClasses.AnyAsync(sc => sc.ClassId == classId);
            if (hasStudents)
            {
                // 🔥 ВАЖНО: Мы не можем удалить класс, пока в нем есть ученики.
                // Нужно либо сначала удалить учеников, либо перевести их в другой класс.
                return BadRequest($"Невозможно удалить класс {schoolClass.ClassName}. В нем числятся ученики. Сначала переведите их.");
            }

            // Проверка на наличие связанных предметов
            // Если у вас есть таблица ClassSubjectTeacher, нужно проверить и ее.
            // bool hasSubjects = await _context.ClassSubjectTeachers.AnyAsync(cst => cst.ClassId == classId);
            // if (hasSubjects) { ... }

            try
            {
                _context.SchoolClasses.Remove(schoolClass);
                await _context.SaveChangesAsync();
                return Ok(new { success = true, message = $"Класс {schoolClass.ClassName} успешно удален." });
            }
            catch (DbUpdateException ex)
            {
                // Обработка возможной ошибки внешнего ключа (если есть другие неожиданные связи)
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
        public async Task<IActionResult> AddSubject(string subjectName) // 🔥 УДАЛЕН teacherId
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

            return Ok(new { success = true, subjectName = newSubject.SubjectName, subjectId = newSubject.SubjectId }); // Возвращаем SubjectId для потенциального удаления на клиенте
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

            // Проверка на связанные данные (если предмет где-то используется, возможно, потребуется CASCADE DELETE или ручное удаление/обнуление связей)
            // Здесь предполагается, что CASCADE DELETE настроен или предмет нигде не используется.

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

            // Удаляем связи ученика с классом
            if (user.StudentClasses.Any())
                _context.StudentClasses.RemoveRange(user.StudentClasses);

            // Если он родитель – удаляем связи «родитель-ученик»
            if (user.StudentParentsAsParent.Any())
                _context.StudentParentses.RemoveRange(user.StudentParentsAsParent);

            // Если он ребёнок – удаляем связи «ученик-родитель»
            if (user.StudentParentsAsStudent.Any())
                _context.StudentParentses.RemoveRange(user.StudentParentsAsStudent);

            // Удаляем самого пользователя
            _context.Users.Remove(user);
            await _context.SaveChangesAsync();

            return Json(new { success = true });
        }

    }
}