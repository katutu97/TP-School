using System.ComponentModel.DataAnnotations;

namespace TP_School.ViewModels
{
    public class AnnouncementViewModel
    {
        [Required(ErrorMessage = "Заголовок обязателен")]
        [StringLength(100, ErrorMessage = "Длина заголовка не должна превышать 100 символов")]
        public string Title { get; set; }

        [Required(ErrorMessage = "Текст объявления обязателен")]
        public string Text { get; set; }

        // Поля CreatedAt и ClassId будут заполняться на стороне сервера.
        // UserId не нужен, так как ClassId уже связывает объявление с классом,
        // который ведет классный руководитель (ClassTeacherId в SchoolClass).
    }
}