namespace TP_School.Models
{
    public class Homework
    {
        public int HomeworkId { get; set; }
        public int LessonId { get; set; }
        public DateTime Date { get; set; }
        public string Text { get; set; }
        public byte[] FilePath { get; set; }
        public int StudentId { get; set; }

        // 🆕 НОВОЕ: Статус (соответствует полю в БД)
        public int Status { get; set; }

        public Schedule Lesson { get; set; }
        public User Student { get; set; }
        public ICollection<Grade> Grades { get; set; }
    }
}