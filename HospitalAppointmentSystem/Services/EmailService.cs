using Microsoft.Extensions.Configuration;
using System.Net;
using System.Net.Mail;
using System.Threading.Tasks;
using System;

namespace HospitalAppointmentSystem.Services
{
    public class EmailService
    {
        private readonly IConfiguration _config;
        public EmailService(IConfiguration config)
        {
            _config = config;
        }

        public async Task SendEmailAsync(string to, string subject, string body)
        {
            var smtpSection = _config.GetSection("Smtp");
            Console.WriteLine($"SMTP User: {smtpSection["User"]}"); // DEBUG LOG
            var smtpClient = new SmtpClient(smtpSection["Host"])
            {
                Port = int.Parse(smtpSection["Port"] ?? "587"),
                Credentials = new NetworkCredential(smtpSection["User"], smtpSection["Pass"]),
                EnableSsl = bool.Parse(smtpSection["EnableSsl"] ?? "true"),
            };
            var mail = new MailMessage(smtpSection["User"], to, subject, body);
            await smtpClient.SendMailAsync(mail);
        }
    }
} 