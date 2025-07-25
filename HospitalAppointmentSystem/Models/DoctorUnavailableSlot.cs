using System;

namespace HospitalAppointmentSystem.Models
{
    public class DoctorUnavailableSlot
    {
        public int Id { get; set; }
        public int DoctorId { get; set; }
        public virtual Doctor Doctor { get; set; } = null!;
        public DateTime Date { get; set; }
        public string StartTime { get; set; } = null!; // e.g. "09:00"
        public string EndTime { get; set; } = null!;   // e.g. "11:00"
    }
} 