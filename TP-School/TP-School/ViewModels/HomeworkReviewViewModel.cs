using System;
using System.Collections.Generic;

namespace TP_School.ViewModels
{
    /// <summary>
    /// Модель данных для одной строки в таблице проверки домашних заданий
    /// Содержит информацию о ДЗ, студенте, оценке и статусе проверки
    /// </summary>
    public class HomeworkReviewItem
    {
        // Базовые идентификаторы
        public int HomeworkId { get; set; }
        public int StudentId { get; set; }
        public string StudentFullName { get; set; }
        public int ClassId { get; set; }
        public string ClassName { get; set; }

        // Информация о предмете и уроке
        public string SubjectName { get; set; }
        public DateTime LessonDate { get; set; }
        public int LessonNumber { get; set; }

        // Информация о сдаче ДЗ
        public DateTime SubmissionDate { get; set; }
        public string StudentAnswer { get; set; }
        public bool HasFile { get; set; }

        // Данные об оценке (могут быть null, если ДЗ еще не проверено)
        public int? GradeId { get; set; }
        public int? CurrentGradeValue { get; set; }
        public string CurrentTeacherComment { get; set; }

        // Статус проверки ДЗ
        public int StatusId { get; set; }

        /// <summary>
        /// Текстовое представление статуса для отображения 
        /// </summary>
        public string ReviewStatus
        {
            get
            {
                // Статус 2 соответствует проверенному ДЗ
                return StatusId == 2 ? "Проверено" : "Ожидает проверки";
            }
        }

        /// <summary>
        /// CSS-классы для стилизации статуса в пользовательском интерфейсе
        /// </summary>
        public string StatusCssClass
        {
            get
            {
                // Зеленый для проверенных, желтый для ожидающих проверки
                return StatusId == 2 ? "bg-green-100 text-green-800" : "bg-yellow-100 text-yellow-800";
            }
        }
    }

    /// <summary>
    /// Основная ViewModel для страницы проверки домашних заданий
    /// Содержит список ДЗ для проверки и фильтры для навигации
    /// </summary>
    public class HomeworkReviewViewModel
    {
        // Список домашних заданий для отображения в таблице
        public List<HomeworkReviewItem> Submissions { get; set; }

        // Словарь доступных классов для фильтрации (ID -> Название)
        public Dictionary<int, string> AvailableClasses { get; set; }

        // ID выбранного класса для фильтрации (может быть null)
        public int? SelectedClassId { get; set; }

        // Название выбранного класса (для отображения в UI)
        public string SelectedClassName { get; set; }
    }
}