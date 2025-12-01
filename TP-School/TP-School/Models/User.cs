using Microsoft.Extensions.Logging;
using System.Collections.Generic;

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
        

        public Role Role { get; set; }
        public ICollection<SchoolClass> ClassesAsTeacher { get; set; }
        public ICollection<StudentClass> StudentClasses { get; set; }
        public ICollection<ClassSubjectTeacher> ClassSubjectTeachers { get; set; }
        public ICollection<ScheduleTemplate> ScheduleTemplates { get; set; }
        public ICollection<Schedule> Schedules { get; set; }
        public ICollection<Homework> Homeworks { get; set; }
        public ICollection<Grade> Grades { get; set; }
        public ICollection<Attendance> Attendances { get; set; }
        public ICollection<Remark> Remarks { get; set; }
        public ICollection<StudentParents> StudentParentsAsStudent { get; set; }
        public ICollection<StudentParents> StudentParentsAsParent { get; set; }
        public ICollection<Message> SentMessages { get; set; }
        public ICollection<Message> ReceivedMessages { get; set; }
        

        // Вычисляемое свойство
        public string FullName => $"{LastName} {FirstName} {MiddleName}".Trim();
    }
}
