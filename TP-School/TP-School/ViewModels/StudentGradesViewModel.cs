using System.Collections.Generic;
using System.Linq;

namespace TP_School.ViewModels
{
    /// Модель предмета с успеваемостью для просмотра учеником
    /// Содержит оценки, пропуски и статистику по одному предмету
    public class StudentSubjectItem
    {

        /// Название учебного предмета
        public string SubjectName { get; set; }

        /// Средний балл по предмету
        public double AverageGrade { get; set; }

        /// Отформатированное отображение среднего балла (с двумя знаками после запятой)
        public string AverageGradeDisplay => AverageGrade.ToString("0.00");

        /// Общее количество пропусков занятий
        public int TotalAbsences { get; set; }

        /// Итоговая оценка за четверть
        public int QuarterFinalGrade { get; set; }


        /// Количество пропусков по болезни (тип "H" - Health/Illness)
        public int AbsentTypeH { get; set; }

        /// Количество пропусков по неуважительной причине (тип "U" - Unexcused)
        public int AbsentTypeU { get; set; }

        /// Количество пропусков по другим причинам (тип "B" - Other/Bad)
        public int AbsentTypeB { get; set; }

        /// Общее количество занятий в выбранном периоде
        public int TotalLessonsInPeriod { get; set; }

        /// Список всех оценок по предмету
        public List<int> AllGrades { get; set; } = new List<int>();

        /// Общее количество оценок
        public int GradeCount => AllGrades.Count;

        /// Количество оценок "5" (отлично)
        public int GradeCount5 => AllGrades.Count(g => g == 5);

        /// Количество оценок "4" (хорошо)
        public int GradeCount4 => AllGrades.Count(g => g == 4);

        /// Количество оценок "3" (удовлетворительно)
        public int GradeCount3 => AllGrades.Count(g => g == 3);

        /// Количество оценок "2" (неудовлетворительно)
        public int GradeCount2 => AllGrades.Count(g => g == 2);

        /// Количество оценок "1" (очень плохо)
        public int GradeCount1 => AllGrades.Count(g => g == 1);

        /// Количество нулевых оценок (не выполнено)
        public int GradeCount0 => AllGrades.Count(g => g == 0);

        /// Строковое представление всех оценок через запятую
        public string AllGradesString => string.Join(", ", AllGrades);


        /// Процент оценок "5" от общего количества
        public double Percent5 => GradeCount > 0 ? (double)GradeCount5 / GradeCount * 100 : 0;

        /// Процент оценок "4" от общего количества
        public double Percent4 => GradeCount > 0 ? (double)GradeCount4 / GradeCount * 100 : 0;

        /// Процент оценок "3" от общего количества
        public double Percent3 => GradeCount > 0 ? (double)GradeCount3 / GradeCount * 100 : 0;

        /// Процент оценок "2" от общего количества
        public double Percent2 => GradeCount > 0 ? (double)GradeCount2 / GradeCount * 100 : 0;

        /// Процент оценок "1" от общего количества
        public double Percent1 => GradeCount > 0 ? (double)GradeCount1 / GradeCount * 100 : 0;

        /// Процент нулевых оценок от общего количества
        public double Percent0 => GradeCount > 0 ? (double)GradeCount0 / GradeCount * 100 : 0;
    }


    /// Модель представления успеваемости ученика
    /// Содержит список предметов с оценками для отображения ученику/родителю
    public class StudentGradesViewModel
    {
        /// Список предметов с оценками ученика
        public List<StudentSubjectItem> Subjects { get; set; } = new List<StudentSubjectItem>();

        /// Доступные учебные периоды для фильтрации
        public List<string> AvailableQuarters { get; set; } = new List<string> { "Итоговые оценки", "I", "II", "III", "IV" };

        /// Выбранный учебный период
        public string SelectedQuarter { get; set; }

        /// Полное имя ученика
        public string StudentFullName { get; set; }

        /// Название класса ученика
        public string ClassName { get; set; }

        /// Флаг, указывающий что просмотр осуществляется родителем
        /// Влияет на отображение интерфейса
        public bool IsParentView { get; set; } = false;
    }

