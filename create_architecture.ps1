# Scaffolding Script for AuditNode Clean Architecture

# 1. Create Solution
dotnet new sln -n AuditNode

# 2. Create Projects
dotnet new classlib -n AuditNode.Domain
dotnet new classlib -n AuditNode.Application
dotnet new classlib -n AuditNode.Infrastructure
dotnet new webapi -n AuditNode.API

# 3. Add Projects to Solution
dotnet sln AuditNode.sln add AuditNode.Domain/AuditNode.Domain.csproj
dotnet sln AuditNode.sln add AuditNode.Application/AuditNode.Application.csproj
dotnet sln AuditNode.sln add AuditNode.Infrastructure/AuditNode.Infrastructure.csproj
dotnet sln AuditNode.sln add AuditNode.API/AuditNode.API.csproj

# 4. Add Project References
dotnet add AuditNode.API/AuditNode.API.csproj reference AuditNode.Infrastructure/AuditNode.Infrastructure.csproj
dotnet add AuditNode.API/AuditNode.API.csproj reference AuditNode.Application/AuditNode.Application.csproj

dotnet add AuditNode.Infrastructure/AuditNode.Infrastructure.csproj reference AuditNode.Application/AuditNode.Application.csproj
dotnet add AuditNode.Infrastructure/AuditNode.Infrastructure.csproj reference AuditNode.Domain/AuditNode.Domain.csproj

dotnet add AuditNode.Application/AuditNode.Application.csproj reference AuditNode.Domain/AuditNode.Domain.csproj

# 5. Add Packages
dotnet add AuditNode.Infrastructure/AuditNode.Infrastructure.csproj package Npgsql.EntityFrameworkCore.PostgreSQL
dotnet add AuditNode.Infrastructure/AuditNode.Infrastructure.csproj package Microsoft.EntityFrameworkCore.Design

# Cleanup default classes
Remove-Item -Path AuditNode.Domain/Class1.cs -ErrorAction SilentlyContinue
Remove-Item -Path AuditNode.Application/Class1.cs -ErrorAction SilentlyContinue
Remove-Item -Path AuditNode.Infrastructure/Class1.cs -ErrorAction SilentlyContinue

Write-Host "Scaffolding completed."
