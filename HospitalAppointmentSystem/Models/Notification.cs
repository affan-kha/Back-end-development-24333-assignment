using System;

namespace HospitalAppointmentSystem.Models
{
    public class Notification
    {
        public int Id { get; set; }
        public int? AppointmentId { get; set; }
        public virtual Appointment? Appointment { get; set; }
        public int? PrescriptionId { get; set; }
        public virtual Prescription? Prescription { get; set; }
        public string? UserId { get; set; }
        public string Message { get; set; } = null!;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public bool IsRead { get; set; } = false;
    }
} 