    /// Модель успеваемости ученика для просмотра учителем
    /// Содержит статистику по конкретному ученику для выбранного предмета
    public class TeacherStudentGradeItem
    {
        /// Идентификатор ученика
        public int StudentId { get; set; }

        /// Полное имя ученика
        public string FullName { get; set; }

        /// Средний балл ученика по предмету
        public double AverageGrade { get; set; }

        /// Отформатированное отображение среднего балла
        public string AverageGradeDisplay => AverageGrade.ToString("0.00");

        /// Общее количество пропусков ученика
        public int TotalAbsences { get; set; }

        /// Итоговая оценка за четверть
        public int QuarterFinalGrade { get; set; }

        /// Количество пропусков по болезни
        public int AbsentTypeH { get; set; }

        /// Количество пропусков по неуважительной причине
        public int AbsentTypeU { get; set; }

        /// Количество пропусков по другим причинам
        public int AbsentTypeB { get; set; }

        /// Общее количество занятий в периоде
        public int TotalLessonsInPeriod { get; set; }

        /// Список всех оценок ученика по предмету
        public List<int> AllGrades { get; set; } = new List<int>();

        /// Общее количество оценок
        public int GradeCount => AllGrades.Count;

        /// Количество оценок "5"
        public int GradeCount5 => AllGrades.Count(g => g == 5);

        /// Количество оценок "4"
        public int GradeCount4 => AllGrades.Count(g => g == 4);

        /// Количество оценок "3"
        public int GradeCount3 => AllGrades.Count(g => g == 3);

        /// Количество оценок "2"
        public int GradeCount2 => AllGrades.Count(g => g == 2);

        /// Количество оценок "1"
        public int GradeCount1 => AllGrades.Count(g => g == 1);

        /// Количество нулевых оценок
        public int GradeCount0 => AllGrades.Count(g => g == 0);

        /// Строковое представление всех оценок
        public string AllGradesString => string.Join(", ", AllGrades);

        /// Процент оценок "5"
        public double Percent5 => GradeCount > 0 ? (double)GradeCount5 / GradeCount * 100 : 0;

        /// Процент оценок "4"
        public double Percent4 => GradeCount > 0 ? (double)GradeCount4 / GradeCount * 100 : 0;

        /// Процент оценок "3"
        public double Percent3 => GradeCount > 0 ? (double)GradeCount3 / GradeCount * 100 : 0;

        /// Процент оценок "2"
        public double Percent2 => GradeCount > 0 ? (double)GradeCount2 / GradeCount * 100 : 0;

        /// Процент оценок "1"
        public double Percent1 => GradeCount > 0 ? (double)GradeCount1 / GradeCount * 100 : 0;

        /// Процент нулевых оценок
        public double Percent0 => GradeCount > 0 ? (double)GradeCount0 / GradeCount * 100 : 0;
    }

    /// Модель представления успеваемости класса для учителя
    /// Позволяет учителю просматривать оценки всех учеников по своему предмету
    public class TeacherClassGradesViewModel
    {
        /// Список учеников класса с их оценками
        public List<TeacherStudentGradeItem> Students { get; set; } = new List<TeacherStudentGradeItem>();

        /// Доступные классы для выбора (ID -> Название)
        public Dictionary<int, string> AvailableClasses { get; set; } = new Dictionary<int, string>();

        /// Доступные предметы для выбора (ID -> Название)
        public Dictionary<int, string> AvailableSubjects { get; set; } = new Dictionary<int, string>();

        /// Доступные учебные периоды
        public List<string> AvailableQuarters { get; set; } = new List<string> { "Итоговые оценки", "I", "II", "III", "IV" };

        /// Идентификатор выбранного класса
        public int SelectedClassId { get; set; }

        /// Идентификатор выбранного предмета
        public int SelectedSubjectId { get; set; }

        /// Выбранный учебный период
        public string SelectedQuarter { get; set; }

        /// Название выбранного класса (для отображения)
        public string SelectedClassName { get; set; }

        /// Название выбранного предмета (для отображения)
        public string SelectedSubjectName { get; set; }
    }
}