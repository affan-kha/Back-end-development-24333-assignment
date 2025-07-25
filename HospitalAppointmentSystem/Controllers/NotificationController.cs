using HospitalAppointmentSystem.Data;
using HospitalAppointmentSystem.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace HospitalAppointmentSystem.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class NotificationController : ControllerBase
    {
        private readonly ApplicationDbContext _context;

        public NotificationController(ApplicationDbContext context)
        {
            _context = context;
        }

        [HttpGet("my")]
        public async Task<IActionResult> MyNotifications()
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var patient = await _context.Patients.FirstOrDefaultAsync(p => p.ApplicationUserId == userId);
            var doctor = await _context.Doctors.FirstOrDefaultAsync(d => d.ApplicationUserId == userId);
            var notifications = new List<Notification>();
            // 1. Broadcast/personal notifications (UserId match)
            var userNotifications = await _context.Notifications.Where(n => n.UserId == userId).ToListAsync();
            // 2. Appointment/prescription notifications (old logic)
            if (patient != null)
            {
                var patientAppointmentIds = await _context.Appointments.Where(a => a.PatientId == patient.Id).Select(a => a.Id).ToListAsync();
                var patientPrescriptionIds = await _context.Prescriptions.Where(p => p.PatientId == patient.Id).Select(p => p.Id).ToListAsync();
                notifications = await _context.Notifications.Where(n => (n.AppointmentId != null && patientAppointmentIds.Contains(n.AppointmentId.Value)) || (n.PrescriptionId != null && patientPrescriptionIds.Contains(n.PrescriptionId.Value))).ToListAsync();
            }
            else if (doctor != null)
            {
                var doctorAppointmentIds = await _context.Appointments.Where(a => a.DoctorId == doctor.Id).Select(a => a.Id).ToListAsync();
                var doctorPrescriptionIds = await _context.Prescriptions.Where(p => p.DoctorId == doctor.Id).Select(p => p.Id).ToListAsync();
                notifications = await _context.Notifications.Where(n => (n.AppointmentId != null && doctorAppointmentIds.Contains(n.AppointmentId.Value)) || (n.PrescriptionId != null && doctorPrescriptionIds.Contains(n.PrescriptionId.Value))).ToListAsync();
            }
            // Combine and remove duplicates
            var allNotifications = userNotifications.Concat(notifications).Distinct().OrderByDescending(n => n.CreatedAt).ToList();
            return Ok(allNotifications);
        }

        [HttpPost("broadcast")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> BroadcastNotification([FromBody] BroadcastNotificationModel model)
        {
            List<string> userIds;
            if (!string.IsNullOrWhiteSpace(model.Role))
            {
                var roleId = await _context.Roles.Where(r => r.Name == model.Role).Select(r => r.Id).FirstOrDefaultAsync();
                if (string.IsNullOrEmpty(roleId)) return BadRequest("Role not found");
                userIds = await _context.UserRoles.Where(ur => ur.RoleId == roleId).Select(ur => ur.UserId).ToListAsync();
            }
            else
            {
                userIds = await _context.Users.Select(u => u.Id).ToListAsync();
            }
            var notifications = userIds.Select(uid => new Notification
            {
                UserId = uid,
                Message = model.Message,
                CreatedAt = DateTime.UtcNow
            }).ToList();
            _context.Notifications.AddRange(notifications);
            await _context.SaveChangesAsync();
            return Ok(new { message = $"Notification sent to {(model.Role ?? "all users")}." });
        }

        public class BroadcastNotificationModel
        {
            public string? Role { get; set; } // null = all users, or "Doctor", "Patient", "Admin"
            public string Message { get; set; } = null!;
        }
    }
} 