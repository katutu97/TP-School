using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using TP_School.Data;
using System.Security.Claims;
using System;
using System.Globalization;
using Microsoft.AspNetCore.Localization;
using System.Collections.Generic;

var builder = WebApplication.CreateBuilder(args);


builder.Services.Configure<Microsoft.AspNetCore.Server.Kestrel.Core.KestrelServerOptions>(options =>
{
    options.Limits.MaxRequestBodySize = 52428800;
});

builder.Services.AddControllersWithViews(options =>
{
    // Устанавливает лимит для всех контроллеров, если не указано иное
    options.Filters.Add(new Microsoft.AspNetCore.Mvc.RequestSizeLimitAttribute(52428800));
});

// 1. НАСТРОЙКА АУТЕНТИФИКАЦИИ И АВТОРИЗАЦИИ
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/Account/Login";
        options.AccessDeniedPath = "/Account/AccessDenied";
        options.ExpireTimeSpan = TimeSpan.FromMinutes(60);
    });

// 2. ДОБАВЛЯЕМ ПОДКЛЮЧЕНИЕ К БАЗЕ ДАННЫХ
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseMySql(
        builder.Configuration.GetConnectionString("DefaultConnection"),
        ServerVersion.AutoDetect(builder.Configuration.GetConnectionString("DefaultConnection"))
    ));

var app = builder.Build();

// 3. КОНФИГУРАЦИЯ ЗАПРОСА ДЛЯ ИСПОЛЬЗОВАНИЯ РУССКОЙ КУЛЬТУРЫ
var defaultCulture = "ru-RU";
var supportedCultures = new List<CultureInfo> { new CultureInfo(defaultCulture) };
app.UseRequestLocalization(new RequestLocalizationOptions
{
    DefaultRequestCulture = new RequestCulture(defaultCulture),
    SupportedCultures = supportedCultures,
    SupportedUICultures = supportedCultures
});

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

// Middleware для аутентификации и авторизации
app.UseAuthentication();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();