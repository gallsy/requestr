# Requestr Database Migrations

This project contains the database migrations for the Requestr application using DbUp.

## Overview

The migration system uses DbUp to manage database schema changes in a consistent and repeatable manner. All SQL scripts are embedded as resources and executed in alphabetical order based on their filename.

## Migration Scripts

The migrations are organized in chronological order:

1. **001_InitialSchema.sql** - Core application schema (FormDefinitions, FormFields, FormRequests, etc.)
2. **002_WorkflowSchema.sql** - Workflow management system
3. **003_BulkRequestSupport.sql** - Bulk request functionality
4. **004_BulkRequestWorkflow.sql** - Workflow integration for bulk requests
5. **005_NotificationSchema.sql** - Email notifications and templates
6. **006_PerformanceOptimizations.sql** - Database indexes and performance improvements
7. **007_FormPermissions.sql** - Role-based access control for forms
8. **008_WorkflowFormConfiguration.sql** - Form-specific workflow configurations
9. **009_SampleData.sql** - Sample reference data for testing

## Configuration

### Connection String

Update the connection string in `appsettings.json`:

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=localhost,1433;Database=RequestrApp;User Id=sa;Password=YourPassword123!;TrustServerCertificate=true;MultipleActiveResultSets=true"
  }
}
```

You can also set the connection string via environment variables:
- `ConnectionStrings__DefaultConnection`

### Command Line

You can override the connection string via command line:
```bash
dotnet run --ConnectionStrings:DefaultConnection="your-connection-string"
```

## Running Migrations

### Using .NET CLI

```bash
# Navigate to the migrations project
cd src/Requestr.Migrations

# Run migrations
dotnet run
```

### Building and Running

```bash
# Build the project
dotnet build

# Run the executable
dotnet run

# Or run the built executable directly
./bin/Debug/net8.0/Requestr.Migrations
```

### Using in CI/CD Pipeline

```bash
# Install dependencies
dotnet restore

# Build the project
dotnet build --configuration Release

# Run migrations
dotnet run --configuration Release
```

## Features

- **Automatic Database Creation**: Creates the database if it doesn't exist
- **Transaction Support**: All migrations run within a transaction
- **Error Handling**: Detailed error messages and stack traces
- **Idempotent**: Safe to run multiple times - only applies new migrations
- **Logging**: Console output showing which migrations were applied
- **Embedded Scripts**: All SQL scripts are embedded as resources for easy deployment

## Adding New Migrations

1. Create a new SQL file in the `Scripts` folder
2. Use the naming convention: `{SequenceNumber:000}_{Description}.sql`
3. Example: `010_AddNewFeature.sql`
4. The file will be automatically included as an embedded resource
5. Build and run the migration tool

## Best Practices

- Always test migrations on a backup/development database first
- Use descriptive names for migration files
- Include comments in SQL scripts explaining the changes
- Keep migrations small and focused on a single feature/change
- Never modify existing migration scripts once they've been deployed
- Use `IF NOT EXISTS` checks for creating new objects
- Use `IF EXISTS` checks before dropping or altering objects

## Troubleshooting

### Migration Fails

If a migration fails:
1. Check the error message in the console output
2. Verify the SQL syntax in the failing script
3. Ensure proper permissions on the database
4. Check that referenced objects exist

### Connection Issues

If you can't connect to the database:
1. Verify the connection string
2. Ensure SQL Server is running
3. Check firewall settings
4. Verify authentication credentials

### Rollback

DbUp doesn't support automatic rollbacks. If you need to rollback:
1. Create a new migration script that reverses the changes
2. Use database backups for major rollbacks

## Schema Tracking

DbUp tracks applied migrations in the `SchemaVersions` table, which is automatically created in your database. This table contains:
- ScriptName: The name of the migration script
- Applied: When the migration was applied

Do not manually modify this table unless you know what you're doing.
