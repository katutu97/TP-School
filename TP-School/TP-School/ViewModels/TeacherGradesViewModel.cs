using System.Collections.Generic;
using System.Linq;

namespace TP_School.ViewModels
{
    public class StudentPerformanceItem
    {
        public int StudentId { get; set; }
        public string FullName { get; set; }
        public double AverageGrade { get; set; }
        public string AverageGradeDisplay => AverageGrade.ToString("0.00");
        public int TotalAbsences { get; set; }
        public string AbsencesPercentage { get; set; } // Например, "10 (50%)"
    }

    public class TeacherGradesViewModel
    {
        public List<StudentPerformanceItem> Students { get; set; } = new List<StudentPerformanceItem>();
        // Для фильтров: отображение доступных опций
        public Dictionary<int, string> AvailableClasses { get; set; } = new Dictionary<int, string>();
        public Dictionary<int, string> AvailableSubjects { get; set; } = new Dictionary<int, string>();
        public List<string> AvailableQuarters { get; set; } = new List<string> { "I", "II", "III", "IV" };

        // Выбранные фильтры
        public int? SelectedClassId { get; set; }
        public int? SelectedSubjectId { get; set; }
        public string SelectedQuarter { get; set; }
    }
}