using System.Collections.Generic;
using System.Linq;

namespace TP_School.ViewModels
{
    /// Модель элемента успеваемости ученика для учителя
    /// Представляет сводную информацию об успеваемости одного ученика
    public class StudentPerformanceItem
    {
        /// Уникальный идентификатор ученика
        /// Используется для ссылок и операций с конкретным учеником
        public int StudentId { get; set; }

        /// Полное имя ученика (Фамилия Имя Отчество)
        public string FullName { get; set; }

        /// Средний балл ученика по выбранному предмету и периоду
        /// Рассчитывается как среднее арифметическое всех оценок
        public double AverageGrade { get; set; }

        /// Отформатированное строковое представление среднего балла
        /// Отображается с двумя знаками после запятой
        public string AverageGradeDisplay => AverageGrade.ToString("0.00");

        /// Общее количество пропущенных занятий учеником
        /// Учитываются все типы пропусков за выбранный период
        public int TotalAbsences { get; set; }

        /// Процентное соотношение пропусков в формате "10 (25%)"
        /// Первое число - количество пропусков, в скобках - процент от общего числа занятий
        public string AbsencesPercentage { get; set; } // Например, "10 (50%)"
    }

    /// Модель представления для отображения оценок учителю
    /// Позволяет учителю просматривать успеваемость учеников с фильтрацией
    public class TeacherGradesViewModel
    {
        /// Список учеников с информацией об их успеваемости
        /// Отображается в виде таблицы или списка
        public List<StudentPerformanceItem> Students { get; set; } = new List<StudentPerformanceItem>();


        /// Словарь доступных классов для выбора
        /// Ключ: идентификатор класса, Значение: название класса
        /// Используется для заполнения выпадающего списка классов
        public Dictionary<int, string> AvailableClasses { get; set; } = new Dictionary<int, string>();

        /// Словарь доступных предметов для выбора
        /// Ключ: идентификатор предмета, Значение: название предмета
        /// Используется для заполнения выпадающего списка предметов
        public Dictionary<int, string> AvailableSubjects { get; set; } = new Dictionary<int, string>();

        /// Список доступных учебных четвертей для фильтрации
        /// Содержит значения: "I", "II", "III", "IV" (четверти учебного года)
        public List<string> AvailableQuarters { get; set; } = new List<string> { "I", "II", "III", "IV" };

        /// Идентификатор выбранного класса для фильтрации
        /// Null - фильтр не применен, отображаются все доступные классы
        public int? SelectedClassId { get; set; }

        /// Идентификатор выбранного предмета для фильтрации
        /// Null - фильтр не применен, отображаются все доступные предметы
        public int? SelectedSubjectId { get; set; }

        /// Выбранная учебная четверть для фильтрации
        /// Определяет период, за который отображаются оценки и пропуски
        public string SelectedQuarter { get; set; }
    }
}