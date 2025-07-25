using HospitalAppointmentSystem.Data;
using HospitalAppointmentSystem.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace HospitalAppointmentSystem.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize(Roles = "Patient")]
    public class PatientController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;

        public PatientController(ApplicationDbContext context, UserManager<ApplicationUser> userManager)
        {
            _context = context;
            _userManager = userManager;
        }

        [HttpGet("profile")]
        public async Task<IActionResult> GetProfile()
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var patient = await _context.Patients.Include(p => p.ApplicationUser)
                .FirstOrDefaultAsync(p => p.ApplicationUserId == userId);
            if (patient == null) return NotFound();
            return Ok(new
            {
                patient.ApplicationUser.Email,
                patient.ApplicationUser.FullName,
                patient.ApplicationUser.ContactInfo,
                patient.MedicalHistory,
                patient.PendingMedicalHistory,
                patient.MedicalHistoryApprovalStatus
            });
        }

        [HttpPut("profile")]
        public async Task<IActionResult> UpdateProfile([FromBody] UpdatePatientProfileModel model)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var patient = await _context.Patients.Include(p => p.ApplicationUser)
                .FirstOrDefaultAsync(p => p.ApplicationUserId == userId);
            if (patient == null) return NotFound();
            // For medical history, require approval
            if (model.MedicalHistory != null && model.MedicalHistory != patient.MedicalHistory)
            {
                patient.PendingMedicalHistory = model.MedicalHistory;
                patient.MedicalHistoryApprovalStatus = "Pending";
            }
            // Directly update FullName and ContactInfo
            if (model.FullName != null)
                patient.ApplicationUser.FullName = model.FullName;
            if (model.ContactInfo != null)
                patient.ApplicationUser.ContactInfo = model.ContactInfo;
            await _context.SaveChangesAsync();
            return Ok(new { message = "Profile update submitted for approval." });
        }

        // Admin/Doctor endpoints (should be in a separate controller, but added here for brevity)
        [HttpPost("approve-medical-history/{patientId}")]
        [Authorize(Roles = "Admin,Doctor")]
        public async Task<IActionResult> ApproveMedicalHistory(int patientId)
        {
            var patient = await _context.Patients.FindAsync(patientId);
            if (patient == null) return NotFound();
            if (string.IsNullOrEmpty(patient.PendingMedicalHistory)) return BadRequest("No pending change.");
            patient.MedicalHistory = patient.PendingMedicalHistory;
            patient.PendingMedicalHistory = null;
            patient.MedicalHistoryApprovalStatus = "Approved";
            await _context.SaveChangesAsync();
            // Optionally: notify patient
            return Ok(new { message = "Medical history approved." });
        }

        [HttpPost("reject-medical-history/{patientId}")]
        [Authorize(Roles = "Admin,Doctor")]
        public async Task<IActionResult> RejectMedicalHistory(int patientId, [FromBody] RejectReasonModel model)
        {
            var patient = await _context.Patients.FindAsync(patientId);
            if (patient == null) return NotFound();
            if (string.IsNullOrEmpty(patient.PendingMedicalHistory)) return BadRequest("No pending change.");
            patient.MedicalHistoryApprovalStatus = "Rejected";
            // Optionally: store rejection reason somewhere
            await _context.SaveChangesAsync();
            // Optionally: notify patient
            return Ok(new { message = "Medical history change rejected.", reason = model.Reason });
        }

        [HttpGet("appointments")]
        public async Task<IActionResult> GetAppointments()
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var patient = await _context.Patients.FirstOrDefaultAsync(p => p.ApplicationUserId == userId);
            if (patient == null) return NotFound();
            var appointments = await _context.Appointments
                .Include(a => a.Doctor).ThenInclude(d => d.ApplicationUser)
                .Where(a => a.PatientId == patient.Id)
                .Select(a => new
                {
                    a.Id,
                    a.AppointmentDateTime,
                    a.Status,
                    DoctorName = a.Doctor.ApplicationUser.Email,
                    a.ReferenceNumber
                })
                .ToListAsync();
            return Ok(appointments);
        }

        [HttpPost("send-message")]
        public async Task<IActionResult> SendMessage([FromBody] SendMessageModel model)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var patient = await _context.Patients.FirstOrDefaultAsync(p => p.ApplicationUserId == userId);
            if (patient == null) return Unauthorized();
            var doctor = await _context.Doctors.FirstOrDefaultAsync(d => d.Id == model.DoctorId);
            if (doctor == null) return BadRequest("Doctor not found");
            var doctorUserId = doctor.ApplicationUserId;
            var message = new Message
            {
                SenderId = patient.ApplicationUserId,
                ReceiverId = doctorUserId,
                AppointmentId = model.AppointmentId,
                Content = model.Content,
                SentAt = DateTime.UtcNow
            };
            _context.Messages.Add(message);
            await _context.SaveChangesAsync();
            return Ok(new { message.Id, message.Content, message.SentAt });
        }

        [HttpGet("messages/{doctorId}")]
        public async Task<IActionResult> GetMessages(int doctorId, [FromQuery] int? appointmentId = null)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var patient = await _context.Patients.FirstOrDefaultAsync(p => p.ApplicationUserId == userId);
            if (patient == null) return Unauthorized();
            var doctor = await _context.Doctors.FirstOrDefaultAsync(d => d.Id == doctorId);
            if (doctor == null) return BadRequest("Doctor not found");
            var doctorUserId = doctor.ApplicationUserId;
            var messages = _context.Messages.Where(m =>
                (m.SenderId == patient.ApplicationUserId && m.ReceiverId == doctorUserId) ||
                (m.SenderId == doctorUserId && m.ReceiverId == patient.ApplicationUserId)
            );
            if (appointmentId.HasValue)
                messages = messages.Where(m => m.AppointmentId == appointmentId);
            var result = await messages.OrderBy(m => m.SentAt).Select(m => new {
                m.Id, m.Content, m.SentAt, m.SenderId, m.ReceiverId, m.AppointmentId, m.IsRead
            }).ToListAsync();
            return Ok(result);
        }

        [HttpGet("medical-info-requests")]
        public async Task<IActionResult> GetMyMedicalInfoRequests()
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var patient = await _context.Patients.FirstOrDefaultAsync(p => p.ApplicationUserId == userId);
            if (patient == null) return Unauthorized();
            var requests = await _context.MedicalInfoRequests
                .Where(r => r.PatientId == patient.Id)
                .OrderByDescending(r => r.CreatedAt)
                .ToListAsync();
            return Ok(requests);
        }

        [HttpPost("medical-info-requests/{id}/respond")]
        public async Task<IActionResult> RespondMedicalInfoRequest(int id, [FromBody] RespondMedicalInfoRequestModel model)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var patient = await _context.Patients.FirstOrDefaultAsync(p => p.ApplicationUserId == userId);
            if (patient == null) return Unauthorized();
            var req = await _context.MedicalInfoRequests.FirstOrDefaultAsync(r => r.Id == id && r.PatientId == patient.Id);
            if (req == null) return NotFound();
            if (req.Status != "Pending") return BadRequest("Already responded or cancelled.");
            req.ResponseText = model.ResponseText;
            req.AttachmentUrl = model.AttachmentUrl;
            req.Status = "Responded";
            req.RespondedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
            return Ok(new { req.Id, req.Status, req.RespondedAt });
        }

        [HttpPut("change-password")]
        public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordModel model)
        {
            if (string.IsNullOrWhiteSpace(model.CurrentPassword) || string.IsNullOrWhiteSpace(model.NewPassword))
                return BadRequest(new { message = "Current and new password are required." });
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var user = await _userManager.FindByIdAsync(userId);
            if (user == null) return Unauthorized();
            var result = await _userManager.ChangePasswordAsync(user, model.CurrentPassword, model.NewPassword);
            if (!result.Succeeded)
                return BadRequest(new { message = string.Join(", ", result.Errors.Select(e => e.Description)) });
            return Ok(new { message = "Password changed successfully." });
        }

        public class UpdatePatientProfileModel
        {
            public string? MedicalHistory { get; set; }
            public string? FullName { get; set; }
            public string? ContactInfo { get; set; }
        }
        public class RejectReasonModel
        {
            public string? Reason { get; set; }
        }

        public class SendMessageModel
        {
            public int DoctorId { get; set; }
            public int? AppointmentId { get; set; }
            public string Content { get; set; } = null!;
        }

        public class RespondMedicalInfoRequestModel
        {
            public string ResponseText { get; set; } = null!;
            public string? AttachmentUrl { get; set; }
        }

        public class ChangePasswordModel
        {
            public string CurrentPassword { get; set; } = null!;
            public string NewPassword { get; set; } = null!;
        }
    }
} 