namespace TP_School.Models
{
    public class Remark
    {
        public int Id { get; set; }
        public int StudentId { get; set; }
        public int LessonId { get; set; }
        public string Text { get; set; }
    }
}
