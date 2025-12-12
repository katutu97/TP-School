using Microsoft.AspNetCore.Mvc.Rendering;
using TP_School.Models;

namespace TP_School.ViewModels
{
    /// <summary>
    /// ViewModel для страницы журнала успеваемости
    /// Содержит данные для отображения оценок и посещаемости за выбранную неделю
    /// </summary>
    public class JournalViewModel
    {
        // === ФИЛЬТРЫ ДЛЯ ВЫБОРА ДАННЫХ ===

        /// ID выбранного класса для отображения в журнале
        public int SelectedClassId { get; set; }

        /// ID выбранного предмета для отображения в журнале
        public int SelectedSubjectId { get; set; }

        /// Начальная дата выбранной недели (понедельник)
        public DateTime WeekStart { get; set; }

        // === СПИСКИ ДЛЯ ВЫПАДАЮЩИХ МЕНЮ (SELECT LIST) ===

        /// Список доступных недель для выбора
        public SelectList Weeks { get; set; }


        /// Список классов, доступных для выбора
        public SelectList Classes { get; set; }

        /// Список предметов, доступных для выбора
        public SelectList Subjects { get; set; }

        // === ДАННЫЕ ДЛЯ ОТОБРАЖЕНИЯ В ТАБЛИЦЕ ===
        /// Расписание уроков на выбранную неделю
        public List<Schedule> LessonsForWeek { get; set; }


        /// Основные данные журнала - строки с информацией о студентах
        /// Каждая строка представляет одного студента и его оценки/посещаемость
        public List<JournalRow> Rows { get; set; }
    }


    /// Представляет одну строку в журнале успеваемости
    /// Соответствует одному студенту и его данным за выбранную неделю
    public class JournalRow
    {

        /// Информация о студенте
        public User Student { get; set; }


        /// Ячейки с данными для каждого урока (оценки, посещаемость)
        /// Ключ: ID урока (или порядковый номер)
        /// Значение: данные ячейки (оценка или отметка о посещении)
        public Dictionary<int, CellData> Cells { get; set; } = new Dictionary<int, CellData>();
    }


    /// Данные одной ячейки в журнале успеваемости
    /// Может содержать оценку или отметку о посещаемости
    public class CellData
    {

        /// Значение ячейки для отображения
        /// Для оценок: "5", "4", "н/а" и т.д.
        /// Для посещаемости: "н" (отсутствовал), "б" (болел) или пусто
        public string Value { get; set; }


        /// Флаг, указывающий что ячейка содержит данные о посещаемости (не об оценке)
        /// true - отметка о посещаемости, false - оценка
        public bool IsAttendance { get; set; }


        /// Флаг, указывающий наличие комментария к ячейке
        public bool HasComment { get; set; }


        /// Текстовый комментарий к оценке или посещаемости
        public string Comment { get; set; }
    }
}