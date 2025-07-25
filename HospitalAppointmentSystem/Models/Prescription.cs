using System;

namespace HospitalAppointmentSystem.Models
{
    public class Prescription
    {
        public int Id { get; set; }
        public int PatientId { get; set; }
        public virtual Patient Patient { get; set; } = null!;
        public int DoctorId { get; set; }
        public virtual Doctor Doctor { get; set; } = null!;
        public int AppointmentId { get; set; }
        public virtual Appointment Appointment { get; set; } = null!;
        public string MedicationName { get; set; } = null!;
        public string Dosage { get; set; } = null!;
        public string Instructions { get; set; } = null!;
        public DateTime IssueDate { get; set; } = DateTime.UtcNow;
        public string Status { get; set; } = "Active"; // Active, Expired, Completed
        public bool IsRenewable { get; set; } = false;
        public int? RefillsRemaining { get; set; }
        public string? Notes { get; set; }
        public bool RenewalRequested { get; set; } = false;
        public string? RenewalStatus { get; set; } // Pending, Approved, Rejected, null
        public string? RenewalReason { get; set; }
        public DateTime? ExpiryDate { get; set; }
        public string? SecureCode { get; set; }
        public string? PharmacyEmail { get; set; }
        public bool SentToPharmacy { get; set; } = false;
    }
} 