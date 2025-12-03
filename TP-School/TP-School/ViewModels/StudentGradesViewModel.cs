using System.Collections.Generic;

namespace TP_School.ViewModels
{
    public class SubjectGradesItem
    {
        public string SubjectName { get; set; }
        public double AverageGrade { get; set; }
        public string AverageGradeDisplay => AverageGrade.ToString("0.00");
        public int TotalAbsences { get; set; }
        public int QuarterFinalGrade { get; set; }

        // --- ПОЛЯ ДЛЯ ДЕТАЛИЗАЦИИ ПРОПУСКОВ (для тултипа) ---
        public int AbsentTypeH { get; set; } // Неуважительная (Н)
        public int AbsentTypeU { get; set; } // Уважительная (У)
        public int AbsentTypeB { get; set; } // Болезнь (Б)
        public int TotalLessonsInPeriod { get; set; } // Общее количество уроков по предмету за период
    }

    public class StudentGradesViewModel
    {
        public List<SubjectGradesItem> Subjects { get; set; } = new List<SubjectGradesItem>();

        public List<string> AvailableQuarters { get; set; } = new List<string> { "Итоговые оценки", "I", "II", "III", "IV" };

        public string SelectedQuarter { get; set; }
        public string StudentFullName { get; set; }
        public string ClassName { get; set; }
    }
}