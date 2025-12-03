using System.ComponentModel.DataAnnotations;
using TP_School.Models;
using Microsoft.AspNetCore.Mvc.Rendering;
using System.Collections.Generic; // Убедиться, что подключены

namespace TP_School.ViewModels
{
    public class UserCreateViewModel
    {
        public int UserId { get; set; }
        // --- Основные поля пользователя ---
        [Required(ErrorMessage = "Требуется фамилия")]
        public string LastName { get; set; }

        [Required(ErrorMessage = "Требуется имя")]
        public string FirstName { get; set; }

        // Это поле в БД NOT NULL, но не обязательно в форме. 
        // Контроллер должен передать "" (пустую строку) вместо null.
        public string MiddleName { get; set; }

        [DataType(DataType.Date)]
        public DateTime? BirthDate { get; set; }

        [Phone]
        public string Phone { get; set; }

        [EmailAddress]
        public string Email { get; set; }

        [Required(ErrorMessage = "Требуется логин")]
        public string Login { get; set; }

        [DataType(DataType.Password)]
        public string? Password { get; set; } // Сделать обнуляемым

        public string Info { get; set; }

        // --- Поля, связанные с ролями и классами ---
        [Required(ErrorMessage = "Требуется роль")]
        public int RoleId { get; set; }

        public int? ClassId { get; set; }
        
        public int? StudentIdForParent { get; set; }
        public List<int> StudentIdsForParent { get; set; } = new List<int>();

        // --- Select Lists (для представления) ---
        
        public List<SelectListItem>? AvailableRoles { get; set; }
        public SelectList? SchoolClasses { get; set; }

        public SelectList? AllStudents { get; set; }
    }
}