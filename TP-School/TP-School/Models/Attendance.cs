namespace TP_School.Models
{
    public class Attendance
    {
        public int Id { get; set; }
        public int StudentId { get; set; }
        public int LessonId { get; set; }
        public string Status { get; set; } // 'P' - присутствовал, 'A' - отсутствовал
    }
}
