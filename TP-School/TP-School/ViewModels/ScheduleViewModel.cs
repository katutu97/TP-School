using System;
using System.Collections.Generic;

namespace TP_School.ViewModels
{
    public class ScheduleViewModel
    {
        // Основные данные
        public Dictionary<DayOfWeek, List<ScheduleItemViewModel>> ScheduleByDay { get; set; }
        public DateTime SelectedDate { get; set; }
        public DateTime StartOfWeek { get; set; }

        // Тип представления
        public bool IsPersonalView { get; set; } // Для учеников/родителей
        public bool IsAdminView { get; set; }    // Для учителей/директоров

        // Для фильтрации
        public int? SelectedClassId { get; set; }
    }

    public class ScheduleItemViewModel
    {
        public int ScheduleId { get; set; }

        // --- ДОБАВЛЕННЫЕ СВОЙСТВА ---
        public int SubjectId { get; set; }
        public int TeacherId { get; set; }
        public string LessonTime { get; set; }
        

        public string SubjectName { get; set; }
        public string TeacherFullName { get; set; }
        public string Classroom { get; set; } 
        public int LessonNumber { get; set; }
        public string LessonTopic { get; set; }
        public string HomeworkText { get; set; }
        public DateTime Date { get; set; }
        public int? Grade { get; set; }
        public string GradeComment { get; set; }
    }
}