using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using HospitalAppointmentSystem.Models;

namespace HospitalAppointmentSystem.Data
{
    public class ApplicationDbContext : IdentityDbContext<ApplicationUser>
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options) { }

        public DbSet<Patient> Patients { get; set; }
        public DbSet<Doctor> Doctors { get; set; }
        public DbSet<Admin> Admins { get; set; }
        public DbSet<Appointment> Appointments { get; set; }
        public DbSet<Notification> Notifications { get; set; }
        public DbSet<Prescription> Prescriptions { get; set; }
        public DbSet<Feedback> Feedbacks { get; set; }
        public DbSet<DoctorUnavailableSlot> DoctorUnavailableSlots { get; set; }
        public DbSet<Message> Messages { get; set; }
        public DbSet<MedicalInfoRequest> MedicalInfoRequests { get; set; }
        public DbSet<SystemLog> SystemLogs { get; set; }

        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);

            // One-to-one ApplicationUser <-> Patient
            builder.Entity<Patient>()
                .HasOne(p => p.ApplicationUser)
                .WithOne(u => u.PatientProfile)
                .HasForeignKey<Patient>(p => p.ApplicationUserId);

            // One-to-one ApplicationUser <-> Doctor
            builder.Entity<Doctor>()
                .HasOne(d => d.ApplicationUser)
                .WithOne(u => u.DoctorProfile)
                .HasForeignKey<Doctor>(d => d.ApplicationUserId);

            // One-to-one ApplicationUser <-> Admin
            builder.Entity<Admin>()
                .HasOne(a => a.ApplicationUser)
                .WithOne(u => u.AdminProfile)
                .HasForeignKey<Admin>(a => a.ApplicationUserId);

            // One-to-many Appointment <-> Feedback
            builder.Entity<Feedback>()
                .HasOne(f => f.Appointment)
                .WithMany(a => a.Feedbacks)
                .HasForeignKey(f => f.AppointmentId);

            // One-to-many Doctor <-> DoctorUnavailableSlot
            builder.Entity<DoctorUnavailableSlot>()
                .HasOne(s => s.Doctor)
                .WithMany()
                .HasForeignKey(s => s.DoctorId);
        }
    }
} 