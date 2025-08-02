Application Repo Link : (https://drive.google.com/file/d/1V7PCPjTKgQuDid7iqAbyDbHPflNxgu6v/view?usp=drive_link)

Application Video Link :(https://drive.google.com/file/d/1GDkzwGLEoTC7Jo4SO0CpDBWDjaCHpmAz/view?usp=sharing)


Document Word File Link :(https://drive.google.com/file/d/1KwvsUL6ZNrJUnCBXBeES1VxWs5-kpMrc/view?usp=sharing)






# 🏥 Hospital Appointment System

A comprehensive full-stack web application for managing hospital appointments, built with ASP.NET Core Web API backend and React.js frontend.

## 📋 Table of Contents

- [Overview](#overview)
- [Features](#features)
- [Technology Stack](#technology-stack)
- [Architecture](#architecture)
- [Prerequisites](#prerequisites)
- [Installation & Setup](#installation--setup)
- [API Documentation](#api-documentation)
- [Database Schema](#database-schema)
- [User Roles](#user-roles)
- [Key Features](#key-features)
- [Background Services](#background-services)
- [Security](#security)
- [Testing](#testing)
- [Deployment](#deployment)
- [Contributing](#contributing)

## 🎯 Overview

The Hospital Appointment System is a modern web application designed to streamline the appointment booking process for hospitals and medical facilities. It provides separate interfaces for patients, doctors, and administrators with comprehensive features for appointment management, prescription handling, and communication.

## ✨ Features

### 🔐 Authentication & Authorization
- JWT-based authentication
- Role-based access control (Patient, Doctor, Admin)
- Secure password policies
- User profile management

### 👥 User Management
- **Patient Features:**
  - Book appointments with doctors
  - View appointment history
  - Manage medical history
  - Receive prescriptions
  - Chat with doctors
  - View notifications

- **Doctor Features:**
  - Manage appointment schedule
  - View patient appointments
  - Create and manage prescriptions
  - Set unavailable time slots
  - Chat with patients
  - Manage profile and specialization

- **Admin Features:**
  - User management (patients, doctors)
  - System-wide appointment overview
  - Analytics and reporting
  - System configuration

### 📅 Appointment Management
- Real-time appointment booking
- Appointment status tracking (Pending, Approved, Rejected, Completed, Cancelled)
- Automated appointment reminders
- Reference number generation

### 💊 Prescription System
- Digital prescription creation
- PDF generation for prescriptions
- Prescription expiry tracking
- Secure code verification
- Pharmacy integration

### 📧 Communication & Notifications
- Email notifications
- In-app notification system
- Real-time messaging between patients and doctors


### 📊 Feedback & Analytics
- Appointment feedback system
- Rating and review system


## 🛠 Technology Stack

### Backend
- **Framework:** ASP.NET Core 8.0 Web API
- **Database:** MySQL with Entity Framework Core
- **Authentication:** JWT Bearer Tokens
- **PDF Generation:** QuestPDF
- **Email Service:** SMTP (Gmail)
- **Documentation:** Swagger/OpenAPI

### Frontend
- **Framework:** React.js 19.1.0
- **Routing:** React Router DOM
- **HTTP Client:** Axios
- **UI Icons:** React Icons
- **Styling:** CSS3

### Development Tools
- **IDE:** Visual Studio / VS Code
- **Package Manager:** npm (Frontend), NuGet (Backend)
- **Version Control:** Git

## 🏗 Architecture

```
Hospital Appointment System
├── Backend (ASP.NET Core Web API)
│   ├── Controllers/
│   │   ├── AuthController.cs
│   │   ├── AppointmentController.cs
│   │   ├── DoctorController.cs
│   │   ├── PatientController.cs
│   │   ├── AdminController.cs
│   │   ├── PrescriptionController.cs
│   │   ├── NotificationController.cs
│   │   └── FeedbackController.cs
│   ├── Models/
│   │   ├── ApplicationUser.cs
│   │   ├── Appointment.cs
│   │   ├── Doctor.cs
│   │   ├── Patient.cs
│   │   ├── Prescription.cs
│   │   ├── Notification.cs
│   │   └── Feedback.cs
│   ├── Services/
│   │   ├── EmailService.cs
│   │   ├── RabbitMqService.cs
│   │   ├── AppointmentReminderService.cs
│   │   └── PrescriptionExpiryAlertService.cs
│   └── Data/
│       └── ApplicationDbContext.cs
└── Frontend (React.js)
    ├── src/
    │   ├── Components/
    │   │   ├── Login.js
    │   │   ├── Register.js
    │   │   ├── Dashboard.js
    │   │   ├── BookAppointment.js
    │   │   ├── MyAppointments.js
    │   │   ├── DoctorDashboard.js
    │   │   ├── AdminDashboard.js
    │   │   └── Prescriptions.js
    │   ├── Context/
    │   │   └── AuthContext.js
    │   └── Utils/
    └── public/
```

## 📋 Prerequisites

Before running this application, ensure you have the following installed:

- **.NET 8.0 SDK**
- **Node.js 18+** and npm
- **MySQL Server 8.0+**
- **RabbitMQ Server**
- **Visual Studio 2022** or **VS Code**

## 🚀 Installation & Setup

### 1. Clone the Repository
```bash
git clone <repository-url>
cd Back-end-development-24333-assignment
```

### 2. Backend Setup

#### Database Configuration
1. Create a MySQL database named `hospital_appointment_db`
2. Update connection string in `HospitalAppointmentSystem/appsettings.json`:
```json
"ConnectionStrings": {
  "DefaultConnection": "server=localhost;port=3306;database=hospital_appointment_db;user=root;password=your_password;"
}
```



```

#### Run Backend
```bash
Run CMD "npm i"
navigate to
cd HospitalAppointmentSystem
Run CMD  : dotnet restore
Run CMD  : dotnet build
Run CMD  : dotnet tool install --global dotnet-ef
Run CMD  :dotnet ef database update
dotnet run --urls "http://localhost:5000"
```



### 3. Frontend Setup

```bash
cd hospital-appointment-frontend
npm install
npm start
```

The React app will be available at `http://localhost:3000`



### Main API Endpoints

#### Authentication
- `POST /api/auth/login` - User login
- `POST /api/auth/register` - User registration
- `POST /api/auth/refresh` - Refresh JWT token

#### Appointments
- `GET /api/appointments` - Get appointments
- `POST /api/appointments` - Create appointment
- `PUT /api/appointments/{id}` - Update appointment
- `DELETE /api/appointments/{id}` - Cancel appointment

#### Doctors
- `GET /api/doctors` - Get all doctors
- `GET /api/doctors/{id}` - Get doctor details
- `PUT /api/doctors/{id}` - Update doctor profile

#### Patients
- `GET /api/patients/{id}` - Get patient details
- `PUT /api/patients/{id}` - Update patient profile

#### Prescriptions
- `GET /api/prescriptions` - Get prescriptions
- `POST /api/prescriptions` - Create prescription
- `GET /api/prescriptions/{id}/pdf` - Download prescription PDF

## 🗄 Database Schema

### Core Entities

#### Users
- **ApplicationUser** - Base user entity with authentication
- **Patient** - Patient-specific information and medical history
- **Doctor** - Doctor information, specialization, and schedule
- **Admin** - Administrative user

#### Appointments
- **Appointment** - Core appointment entity with status tracking
- **DoctorUnavailableSlot** - Doctor's unavailable time slots

#### Medical Records
- **Prescription** - Digital prescriptions with PDF generation
- **MedicalInfoRequest** - Medical information requests

#### Communication
- **Notification** - System notifications
- **Message** - Chat messages between users
- **Feedback** - Appointment feedback and ratings

## 👥 User Roles

### 1. Patient
- Book appointments with available doctors
- View appointment history and status
- Manage medical history
- Receive and view prescriptions
- Chat with doctors
- Provide feedback on appointments

### 2. Doctor
- Manage appointment schedule
- View and update patient appointments
- Create digital prescriptions
- Set unavailable time slots
- Chat with patients
- Update profile and specialization

### 3. Administrator
- Manage all users (patients, doctors)
- View system-wide appointments
- Access analytics and reports
- Configure system settings
- Monitor system health

## 🔧 Key Features

### Real-time Notifications
- Email notifications for appointment confirmations
- In-app notifications for updates
- Automated appointment reminders

### Prescription Management
- Digital prescription creation
- PDF generation with secure codes
- Expiry tracking and alerts
- Pharmacy integration



### Background Services
- **Appointment Reminder Service** - Sends automated reminders
- **Prescription Expiry Alert Service** - Tracks prescription expiry


## 🔒 Security

- **JWT Authentication** with secure token validation
- **Role-based Authorization** for all endpoints
- **Password Policies** with complexity requirements
- **CORS Configuration** for frontend integration
- **HTTPS Enforcement** in production
- **Input Validation** and sanitization

## 🧪 Testing

The project includes unit tests in the `HospitalAppointmentSystem.Tests` directory:

```bash
cd HospitalAppointmentSystem.Tests
dotnet test
```


### Database Migration
```bash
dotnet ef database update
```


