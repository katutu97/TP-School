namespace TP_School.Models
{
    public class ScheduleTemplate
    {
        public int Id { get; set; }
        public int ClassId { get; set; }
        public int SubjectId { get; set; }
        public int TeacherId { get; set; }
        public byte DayOfWeek { get; set; }
        public int LessonNumber { get; set; }
        public string Room { get; set; }

        public SchoolClass Class { get; set; }
        public Subject Subject { get; set; }
        public User Teacher { get; set; }
    }
}
