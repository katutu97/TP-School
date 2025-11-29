namespace TP_School.Models
{
    public class Message
    {
        public int MessageId { get; set; }
        public DateTime SentAt { get; set; }
        public int FromUserId { get; set; }
        public int ToUserId { get; set; }
        public string MessageText { get; set; }
    }
}