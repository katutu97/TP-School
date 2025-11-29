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
    }
}
