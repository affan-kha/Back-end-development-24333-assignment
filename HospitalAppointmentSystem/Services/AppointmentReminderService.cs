using HospitalAppointmentSystem.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace HospitalAppointmentSystem.Services
{
    public class AppointmentReminderService : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly TimeSpan _interval = TimeSpan.FromMinutes(30); // check every 30 minutes

        public AppointmentReminderService(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                using (var scope = _serviceProvider.CreateScope())
                {
                    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                    var emailService = scope.ServiceProvider.GetRequiredService<EmailService>();
                    var now = DateTime.UtcNow;
                    var soon = now.AddHours(24);
                    var later = now.AddHours(25);
                    var appointments = await db.Appointments
                        .Include(a => a.Patient).ThenInclude(p => p.ApplicationUser)
                        .Where(a => !a.ReminderSent && a.Status == "Scheduled" && a.AppointmentDateTime >= soon && a.AppointmentDateTime < later)
                        .ToListAsync();
                    foreach (var appt in appointments)
                    {
                        var to = appt.Patient.ApplicationUser.Email;
                        var subject = "Appointment Reminder";
                        var body = $"Dear {appt.Patient.ApplicationUser.Email},\n\nThis is a reminder for your appointment scheduled at {appt.AppointmentDateTime}.\n\nThank you.";
                        try
                        {
                            await emailService.SendEmailAsync(to, subject, body);
                            appt.ReminderSent = true;
                        }
                        catch { /* log error if needed */ }
                    }
                    await db.SaveChangesAsync();
                }
                await Task.Delay(_interval, stoppingToken);
            }
        }
    }
} 