using Bogus;
using HospitalAppointmentSystem.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace HospitalAppointmentSystem.Data
{
    public static class DbSeeder
    {
        public static async Task SeedAsync(IServiceProvider serviceProvider)
        {
            using var scope = serviceProvider.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();

            // Ensure DB is created
            context.Database.Migrate();

            // Seed Roles
            string[] roles = { "Patient", "Doctor", "Admin" };
            foreach (var role in roles)
            {
                if (!await roleManager.RoleExistsAsync(role))
                {
                    await roleManager.CreateAsync(new IdentityRole(role));
                }
            }

            // Seed 1 admin user
            if (!context.Admins.Any())
            {
                var adminEmail = "admin@hospital.com";
                var adminUser = new ApplicationUser
                {
                    UserName = adminEmail,
                    Email = adminEmail,
                    EmailConfirmed = true
                };
                var result = await userManager.CreateAsync(adminUser, "Admin@123");
                if (result.Succeeded)
                {
                    await userManager.AddToRoleAsync(adminUser, "Admin");
                    var adminProfile = new Admin { ApplicationUserId = adminUser.Id };
                    context.Admins.Add(adminProfile);
                    await context.SaveChangesAsync();
                }
            }

            // Seed 5 fake doctors
            if (!context.Doctors.Any())
            {
                var faker = new Faker("en");
                var specializations = new[] { "Cardiology", "Dermatology", "Neurology", "Pediatrics", "Orthopedics" };
                for (int i = 0; i < 5; i++)
                {
                    var email = $"doctor{i + 1}@hospital.com";
                    var user = new ApplicationUser
                    {
                        UserName = email,
                        Email = email,
                        EmailConfirmed = true
                    };
                    var result = await userManager.CreateAsync(user, "Doctor@123");
                    if (result.Succeeded)
                    {
                        await userManager.AddToRoleAsync(user, "Doctor");
                        var doctorProfile = new Doctor
                        {
                            ApplicationUserId = user.Id,
                            Specialization = specializations[i % specializations.Length],
                            Schedule = "{}" // Placeholder for schedule
                        };
                        context.Doctors.Add(doctorProfile);
                    }
                }
                await context.SaveChangesAsync();
            }

            // Seed 1000 fake patients
            if (!context.Patients.Any())
            {
                var faker = new Faker("en");
                var fakePatients = new List<(ApplicationUser, Patient)>();
                for (int i = 0; i < 1000; i++)
                {
                    var email = faker.Internet.Email();
                    var user = new ApplicationUser
                    {
                        UserName = email,
                        Email = email,
                        EmailConfirmed = true
                    };
                    var patient = new Patient
                    {
                        MedicalHistory = faker.Lorem.Sentence()
                    };
                    fakePatients.Add((user, patient));
                }
                foreach (var (user, patient) in fakePatients)
                {
                    var result = await userManager.CreateAsync(user, "Test@123");
                    if (result.Succeeded)
                    {
                        await userManager.AddToRoleAsync(user, "Patient");
                        patient.ApplicationUserId = user.Id;
                        context.Patients.Add(patient);
                    }
                }
                await context.SaveChangesAsync();
            }

            // Seed messages for testing
            if (!context.Messages.Any())
            {
                var docUser = context.Users.FirstOrDefault(u => u.Email.Contains("doctor"));
                var patUser = context.Users.FirstOrDefault(u => u.Email.Contains("patient"));
                if (docUser != null && patUser != null)
                {
                    context.Messages.Add(new Message
                    {
                        SenderId = docUser.Id,
                        ReceiverId = patUser.Id,
                        Content = "Hello, how are you feeling today?",
                        SentAt = DateTime.UtcNow.AddMinutes(-10)
                    });
                    context.Messages.Add(new Message
                    {
                        SenderId = patUser.Id,
                        ReceiverId = docUser.Id,
                        Content = "I am feeling better, thank you!",
                        SentAt = DateTime.UtcNow.AddMinutes(-5)
                    });
                    context.SaveChanges();
                }
            }
        }
    }
} 