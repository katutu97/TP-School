namespace TP_School.Models
{
    public class Subject
    {
        public int SubjectId { get; set; }
        public string SubjectName { get; set; }

        public ICollection<ClassSubjectTeacher> ClassSubjectTeachers { get; set; }
        public ICollection<ScheduleTemplate> ScheduleTemplates { get; set; }
        public ICollection<Schedule> Schedules { get; set; }
    }
}