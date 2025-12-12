using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using System.Threading.Tasks;
using TP_School.Data;
using TP_School.Models;
using TP_School.ViewModels;

// Контроллер, отвечающий за аутентификацию (вход/выход) пользователей в системе.
// Использует cookie-based аутентификацию для сохранения сессии пользователя.
public class AccountController : Controller
{
    private readonly ApplicationDbContext _context;

    // Внедрение зависимости ApplicationDbContext через конструктор
    public AccountController(ApplicationDbContext context)
    {
        _context = context;
    }


    // [GET] /Account/Login
    // Отображает страницу входа (форму авторизации)
    [HttpGet]
    public IActionResult Login()
    {
        // Проверяем, если пользователь уже аутентифицирован (вошел в систему),
        // то перенаправляем его на главную страницу, чтобы избежать повторного входа.
        if (User.Identity?.IsAuthenticated == true)
        {
            return RedirectToAction("Index", "Home");
        }
        return View(); // Возвращаем представление с формой входа
    }


    // [POST] /Account/Login
    // Обрабатывает отправку формы входа (POST-запрос с данными формы)
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(LoginViewModel model)
    {
        // Проверка валидности модели (валидация на стороне сервера)
        if (ModelState.IsValid)
        {
            // 1. Поиск пользователя в базе данных по логину и паролю.
            var user = await _context.Users
                .Include(u => u.Role) // Важно: загружаем связанную роль для создания Claims
                .FirstOrDefaultAsync(u => u.Login == model.Login && u.Password == model.Password);

            if (user != null)
            {
                // 2. Создание "утверждений" (Claims) для пользователя.
                // Claims - это фрагменты информации о пользователе, которые хранятся в аутентификационном билете.
                var claims = new List<Claim>
                {
                    // ClaimTypes.NameIdentifier - уникальный идентификатор пользователя
                    new Claim(ClaimTypes.NameIdentifier, user.UserId.ToString()), 
                    
                    // ClaimTypes.Name - отображаемое имя пользователя (используется для приветствий)
                    new Claim(ClaimTypes.Name, user.FullName), 
                    
                    // ClaimTypes.Role - роль пользователя (определяет права доступа)
                    new Claim(ClaimTypes.Role, user.Role.RoleName)
                };

                // 3. Создание объекта ClaimsIdentity, который представляет удостоверение пользователя
                var claimsIdentity = new ClaimsIdentity(
                    claims,
                    CookieAuthenticationDefaults.AuthenticationScheme // Используем cookie-аутентификацию
                );

                // 4. Создание аутентификационного куки и "вход" пользователя в систему
                await HttpContext.SignInAsync(
                    CookieAuthenticationDefaults.AuthenticationScheme, // Схема аутентификации
                    new ClaimsPrincipal(claimsIdentity),               // Принципал (объект пользователя)
                    new AuthenticationProperties
                    {
                        // Дополнительные настройки аутентификации:
                        IsPersistent = false, // true - "запомнить меня" (постоянные куки)
                        ExpiresUtc = DateTimeOffset.UtcNow.AddHours(2) // Время жизни сессии
                    }
                );

                // 5. Перенаправление на главную страницу после успешного входа
                return RedirectToAction("Index", "Home");
            }

            // Если пользователь не найден (неверный логин или пароль)
            // Не говорим точно, что именно неверно, чтобы не помогать злоумышленникам
            ViewData["ErrorMessage"] = "Неверный логин или пароль.";
        }

        // Если модель невалидна или аутентификация не удалась,
        // возвращаемся на страницу входа с сообщением об ошибке
        return View(model);
    }

    // [POST] /Account/Logout
    // Обрабатывает выход пользователя из системы
    [HttpPost]
    public async Task<IActionResult> Logout()
    {
        // Удаление аутентификационных куки и завершение сессии
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);

        // Перенаправление на страницу входа после выхода
        return RedirectToAction("Login", "Account");
    }
}