using System;
using System.Collections.Generic;

namespace TP_School.ViewModels
{

    /// Модель представления для отображения расписания занятий
    public class ScheduleViewModel
    {

        /// Расписание, сгруппированное по дням недели
        /// Ключ: день недели, Значение: список занятий в этот день
        public Dictionary<DayOfWeek, List<ScheduleItemViewModel>> ScheduleByDay { get; set; }

        /// Выбранная пользователем дата для просмотра расписания
        public DateTime SelectedDate { get; set; }

        /// Начальная дата текущей отображаемой недели
        /// Используется для навигации по неделям
        public DateTime StartOfWeek { get; set; }


        /// Флаг, указывающий что расписание отображается в персональном режиме
        /// true - для учеников и родителей (видно только расписание конкретного ученика)
        public bool IsPersonalView { get; set; }


        /// Флаг, указывающий что расписание отображается в административном режиме
        /// true - для учителей и директоров (возможность редактирования, просмотр всего расписания)
        public bool IsAdminView { get; set; }

        /// Идентификатор выбранного класса для фильтрации расписания
        /// null - фильтрация не применяется
        public int? SelectedClassId { get; set; }
    }

    /// Модель представления для одного элемента расписания (одно занятие)
    public class ScheduleItemViewModel
    {

        /// Уникальный идентификатор записи в расписании
        public int ScheduleId { get; set; }

        /// Идентификатор предмета/дисциплины
        public int SubjectId { get; set; }


        /// Идентификатор преподавателя
        public int TeacherId { get; set; }


        /// Время проведения урока в строковом формате (например: "09:00 - 09:45")
        public string LessonTime { get; set; }


        /// Название предмета/дисциплины
        public string SubjectName { get; set; }


        /// Полное имя преподавателя (Фамилия И.О.)
        public string TeacherFullName { get; set; }

        /// Номер или название кабинета, где проводится занятие
        public string Classroom { get; set; }


        /// Порядковый номер урока в учебном дне (1-й, 2-й, и т.д.)
        public int LessonNumber { get; set; }

        /// Тема урока/занятия
        public string LessonTopic { get; set; }

        /// Текст домашнего задания
        public string HomeworkText { get; set; }

        /// Конкретная дата проведения занятия
        public DateTime Date { get; set; }

        /// Оценка за урок (если есть, null если оценка не выставлена)
        public int? Grade { get; set; }

        /// Комментарий к оценке или уроку
        public string GradeComment { get; set; }
    }
}