using System.ComponentModel.DataAnnotations;

namespace TP_School.Models
{
    public class SchoolClass
    {
        [Key]
        public int ClassId { get; set; }
        public int ClassNumber { get; set; }
        public string ClassLetter { get; set; }
        public int ClassTeacherId { get; set; }

        public User ClassTeacher { get; set; }
        public ICollection<StudentClass> StudentClasses { get; set; }
        public ICollection<ClassSubjectTeacher> ClassSubjectTeachers { get; set; }
        public ICollection<ScheduleTemplate> ScheduleTemplates { get; set; }
        public ICollection<Schedule> Schedules { get; set; }
        public ICollection<Announcement> Announcements { get; set; }

        // Вычисляемое свойство
        public string ClassName => $"{ClassNumber}-{ClassLetter}";
    }
}