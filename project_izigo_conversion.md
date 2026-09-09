---
name: project-izigo-conversion
description: "PHP Laravel (izigo-backend-copy) converted to C# .NET 9 — superseded by the full ride-hailing rewrite at C:\\Projects\\izigo-api"
metadata: 
  node_type: memory
  type: project
  originSessionId: 8c61090f-dc6e-430a-865e-409f73731891
---

> **Note:** This was an earlier food-delivery proof-of-concept conversion. The authoritative project is the
> ride-hailing API at `C:\Projects\izigo-api` (Izigo.sln). The architecture below is for the OLD project only.

PHP Laravel app (izigo-backend-copy) converted to C# ASP.NET Core 9 Clean Architecture.

**Solution path:** `C:\Users\Chuks\source\repos\IzigoBackend\IzigoBackend.sln`

**Why:** User requested full codebase conversion preserving all logic.

**Architecture:**
- `Izigo.Domain` — 25 entities (User, Order, Product, Category, DeliveryMan, Branch, Coupon, Banner, etc.)
- `Izigo.Application` — Interfaces (IApplicationDbContext, IFcmService, ITokenService), DTOs, BusinessHelpers
- `Izigo.Infrastructure` — EF Core + Pomelo MySQL, FcmService, TokenService (JWT)
- `Izigo.API` — ASP.NET Core 9 REST API, JWT Bearer auth, Swagger
- `Izigo.UnitTests` — 38 xUnit tests (all passing) with Moq + FluentAssertions
- `Izigo.IntegrationTests` — xUnit with WebApplicationFactory + InMemory EF

**DB:** MySQL, table `delivery_app_db`, connection string in appsettings.json

**Auth:** Customers use JWT Bearer; DeliveryMen use custom auth_token (random 120-char hex string, passed as query param `token`)

**Key API prefix:** `/api/v1/`

**How to apply:** Open `IzigoBackend.sln` in Visual Studio 2022. Configure DB in appsettings.json. Run migrations if needed.
