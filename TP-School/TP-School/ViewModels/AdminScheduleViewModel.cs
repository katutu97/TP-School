using TP_School.Models;

namespace TP_School.ViewModels
{
    public class AdminScheduleViewModel
    {
        // --- Параметры Фильтрации и Навигации ---
        public DateTime StartOfWeek { get; set; }
        public int? SelectedTeacherId { get; set; } 
        public int? SelectedClassId { get; set; }  
        public string FilterType { get; set; } 

        // --- Основные данные для Таблицы ---
        // Ключ: День недели (DayOfWeek), Значение: Список уроков в этот день
        public Dictionary<DayOfWeek, List<AdminScheduleItemViewModel>> ScheduleByDay { get; set; }

        // --- Справочники для Выпадающих Списков ---
        public IEnumerable<User> AvailableTeachers { get; set; } 
        public IEnumerable<SchoolClass> AvailableClasses { get; set; } 
    }

    public class AdminScheduleItemViewModel
    {
        // ID записи в таблице Schedule (для редактирования конкретного дня)
        // Nullable, если урок взят из ScheduleTemplate
        public int? ScheduleId { get; set; }

        // Обязательные поля
        public DayOfWeek DayOfWeek { get; set; }
        public int LessonNumber { get; set; }
        public string LessonTime { get; set; } 

        // Связанные данные
        public int ClassId { get; set; }
        public string ClassName { get; set; } 

        public int SubjectId { get; set; }
        public string SubjectName { get; set; } 

        public int TeacherId { get; set; }
        public string TeacherFullName { get; set; }

        public string Classroom { get; set; }
        public string LessonTopic { get; set; }
        public string HomeworkText { get; set; }

        // Флаг, указывающий, что это *НЕ* шаблон (т.е. индивидуальная запись)
        public bool IsCustomLesson { get; set; } = false;
    }
}
