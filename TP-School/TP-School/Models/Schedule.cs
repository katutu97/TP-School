namespace TP_School.Models
{
    public class Schedule
    {
        public int LessonId { get; set; }
        public DateTime Date { get; set; }
        public int LessonNumber { get; set; }
        public int ClassId { get; set; }
        public int SubjectId { get; set; }
        public int TeacherId { get; set; }
        public string Room { get; set; }
        public string LessonTopic { get; set; }
        public string HomeworkText { get; set; }
    }
}
