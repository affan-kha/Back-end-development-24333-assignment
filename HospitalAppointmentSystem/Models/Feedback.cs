using System;

namespace HospitalAppointmentSystem.Models
{
    public class Feedback
    {
        public int Id { get; set; }
        public int AppointmentId { get; set; }
        public virtual Appointment Appointment { get; set; } = null!;
        public int PatientId { get; set; }
        public virtual Patient Patient { get; set; } = null!;
        public int Rating { get; set; } // 1-5
        public string? Comments { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
} 