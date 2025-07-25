using System;

namespace HospitalAppointmentSystem.Models
{
    public class Message
    {
        public int Id { get; set; }
        public string SenderId { get; set; } = null!; // ApplicationUserId
        public string ReceiverId { get; set; } = null!; // ApplicationUserId
        public int? AppointmentId { get; set; }
        public string Content { get; set; } = null!;
        public DateTime SentAt { get; set; } = DateTime.UtcNow;
        public bool IsRead { get; set; } = false;
    }
} 