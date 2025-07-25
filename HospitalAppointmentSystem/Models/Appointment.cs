using System;
using System.Collections.Generic;

namespace HospitalAppointmentSystem.Models
{
    public class Appointment
    {
        public int Id { get; set; }
        public int PatientId { get; set; }
        public virtual Patient Patient { get; set; } = null!;
        public int DoctorId { get; set; }
        public virtual Doctor Doctor { get; set; } = null!;
        public DateTime AppointmentDateTime { get; set; }
        public string Status { get; set; } = "Pending"; // Pending, Approved, Rejected, Completed, Cancelled
        public string? ReferenceNumber { get; set; }
        public virtual ICollection<Notification> Notifications { get; set; } = new List<Notification>();
        public virtual ICollection<Prescription> Prescriptions { get; set; } = new List<Prescription>();
        public bool ReminderSent { get; set; } = false;
        public bool IsTelehealth { get; set; } = false;
        public string? VideoLink { get; set; }
        public virtual ICollection<Feedback> Feedbacks { get; set; } = new List<Feedback>();
    }
} 