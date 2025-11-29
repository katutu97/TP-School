namespace TP_School.Models
{
    public class SchoolClass
    {
        public int ClassId { get; set; }
        public int ClassNumber { get; set; }
        public string ClassLetter { get; set; }
        public int ClassTeacherId { get; set; }

        // Вычисляемое свойство
        public string ClassName => $"{ClassNumber}-{ClassLetter}";
    }
}