using Microsoft.Extensions.Hosting;
// using RabbitMQ.Client;
// using RabbitMQ.Client.Events;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using HospitalAppointmentSystem.Services;

namespace HospitalAppointmentSystem.Services
{
    public class RabbitMqNotificationConsumer : BackgroundService
    {
        private readonly string _hostname = "localhost";
        private readonly string _queueName = "appointment_notifications";
     
        private readonly IServiceProvider _serviceProvider;

        public RabbitMqNotificationConsumer(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
            
        }

        protected override Task ExecuteAsync(CancellationToken stoppingToken)
        {
           
            Console.WriteLine("[RabbitMQ] Consumer service started (demo mode)");
            return Task.CompletedTask;
        }

        public override void Dispose()
        {
         
            base.Dispose();
        }
    }
} 