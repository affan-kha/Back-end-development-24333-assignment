using System.Collections.Generic;

namespace HospitalAppointmentSystem.Models
{
    public class Doctor
    {
        public int Id { get; set; }
        public string ApplicationUserId { get; set; } = null!;
        public virtual ApplicationUser ApplicationUser { get; set; } = null!;
        public string Specialization { get; set; } = null!;
        public string? Location { get; set; }
        public string? Schedule { get; set; } // JSON or string for available slots
        public virtual ICollection<Appointment> Appointments { get; set; } = new List<Appointment>();
        public virtual ICollection<Prescription> Prescriptions { get; set; } = new List<Prescription>();
    }
} 