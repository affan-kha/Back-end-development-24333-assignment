using HospitalAppointmentSystem.Data;
using HospitalAppointmentSystem.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using HospitalAppointmentSystem.Services;

namespace HospitalAppointmentSystem.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class AppointmentController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly RabbitMqService _rabbitMqService;

        public AppointmentController(ApplicationDbContext context, RabbitMqService rabbitMqService)
        {
            _context = context;
            _rabbitMqService = rabbitMqService;
        }

        [HttpPost("book")]
        [Authorize(Roles = "Patient")]
        public async Task<IActionResult> BookAppointment([FromBody] BookAppointmentModel model)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var patient = await _context.Patients.FirstOrDefaultAsync(p => p.ApplicationUserId == userId);
            if (patient == null) return BadRequest("Patient not found");
            var doctor = await _context.Doctors.FindAsync(model.DoctorId);
            if (doctor == null) return BadRequest("Doctor not found");
            // Enforce doctor's schedule
            if (!string.IsNullOrWhiteSpace(doctor.Schedule))
            {
                try
                {
                    var schedule = System.Text.Json.JsonDocument.Parse(doctor.Schedule).RootElement;
                    var apptDate = model.AppointmentDateTime.Date;
                    var apptDay = model.AppointmentDateTime.DayOfWeek.ToString();
                    var apptTime = model.AppointmentDateTime.ToString("HH:mm");
                    // Days off
                    if (schedule.TryGetProperty("daysOff", out var daysOff) && daysOff.ValueKind == System.Text.Json.JsonValueKind.Array)
                    {
                        foreach (var d in daysOff.EnumerateArray())
                        {
                            if (d.GetString()?.Equals(apptDay, StringComparison.OrdinalIgnoreCase) == true)
                                return BadRequest($"Doctor is not available on {apptDay}");
                        }
                    }
                    // Vacations
                    if (schedule.TryGetProperty("vacations", out var vacations) && vacations.ValueKind == System.Text.Json.JsonValueKind.Array)
                    {
                        foreach (var v in vacations.EnumerateArray())
                        {
                            if (DateTime.TryParse(v.GetString(), out var vacDate) && vacDate.Date == apptDate)
                                return BadRequest("Doctor is on vacation this day");
                        }
                    }
                    // Holidays
                    if (schedule.TryGetProperty("holidays", out var holidays) && holidays.ValueKind == System.Text.Json.JsonValueKind.Array)
                    {
                        foreach (var h in holidays.EnumerateArray())
                        {
                            if (DateTime.TryParse(h.GetString(), out var holDate) && holDate.Date == apptDate)
                                return BadRequest("Doctor is on holiday this day");
                        }
                    }
                    // Working hours
                    string? start = null, end = null;
                    if (schedule.TryGetProperty("workingHoursStart", out var whStart)) start = whStart.GetString();
                    if (schedule.TryGetProperty("workingHoursEnd", out var whEnd)) end = whEnd.GetString();
                    if (!string.IsNullOrWhiteSpace(start) && !string.IsNullOrWhiteSpace(end))
                    {
                        if (string.Compare(apptTime, start) < 0 || string.Compare(apptTime, end) >= 0)
                            return BadRequest($"Doctor is only available between {start} and {end}");
                    }
                }
                catch { /* ignore schedule parse errors, allow booking */ }
            }
            // Prevent double booking
            var conflict = await _context.Appointments.AnyAsync(a => a.DoctorId == doctor.Id && a.AppointmentDateTime == model.AppointmentDateTime && a.Status != "Cancelled");
            if (conflict) return BadRequest("This slot is already booked.");
            // Prevent booking in unavailable slots
            var blocked = await _context.DoctorUnavailableSlots.AnyAsync(s => s.DoctorId == doctor.Id && s.Date == model.AppointmentDateTime.Date && string.Compare(s.StartTime, model.AppointmentDateTime.ToString("HH:mm")) <= 0 && string.Compare(s.EndTime, model.AppointmentDateTime.ToString("HH:mm")) > 0);
            if (blocked) return BadRequest("This slot is unavailable (doctor not available).");
            string? videoLink = null;
            if (model.IsTelehealth)
            {
                // Generate a secure random video link (placeholder: Jitsi)
                var room = Guid.NewGuid().ToString("N");
                videoLink = $"https://meet.jit.si/{room}";
            }
            var appointment = new Appointment
            {
                PatientId = patient.Id,
                DoctorId = doctor.Id,
                AppointmentDateTime = model.AppointmentDateTime,
                Status = "Pending",
                ReferenceNumber = Guid.NewGuid().ToString().Substring(0, 8),
                IsTelehealth = model.IsTelehealth,
                VideoLink = videoLink
            };
            _context.Appointments.Add(appointment);
            await _context.SaveChangesAsync();
            // Save notification for patient/doctor
            _context.Notifications.Add(new Notification {
                AppointmentId = appointment.Id,
                Message = $"Appointment booked: {appointment.ReferenceNumber} for patient {patient.Id} with doctor {doctor.Id} at {appointment.AppointmentDateTime}",
                CreatedAt = DateTime.UtcNow
            });
            // Save notification for all admins
            var adminUserIds = await _context.Admins.Select(a => a.ApplicationUserId).ToListAsync();
            foreach (var adminUserId in adminUserIds)
            {
                _context.Notifications.Add(new Notification {
                    UserId = adminUserId,
                    Message = $"New appointment booked: {appointment.ReferenceNumber} (Patient: {patient.Id}, Doctor: {doctor.Id}, Date: {appointment.AppointmentDateTime:yyyy-MM-dd HH:mm})",
                    CreatedAt = DateTime.UtcNow
                });
            }
            await _context.SaveChangesAsync();
            // Publish notification (RabbitMQ)
            _rabbitMqService.Publish($"Appointment booked: {appointment.ReferenceNumber} for patient {patient.Id} with doctor {doctor.Id} at {appointment.AppointmentDateTime}");
            return Ok(new { appointment.Id, appointment.ReferenceNumber, appointment.IsTelehealth, appointment.VideoLink });
        }

        [HttpGet("my")]
        public async Task<IActionResult> MyAppointments()
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var patient = await _context.Patients.FirstOrDefaultAsync(p => p.ApplicationUserId == userId);
            var doctor = await _context.Doctors.FirstOrDefaultAsync(d => d.ApplicationUserId == userId);
            if (patient != null)
            {
                var appointments = await _context.Appointments
                    .Include(a => a.Doctor).ThenInclude(d => d.ApplicationUser)
                    .Where(a => a.PatientId == patient.Id)
                    .Select(a => new {
                        a.Id,
                        a.AppointmentDateTime,
                        a.Status,
                        a.ReferenceNumber,
                        a.DoctorId,
                        PatientId = a.PatientId,
                        DoctorEmail = a.Doctor != null ? a.Doctor.ApplicationUser.Email : null
                    })
                    .ToListAsync();
                return Ok(appointments);
            }
            if (doctor != null)
            {
                var appointments = await _context.Appointments
                    .Include(a => a.Patient).ThenInclude(p => p.ApplicationUser)
                    .Where(a => a.DoctorId == doctor.Id)
                    .Select(a => new {
                        a.Id,
                        a.AppointmentDateTime,
                        a.Status,
                        a.ReferenceNumber,
                        a.DoctorId,
                        a.PatientId,
                        PatientEmail = a.Patient != null ? a.Patient.ApplicationUser.Email : null
                    })
                    .ToListAsync();
                return Ok(appointments);
            }
            return Unauthorized();
        }

        [HttpPost("cancel/{id}")]
        [Authorize(Roles = "Patient")]
        public async Task<IActionResult> Cancel(int id)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var patient = await _context.Patients.FirstOrDefaultAsync(p => p.ApplicationUserId == userId);
            if (patient == null) return Unauthorized();
            var appointment = await _context.Appointments.FirstOrDefaultAsync(a => a.Id == id && a.PatientId == patient.Id);
            if (appointment == null) return NotFound();
            if ((appointment.AppointmentDateTime - DateTime.UtcNow).TotalHours < 48)
                return BadRequest("Cannot cancel within 48 hours of appointment.");
            appointment.Status = "Cancelled";
            await _context.SaveChangesAsync();
            // Save notification
            _context.Notifications.Add(new Notification {
                AppointmentId = appointment.Id,
                Message = $"Appointment cancelled: {appointment.ReferenceNumber} by patient {patient.Id}",
                CreatedAt = DateTime.UtcNow
            });
            await _context.SaveChangesAsync();
            // Publish notification
            _rabbitMqService.Publish($"Appointment cancelled: {appointment.ReferenceNumber} by patient {patient.Id}");
            return Ok();
        }

        [HttpPost("reschedule/{id}")]
        [Authorize(Roles = "Patient")]
        public async Task<IActionResult> Reschedule(int id, [FromBody] RescheduleAppointmentModel model)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var patient = await _context.Patients.Include(p => p.ApplicationUser).FirstOrDefaultAsync(p => p.ApplicationUserId == userId);
            if (patient == null) return Unauthorized();
            var appointment = await _context.Appointments.Include(a => a.Doctor).ThenInclude(d => d.ApplicationUser).FirstOrDefaultAsync(a => a.Id == id && a.PatientId == patient.Id);
            if (appointment == null) return NotFound();
            if ((appointment.AppointmentDateTime - DateTime.UtcNow).TotalHours < 48)
                return BadRequest("Cannot reschedule within 48 hours of appointment.");
            // Prevent double booking
            var conflict = await _context.Appointments.AnyAsync(a => a.DoctorId == appointment.DoctorId && a.AppointmentDateTime == model.NewDateTime && a.Status != "Cancelled");
            if (conflict) return BadRequest("This slot is already booked.");
            appointment.AppointmentDateTime = model.NewDateTime;
            appointment.Status = "Pending";
            await _context.SaveChangesAsync();
            // Save notification
            _context.Notifications.Add(new Notification {
                AppointmentId = appointment.Id,
                Message = $"Appointment rescheduled: {appointment.ReferenceNumber} by patient {patient.Id} to {appointment.AppointmentDateTime}",
                CreatedAt = DateTime.UtcNow
            });
            await _context.SaveChangesAsync();
            // Publish notification
            _rabbitMqService.Publish($"Appointment rescheduled: {appointment.ReferenceNumber} by patient {patient.Id} to {appointment.AppointmentDateTime}");
            // Send email to doctor and patient
            try {
                var emailService = (EmailService)HttpContext.RequestServices.GetService(typeof(EmailService));
                var doctorEmail = appointment.Doctor?.ApplicationUser?.Email;
                var patientEmail = patient.ApplicationUser.Email;
                var subject = "Appointment Rescheduled";
                var body = $"Your appointment (Ref: {appointment.ReferenceNumber}) has been rescheduled to {appointment.AppointmentDateTime}.";
                if (!string.IsNullOrWhiteSpace(doctorEmail)) await emailService.SendEmailAsync(doctorEmail, subject, body);
                if (!string.IsNullOrWhiteSpace(patientEmail)) await emailService.SendEmailAsync(patientEmail, subject, body);
            } catch {}
            return Ok();
        }

        [HttpPost("confirm/{id}")]
        [Authorize(Roles = "Patient")]
        public async Task<IActionResult> Confirm(int id)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var patient = await _context.Patients.FirstOrDefaultAsync(p => p.ApplicationUserId == userId);
            if (patient == null) return Unauthorized();
            var appointment = await _context.Appointments.FirstOrDefaultAsync(a => a.Id == id && a.PatientId == patient.Id);
            if (appointment == null) return NotFound();
            if (appointment.Status != "Scheduled") return BadRequest("Only scheduled appointments can be confirmed.");
            appointment.Status = "Confirmed";
            await _context.SaveChangesAsync();
            // Save notification
            _context.Notifications.Add(new Notification {
                AppointmentId = appointment.Id,
                Message = $"Appointment confirmed: {appointment.ReferenceNumber} by patient {patient.Id}",
                CreatedAt = DateTime.UtcNow
            });
            await _context.SaveChangesAsync();
            // Publish notification
            _rabbitMqService.Publish($"Appointment confirmed: {appointment.ReferenceNumber} by patient {patient.Id}");
            return Ok();
        }

        [HttpGet("filter")]
        public async Task<IActionResult> Filter([FromQuery] DateTime? date, [FromQuery] int? doctorId, [FromQuery] string? status)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var patient = await _context.Patients.FirstOrDefaultAsync(p => p.ApplicationUserId == userId);
            var doctor = await _context.Doctors.FirstOrDefaultAsync(d => d.ApplicationUserId == userId);
            IQueryable<Appointment> query = _context.Appointments.Include(a => a.Doctor).Include(a => a.Patient);
            if (patient != null)
                query = query.Where(a => a.PatientId == patient.Id);
            if (doctor != null)
                query = query.Where(a => a.DoctorId == doctor.Id);
            if (date.HasValue)
                query = query.Where(a => a.AppointmentDateTime.Date == date.Value.Date);
            if (doctorId.HasValue)
                query = query.Where(a => a.DoctorId == doctorId.Value);
            if (!string.IsNullOrWhiteSpace(status))
                query = query.Where(a => a.Status == status);
            var results = await query.Select(a => new {
                a.Id,
                a.AppointmentDateTime,
                a.Status,
                a.ReferenceNumber,
                Doctor = a.Doctor.ApplicationUser.Email,
                Patient = a.Patient.ApplicationUser.Email
            }).ToListAsync();
            return Ok(results);
        }
    }

    public class BookAppointmentModel
    {
        public int DoctorId { get; set; }
        public DateTime AppointmentDateTime { get; set; }
        public bool IsTelehealth { get; set; } = false;
    }
    public class RescheduleAppointmentModel
    {
        public DateTime NewDateTime { get; set; }
    }
} 