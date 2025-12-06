using System.Collections.Generic;
using System.Linq;

namespace TP_School.ViewModels
{
    // Для ученика
    public class StudentSubjectItem
    {
        public string SubjectName { get; set; }
        public double AverageGrade { get; set; }
        public string AverageGradeDisplay => AverageGrade.ToString("0.00");
        public int TotalAbsences { get; set; }
        public int QuarterFinalGrade { get; set; }

        // Детализация пропусков
        public int AbsentTypeH { get; set; }
        public int AbsentTypeU { get; set; }
        public int AbsentTypeB { get; set; }
        public int TotalLessonsInPeriod { get; set; }

        // Детализация оценок
        public List<int> AllGrades { get; set; } = new List<int>();
        public int GradeCount => AllGrades.Count;
        public int GradeCount5 => AllGrades.Count(g => g == 5);
        public int GradeCount4 => AllGrades.Count(g => g == 4);
        public int GradeCount3 => AllGrades.Count(g => g == 3);
        public int GradeCount2 => AllGrades.Count(g => g == 2);
        public int GradeCount1 => AllGrades.Count(g => g == 1);
        public int GradeCount0 => AllGrades.Count(g => g == 0);

        public string AllGradesString => string.Join(", ", AllGrades);

        public double Percent5 => GradeCount > 0 ? (double)GradeCount5 / GradeCount * 100 : 0;
        public double Percent4 => GradeCount > 0 ? (double)GradeCount4 / GradeCount * 100 : 0;
        public double Percent3 => GradeCount > 0 ? (double)GradeCount3 / GradeCount * 100 : 0;
        public double Percent2 => GradeCount > 0 ? (double)GradeCount2 / GradeCount * 100 : 0;
        public double Percent1 => GradeCount > 0 ? (double)GradeCount1 / GradeCount * 100 : 0;
        public double Percent0 => GradeCount > 0 ? (double)GradeCount0 / GradeCount * 100 : 0;
    }

    public class StudentGradesViewModel
    {
        public List<StudentSubjectItem> Subjects { get; set; } = new List<StudentSubjectItem>();
        public List<string> AvailableQuarters { get; set; } = new List<string> { "Итоговые оценки", "I", "II", "III", "IV" };
        public string SelectedQuarter { get; set; }
        public string StudentFullName { get; set; }
        public string ClassName { get; set; }
    }

    // Для учителя - переименовал классы
    public class TeacherStudentGradeItem
    {
        public int StudentId { get; set; }
        public string FullName { get; set; }
        public double AverageGrade { get; set; }
        public string AverageGradeDisplay => AverageGrade.ToString("0.00");
        public int TotalAbsences { get; set; }
        public int QuarterFinalGrade { get; set; }

        // Детализация пропусков
        public int AbsentTypeH { get; set; }
        public int AbsentTypeU { get; set; }
        public int AbsentTypeB { get; set; }
        public int TotalLessonsInPeriod { get; set; }

        // Детализация оценок
        public List<int> AllGrades { get; set; } = new List<int>();
        public int GradeCount => AllGrades.Count;
        public int GradeCount5 => AllGrades.Count(g => g == 5);
        public int GradeCount4 => AllGrades.Count(g => g == 4);
        public int GradeCount3 => AllGrades.Count(g => g == 3);
        public int GradeCount2 => AllGrades.Count(g => g == 2);
        public int GradeCount1 => AllGrades.Count(g => g == 1);
        public int GradeCount0 => AllGrades.Count(g => g == 0);

        public string AllGradesString => string.Join(", ", AllGrades);

        public double Percent5 => GradeCount > 0 ? (double)GradeCount5 / GradeCount * 100 : 0;
        public double Percent4 => GradeCount > 0 ? (double)GradeCount4 / GradeCount * 100 : 0;
        public double Percent3 => GradeCount > 0 ? (double)GradeCount3 / GradeCount * 100 : 0;
        public double Percent2 => GradeCount > 0 ? (double)GradeCount2 / GradeCount * 100 : 0;
        public double Percent1 => GradeCount > 0 ? (double)GradeCount1 / GradeCount * 100 : 0;
        public double Percent0 => GradeCount > 0 ? (double)GradeCount0 / GradeCount * 100 : 0;
    }

    public class TeacherClassGradesViewModel
    {
        public List<TeacherStudentGradeItem> Students { get; set; } = new List<TeacherStudentGradeItem>();
        public Dictionary<int, string> AvailableClasses { get; set; } = new Dictionary<int, string>();
        public Dictionary<int, string> AvailableSubjects { get; set; } = new Dictionary<int, string>();
        public List<string> AvailableQuarters { get; set; } = new List<string> { "Итоговые оценки", "I", "II", "III", "IV" };
        public int SelectedClassId { get; set; }
        public int SelectedSubjectId { get; set; }
        public string SelectedQuarter { get; set; }
        public string SelectedClassName { get; set; }
        public string SelectedSubjectName { get; set; }
    }
}