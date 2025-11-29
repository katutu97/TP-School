namespace TP_School.Models
{
    public class Announcement
    {
        public int AnnouncementId { get; set; }
        public int ClassId { get; set; }
        public DateTime CreatedAt { get; set; }
        public string Title { get; set; }
        public string Text { get; set; }
    }
}
