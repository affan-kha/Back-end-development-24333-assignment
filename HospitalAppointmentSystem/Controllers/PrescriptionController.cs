using HospitalAppointmentSystem.Data;
using HospitalAppointmentSystem.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using HospitalAppointmentSystem.Services;
using System.Collections.Generic;

namespace HospitalAppointmentSystem.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class PrescriptionController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly EmailService _emailService;

        public PrescriptionController(ApplicationDbContext context, EmailService emailService)
        {
            _context = context;
            _emailService = emailService;
        }

        [HttpPost("issue")]
        [Authorize(Roles = "Doctor")]
        public async Task<IActionResult> Issue([FromBody] IssuePrescriptionModel model)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var doctor = await _context.Doctors.Include(d => d.ApplicationUser).FirstOrDefaultAsync(d => d.ApplicationUserId == userId);
            if (doctor == null) return Unauthorized();
            var patient = await _context.Patients.Include(p => p.ApplicationUser).FirstOrDefaultAsync(p => p.Id == model.PatientId);
            if (patient == null) return BadRequest("Patient not found");
            var appointment = await _context.Appointments.FindAsync(model.AppointmentId);
            if (appointment == null) return BadRequest("Appointment not found");

            // --- Medication interaction check ---
            // Static demo interaction data
            var interactionMap = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase)
            {
                { "Aspirin", new List<string> { "Warfarin", "Ibuprofen" } },
                { "Warfarin", new List<string> { "Aspirin", "Amiodarone" } },
                { "Ibuprofen", new List<string> { "Aspirin" } },
                { "Amiodarone", new List<string> { "Warfarin" } },
                // Add more as needed
            };
            // Get patient's active prescriptions (excluding current appointment)
            var activeMeds = await _context.Prescriptions
                .Where(p => p.PatientId == patient.Id && p.Status == "Active" && p.AppointmentId != appointment.Id)
                .Select(p => p.MedicationName)
                .ToListAsync();
            var interactionWarnings = new List<string>();
            foreach (var med in activeMeds)
            {
                if (interactionMap.TryGetValue(model.MedicationName, out var interactsWith) && interactsWith.Contains(med, StringComparer.OrdinalIgnoreCase))
                {
                    interactionWarnings.Add($"{model.MedicationName} interacts with {med}");
                }
                if (interactionMap.TryGetValue(med, out var interactsWith2) && interactsWith2.Contains(model.MedicationName, StringComparer.OrdinalIgnoreCase))
                {
                    interactionWarnings.Add($"{med} interacts with {model.MedicationName}");
                }
            }
            // --- End interaction check ---

            var prescription = new Prescription
            {
                PatientId = patient.Id,
                DoctorId = doctor.Id,
                AppointmentId = appointment.Id,
                MedicationName = model.MedicationName,
                Dosage = model.Dosage,
                Instructions = model.Instructions,
                Status = "Active",
                IsRenewable = model.IsRenewable,
                RefillsRemaining = model.RefillsRemaining,
                Notes = model.Notes,
                SecureCode = Guid.NewGuid().ToString("N").Substring(0, 8)
            };
            _context.Prescriptions.Add(prescription);
            await _context.SaveChangesAsync();
            // Save notification
            _context.Notifications.Add(new Notification {
                PrescriptionId = prescription.Id,
                Message = $"Prescription issued for patient {patient.Id} by doctor {doctor.Id} for appointment {appointment.Id}",
                CreatedAt = DateTime.UtcNow
            });
            await _context.SaveChangesAsync();
            return Ok(new {
                prescription.Id,
                prescription.MedicationName,
                prescription.Dosage,
                prescription.Instructions,
                prescription.IssueDate,
                prescription.Status,
                prescription.IsRenewable,
                prescription.RefillsRemaining,
                prescription.Notes,
                prescription.ExpiryDate,
                prescription.SecureCode,
                Doctor = doctor != null ? doctor.ApplicationUser.Email : null,
                Patient = patient != null ? patient.ApplicationUser.Email : null,
                interactionWarnings
            });
        }

        [HttpGet("my")]
        [Authorize(Roles = "Patient")]
        public async Task<IActionResult> MyPrescriptions()
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var patient = await _context.Patients.Include(p => p.ApplicationUser).FirstOrDefaultAsync(p => p.ApplicationUserId == userId);
            if (patient == null) return Unauthorized();
            var prescriptions = await _context.Prescriptions
                .Include(p => p.Doctor).ThenInclude(d => d.ApplicationUser)
                .Include(p => p.Patient).ThenInclude(pat => pat.ApplicationUser)
                .Where(p => p.PatientId == patient.Id)
                .ToListAsync();
            return Ok(prescriptions.Select(p => new {
                p.Id,
                p.MedicationName,
                p.Dosage,
                p.Instructions,
                p.IssueDate,
                p.Status,
                p.IsRenewable,
                p.RefillsRemaining,
                p.Notes,
                p.ExpiryDate,
                p.SecureCode,
                Doctor = p.Doctor != null ? p.Doctor.ApplicationUser.Email : null,
                Patient = p.Patient != null ? (p.Patient.ApplicationUser.FullName ?? p.Patient.ApplicationUser.Email) : null
            }));
        }

        [HttpGet("doctor")]
        [Authorize(Roles = "Doctor")]
        public async Task<IActionResult> DoctorPrescriptions()
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var doctor = await _context.Doctors.Include(d => d.ApplicationUser).FirstOrDefaultAsync(d => d.ApplicationUserId == userId);
            if (doctor == null) return Unauthorized();
            var prescriptions = await _context.Prescriptions
                .Include(p => p.Patient).ThenInclude(pat => pat.ApplicationUser)
                .Where(p => p.DoctorId == doctor.Id)
                .ToListAsync();
            return Ok(prescriptions.Select(p => new {
                p.Id,
                p.MedicationName,
                p.Dosage,
                p.Instructions,
                p.IssueDate,
                p.Status,
                p.IsRenewable,
                p.RefillsRemaining,
                p.Notes,
                p.ExpiryDate,
                p.SecureCode,
                Patient = p.Patient != null ? p.Patient.ApplicationUser.Email : null
            }));
        }

        [HttpGet("{id}")]
        public async Task<IActionResult> GetPrescription(int id)
        {
            var prescription = await _context.Prescriptions
                .Include(p => p.Doctor).ThenInclude(d => d.ApplicationUser)
                .Include(p => p.Patient).ThenInclude(pat => pat.ApplicationUser)
                .FirstOrDefaultAsync(p => p.Id == id);
            if (prescription == null) return NotFound();
            return Ok(new {
                prescription.Id,
                prescription.MedicationName,
                prescription.Dosage,
                prescription.Instructions,
                prescription.IssueDate,
                prescription.Status,
                prescription.IsRenewable,
                prescription.RefillsRemaining,
                prescription.Notes,
                prescription.ExpiryDate,
                prescription.SecureCode,
                Doctor = prescription.Doctor.ApplicationUser.Email,
                Patient = prescription.Patient.ApplicationUser.Email
            });
        }

        [HttpPost("{id}/renew")]
        [Authorize(Roles = "Patient")]
        public async Task<IActionResult> RequestRenewal(int id)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var patient = await _context.Patients.FirstOrDefaultAsync(p => p.ApplicationUserId == userId);
            if (patient == null) return Unauthorized();
            var prescription = await _context.Prescriptions.FirstOrDefaultAsync(p => p.Id == id && p.PatientId == patient.Id);
            if (prescription == null) return NotFound();
            if (!prescription.IsRenewable || prescription.Status != "Active") return BadRequest("Prescription is not renewable.");
            if (prescription.RenewalRequested) return BadRequest("Renewal already requested.");
            prescription.RenewalRequested = true;
            prescription.RenewalStatus = "Pending";
            await _context.SaveChangesAsync();
            // Optionally: notify doctor
            return Ok(new { message = "Renewal request submitted." });
        }

        [HttpPost("{id}/approve-renewal")]
        [Authorize(Roles = "Doctor")]
        public async Task<IActionResult> ApproveRenewal(int id)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var doctor = await _context.Doctors.FirstOrDefaultAsync(d => d.ApplicationUserId == userId);
            if (doctor == null) return Unauthorized();
            var prescription = await _context.Prescriptions.FirstOrDefaultAsync(p => p.Id == id && p.DoctorId == doctor.Id);
            if (prescription == null) return NotFound();
            if (!prescription.RenewalRequested || prescription.RenewalStatus != "Pending") return BadRequest("No pending renewal request.");
            prescription.RenewalStatus = "Approved";
            prescription.RenewalRequested = false;
            if (prescription.RefillsRemaining.HasValue && prescription.RefillsRemaining > 0)
                prescription.RefillsRemaining--;
            await _context.SaveChangesAsync();
            // Notify patient
            _context.Notifications.Add(new Notification {
                PrescriptionId = prescription.Id,
                Message = "Your prescription renewal was approved.",
                CreatedAt = DateTime.UtcNow
            });
            await _context.SaveChangesAsync();
            return Ok(new { message = "Renewal approved." });
        }

        [HttpPost("{id}/reject-renewal")]
        [Authorize(Roles = "Doctor")]
        public async Task<IActionResult> RejectRenewal(int id, [FromBody] RejectRenewalModel model)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var doctor = await _context.Doctors.FirstOrDefaultAsync(d => d.ApplicationUserId == userId);
            if (doctor == null) return Unauthorized();
            var prescription = await _context.Prescriptions.FirstOrDefaultAsync(p => p.Id == id && p.DoctorId == doctor.Id);
            if (prescription == null) return NotFound();
            if (!prescription.RenewalRequested || prescription.RenewalStatus != "Pending") return BadRequest("No pending renewal request.");
            prescription.RenewalStatus = "Rejected";
            prescription.RenewalRequested = false;
            prescription.RenewalReason = model.Reason;
            await _context.SaveChangesAsync();
            // Notify patient
            _context.Notifications.Add(new Notification {
                PrescriptionId = prescription.Id,
                Message = $"Your prescription renewal was rejected. Reason: {model.Reason}",
                CreatedAt = DateTime.UtcNow
            });
            await _context.SaveChangesAsync();
            return Ok(new { message = "Renewal rejected." });
        }

        [HttpPost("{id}/send-to-pharmacy")]
        [Authorize(Roles = "Doctor")]
        public async Task<IActionResult> SendToPharmacy(int id, [FromBody] SendToPharmacyModel model)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var doctor = await _context.Doctors.Include(d => d.ApplicationUser).FirstOrDefaultAsync(d => d.ApplicationUserId == userId);
            if (doctor == null) return Unauthorized();
            var prescription = await _context.Prescriptions.Include(p => p.Patient).ThenInclude(pat => pat.ApplicationUser).FirstOrDefaultAsync(p => p.Id == id && p.DoctorId == doctor.Id);
            if (prescription == null) return NotFound();
            if (prescription.SentToPharmacy) return BadRequest("Already sent to pharmacy.");
            if (string.IsNullOrWhiteSpace(model.PharmacyEmail)) return BadRequest("Pharmacy email required.");
            // Compose email
            var subject = $"Prescription for {prescription.Patient.ApplicationUser.FullName ?? prescription.Patient.ApplicationUser.Email}";
            var body = $"Prescription Details:\n\nMedication: {prescription.MedicationName}\nDosage: {prescription.Dosage}\nInstructions: {prescription.Instructions}\nNotes: {prescription.Notes}\nDoctor: {doctor.ApplicationUser.Email}\nPatient: {prescription.Patient.ApplicationUser.FullName ?? prescription.Patient.ApplicationUser.Email}\nIssued: {prescription.IssueDate}\nSecure Code: {prescription.SecureCode}";
            await _emailService.SendEmailAsync(model.PharmacyEmail, subject, body);
            prescription.PharmacyEmail = model.PharmacyEmail;
            prescription.SentToPharmacy = true;
            await _context.SaveChangesAsync();
            return Ok(new { message = "Prescription sent to pharmacy." });
        }

        [HttpDelete("{id}")]
        [Authorize(Roles = "Doctor")]
        public async Task<IActionResult> DeletePrescription(int id)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var doctor = await _context.Doctors.FirstOrDefaultAsync(d => d.ApplicationUserId == userId);
            if (doctor == null) return Unauthorized();
            var prescription = await _context.Prescriptions.FirstOrDefaultAsync(p => p.Id == id && p.DoctorId == doctor.Id);
            if (prescription == null) return NotFound();
            // Delete related notifications
            var notifications = await _context.Notifications.Where(n => n.PrescriptionId == prescription.Id).ToListAsync();
            _context.Notifications.RemoveRange(notifications);
            _context.Prescriptions.Remove(prescription);
            await _context.SaveChangesAsync();
            return Ok(new { message = "Prescription deleted." });
        }
    }

    public class IssuePrescriptionModel
    {
        public int PatientId { get; set; }
        public int AppointmentId { get; set; }
        public string MedicationName { get; set; } = null!;
        public string Dosage { get; set; } = null!;
        public string Instructions { get; set; } = null!;
        public bool IsRenewable { get; set; }
        public int? RefillsRemaining { get; set; }
        public string? Notes { get; set; }
    }

    public class RejectRenewalModel
    {
        public string? Reason { get; set; }
    }

    public class SendToPharmacyModel
    {
        public string PharmacyEmail { get; set; } = null!;
    }
} 