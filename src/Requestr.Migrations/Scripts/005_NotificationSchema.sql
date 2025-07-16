-- Notification System Schema Migration
-- This script creates the notification and email configuration system
-- Migration: 005

USE RequestrApp;
GO

-- Email Configuration table (single row for system-wide settings)
IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='EmailConfiguration' AND xtype='U')
BEGIN
    CREATE TABLE EmailConfiguration (
        Id INT IDENTITY(1,1) PRIMARY KEY,
        Provider INT NOT NULL DEFAULT 0, -- 0 = SMTP, 1 = SendGrid
        IsEnabled BIT NOT NULL DEFAULT 0,
        FromEmail NVARCHAR(255) NULL,
        FromName NVARCHAR(255) NULL,
        
        -- SMTP Settings
        SmtpHost NVARCHAR(255) NULL,
        SmtpPort INT NULL DEFAULT 587,
        SmtpUseSsl BIT NULL DEFAULT 1,
        SmtpUsername NVARCHAR(255) NULL,
        SmtpPassword NVARCHAR(255) NULL,
        
        -- SendGrid Settings
        SendGridApiKey NVARCHAR(255) NULL,
        
        CreatedAt DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
        UpdatedAt DATETIME2 NOT NULL DEFAULT GETUTCDATE()
    );
    PRINT 'EmailConfiguration table created successfully.';
END
GO

-- Notification Templates table
IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='NotificationTemplates' AND xtype='U')
BEGIN
    CREATE TABLE NotificationTemplates (
        Id INT IDENTITY(1,1) PRIMARY KEY,
        Name NVARCHAR(255) NOT NULL,
        TemplateKey NVARCHAR(100) NOT NULL UNIQUE,
        Subject NVARCHAR(500) NOT NULL,
        Body NTEXT NOT NULL,
        IsEnabled BIT NOT NULL DEFAULT 1,
        CreatedAt DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
        UpdatedAt DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
        INDEX IX_NotificationTemplates_TemplateKey (TemplateKey),
        INDEX IX_NotificationTemplates_IsEnabled (IsEnabled)
    );
    PRINT 'NotificationTemplates table created successfully.';
END
GO

-- Insert default notification templates
IF NOT EXISTS (SELECT 1 FROM NotificationTemplates WHERE TemplateKey = 'NEW_REQUEST_CREATED')
BEGIN
    INSERT INTO NotificationTemplates (Name, TemplateKey, Subject, Body, IsEnabled)
    VALUES (
        'New Request Created',
        'NEW_REQUEST_CREATED',
        'New Request: {{FormName}} - {{RequestId}}',
        '<h2>New Request Created</h2>
<p>A new request has been submitted for form: <strong>{{FormName}}</strong></p>
<p><strong>Request ID:</strong> {{RequestId}}</p>
<p><strong>Description:</strong> {{RequestDescription}}</p>
<p><strong>Created by:</strong> {{CreatingUser}} ({{CreatingUserEmail}})</p>
<p><strong>Comments:</strong> {{RequestComments}}</p>
<p><strong>Created Date:</strong> {{RequestCreatedDate}}</p>
<p><a href="{{RequestUrl}}">View Request</a></p>',
        1
    );
END
GO

IF NOT EXISTS (SELECT 1 FROM NotificationTemplates WHERE TemplateKey = 'REQUEST_APPROVED')
BEGIN
    INSERT INTO NotificationTemplates (Name, TemplateKey, Subject, Body, IsEnabled)
    VALUES (
        'Request Approved',
        'REQUEST_APPROVED',
        'Request Approved: {{FormName}} - {{RequestId}}',
        '<h2>Request Approved</h2>
<p>Your request for form: <strong>{{FormName}}</strong> has been approved.</p>
<p><strong>Request ID:</strong> {{RequestId}}</p>
<p><strong>Description:</strong> {{RequestDescription}}</p>
<p><strong>Approved by:</strong> {{ApproverName}} ({{ApproverEmail}})</p>
<p><a href="{{RequestUrl}}">View Request</a></p>',
        1
    );
END
GO

IF NOT EXISTS (SELECT 1 FROM NotificationTemplates WHERE TemplateKey = 'REQUEST_REJECTED')
BEGIN
    INSERT INTO NotificationTemplates (Name, TemplateKey, Subject, Body, IsEnabled)
    VALUES (
        'Request Rejected',
        'REQUEST_REJECTED',
        'Request Rejected: {{FormName}} - {{RequestId}}',
        '<h2>Request Rejected</h2>
<p>Your request for form: <strong>{{FormName}}</strong> has been rejected.</p>
<p><strong>Request ID:</strong> {{RequestId}}</p>
<p><strong>Description:</strong> {{RequestDescription}}</p>
<p><strong>Rejected by:</strong> {{ApproverName}} ({{ApproverEmail}})</p>
<p><a href="{{RequestUrl}}">View Request</a></p>',
        1
    );
END
GO

IF NOT EXISTS (SELECT 1 FROM NotificationTemplates WHERE TemplateKey = 'WORKFLOW_STEP_PENDING')
BEGIN
    INSERT INTO NotificationTemplates (Name, TemplateKey, Subject, Body, IsEnabled)
    VALUES (
        'Workflow Step Pending',
        'WORKFLOW_STEP_PENDING',
        'Action Required: {{WorkflowStepName}} - {{RequestId}}',
        '<h2>Action Required</h2>
<p>A workflow step requires your attention.</p>
<p><strong>Workflow:</strong> {{WorkflowName}}</p>
<p><strong>Step:</strong> {{WorkflowStepName}}</p>
<p><strong>Request ID:</strong> {{RequestId}}</p>
<p><strong>Form:</strong> {{FormName}}</p>
<p><strong>Description:</strong> {{RequestDescription}}</p>
<p><a href="{{RequestUrl}}">Take Action</a></p>',
        1
    );
END
GO

-- Add notification columns to FormDefinitions if they don't exist
IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('FormDefinitions') AND name = 'NotificationEmail')
BEGIN
    ALTER TABLE FormDefinitions ADD NotificationEmail nvarchar(255) NULL;
    PRINT 'Added NotificationEmail column to FormDefinitions table.';
END
GO

IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('FormDefinitions') AND name = 'NotifyOnCreation')
BEGIN
    ALTER TABLE FormDefinitions ADD NotifyOnCreation bit NOT NULL DEFAULT 0;
    PRINT 'Added NotifyOnCreation column to FormDefinitions table.';
END
GO

IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('FormDefinitions') AND name = 'NotifyOnCompletion')
BEGIN
    ALTER TABLE FormDefinitions ADD NotifyOnCompletion bit NOT NULL DEFAULT 0;
    PRINT 'Added NotifyOnCompletion column to FormDefinitions table.';
END
GO

PRINT 'Notification system schema created successfully.';
GO
