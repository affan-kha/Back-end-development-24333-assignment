using System;

namespace HospitalAppointmentSystem.Models
{
    public class MedicalInfoRequest
    {
        public int Id { get; set; }
        public int DoctorId { get; set; }
        public int PatientId { get; set; }
        public string RequestText { get; set; } = null!;
        public string? ResponseText { get; set; }
        public string? AttachmentUrl { get; set; }
        public string Status { get; set; } = "Pending"; // Pending, Responded, Cancelled
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? RespondedAt { get; set; }
    }
} 