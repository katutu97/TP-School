using System;
using System.Collections.Generic;

namespace TP_School.ViewModels
{
    // Модель данных для одной строки в таблице проверки ДЗ
    public class HomeworkReviewItem
    {
        public int HomeworkId { get; set; }
        public int StudentId { get; set; }
        public string StudentFullName { get; set; }
        public int ClassId { get; set; }
        public string ClassName { get; set; }
        public string SubjectName { get; set; }
        public DateTime LessonDate { get; set; }
        public int LessonNumber { get; set; }
        // Дата загрузки ДЗ учеником
        public DateTime SubmissionDate { get; set; }
        public string StudentAnswer { get; set; }
        public bool HasFile { get; set; }

        // Данные, полученные из модели Grade
        public int? GradeId { get; set; } // ID записи в Grade для обновления
        public int? CurrentGradeValue { get; set; }
        public string CurrentTeacherComment { get; set; }
        public int StatusId { get; set; }
        // 🆕 НОВОЕ: Вычисляемое свойство для отображения статуса и стилей
        public string ReviewStatus
        {
            get
            {
                // Используем StatusId: 1 = Ожидает проверки, 2 = Проверено
                return StatusId == 2 ? "Проверено" : "Ожидает проверки";
            }
        }

        public string StatusCssClass
        {
            get
            {
                return StatusId == 2 ? "bg-green-100 text-green-800" : "bg-yellow-100 text-yellow-800";
            }
        }
    }

    // Основная модель представления
    public class HomeworkReviewViewModel
    {
        public List<HomeworkReviewItem> Submissions { get; set; }
        // Словарь доступных классов: Key = ClassId, Value = ClassName
        public Dictionary<int, string> AvailableClasses { get; set; }
        public int? SelectedClassId { get; set; }
        public string SelectedClassName { get; set; }
    }
}