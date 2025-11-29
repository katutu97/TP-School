namespace TP_School.Models
{
    public class User
    {
        public int UserId { get; set; }
        public int RoleId { get; set; }
        public string LastName { get; set; }
        public string FirstName { get; set; }
        public string MiddleName { get; set; }
        public DateTime? BirthDate { get; set; }
        public string Phone { get; set; }
        public string Email { get; set; }
        public string Login { get; set; }
        public string Password { get; set; }
        public string Info { get; set; }

        // Вычисляемое свойство
        public string FullName => $"{LastName} {FirstName} {MiddleName}".Trim();
    }
}
