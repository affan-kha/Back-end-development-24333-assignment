using System.Collections.Generic;

namespace HospitalAppointmentSystem.Models
{
    public class Patient
    {
        public int Id { get; set; }
        public string ApplicationUserId { get; set; } = null!;
        public virtual ApplicationUser ApplicationUser { get; set; } = null!;
        public string? MedicalHistory { get; set; }
        public string? PendingMedicalHistory { get; set; }
        public string? MedicalHistoryApprovalStatus { get; set; } // Pending, Approved, Rejected, null
        public virtual ICollection<Appointment> Appointments { get; set; } = new List<Appointment>();
        public virtual ICollection<Prescription> Prescriptions { get; set; } = new List<Prescription>();
    }
} 