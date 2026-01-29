# Subscription Billing API

A production-ready backend REST API built with .NET 8 to manage users, subscription plans, and billing logic for a SaaS-style application.

This project was designed to demonstrate backend architecture, clean code practices, authentication, and real-world business logic commonly used in subscription-based products.

---

## 🚀 Tech Stack

- **.NET 8 (ASP.NET Web API)**
- **C#**
- **Entity Framework Core**
- **SQL Server**
- **JWT Authentication**
- **Swagger / OpenAPI**
- **Docker & Docker Compose**
- **Azure (App Service / Container-ready)**

---

## 🧱 Architecture

The project follows a layered architecture to ensure separation of concerns and scalability:

src/
├── Api # Controllers, middleware, configuration
├── Application # Business logic, DTOs, services
├── Domain # Entities, enums, domain rules
└── Infrastructure # EF Core, repositories, database context

Key principles:
- Dependency Injection
- DTO-based communication
- Centralized error handling
- Clean and testable code structure

---

## 🔐 Authentication

- User registration and login
- JWT-based authentication
- Secure endpoints using authorization policies
- Role-based access (basic)

---

## 📦 Core Features

### Users
- Register
- Login
- Get authenticated user profile

### Plans
- Create subscription plans (Free / Pro / Enterprise)
- Update and delete plans
- List available plans

### Subscriptions
- Subscribe to a plan
- Cancel subscription
- List active subscriptions per user

### Billing
- Mock payment processing
- Generate PDF invoices for subscriptions
- Store invoice history

---

## 🛠️ Extra Features

- Global exception handling middleware
- Pagination for list endpoints
- Soft delete
- Swagger documentation
- SQL indexes for performance
- Dockerized environment

---

## 🧪 API Documentation

Once the application is running, Swagger is available at:

/swagger

It provides a full overview of all endpoints, request/response models, and authentication flow.

---

## 🐳 Running with Docker
docker-compose up --build

The API will be available at:

http://localhost:5000
