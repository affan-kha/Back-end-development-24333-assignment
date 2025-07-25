using HospitalAppointmentSystem.Data;
using HospitalAppointmentSystem.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using HospitalAppointmentSystem.Services;
using CsvHelper;
using System.Globalization;
using QuestPDF.Fluent;
using System.IO;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace HospitalAppointmentSystem.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize(Roles = "Admin")]
    public class AdminController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly RabbitMqService _rabbitMqService;

        public AdminController(ApplicationDbContext context, UserManager<ApplicationUser> userManager, RabbitMqService rabbitMqService)
        {
            _context = context;
            _userManager = userManager;
            _rabbitMqService = rabbitMqService;
        }

        [HttpGet("users")]
        public async Task<IActionResult> GetUsers([FromQuery] string? email, [FromQuery] string? name, [FromQuery] string? role, [FromQuery] bool? isActive)
        {
            var usersQuery = _context.Users.AsQueryable();
            if (!string.IsNullOrWhiteSpace(email))
                usersQuery = usersQuery.Where(u => u.Email.Contains(email));
            if (!string.IsNullOrWhiteSpace(name))
                usersQuery = usersQuery.Where(u => u.UserName.Contains(name));
            if (isActive.HasValue)
                usersQuery = usersQuery.Where(u => u.IsActive == isActive.Value);

            // Role filtering at DB level
            if (!string.IsNullOrWhiteSpace(role))
            {
                var roleId = await _context.Roles
                    .Where(r => r.Name == role)
                    .Select(r => r.Id)
                    .FirstOrDefaultAsync();
                if (!string.IsNullOrEmpty(roleId))
                {
                    var userIdsWithRole = await _context.UserRoles
                        .Where(ur => ur.RoleId == roleId)
                        .Select(ur => ur.UserId)
                        .ToListAsync();
                    usersQuery = usersQuery.Where(u => userIdsWithRole.Contains(u.Id));
                }
                else
                {
                    // No users if role doesn't exist
                    return Ok(new List<object>());
                }
            }

            var users = await usersQuery.ToListAsync();
            var userList = new List<object>();
            foreach (var u in users)
            {
                var roles = await _userManager.GetRolesAsync(u);
                userList.Add(new { u.Id, u.Email, name = u.UserName, role = roles.FirstOrDefault() ?? "", isActive = u.IsActive });
            }
            return Ok(userList);
        }

        [HttpPost("users")]
        public async Task<IActionResult> CreateUser([FromBody] CreateUserModel model)
        {
            // Check for duplicate email
            var existing = await _userManager.FindByEmailAsync(model.Email);
            if (existing != null)
                return BadRequest(new { message = "Email already exists" });
            var user = new ApplicationUser { UserName = model.Email, Email = model.Email, EmailConfirmed = true, IsActive = true };
            var result = await _userManager.CreateAsync(user, model.Password);
            if (!result.Succeeded)
                return BadRequest(new { message = string.Join(", ", result.Errors.Select(e => e.Description)) });
            await _userManager.AddToRoleAsync(user, model.Role);
            // Add to Patients/Doctors table if needed
            if (model.Role == "Patient")
            {
                _context.Patients.Add(new Patient { ApplicationUserId = user.Id });
                await _context.SaveChangesAsync();
            }
            else if (model.Role == "Doctor")
            {
                _context.Doctors.Add(new Doctor { ApplicationUserId = user.Id, Specialization = model.Specialization ?? "General", Schedule = "{}" });
                await _context.SaveChangesAsync();
            }
            else if (model.Role == "Admin")
            {
                _context.Admins.Add(new Admin { ApplicationUserId = user.Id });
                await _context.SaveChangesAsync();
            }
            return Ok(new { message = "User created successfully" });
        }

        public class CreateUserModel
        {
            public string Email { get; set; } = null!;
            public string Password { get; set; } = null!;
            public string Role { get; set; } = null!;
            public string? Specialization { get; set; } // for doctor
        }

        [HttpPut("users/{id}/name")]
        public async Task<IActionResult> UpdateUserName(string id, [FromBody] string newName)
        {
            var user = await _userManager.FindByIdAsync(id);
            if (user == null) return NotFound();
            user.UserName = newName;
            await _userManager.UpdateAsync(user);
            return Ok(new { message = "Name updated" });
        }

        [HttpPut("users/{id}/role")]
        public async Task<IActionResult> UpdateUserRole(string id, [FromBody] string newRole)
        {
            var user = await _userManager.FindByIdAsync(id);
            if (user == null) return NotFound();
            var currentRoles = await _userManager.GetRolesAsync(user);
            await _userManager.RemoveFromRolesAsync(user, currentRoles);
            await _userManager.AddToRoleAsync(user, newRole);
            return Ok(new { message = "Role updated" });
        }

        [HttpPut("users/{id}/deactivate")]
        public async Task<IActionResult> DeactivateUser(string id)
        {
            var user = await _userManager.FindByIdAsync(id);
            if (user == null) return NotFound();
            user.IsActive = false;
            await _userManager.UpdateAsync(user);
            return Ok(new { message = "User deactivated" });
        }

        [HttpPut("users/{id}/activate")]
        public async Task<IActionResult> ActivateUser(string id)
        {
            var user = await _userManager.FindByIdAsync(id);
            if (user == null) return NotFound();
            user.IsActive = true;
            await _userManager.UpdateAsync(user);
            return Ok(new { message = "User activated" });
        }

        [HttpDelete("users/{id}")]
        public async Task<IActionResult> DeleteUser(string id)
        {
            var user = await _userManager.FindByIdAsync(id);
            if (user == null) return NotFound();
            await _userManager.DeleteAsync(user);
            return Ok(new { message = "User deleted" });
        }

        [HttpGet("doctors")]
        public async Task<IActionResult> GetDoctors()
        {
            var doctors = await _context.Doctors.Include(d => d.ApplicationUser).ToListAsync();
            return Ok(doctors.Select(d => new { d.Id, d.ApplicationUser.Email, d.Specialization, d.Schedule }));
        }

        [HttpGet("patients")]
        public async Task<IActionResult> GetPatients()
        {
            var patients = await _context.Patients.Include(p => p.ApplicationUser).ToListAsync();
            return Ok(patients.Select(p => new { p.Id, p.ApplicationUser.Email, p.MedicalHistory }));
        }

        [HttpGet("appointments")]
        public async Task<IActionResult> GetAppointments([FromQuery] string? doctor, [FromQuery] string? patient, [FromQuery] DateTime? date, [FromQuery] string? status, [FromQuery] int? doctorId, [FromQuery] int? patientId)
        {
            var query = _context.Appointments
                .Include(a => a.Patient).ThenInclude(p => p.ApplicationUser)
                .Include(a => a.Doctor).ThenInclude(d => d.ApplicationUser)
                .AsQueryable();
            if (!string.IsNullOrWhiteSpace(doctor))
                query = query.Where(a => a.Doctor.ApplicationUser.Email.Contains(doctor));
            if (!string.IsNullOrWhiteSpace(patient))
                query = query.Where(a => a.Patient.ApplicationUser.Email.Contains(patient));
            if (date.HasValue)
                query = query.Where(a => a.AppointmentDateTime.Date == date.Value.Date);
            if (!string.IsNullOrWhiteSpace(status))
                query = query.Where(a => a.Status == status);
            if (doctorId.HasValue)
                query = query.Where(a => a.DoctorId == doctorId.Value);
            if (patientId.HasValue)
                query = query.Where(a => a.PatientId == patientId.Value);
            var appointments = await query.ToListAsync();
            // Fetch cancel reasons from notifications for cancelled appointments
            var cancelledIds = appointments.Where(a => a.Status == "Cancelled").Select(a => a.Id).ToList();
            var cancelNotifs = await _context.Notifications.Where(n => n.AppointmentId != null && cancelledIds.Contains(n.AppointmentId.Value) && n.Message.Contains("cancelled")).ToListAsync();
            return Ok(appointments.Select(a => new
            {
                Id = a.Id,
                a.AppointmentDateTime,
                a.Status,
                PatientEmail = a.Patient.ApplicationUser.Email,
                DoctorEmail = a.Doctor.ApplicationUser.Email,
                a.ReferenceNumber,
                a.PatientId,
                a.DoctorId,
                justification = a.Status == "Cancelled" ? cancelNotifs.FirstOrDefault(n => n.AppointmentId == a.Id)?.Message : null
            }));
        }

        [HttpPost("cancel/{id}")]
        public async Task<IActionResult> CancelAppointment(int id, [FromBody] CancelOverrideModel? model)
        {
            var appointment = await _context.Appointments.Include(a => a.Patient).ThenInclude(p => p.ApplicationUser).Include(a => a.Doctor).ThenInclude(d => d.ApplicationUser).FirstOrDefaultAsync(a => a.Id == id);
            if (appointment == null) return NotFound();
            var isOverride = false;
            string? justification = model?.Justification;
            if ((appointment.AppointmentDateTime - DateTime.UtcNow).TotalHours < 48)
            {
                if (string.IsNullOrWhiteSpace(justification))
                return BadRequest("Justification required to override 48-hour rule.");
                isOverride = true;
            }
            appointment.Status = "Cancelled";
            await _context.SaveChangesAsync();
            // Log override and justification in notification
            var msg = isOverride
                ? $"Appointment cancelled by admin with override. Reason: {justification}"
                : "Appointment cancelled by admin.";
            _context.Notifications.Add(new Notification {
                AppointmentId = appointment.Id,
                Message = msg,
                CreatedAt = DateTime.UtcNow
            });
            await _context.SaveChangesAsync();
            // Optionally: send email to patient and doctor
            try {
                var emailService = (EmailService)HttpContext.RequestServices.GetService(typeof(EmailService));
                var doctorEmail = appointment.Doctor?.ApplicationUser?.Email;
                var patientEmail = appointment.Patient?.ApplicationUser?.Email;
                var subject = "Appointment Cancelled";
                var body = isOverride
                    ? $"Your appointment (Ref: {appointment.ReferenceNumber}) was cancelled by admin with override. Reason: {justification}"
                    : $"Your appointment (Ref: {appointment.ReferenceNumber}) was cancelled by admin.";
                if (!string.IsNullOrWhiteSpace(doctorEmail)) await emailService.SendEmailAsync(doctorEmail, subject, body);
                if (!string.IsNullOrWhiteSpace(patientEmail)) await emailService.SendEmailAsync(patientEmail, subject, body);
            } catch {}
            // Publish to RabbitMQ
            _rabbitMqService.Publish($"Appointment cancelled by admin: {appointment.ReferenceNumber} for patient {appointment.PatientId} with doctor {appointment.DoctorId} at {appointment.AppointmentDateTime}");
            return Ok(new { message = "Appointment cancelled" + (isOverride ? " with override." : ".") });
        }

        [HttpPut("appointments/{id}/status")]
        public async Task<IActionResult> UpdateAppointmentStatus(int id, [FromBody] UpdateStatusModel model)
        {
            var appointment = await _context.Appointments.FindAsync(id);
            if (appointment == null) return NotFound();
            appointment.Status = model.Status;
            await _context.SaveChangesAsync();
            return Ok(new { message = "Appointment status updated." });
        }
        public class UpdateStatusModel
        {
            public string Status { get; set; } = null!;
        }

        [HttpPut("appointments/{id}/reschedule")]
        public async Task<IActionResult> AdminRescheduleAppointment(int id, [FromBody] AdminRescheduleModel model)
        {
            var appointment = await _context.Appointments.Include(a => a.Patient).ThenInclude(p => p.ApplicationUser).Include(a => a.Doctor).ThenInclude(d => d.ApplicationUser).FirstOrDefaultAsync(a => a.Id == id);
            if (appointment == null) return NotFound();
            // Prevent double booking
            var conflict = await _context.Appointments.AnyAsync(a => a.DoctorId == appointment.DoctorId && a.AppointmentDateTime == model.NewDateTime && a.Status != "Cancelled" && a.Id != id);
            if (conflict) return BadRequest("This slot is already booked.");
            appointment.AppointmentDateTime = model.NewDateTime;
            appointment.Status = "Pending";
            await _context.SaveChangesAsync();
            // Log notification
            _context.Notifications.Add(new Notification {
                AppointmentId = appointment.Id,
                Message = $"Appointment rescheduled by admin to {appointment.AppointmentDateTime}",
                CreatedAt = DateTime.UtcNow
            });
            await _context.SaveChangesAsync();
            // Send email to doctor and patient
            try {
                var emailService = (EmailService)HttpContext.RequestServices.GetService(typeof(EmailService));
                var doctorEmail = appointment.Doctor?.ApplicationUser?.Email;
                var patientEmail = appointment.Patient?.ApplicationUser?.Email;
                var subject = "Appointment Rescheduled";
                var body = $"Your appointment (Ref: {appointment.ReferenceNumber}) has been rescheduled by admin to {appointment.AppointmentDateTime}.";
                if (!string.IsNullOrWhiteSpace(doctorEmail)) await emailService.SendEmailAsync(doctorEmail, subject, body);
                if (!string.IsNullOrWhiteSpace(patientEmail)) await emailService.SendEmailAsync(patientEmail, subject, body);
            } catch {}
            // Publish to RabbitMQ
            _rabbitMqService.Publish($"Appointment rescheduled by admin: {appointment.ReferenceNumber} for patient {appointment.PatientId} with doctor {appointment.DoctorId} to {appointment.AppointmentDateTime}");
            return Ok(new { message = "Appointment rescheduled by admin." });
        }
        public class AdminRescheduleModel
        {
            public DateTime NewDateTime { get; set; }
        }

        [HttpGet("doctor-schedule/{doctorId}")]
        public async Task<IActionResult> GetDoctorSchedule(int doctorId)
        {
            var doctor = await _context.Doctors.FirstOrDefaultAsync(d => d.Id == doctorId);
            if (doctor == null) return NotFound();
            return Ok(new { doctor.Id, doctor.Schedule });
        }

        [HttpPut("doctor-schedule/{doctorId}")]
        public async Task<IActionResult> UpdateDoctorSchedule(int doctorId, [FromBody] UpdateDoctorScheduleModel model)
        {
            var doctor = await _context.Doctors.FirstOrDefaultAsync(d => d.Id == doctorId);
            if (doctor == null) return NotFound();
            doctor.Schedule = model.ScheduleJson;
            await _context.SaveChangesAsync();
            return Ok(new { message = "Doctor schedule updated." });
        }
        public class UpdateDoctorScheduleModel
        {
            public string ScheduleJson { get; set; } = null!; 
        }

        private bool ValidateTokenFromQuery(string? token, out string? userId)
        {
            userId = null;
            if (string.IsNullOrWhiteSpace(token)) return false;
            var jwtSettings = new JwtSecurityTokenHandler();
            try
            {
                var config = HttpContext.RequestServices.GetService(typeof(IConfiguration)) as IConfiguration;
                var key = config["JwtSettings:SecretKey"];
                var validationParameters = new TokenValidationParameters
                {
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)),
                    ValidateIssuer = false,
                    ValidateAudience = false,
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.Zero
                };
                var principal = jwtSettings.ValidateToken(token, validationParameters, out var validatedToken);
                userId = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                return true;
            }
            catch { return false; }
        }

        [HttpGet("export/appointments")]
        public async Task<IActionResult> ExportAppointments([FromQuery] string format = "csv", [FromQuery] string? token = null)
        {
            // If token is present in query, validate it
            if (!User.Identity.IsAuthenticated && !string.IsNullOrWhiteSpace(token))
            {
                if (!ValidateTokenFromQuery(token, out var userId))
                    return Unauthorized();
                // Optionally, check if user is admin
            }
            var appointments = await _context.Appointments
                .Include(a => a.Patient).ThenInclude(p => p.ApplicationUser)
                .Include(a => a.Doctor).ThenInclude(d => d.ApplicationUser)
                .OrderBy(a => a.AppointmentDateTime)
                .ToListAsync();
            var rows = appointments.Select(a => new AppointmentExportRow {
                Date = a.AppointmentDateTime.ToString("yyyy-MM-dd"),
                Time = a.AppointmentDateTime.ToString("HH:mm"),
                Patient = a.Patient.ApplicationUser.FullName ?? a.Patient.ApplicationUser.Email,
                Doctor = a.Doctor.ApplicationUser.FullName ?? a.Doctor.ApplicationUser.Email,
                Status = a.Status,
                Reference = a.ReferenceNumber ?? ""
            }).ToList();
            if (format.ToLower() == "csv")
            {
                using var mem = new MemoryStream();
                using (var writer = new StreamWriter(mem, leaveOpen: true))
                using (var csv = new CsvWriter(writer, CultureInfo.InvariantCulture))
                {
                    csv.WriteRecords(rows);
                }
                mem.Position = 0;
                await LogAction("ExportAppointments", $"Exported {rows.Count} appointments as CSV");
                return File(mem.ToArray(), "text/csv", $"appointments_{DateTime.Now:yyyyMMdd_HHmm}.csv");
            }
            else // PDF
            {
                var pdfBytes = Document.Create(container =>
                {
                    container.Page(page =>
                    {
                        page.Margin(20);
                        page.Header().Text($"All Appointments Report").FontSize(18).Bold();
                        page.Content().Table(table =>
                        {
                            table.ColumnsDefinition(columns =>
                            {
                                columns.ConstantColumn(90); // Date
                                columns.ConstantColumn(60); // Time
                                columns.RelativeColumn();   // Patient
                                columns.RelativeColumn();   // Doctor
                                columns.ConstantColumn(80); // Status
                                columns.ConstantColumn(90); // Reference
                            });
                            table.Header(header =>
                            {
                                header.Cell().Text("Date").Bold();
                                header.Cell().Text("Time").Bold();
                                header.Cell().Text("Patient").Bold();
                                header.Cell().Text("Doctor").Bold();
                                header.Cell().Text("Status").Bold();
                                header.Cell().Text("Reference").Bold();
                            });
                            foreach (var row in rows)
                            {
                                table.Cell().Text(row.Date);
                                table.Cell().Text(row.Time);
                                table.Cell().Text(row.Patient);
                                table.Cell().Text(row.Doctor);
                                table.Cell().Text(row.Status);
                                table.Cell().Text(row.Reference);
                            }
                        });
                        page.Footer().AlignCenter().Text($"Generated: {DateTime.Now:yyyy-MM-dd HH:mm}").FontSize(10);
                    });
                }).GeneratePdf();
                await LogAction("ExportAppointments", $"Exported {rows.Count} appointments as PDF");
                return File(pdfBytes, "application/pdf", $"appointments_{DateTime.Now:yyyyMMdd_HHmm}.pdf");
            }
        }

        [HttpGet("export/users")]
        public async Task<IActionResult> ExportUsers([FromQuery] string format = "csv", [FromQuery] string? token = null)
        {
            // If token is present in query, validate it
            if (!User.Identity.IsAuthenticated && !string.IsNullOrWhiteSpace(token))
            {
                if (!ValidateTokenFromQuery(token, out var userId))
                    return Unauthorized();
                // Optionally, check if user is admin
            }
            var users = await _context.Users.ToListAsync();
            var userList = new List<UserExportRow>();
            foreach (var u in users)
            {
                var roles = await _userManager.GetRolesAsync(u);
                userList.Add(new UserExportRow {
                    Email = u.Email ?? "",
                    Name = u.UserName ?? "",
                    Role = roles.FirstOrDefault() ?? "",
                    IsActive = u.IsActive ? "Active" : "Inactive"
                });
            }
            if (format.ToLower() == "csv")
            {
                using var mem = new MemoryStream();
                using (var writer = new StreamWriter(mem, leaveOpen: true))
                using (var csv = new CsvWriter(writer, CultureInfo.InvariantCulture))
                {
                    csv.WriteRecords(userList);
                }
                mem.Position = 0;
                await LogAction("ExportUsers", $"Exported {userList.Count} users as CSV");
                return File(mem.ToArray(), "text/csv", $"users_{DateTime.Now:yyyyMMdd_HHmm}.csv");
            }
            else // PDF
            {
                var pdfBytes = Document.Create(container =>
                {
                    container.Page(page =>
                    {
                        page.Margin(20);
                        page.Header().Text($"All Users Report").FontSize(18).Bold();
                        page.Content().Table(table =>
                        {
                            table.ColumnsDefinition(columns =>
                            {
                                columns.RelativeColumn();   // Email
                                columns.RelativeColumn();   // Name
                                columns.ConstantColumn(80); // Role
                                columns.ConstantColumn(80); // Status
                            });
                            table.Header(header =>
                            {
                                header.Cell().Text("Email").Bold();
                                header.Cell().Text("Name").Bold();
                                header.Cell().Text("Role").Bold();
                                header.Cell().Text("Active").Bold();
                            });
                            foreach (var row in userList)
                            {
                                table.Cell().Text(row.Email);
                                table.Cell().Text(row.Name);
                                table.Cell().Text(row.Role);
                                table.Cell().Text(row.IsActive);
                            }
                        });
                        page.Footer().AlignCenter().Text($"Generated: {DateTime.Now:yyyy-MM-dd HH:mm}").FontSize(10);
                    });
                }).GeneratePdf();
                await LogAction("ExportUsers", $"Exported {userList.Count} users as PDF");
                return File(pdfBytes, "application/pdf", $"users_{DateTime.Now:yyyyMMdd_HHmm}.pdf");
            }
        }

        [HttpGet("system-logs")]
        public async Task<IActionResult> GetSystemLogs([FromQuery] string? userId, [FromQuery] string? action, [FromQuery] DateTime? from, [FromQuery] DateTime? to)
        {
            var logs = _context.SystemLogs.AsQueryable();
            if (!string.IsNullOrWhiteSpace(userId))
                logs = logs.Where(l => l.UserId == userId);
            if (!string.IsNullOrWhiteSpace(action))
                logs = logs.Where(l => l.Action.Contains(action));
            if (from.HasValue)
                logs = logs.Where(l => l.Timestamp >= from.Value);
            if (to.HasValue)
                logs = logs.Where(l => l.Timestamp <= to.Value);
            var result = await logs.OrderByDescending(l => l.Timestamp).Take(500).ToListAsync();
            return Ok(result);
        }

        private async Task LogAction(string action, string details)
        {
            var userId = User?.Identity?.IsAuthenticated == true ? User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value : null;
            _context.SystemLogs.Add(new SystemLog
            {
                UserId = userId,
                Action = action,
                Details = details,
                Timestamp = DateTime.UtcNow
            });
            await _context.SaveChangesAsync();
        }

        [HttpDelete("appointments/all")]
        public async Task<IActionResult> DeleteAllAppointments()
        {
            var allAppointments = _context.Appointments.ToList();
            _context.Appointments.RemoveRange(allAppointments);
            await _context.SaveChangesAsync();
            return Ok(new { message = "All appointments deleted." });
        }

        [HttpDelete("prescriptions/all")]
        public async Task<IActionResult> DeleteAllPrescriptions()
        {
            var allPrescriptions = _context.Prescriptions.ToList();
            _context.Prescriptions.RemoveRange(allPrescriptions);
            await _context.SaveChangesAsync();
            return Ok(new { message = "All prescriptions deleted." });
        }

        [HttpDelete("notifications/all")]
        public async Task<IActionResult> DeleteAllNotifications()
        {
            var allNotifications = _context.Notifications.ToList();
            _context.Notifications.RemoveRange(allNotifications);
            await _context.SaveChangesAsync();
            return Ok(new { message = "All notifications deleted." });
        }

        [HttpGet("profile")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> GetProfile()
        {
            var userId = User.FindFirstValue(System.Security.Claims.ClaimTypes.NameIdentifier);
            var admin = await _context.Admins.Include(a => a.ApplicationUser)
                .FirstOrDefaultAsync(a => a.ApplicationUserId == userId);
            if (admin == null) return NotFound();
            return Ok(new
            {
                admin.ApplicationUser.Email,
                admin.ApplicationUser.FullName
            });
        }

        [HttpPut("profile")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> UpdateProfile([FromBody] UpdateAdminProfileModel model)
        {
            var userId = User.FindFirstValue(System.Security.Claims.ClaimTypes.NameIdentifier);
            var admin = await _context.Admins.Include(a => a.ApplicationUser)
                .FirstOrDefaultAsync(a => a.ApplicationUserId == userId);
            if (admin == null) return NotFound();
            if (model.FullName != null)
                admin.ApplicationUser.FullName = model.FullName;
            await _context.SaveChangesAsync();
            return Ok(new { message = "Profile updated successfully." });
        }

        public class UpdateAdminProfileModel
        {
            public string? FullName { get; set; }
        }

        [HttpPut("change-password")]
        [Authorize(Roles = "Admin")]
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

    public class CancelOverrideModel
    {
        public string? Justification { get; set; }
    }

    public class AppointmentExportRow
    {
        public string Date { get; set; } = string.Empty;
        public string Time { get; set; } = string.Empty;
        public string Patient { get; set; } = string.Empty;
        public string Doctor { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string Reference { get; set; } = string.Empty;
    }
    public class UserExportRow
    {
        public string Email { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Role { get; set; } = string.Empty;
        public string IsActive { get; set; } = string.Empty;
    }
} 