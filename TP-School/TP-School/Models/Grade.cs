namespace TP_School.Models
{
    public class Grade
    {
        public int GradeId { get; set; }
        public int StudentId { get; set; }
        public int? HomeworkId { get; set; }
        public int LessonId { get; set; }
        public string Comment { get; set; }
        public DateTime Date { get; set; }
        public int GradeValue { get; set; }
    }
}
