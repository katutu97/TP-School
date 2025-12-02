using Microsoft.AspNetCore.Mvc.Rendering;
using TP_School.Models;

namespace TP_School.ViewModels
{
    public class JournalViewModel
    {
        // Фильтры
        public int SelectedClassId { get; set; }
        public int SelectedSubjectId { get; set; }
        public DateTime WeekStart { get; set; }

        public SelectList Weeks { get; set; }

        // Списки для выпадающих меню
        public SelectList Classes { get; set; }
        public SelectList Subjects { get; set; }

        // Данные для таблицы
        public List<Schedule> LessonsForWeek { get; set; }
        public List<JournalRow> Rows { get; set; }
    }

    public class JournalRow
    {
        public User Student { get; set; }
        public Dictionary<int, CellData> Cells { get; set; } = new Dictionary<int, CellData>();
    }

    public class CellData
    {
        public string Value { get; set; }
        public bool IsAttendance { get; set; }
        public bool HasComment { get; set; }
        public string Comment { get; set; }
    }
}