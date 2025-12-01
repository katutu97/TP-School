namespace TP_School.Models
{
    public class StudentParents
    {
        public int Id { get; set; }
        public int StudentId { get; set; }
        public int ParentId { get; set; }

        public User Student { get; set; }
        public User Parent { get; set; }
    }
}
