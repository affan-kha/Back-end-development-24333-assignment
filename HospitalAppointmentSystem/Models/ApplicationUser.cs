using Microsoft.AspNetCore.Identity;

namespace HospitalAppointmentSystem.Models
{
    public class ApplicationUser : IdentityUser
    {
        public virtual Patient? PatientProfile { get; set; }
        public virtual Doctor? DoctorProfile { get; set; }
        public virtual Admin? AdminProfile { get; set; }
        public string? FullName { get; set; }
        public string? ContactInfo { get; set; }
        public bool IsActive { get; set; } = true;
    }
} 