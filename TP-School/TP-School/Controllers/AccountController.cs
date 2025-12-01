using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using System.Threading.Tasks;
using TP_School.Data;
using TP_School.Models;
using TP_School.ViewModels;

// Контроллер, отвечающий за вход и выход из системы.
public class AccountController : Controller
{
    private readonly ApplicationDbContext _context;

    // Внедрение ApplicationDbContext через конструктор.
    public AccountController(ApplicationDbContext context)
    {
        _context = context;
    }

    // [GET] /Account/Login
    // Отображает страницу входа.
    [HttpGet]
    public IActionResult Login()
    {
        // Проверяем, если пользователь уже аутентифицирован,
        // то перенаправляем его на главную страницу.
        if (User.Identity?.IsAuthenticated == true)
        {
            return RedirectToAction("Index", "Home");
        }
        return View();
    }

    // [POST] /Account/Login
    // Обрабатывает отправку формы входа.
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(LoginViewModel model)
    {
        if (ModelState.IsValid)
        {
            // 1. Поиск пользователя в базе данных по логину и паролю.
            // Внимание: В реальном приложении пароль должен быть захэширован (например, с помощью BCrypt).
            // Здесь мы используем прямое сравнение для демонстрации.
            var user = await _context.Users
                .Include(u => u.Role) // Важно загрузить роль для Claims
                .FirstOrDefaultAsync(u => u.Login == model.Login && u.Password == model.Password);

            if (user != null)
            {
                // 2. Создание identity (набора утверждений)
                var claims = new List<Claim>
                {
                    // Основной идентификатор пользователя
                    new Claim(ClaimTypes.NameIdentifier, user.UserId.ToString()), 
                    // Отображаемое имя (Full Name)
                    new Claim(ClaimTypes.Name, user.FullName), 
                    // Роль пользователя
                    new Claim(ClaimTypes.Role, user.Role.RoleName)
                };

                var claimsIdentity = new ClaimsIdentity(
                    claims,
                    CookieAuthenticationDefaults.AuthenticationScheme
                );

                // 3. Установка аутентификационных куки
                await HttpContext.SignInAsync(
                    CookieAuthenticationDefaults.AuthenticationScheme,
                    new ClaimsPrincipal(claimsIdentity)
                );

                // Перенаправление на главную страницу после успешного входа
                return RedirectToAction("Index", "Home");
            }

            // Если пользователь не найден
            ViewData["ErrorMessage"] = "Неверный логин или пароль.";
        }

        // Возвращаемся на страницу входа с сообщением об ошибке
        return View(model);
    }

    // [POST] /Account/Logout
    // Обрабатывает выход из системы.
    [HttpPost]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return RedirectToAction("Login", "Account");
    }
}