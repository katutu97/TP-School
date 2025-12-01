namespace TP_School.Models
{
    public class StudentClass
    {
        public int Id { get; set; }
        public int StudentId { get; set; }
        public int ClassId { get; set; }

        public User Student { get; set; }
        public SchoolClass Class { get; set; }
    }
}
