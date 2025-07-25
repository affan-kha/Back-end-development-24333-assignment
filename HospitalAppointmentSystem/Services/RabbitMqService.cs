using RabbitMQ.Client;
using System.Text;
using Microsoft.Extensions.Logging;
using System;
using System.Threading.Tasks;

namespace HospitalAppointmentSystem.Services
{
    public class RabbitMqService : IDisposable
    {
        private readonly string _hostname = "localhost";
        private readonly string _queueName = "appointment_notifications";
        private IConnection _connection;
        private IModel _channel;
        private readonly ILogger<RabbitMqService> _logger;
        private bool _disposed = false;

        public RabbitMqService(ILogger<RabbitMqService> logger)
        {
            _logger = logger;
            InitializeRabbitMq();
        }

        private void InitializeRabbitMq()
        {
            try
            {
                var factory = new ConnectionFactory() 
                { 
                    HostName = _hostname,
                    UserName = "guest",
                    Password = "guest",
                    AutomaticRecoveryEnabled = true,
                    NetworkRecoveryInterval = TimeSpan.FromSeconds(10)
                };
                
                _connection = factory.CreateConnection();
                _connection.ConnectionShutdown += (sender, e) => 
                    _logger.LogWarning("RabbitMQ connection shutdown: {ReplyText}", e.ReplyText);
                
                _channel = _connection.CreateModel();
                _channel.ModelShutdown += (sender, e) => 
                    _logger.LogWarning("RabbitMQ channel shutdown: {ReplyText}", e.ReplyText);
                
                _channel.QueueDeclare(
                    queue: _queueName,
                    durable: true,
                    exclusive: false,
                    autoDelete: false,
                    arguments: null);
                
                _logger.LogInformation("RabbitMQ service initialized successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error initializing RabbitMQ service");
                // Don't throw here to allow the application to start
            }
        }

        public void Publish(string message)
        {
            if (_channel == null || _channel.IsClosed)
            {
                _logger.LogWarning("RabbitMQ channel is not available. Message not sent: {Message}", message);
                return;
            }

            try
            {
                var body = Encoding.UTF8.GetBytes(message);
                var properties = _channel.CreateBasicProperties();
                properties.Persistent = true;
                
                _channel.BasicPublish(
                    exchange: string.Empty,
                    routingKey: _queueName,
                    basicProperties: properties,
                    body: body);
                
                _logger.LogInformation("Message published to RabbitMQ: {Message}", message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error publishing message to RabbitMQ");
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (_disposed) return;
            
            if (disposing)
            {
                try
                {
                    _channel?.Close();
                    _connection?.Close();
                    _channel?.Dispose();
                    _connection?.Dispose();
                    _logger.LogInformation("RabbitMQ service disposed");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error disposing RabbitMQ resources");
                }
            }
            
            _disposed = true;
        }
    }
}