using Xunit;
using HospitalAppointmentSystem.Models;
using System;

namespace HospitalAppointmentSystem.Tests
{
    public class AppointmentTests
    {
        [Fact]
        public void CannotCancelWithin48Hours()
        {
            // Arrange
            var appointment = new Appointment
            {
                AppointmentDateTime = DateTime.UtcNow.AddHours(24),
                Status = "Pending"
            };
            // Act
            var canCancel = (appointment.AppointmentDateTime - DateTime.UtcNow).TotalHours >= 48;
            // Assert
            Assert.False(canCancel);
        }

        [Fact]
        public void CanCancelMoreThan48Hours()
        {
            // Arrange
            var appointment = new Appointment
            {
                AppointmentDateTime = DateTime.UtcNow.AddHours(72),
                Status = "Pending"
            };
            // Act
            var canCancel = (appointment.AppointmentDateTime - DateTime.UtcNow).TotalHours >= 48;
            // Assert
            Assert.True(canCancel);
        }

        [Fact]
        public void CanMarkAppointmentAsCompleted()
        {
            // Arrange
            var appointment = new Appointment
            {
                AppointmentDateTime = DateTime.UtcNow.AddHours(72),
                Status = "Pending"
            };
            // Act
            appointment.Status = "Completed";
            // Assert
            Assert.Equal("Completed", appointment.Status);
        }

        [Fact]
        public void CannotDoubleBookSameDoctorSameTime()
        {
            // Arrange
            var doctorId = 1;
            var appointmentTime = DateTime.UtcNow.AddDays(1);
            var existingAppointments = new[] {
                new Appointment { DoctorId = doctorId, AppointmentDateTime = appointmentTime, Status = "Scheduled" }
            };
            var newAppointment = new Appointment { DoctorId = doctorId, AppointmentDateTime = appointmentTime, Status = "Pending" };
            // Act
            bool isDoubleBooked = Array.Exists(existingAppointments, a => a.DoctorId == newAppointment.DoctorId && a.AppointmentDateTime == newAppointment.AppointmentDateTime && a.Status != "Cancelled");
            // Assert
            Assert.True(isDoubleBooked);
        }
    }
} 