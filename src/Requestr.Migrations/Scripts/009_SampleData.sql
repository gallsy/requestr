-- Sample Reference Data for Testing
-- This script creates sample tables that can be managed through the Requestr application
-- Migration: 009

USE RequestrApp;
GO

-- Countries reference table
IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='Countries' AND xtype='U')
BEGIN
    CREATE TABLE Countries (
        CountryCode varchar(3) PRIMARY KEY,
        CountryName nvarchar(255) NOT NULL,
        ISO2Code varchar(2) NOT NULL,
        ISO3Code varchar(3) NOT NULL,
        Region nvarchar(100) NULL,
        IsActive bit NOT NULL DEFAULT 1,
        CreatedAt datetime2 NOT NULL DEFAULT GETUTCDATE(),
        UpdatedAt datetime2 NULL,
        UNIQUE (ISO2Code),
        UNIQUE (ISO3Code),
        INDEX IX_Countries_CountryName (CountryName),
        INDEX IX_Countries_Region (Region),
        INDEX IX_Countries_IsActive (IsActive)
    );
    PRINT 'Countries table created successfully.';
END
GO

-- Insert sample countries data
IF NOT EXISTS (SELECT * FROM Countries WHERE CountryCode = 'AUS')
BEGIN
    INSERT INTO Countries (CountryCode, CountryName, ISO2Code, ISO3Code, Region, IsActive)
    VALUES 
        ('AUS', 'Australia', 'AU', 'AUS', 'Oceania', 1),
        ('USA', 'United States of America', 'US', 'USA', 'North America', 1),
        ('CAN', 'Canada', 'CA', 'CAN', 'North America', 1),
        ('GBR', 'United Kingdom', 'GB', 'GBR', 'Europe', 1),
        ('FRA', 'France', 'FR', 'FRA', 'Europe', 1),
        ('DEU', 'Germany', 'DE', 'DEU', 'Europe', 1),
        ('JPN', 'Japan', 'JP', 'JPN', 'Asia', 1),
        ('CHN', 'China', 'CN', 'CHN', 'Asia', 1),
        ('IND', 'India', 'IN', 'IND', 'Asia', 1),
        ('BRA', 'Brazil', 'BR', 'BRA', 'South America', 1);
    PRINT 'Sample countries data inserted.';
END
GO

-- Departments reference table
IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='Departments' AND xtype='U')
BEGIN
    CREATE TABLE Departments (
        DepartmentId int IDENTITY(1,1) PRIMARY KEY,
        DepartmentCode nvarchar(20) NOT NULL,
        DepartmentName nvarchar(255) NOT NULL,
        Description nvarchar(max) NULL,
        ManagerName nvarchar(255) NULL,
        BudgetAmount decimal(18,2) NULL,
        IsActive bit NOT NULL DEFAULT 1,
        CreatedAt datetime2 NOT NULL DEFAULT GETUTCDATE(),
        UpdatedAt datetime2 NULL,
        UNIQUE (DepartmentCode),
        INDEX IX_Departments_DepartmentName (DepartmentName),
        INDEX IX_Departments_IsActive (IsActive)
    );
    PRINT 'Departments table created successfully.';
END
GO

-- Insert sample departments data
IF NOT EXISTS (SELECT * FROM Departments WHERE DepartmentCode = 'HR')
BEGIN
    INSERT INTO Departments (DepartmentCode, DepartmentName, Description, ManagerName, BudgetAmount, IsActive)
    VALUES 
        ('HR', 'Human Resources', 'Manages employee relations, recruitment, and policies', 'Sarah Johnson', 250000.00, 1),
        ('IT', 'Information Technology', 'Manages technology infrastructure and software development', 'Michael Chen', 500000.00, 1),
        ('FIN', 'Finance', 'Manages financial operations, budgeting, and accounting', 'David Smith', 300000.00, 1),
        ('MKT', 'Marketing', 'Manages marketing campaigns, brand management, and customer engagement', 'Emily Rodriguez', 400000.00, 1),
        ('OPS', 'Operations', 'Manages day-to-day business operations and processes', 'James Wilson', 350000.00, 1),
        ('RND', 'Research and Development', 'Manages product research, development, and innovation', 'Dr. Lisa Chang', 600000.00, 1);
    PRINT 'Sample departments data inserted.';
END
GO

-- Insert a default database connection for the sample data
IF NOT EXISTS (SELECT * FROM DatabaseConnections WHERE Name = 'DefaultConnection')
BEGIN
    INSERT INTO DatabaseConnections (Name, Description, ConnectionString, DatabaseType, IsActive, CreatedBy)
    VALUES (
        'DefaultConnection',
        'Default connection to the main RequestrApp database',
        'Server=localhost,1433;Database=RequestrApp;User Id=sa;Password=YourPassword123!;TrustServerCertificate=true;MultipleActiveResultSets=true',
        0, -- SqlServer
        1,
        'System Migration'
    );
    PRINT 'Default database connection inserted.';
END
GO

PRINT 'Sample reference data created successfully!';
GO
