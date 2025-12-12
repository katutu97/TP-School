using Microsoft.AspNetCore.Http;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace TP_School.ViewModels
{
    public class HomeworkSubmitViewModel
    {
        [Required]
        public int LessonId { get; set; }

        [Display(Name = "Ваш ответ")]
        public string AnswerText { get; set; }
    }
}