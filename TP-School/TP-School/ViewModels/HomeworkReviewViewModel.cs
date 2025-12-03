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