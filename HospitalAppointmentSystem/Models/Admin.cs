namespace HospitalAppointmentSystem.Models
{
    public class Admin
    {
        public int Id { get; set; }
        public string ApplicationUserId { get; set; } = null!;
        public virtual ApplicationUser ApplicationUser { get; set; } = null!;
    }
} 