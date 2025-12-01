using System.ComponentModel.DataAnnotations;

namespace TP_School.Models
{
    public class Schedule
    {
        [Key]
        public int LessonId { get; set; }
        public DateTime Date { get; set; }
        public int LessonNumber { get; set; }
        public int ClassId { get; set; }
        public int SubjectId { get; set; }
        public int TeacherId { get; set; }
        public string Room { get; set; }
        public string LessonTopic { get; set; }
        public string HomeworkText { get; set; }

        public SchoolClass Class { get; set; }
        public Subject Subject { get; set; }
        public User Teacher { get; set; }
        public ICollection<Homework> Homeworks { get; set; }
        public ICollection<Grade> Grades { get; set; }
        public ICollection<Attendance> Attendances { get; set; }
        public ICollection<Remark> Remarks { get; set; }
    }
}
