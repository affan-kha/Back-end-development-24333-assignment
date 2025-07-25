using HospitalAppointmentSystem.Data;
using HospitalAppointmentSystem.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using CsvHelper;
using System.Globalization;
using System.IO;
using HospitalAppointmentSystem.Services;

namespace HospitalAppointmentSystem.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize(Roles = "Doctor")]
    public class DoctorController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly RabbitMqService _rabbitMqService;

        public DoctorController(ApplicationDbContext context, UserManager<ApplicationUser> userManager, RabbitMqService rabbitMqService)
        {
            _context = context;
            _userManager = userManager;
            _rabbitMqService = rabbitMqService;
        }

        [HttpGet("profile")]
        public async Task<IActionResult> GetProfile()
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var doctor = await _context.Doctors.Include(d => d.ApplicationUser)
                .FirstOrDefaultAsync(d => d.ApplicationUserId == userId);
            if (doctor == null) return NotFound();
            return Ok(new
            {
                id = doctor.Id, // Ensure doctor Id is returned
                doctor.ApplicationUser.Email,
                doctor.Specialization,
                doctor.Schedule
            });
        }

        [HttpPut("profile")]
        public async Task<IActionResult> UpdateProfile([FromBody] UpdateDoctorProfileModel model)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var doctor = await _context.Doctors.Include(d => d.ApplicationUser)
                .FirstOrDefaultAsync(d => d.ApplicationUserId == userId);
            if (doctor == null) return NotFound();
            doctor.Specialization = model.Specialization ?? doctor.Specialization;
            doctor.Schedule = model.Schedule ?? doctor.Schedule;
            await _context.SaveChangesAsync();
            return Ok();
        }

        [HttpGet("appointments")]
        public async Task<IActionResult> GetAppointments()
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var doctor = await _context.Doctors.FirstOrDefaultAsync(d => d.ApplicationUserId == userId);
            if (doctor == null) return NotFound();
            var appointments = await _context.Appointments
                .Include(a => a.Patient).ThenInclude(p => p.ApplicationUser)
                .Include(a => a.Feedbacks)
                .Where(a => a.DoctorId == doctor.Id)
                .Select(a => new
                {
                    a.Id,
                    a.AppointmentDateTime,
                    a.Status,
                    a.PatientId,
                    PatientName = a.Patient.ApplicationUser.Email,
                    a.ReferenceNumber,
                    Feedback = a.Feedbacks.Select(f => new { f.Rating, f.Comments }).FirstOrDefault()
                })
                .ToListAsync();
            return Ok(appointments);
        }

        [HttpPost("appointments/{id}/cancel")]
        public async Task<IActionResult> CancelAppointment(int id)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var doctor = await _context.Doctors.FirstOrDefaultAsync(d => d.ApplicationUserId == userId);
            if (doctor == null) return Unauthorized();
            var appointment = await _context.Appointments.FirstOrDefaultAsync(a => a.Id == id && a.DoctorId == doctor.Id);
            if (appointment == null) return NotFound();
            appointment.Status = "Cancelled";
            await _context.SaveChangesAsync();
            // Notify patient
            _context.Notifications.Add(new Notification {
                AppointmentId = appointment.Id,
                Message = $"Your appointment on {appointment.AppointmentDateTime:yyyy-MM-dd HH:mm} was cancelled by the doctor.",
                CreatedAt = DateTime.UtcNow
            });
            await _context.SaveChangesAsync();
            // Publish to RabbitMQ
            _rabbitMqService.Publish($"Appointment cancelled by doctor: {appointment.ReferenceNumber} for patient {appointment.PatientId} at {appointment.AppointmentDateTime}");
            return Ok(new { message = "Appointment cancelled and patient notified." });
        }

        [HttpPost("appointments/{id}/complete")]
        public async Task<IActionResult> CompleteAppointment(int id)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var doctor = await _context.Doctors.FirstOrDefaultAsync(d => d.ApplicationUserId == userId);
            if (doctor == null) return Unauthorized();
            var appointment = await _context.Appointments.FirstOrDefaultAsync(a => a.Id == id && a.DoctorId == doctor.Id);
            if (appointment == null) return NotFound();
            appointment.Status = "Completed";
            await _context.SaveChangesAsync();
            // Optionally: notify patient
            return Ok(new { message = "Appointment marked as completed." });
        }

        [HttpPost("appointments/{id}/reschedule")]
        public async Task<IActionResult> RescheduleAppointment(int id, [FromBody] DoctorRescheduleModel model)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var doctor = await _context.Doctors.FirstOrDefaultAsync(d => d.ApplicationUserId == userId);
            if (doctor == null) return Unauthorized();
            var appointment = await _context.Appointments.FirstOrDefaultAsync(a => a.Id == id && a.DoctorId == doctor.Id);
            if (appointment == null) return NotFound();
            // Prevent double booking
            var conflict = await _context.Appointments.AnyAsync(a => a.DoctorId == doctor.Id && a.AppointmentDateTime == model.NewDateTime && a.Status != "Cancelled" && a.Id != id);
            if (conflict) return BadRequest("This slot is already booked.");
            appointment.AppointmentDateTime = model.NewDateTime;
            appointment.Status = "Pending";
            await _context.SaveChangesAsync();
            // Notify patient
            _context.Notifications.Add(new Notification {
                AppointmentId = appointment.Id,
                Message = $"Your appointment was rescheduled by the doctor to {appointment.AppointmentDateTime:yyyy-MM-dd HH:mm}.",
                CreatedAt = DateTime.UtcNow
            });
            await _context.SaveChangesAsync();
            // Send email to patient and doctor
            try {
                var emailService = (EmailService)HttpContext.RequestServices.GetService(typeof(EmailService));
                var doctorEmail = doctor.ApplicationUser?.Email;
                var patient = await _context.Patients.Include(p => p.ApplicationUser).FirstOrDefaultAsync(p => p.Id == appointment.PatientId);
                var patientEmail = patient?.ApplicationUser?.Email;
                var subject = "Appointment Rescheduled";
                var body = $"Your appointment (Ref: {appointment.ReferenceNumber}) has been rescheduled by the doctor to {appointment.AppointmentDateTime}.";
                if (!string.IsNullOrWhiteSpace(doctorEmail)) await emailService.SendEmailAsync(doctorEmail, subject, body);
                if (!string.IsNullOrWhiteSpace(patientEmail)) await emailService.SendEmailAsync(patientEmail, subject, body);
            } catch {}
            // Publish to RabbitMQ
            _rabbitMqService.Publish($"Appointment rescheduled by doctor: {appointment.ReferenceNumber} for patient {appointment.PatientId} to {appointment.AppointmentDateTime}");
            return Ok(new { message = "Appointment rescheduled and patient notified." });
        }

        public class DoctorRescheduleModel
        {
            public DateTime NewDateTime { get; set; }
        }

        [HttpGet("search")]
        [AllowAnonymous]
        public async Task<IActionResult> Search([FromQuery] string? name, [FromQuery] string? specialization, [FromQuery] string? location)
        {
            var query = _context.Doctors.Include(d => d.ApplicationUser).AsQueryable();
            if (!string.IsNullOrWhiteSpace(name))
                query = query.Where(d => d.ApplicationUser.Email.Contains(name));
            if (!string.IsNullOrWhiteSpace(specialization))
                query = query.Where(d => d.Specialization.Contains(specialization));
            if (!string.IsNullOrWhiteSpace(location))
                query = query.Where(d => d.Location != null && d.Location.Contains(location));
            var results = await query.Select(d => new {
                d.Id,
                d.ApplicationUser.Email,
                d.Specialization,
                d.Schedule,
                d.Location
            }).ToListAsync();
            return Ok(results);
        }

        [HttpGet("{doctorId}/appointments")]
        [AllowAnonymous]
        public async Task<IActionResult> GetDoctorAppointmentsForDate(int doctorId, [FromQuery] DateTime date)
        {
            var appointments = await _context.Appointments
                .Where(a => a.DoctorId == doctorId && a.AppointmentDateTime.Date == date.Date && a.Status != "Cancelled")
                .Select(a => a.AppointmentDateTime)
                .ToListAsync();
            return Ok(appointments);
        }

        [HttpGet("patients")]
        public async Task<IActionResult> GetMyPatientsWithCompletedAppointments()
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var doctor = await _context.Doctors.FirstOrDefaultAsync(d => d.ApplicationUserId == userId);
            if (doctor == null) return Unauthorized();
            var patientIds = await _context.Appointments
                .Where(a => a.DoctorId == doctor.Id && a.Status == "Completed")
                .Select(a => a.PatientId)
                .Distinct()
                .ToListAsync();
            var patients = await _context.Patients
                .Where(p => patientIds.Contains(p.Id))
                .Include(p => p.ApplicationUser)
                .ToListAsync();
            return Ok(patients.Select(p => new { p.Id, Email = p.ApplicationUser.Email }));
        }

        [HttpGet("patients/{patientId}/appointments")]
        public async Task<IActionResult> GetCompletedAppointmentsForPatient(int patientId)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var doctor = await _context.Doctors.FirstOrDefaultAsync(d => d.ApplicationUserId == userId);
            if (doctor == null) return Unauthorized();
            var appointments = await _context.Appointments
                .Where(a => a.DoctorId == doctor.Id && a.PatientId == patientId && a.Status == "Completed")
                .Select(a => new {
                    a.Id,
                    a.AppointmentDateTime,
                    a.Status,
                    a.ReferenceNumber
                })
                .ToListAsync();
            return Ok(appointments);
        }

        [HttpGet("unavailable-slots")]
        public async Task<IActionResult> GetUnavailableSlots()
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var doctor = await _context.Doctors.FirstOrDefaultAsync(d => d.ApplicationUserId == userId);
            if (doctor == null) return Unauthorized();
            var slots = await _context.DoctorUnavailableSlots
                .Where(s => s.DoctorId == doctor.Id)
                .Select(s => new { s.Id, s.Date, s.StartTime, s.EndTime })
                .ToListAsync();
            return Ok(slots);
        }

        [HttpPost("unavailable-slots")]
        public async Task<IActionResult> AddUnavailableSlot([FromBody] AddUnavailableSlotModel model)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var doctor = await _context.Doctors.FirstOrDefaultAsync(d => d.ApplicationUserId == userId);
            if (doctor == null) return Unauthorized();
            var slot = new DoctorUnavailableSlot
            {
                DoctorId = doctor.Id,
                Date = model.Date.Date,
                StartTime = model.StartTime,
                EndTime = model.EndTime
            };
            _context.DoctorUnavailableSlots.Add(slot);
            await _context.SaveChangesAsync();
            return Ok(new { message = "Slot blocked.", slot });
        }

        [HttpDelete("unavailable-slots/{id}")]
        public async Task<IActionResult> DeleteUnavailableSlot(int id)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var doctor = await _context.Doctors.FirstOrDefaultAsync(d => d.ApplicationUserId == userId);
            if (doctor == null) return Unauthorized();
            var slot = await _context.DoctorUnavailableSlots.FirstOrDefaultAsync(s => s.Id == id && s.DoctorId == doctor.Id);
            if (slot == null) return NotFound();
            _context.DoctorUnavailableSlots.Remove(slot);
            await _context.SaveChangesAsync();
            return Ok(new { message = "Slot unblocked." });
        }

        public class AddUnavailableSlotModel
        {
            public DateTime Date { get; set; }
            public string StartTime { get; set; } = null!;
            public string EndTime { get; set; } = null!;
        }

        [HttpPost("send-message")]
        public async Task<IActionResult> SendMessage([FromBody] SendMessageModel model)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var doctor = await _context.Doctors.FirstOrDefaultAsync(d => d.ApplicationUserId == userId);
            if (doctor == null) return Unauthorized();
            var patient = await _context.Patients.FirstOrDefaultAsync(p => p.Id == model.PatientId);
            if (patient == null) return BadRequest("Patient not found");
            var patientUserId = patient.ApplicationUserId;
            var message = new Message
            {
                SenderId = doctor.ApplicationUserId,
                ReceiverId = patientUserId,
                AppointmentId = model.AppointmentId,
                Content = model.Content,
                SentAt = DateTime.UtcNow
            };
            _context.Messages.Add(message);
            await _context.SaveChangesAsync();
            return Ok(new { message.Id, message.Content, message.SentAt });
        }

        [HttpGet("messages/{patientId}")]
        public async Task<IActionResult> GetMessages(int patientId, [FromQuery] int? appointmentId = null)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var doctor = await _context.Doctors.FirstOrDefaultAsync(d => d.ApplicationUserId == userId);
            if (doctor == null) return Unauthorized();
            var patient = await _context.Patients.FirstOrDefaultAsync(p => p.Id == patientId);
            if (patient == null) return BadRequest("Patient not found");
            var patientUserId = patient.ApplicationUserId;
            var messages = _context.Messages.Where(m =>
                (m.SenderId == doctor.ApplicationUserId && m.ReceiverId == patientUserId) ||
                (m.SenderId == patientUserId && m.ReceiverId == doctor.ApplicationUserId)
            );
            if (appointmentId.HasValue)
                messages = messages.Where(m => m.AppointmentId == appointmentId);
            var result = await messages.OrderBy(m => m.SentAt).Select(m => new {
                m.Id, m.Content, m.SentAt, m.SenderId, m.ReceiverId, m.AppointmentId, m.IsRead
            }).ToListAsync();
            return Ok(result);
        }

        public class SendMessageModel
        {
            public int PatientId { get; set; }
            public int? AppointmentId { get; set; }
            public string Content { get; set; } = null!;
        }

        [HttpGet("profile-by-id/{doctorId}")]
        [AllowAnonymous]
        public async Task<IActionResult> GetProfileById(int doctorId)
        {
            var doctor = await _context.Doctors.Include(d => d.ApplicationUser).FirstOrDefaultAsync(d => d.Id == doctorId);
            if (doctor == null) return NotFound();
            return Ok(new { doctor.Id, doctor.ApplicationUserId, Email = doctor.ApplicationUser.Email });
        }

        [HttpPost("medical-info-request")]
        public async Task<IActionResult> CreateMedicalInfoRequest([FromBody] CreateMedicalInfoRequestModel model)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var doctor = await _context.Doctors.FirstOrDefaultAsync(d => d.ApplicationUserId == userId);
            if (doctor == null) return Unauthorized();
            var patient = await _context.Patients.FirstOrDefaultAsync(p => p.Id == model.PatientId);
            if (patient == null) return BadRequest("Patient not found");
            var req = new MedicalInfoRequest
            {
                DoctorId = doctor.Id,
                PatientId = patient.Id,
                RequestText = model.RequestText,
                Status = "Pending",
                CreatedAt = DateTime.UtcNow
            };
            _context.MedicalInfoRequests.Add(req);
            await _context.SaveChangesAsync();
            return Ok(new { req.Id, req.RequestText, req.Status, req.CreatedAt });
        }

        [HttpGet("medical-info-requests")]
        public async Task<IActionResult> GetAllMedicalInfoRequests()
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var doctor = await _context.Doctors.FirstOrDefaultAsync(d => d.ApplicationUserId == userId);
            if (doctor == null) return Unauthorized();
            var requests = await _context.MedicalInfoRequests
                .Where(r => r.DoctorId == doctor.Id)
                .OrderByDescending(r => r.CreatedAt)
                .ToListAsync();
            return Ok(requests);
        }

        [HttpGet("medical-info-requests/{patientId}")]
        public async Task<IActionResult> GetMedicalInfoRequestsForPatient(int patientId)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var doctor = await _context.Doctors.FirstOrDefaultAsync(d => d.ApplicationUserId == userId);
            if (doctor == null) return Unauthorized();
            var requests = await _context.MedicalInfoRequests
                .Where(r => r.DoctorId == doctor.Id && r.PatientId == patientId)
                .OrderByDescending(r => r.CreatedAt)
                .ToListAsync();
            return Ok(requests);
        }

        public class CreateMedicalInfoRequestModel
        {
            public int PatientId { get; set; }
            public string RequestText { get; set; } = null!;
        }

        [HttpGet("appointments/report")]
        public async Task<IActionResult> GetAppointmentReport([FromQuery] DateTime startDate, [FromQuery] DateTime endDate, [FromQuery] string format = "pdf")
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var doctor = await _context.Doctors.Include(d => d.ApplicationUser).FirstOrDefaultAsync(d => d.ApplicationUserId == userId);
            if (doctor == null) return Unauthorized();
            var appointments = await _context.Appointments
                .Include(a => a.Patient).ThenInclude(p => p.ApplicationUser)
                .Where(a => a.DoctorId == doctor.Id && a.AppointmentDateTime.Date >= startDate.Date && a.AppointmentDateTime.Date <= endDate.Date)
                .OrderBy(a => a.AppointmentDateTime)
                .ToListAsync();
            var reportRows = appointments.Select(a => new
            {
                Date = a.AppointmentDateTime.ToString("yyyy-MM-dd"),
                Time = a.AppointmentDateTime.ToString("HH:mm"),
                Patient = a.Patient.ApplicationUser.FullName ?? a.Patient.ApplicationUser.Email,
                Status = a.Status,
                Reference = a.ReferenceNumber
            }).ToList();
            if (format.ToLower() == "csv")
            {
                using var mem = new MemoryStream();
                using (var writer = new StreamWriter(mem, leaveOpen: true))
                using (var csv = new CsvWriter(writer, CultureInfo.InvariantCulture))
                {
                    csv.WriteRecords(reportRows);
                }
                mem.Position = 0;
                return File(mem.ToArray(), "text/csv", $"appointments_{startDate:yyyyMMdd}_{endDate:yyyyMMdd}.csv");
            }
            else // PDF
            {
                var pdfBytes = QuestPDF.Fluent.Document.Create(container =>
                {
                    container.Page(page =>
                    {
                        page.Margin(20);
                        page.Header().Text($"Appointment Report: {doctor.ApplicationUser.FullName ?? doctor.ApplicationUser.Email}").FontSize(18).Bold();
                        page.Content().Table(table =>
                        {
                            table.ColumnsDefinition(columns =>
                            {
                                columns.ConstantColumn(90); // Date
                                columns.ConstantColumn(60); // Time
                                columns.RelativeColumn();   // Patient
                                columns.ConstantColumn(80); // Status
                                columns.ConstantColumn(90); // Reference
                            });
                            table.Header(header =>
                            {
                                header.Cell().Text("Date").Bold();
                                header.Cell().Text("Time").Bold();
                                header.Cell().Text("Patient").Bold();
                                header.Cell().Text("Status").Bold();
                                header.Cell().Text("Reference").Bold();
                            });
                            foreach (var row in reportRows)
                            {
                                table.Cell().Text(row.Date);
                                table.Cell().Text(row.Time);
                                table.Cell().Text(row.Patient);
                                table.Cell().Text(row.Status);
                                table.Cell().Text(row.Reference);
                            }
                        });
                        page.Footer().AlignCenter().Text($"Generated: {DateTime.Now:yyyy-MM-dd HH:mm}").FontSize(10);
                    });
                }).GeneratePdf();
                return File(pdfBytes, "application/pdf", $"appointments_{startDate:yyyyMMdd}_{endDate:yyyyMMdd}.pdf");
            }
        }

        [HttpPost("appointments/{id}/scheduled")]
        public async Task<IActionResult> MarkScheduled(int id)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var doctor = await _context.Doctors.FirstOrDefaultAsync(d => d.ApplicationUserId == userId);
            if (doctor == null) return Unauthorized();
            var appointment = await _context.Appointments.FirstOrDefaultAsync(a => a.Id == id && a.DoctorId == doctor.Id);
            if (appointment == null) return NotFound();
            appointment.Status = "Scheduled";
            await _context.SaveChangesAsync();
            return Ok(new { message = "Appointment marked as scheduled." });
        }

        [HttpGet("schedule/{doctorId}")]
        [AllowAnonymous]
        public async Task<IActionResult> GetDoctorSchedulePublic(int doctorId)
        {
            var doctor = await _context.Doctors.FirstOrDefaultAsync(d => d.Id == doctorId);
            if (doctor == null) return NotFound();
            return Ok(new { doctor.Id, doctor.Schedule });
        }

        [HttpPut("change-password")]
        public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordModel model)
        {
            if (string.IsNullOrWhiteSpace(model.CurrentPassword) || string.IsNullOrWhiteSpace(model.NewPassword))
                return BadRequest(new { message = "Current and new password are required." });
            var userId = User.FindFirstValue(System.Security.Claims.ClaimTypes.NameIdentifier);
            var user = await _userManager.FindByIdAsync(userId);
            if (user == null) return Unauthorized();
            var result = await _userManager.ChangePasswordAsync(user, model.CurrentPassword, model.NewPassword);
            if (!result.Succeeded)
                return BadRequest(new { message = string.Join(", ", result.Errors.Select(e => e.Description)) });
            return Ok(new { message = "Password changed successfully." });
        }
        public class ChangePasswordModel
        {
            public string CurrentPassword { get; set; } = null!;
            public string NewPassword { get; set; } = null!;
        }
    }

    public class UpdateDoctorProfileModel
    {
        public string? Specialization { get; set; }
        public string? Schedule { get; set; }
    }
} 