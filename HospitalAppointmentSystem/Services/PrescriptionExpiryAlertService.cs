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
    public class PrescriptionExpiryAlertService : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly TimeSpan _interval = TimeSpan.FromHours(12); // check twice a day

        public PrescriptionExpiryAlertService(IServiceProvider serviceProvider)
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
                    var now = DateTime.UtcNow;
                    var soon = now.AddDays(3);
                    var expiring = await db.Prescriptions
                        .Include(p => p.Patient).ThenInclude(pat => pat.ApplicationUser)
                        .Where(p => p.ExpiryDate != null && p.ExpiryDate > now && p.ExpiryDate <= soon && p.Status == "Active")
                        .ToListAsync();
                    foreach (var pres in expiring)
                    {
                        // Check if already alerted (by notification)
                        bool alreadyAlerted = await db.Notifications.AnyAsync(n => n.PrescriptionId == pres.Id && n.Message.Contains("expire"));
                        if (alreadyAlerted) continue;
                        db.Notifications.Add(new Models.Notification
                        {
                            PrescriptionId = pres.Id,
                            Message = $"Your prescription will expire on {pres.ExpiryDate:yyyy-MM-dd}. Please renew or consult your doctor.",
                            CreatedAt = DateTime.UtcNow
                        });
                    }
                    await db.SaveChangesAsync();
                }
                await Task.Delay(_interval, stoppingToken);
            }
        }
    }
} 