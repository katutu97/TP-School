using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TP_School.Models;

namespace TP_School.Data
{
    public class ApplicationDbContext : DbContext
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options) { }

        // DbSets для всех моделей
        public DbSet<Role> Roles { get; set; }
        public DbSet<User> Users { get; set; }
        public DbSet<SchoolClass> SchoolClasses { get; set; }
        public DbSet<StudentClass> StudentClasses { get; set; }
        public DbSet<Subject> Subjects { get; set; }
        public DbSet<ClassSubjectTeacher> ClassSubjectTeachers { get; set; }
        public DbSet<ScheduleTemplate> ScheduleTemplates { get; set; }
        public DbSet<Schedule> Schedules { get; set; }
        public DbSet<Homework> Homeworks { get; set; }
        public DbSet<Grade> Grades { get; set; }
        public DbSet<Attendance> Attendances { get; set; }
        public DbSet<Remark> Remarks { get; set; }
        public DbSet<Announcement> Announcements { get; set; }
        public DbSet<StudentParents> StudentParentses { get; set; }
        public DbSet<Message> Messages { get; set; }
        

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            
            // Настройка отношений для User
            modelBuilder.Entity<User>()
                .HasMany(u => u.SentMessages)
                .WithOne(m => m.FromUser)
                .HasForeignKey(m => m.FromUserId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<User>()
                .HasMany(u => u.ReceivedMessages)
                .WithOne(m => m.ToUser)
                .HasForeignKey(m => m.ToUserId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<User>()
                .HasMany(u => u.StudentParentsAsStudent)
                .WithOne(sp => sp.Student)
                .HasForeignKey(sp => sp.StudentId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<User>()
                .HasMany(u => u.StudentParentsAsParent)
                .WithOne(sp => sp.Parent)
                .HasForeignKey(sp => sp.ParentId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<User>()
                .HasMany(u => u.ClassesAsTeacher)
                .WithOne(c => c.ClassTeacher)
                .HasForeignKey(c => c.ClassTeacherId)
                .OnDelete(DeleteBehavior.Restrict);

            // Настройка отношений для StudentClass (составной ключ)
            modelBuilder.Entity<StudentClass>()
                .HasKey(sc => sc.Id);

            modelBuilder.Entity<StudentClass>()
                .HasIndex(sc => new { sc.StudentId, sc.ClassId })
                .IsUnique();

            // Настройка отношений для ClassSubjectTeacher (составной ключ)
            modelBuilder.Entity<ClassSubjectTeacher>()
                .HasKey(cst => cst.Id);

            modelBuilder.Entity<ClassSubjectTeacher>()
                .HasIndex(cst => new { cst.ClassId, cst.SubjectId, cst.TeacherId })
                .IsUnique();

            // Настройка отношений для StudentParent (составной ключ)
            modelBuilder.Entity<StudentParents>()
                .HasKey(sp => sp.Id);

            modelBuilder.Entity<StudentParents>()
                .HasIndex(sp => new { sp.StudentId, sp.ParentId })
                .IsUnique();

            // Настройка отношений для Attendance (составной ключ)
            modelBuilder.Entity<Attendance>()
                .HasKey(a => a.Id);

            modelBuilder.Entity<Attendance>()
                .HasIndex(a => new { a.StudentId, a.LessonId })
                .IsUnique();

            // Настройка отношений для Remark (составной ключ)
            modelBuilder.Entity<Remark>()
                .HasKey(r => r.Id);

            modelBuilder.Entity<Remark>()
                .HasIndex(r => new { r.StudentId, r.LessonId })
                .IsUnique();

            // Настройка ScheduleTemplate
            modelBuilder.Entity<ScheduleTemplate>()
                .HasIndex(st => new { st.ClassId, st.DayOfWeek, st.LessonNumber })
                .IsUnique();

            // Настройка Schedule
            modelBuilder.Entity<Schedule>()
                .HasIndex(s => new { s.ClassId, s.Date, s.LessonNumber })
                .IsUnique();

            // Настройка внешних ключей для Grade
            modelBuilder.Entity<Grade>()
                .HasOne(g => g.Homework)
                .WithMany(h => h.Grades)
                .HasForeignKey(g => g.HomeworkId)
                .OnDelete(DeleteBehavior.Restrict);

            // Настройка внешних ключей для Homework
            modelBuilder.Entity<Homework>()
                .HasOne(h => h.Student)
                .WithMany(u => u.Homeworks)
                .HasForeignKey(h => h.StudentId)
                .OnDelete(DeleteBehavior.Restrict);
        }
    }
}