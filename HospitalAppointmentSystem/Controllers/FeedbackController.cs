using HospitalAppointmentSystem.Data;
using HospitalAppointmentSystem.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using System.Threading.Tasks;
using System.Linq;

namespace HospitalAppointmentSystem.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class FeedbackController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        public FeedbackController(ApplicationDbContext context)
        {
            _context = context;
        }

        // POST: /api/feedback
        [HttpPost]
        [Authorize(Roles = "Patient")]
        public async Task<IActionResult> SubmitFeedback([FromBody] FeedbackModel model)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var patient = await _context.Patients.FirstOrDefaultAsync(p => p.ApplicationUserId == userId);
            if (patient == null) return Unauthorized();
            var appointment = await _context.Appointments.FirstOrDefaultAsync(a => a.Id == model.AppointmentId && a.PatientId == patient.Id && a.Status == "Completed");
            if (appointment == null) return BadRequest("Invalid appointment or not completed.");
            var already = await _context.Feedbacks.AnyAsync(f => f.AppointmentId == appointment.Id && f.PatientId == patient.Id);
            if (already) return BadRequest("Feedback already submitted.");
            var feedback = new Feedback
            {
                AppointmentId = appointment.Id,
                PatientId = patient.Id,
                Rating = model.Rating,
                Comments = model.Comments
            };
            _context.Feedbacks.Add(feedback);
            await _context.SaveChangesAsync();
            return Ok(new { message = "Feedback submitted" });
        }

        // GET: /api/feedback/my
        [HttpGet("my")]
        [Authorize(Roles = "Patient")]
        public async Task<IActionResult> MyFeedback()
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var patient = await _context.Patients.FirstOrDefaultAsync(p => p.ApplicationUserId == userId);
            if (patient == null) return Unauthorized();
            var feedbacks = await _context.Feedbacks.Where(f => f.PatientId == patient.Id)
                .Select(f => new { f.Id, f.AppointmentId, f.Rating, f.Comments, f.CreatedAt })
                .ToListAsync();
            return Ok(feedbacks);
        }

        // GET: /api/feedback/doctor/{doctorId}
        [HttpGet("doctor/{doctorId}")]
        [Authorize(Roles = "Doctor,Admin")]
        public async Task<IActionResult> DoctorFeedback(int doctorId)
        {
            var feedbacks = await _context.Feedbacks
                .Join(_context.Appointments,
                      f => f.AppointmentId,
                      a => a.Id,
                      (f, a) => new { Feedback = f, Appointment = a })
                .Where(joined => joined.Appointment.DoctorId == doctorId)
                .Select(joined => new {
                    joined.Feedback.Id,
                    joined.Feedback.AppointmentId,
                    joined.Feedback.Rating,
                    joined.Feedback.Comments,
                    joined.Feedback.CreatedAt,
                    PatientId = joined.Feedback.PatientId
                })
                .ToListAsync();
            return Ok(feedbacks);
        }

        // GET: /api/feedback/appointment/{appointmentId}
        [HttpGet("appointment/{appointmentId}")]
        public async Task<IActionResult> FeedbackForAppointment(int appointmentId)
        {
            var feedback = await _context.Feedbacks.FirstOrDefaultAsync(f => f.AppointmentId == appointmentId);
            if (feedback == null) return NotFound();
            return Ok(new { feedback.Id, feedback.Rating, feedback.Comments, feedback.CreatedAt });
        }
    }

    public class FeedbackModel
    {
        public int AppointmentId { get; set; }
        public int Rating { get; set; }
        public string? Comments { get; set; }
    }
} 