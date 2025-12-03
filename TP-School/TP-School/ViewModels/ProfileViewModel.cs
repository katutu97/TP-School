using System.ComponentModel.DataAnnotations;

namespace TP_School.ViewModels
{
    public class ProfileViewModel
    {
        public int UserId { get; set; }

        [Display(Name = "Фамилия")]
        public string LastName { get; set; }

        [Display(Name = "Имя")]
        public string FirstName { get; set; }

        [Display(Name = "Отчество")]
        public string MiddleName { get; set; }

        [Display(Name = "Логин")]
        public string Login { get; set; }

        [Display(Name = "Email")]
        public string Email { get; set; }

        [Display(Name = "Телефон")]
        public string Phone { get; set; }

        [Display(Name = "Дата рождения")]
        [DataType(DataType.Date)]
        public DateTime? BirthDate { get; set; }

        [Display(Name = "Роль")]
        public string RoleName { get; set; }

        [Display(Name = "Дополнительная информация")]
        public string Info { get; set; }

        // Динамические поля
        [Display(Name = "Класс")]
        public string ClassInfo { get; set; } // Для ученика или учителя

        [Display(Name = "Ребенок")]
        public string StudentInfo { get; set; } // Для родителя
    }
}