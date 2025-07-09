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

2. **Update configuration:**
   - Edit `docker-compose.yml` and replace `your-tenant-id-here` with your Azure AD tenant ID
   - For development, you can use the default configuration

3. **Start the application:**
   ```bash
   docker compose up -d
   ```

4. **Access the application:**
   - Web Application: http://localhost:8080
   - SQL Server: localhost:1433 (sa/DevPassword123!)

### Local Development

1. **Start SQL Server:**
   ```bash
   docker run -e "ACCEPT_EULA=Y" -e "SA_PASSWORD=DevPassword123!" \
     -p 1433:1433 -d mcr.microsoft.com/mssql/server:2022-latest
   ```

2. **Run the application:**
   ```bash
   dotnet restore
   cd src/Requestr.Web
   dotnet run
   ```

3. **Access at:** https://localhost:5001

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
└── Requestr.Web/            # Blazor web application
    ├── Pages/               # Razor pages and components
    ├── Shared/              # Shared UI components
    └── wwwroot/             # Static files
```

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
