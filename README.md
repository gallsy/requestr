# Requestr - Database-First Forms Application

A modern Blazor .NET 8 application that enables administrators to build dynamic forms from database schemas with built-in approval workflows using Entra ID roles.

## ✨ Features

- **Database-First Form Building**: Create forms by connecting to databases and selecting tables
- **Drag-and-Drop Form Designer**: Intuitive form builder with validation rules and field customization
- **Approval Workflows**: Group-based approvals using Entra ID application roles
- **Multi-Database Support**: Connect to multiple databases simultaneously
- **Request Tracking**: Track all form submissions and their approval status
- **Modern UI**: Built with BlazorBootstrap for a responsive interface

## 🚀 Quick Start

### Prerequisites
- Docker & Docker Compose
- .NET 8 SDK (for development)

### Running with Docker (Recommended)

1. **Clone the repository:**
   ```bash
   git clone https://github.com/yourusername/requestr.git
   cd requestr
   ```

2. **Start the complete stack:**
   ```bash
   # This will start SQL Server, run migrations, then start the web app
   docker compose up -d
   ```

3. **Access the application:**
   - Web Application: http://localhost:8080
   - SQL Server: localhost:1433 (sa/DevPassword123!)

**Note:** The first startup may take a few minutes as it builds the containers and runs database migrations.

#### Alternative: Step-by-step startup

If you prefer more control over the startup process:

```bash
# Start SQL Server first
docker compose up -d sqlserver

# Run migrations (optional - they run automatically with full stack)
./migrate.sh --docker

# Start the web application
docker compose up -d requestr-web
```

### Local Development

1. **Start SQL Server:**
   ```bash
   docker run -e "ACCEPT_EULA=Y" -e "SA_PASSWORD=DevPassword123!" \
     -p 1433:1433 -d mcr.microsoft.com/mssql/server:2022-latest
   ```

2. **Run database migrations:**
   ```bash
   # Update connection string in src/Requestr.Migrations/appsettings.json if needed
   cd src/Requestr.Migrations
   dotnet run
   ```

3. **Run the application:**
   ```bash
   dotnet restore
   cd src/Requestr.Web
   dotnet run
   ```

4. **Access at:** https://localhost:5001

## 🛠️ Technology Stack

- **Backend**: .NET 8, Blazor Server
- **Data Access**: Dapper
- **Database**: Microsoft SQL Server 2022
- **Authentication**: Microsoft Entra ID
- **UI Framework**: BlazorBootstrap
- **Containerization**: Docker & Docker Compose

## 📁 Project Structure

```
src/
├── Requestr.Core/           # Core business logic and data access
│   ├── Models/              # Domain models and DTOs
│   ├── Interfaces/          # Service contracts
│   ├── Services/            # Service implementations
│   └── Extensions/          # Dependency injection setup
├── Requestr.Migrations/     # Database migration tool (DbUp)
│   ├── Scripts/             # SQL migration scripts
│   ├── Program.cs           # Migration runner
│   └── README.md            # Migration documentation
└── Requestr.Web/            # Blazor web application
    ├── Pages/               # Razor pages and components
    ├── Shared/              # Shared UI components
    └── wwwroot/             # Static files
```

## 📦 Database Migrations

The project uses DbUp for database schema management. All database changes are managed through versioned SQL scripts.

### Migration Scripts Location
- `src/Requestr.Migrations/Scripts/` - Contains all migration scripts
- Scripts are executed in alphabetical order based on filename
- Each script is only run once and tracked in the database

### Running Migrations

#### Local Development
```bash
# Quick way - using the provided script
./migrate.sh

# Docker way - using containers
./migrate.sh --docker

# Manual way
cd src/Requestr.Migrations
dotnet run

# With custom connection string
dotnet run --ConnectionStrings:DefaultConnection="your-connection-string"
```

#### Docker Compose
```bash
# Full stack (recommended for first-time setup)
docker compose up -d

# Just migrations
./migrate.sh --docker

# Individual services
docker compose up -d sqlserver    # Just SQL Server
docker compose up migrations      # Just migrations
docker compose up -d requestr-web # Just web app
```

### Docker Compose Configuration

The project uses a single `docker-compose.yml` file optimized for local development:

- **SQL Server** with health checks to ensure it's ready before migrations
- **Automatic migrations** that run before the web application starts
- **Web application** that depends on successful migration completion
- **Volume persistence** for SQL Server data
- **Development-friendly** environment variables and ports

### Adding New Migrations
1. Create a new SQL file in `src/Requestr.Migrations/Scripts/`
2. Use naming convention: `{SequenceNumber:000}_{Description}.sql`
3. Example: `010_AddNewFeature.sql`
4. Run the migration tool to apply changes

For detailed migration documentation, see: `src/Requestr.Migrations/README.md`

## ⚙️ Configuration

### Development
The default `docker-compose.yml` includes development-friendly settings with sample data.

### Production
For production deployment with Entra ID:

1. **Set up Entra ID App Registration**
2. **Configure application settings:**
   ```json
   {
     "AzureAd": {
       "TenantId": "your-tenant-id",
       "ClientId": "your-client-id",
       "ClientSecret": "your-client-secret"
     }
   }
   ```
3. **Configure database connections**

## 🔐 Security & Roles

The application uses Entra ID roles for authorization:
- **Admin** - Full access to form management
- **FormAdmin** - Can create and manage forms
- **DataAdmin** - Can approve data change requests
- **ReferenceDataApprover** - Can approve reference data changes

## 📝 Usage

1. **Administrators** create forms by selecting database tables and configuring fields
2. **Users** fill out forms to request data changes
3. **Approvers** review and approve/reject requests
4. **Approved changes** are automatically applied to the target database

## 🤝 Contributing

We welcome contributions! Please see our [Contributing Guide](CONTRIBUTING.md) for details.

1. Fork the repository
2. Create a feature branch
3. Make your changes
4. Submit a pull request

## 📄 License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.

## 🆘 Support

- **Issues**: Create an issue in this repository
- **Documentation**: Check the `/docs` folder for detailed guides
- **Discussions**: Use GitHub Discussions for questions

## 🚧 Project Status

This project is actively maintained and ready for production use. See the [CHANGELOG](CHANGELOG.md) for recent updates.
