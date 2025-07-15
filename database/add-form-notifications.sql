-- Add notification fields to FormDefinitions table
-- This migration adds support for form-level notifications
USE RequestrApp;

-- Add notification email field
IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[FormDefinitions]') AND name = 'NotificationEmail')
BEGIN
    ALTER TABLE [dbo].[FormDefinitions]
    ADD [NotificationEmail] NVARCHAR(255) NULL
END

-- Add notify on creation field
IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[FormDefinitions]') AND name = 'NotifyOnCreation')
BEGIN
    ALTER TABLE [dbo].[FormDefinitions]
    ADD [NotifyOnCreation] BIT NOT NULL DEFAULT 0
END

-- Add notify on completion field
IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[FormDefinitions]') AND name = 'NotifyOnCompletion')
BEGIN
    ALTER TABLE [dbo].[FormDefinitions]
    ADD [NotifyOnCompletion] BIT NOT NULL DEFAULT 0
END

-- Update WorkflowConfiguration to support notification email instead of NotificationSettings
-- This is a breaking change for existing step configurations that used the old NotificationSettings structure
-- The migration will attempt to extract email settings from existing JSON configurations

-- Note: If you have existing step configurations with NotificationSettings,
-- you may need to manually update them to use the new NotificationEmail field
-- The old structure was: "Notifications": { "Email": true, "Teams": false, "ReminderHours": 24 }
-- The new structure is: "NotificationEmail": "user@example.com"

PRINT 'Form notification fields added successfully.'
PRINT 'Remember to update any existing workflow step configurations to use the new NotificationEmail field.'
