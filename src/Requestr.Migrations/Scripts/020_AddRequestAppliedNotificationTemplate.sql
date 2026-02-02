-- Add REQUEST_APPLIED notification template
-- This template is sent to the requestor when their request has been applied to the database
-- Migration: 020

USE Requestr;
GO

IF NOT EXISTS (SELECT 1 FROM NotificationTemplates WHERE TemplateKey = 'REQUEST_APPLIED')
BEGIN
    INSERT INTO NotificationTemplates (Name, TemplateKey, Subject, Body, IsEnabled)
    VALUES (
        'Request Applied to Database',
        'REQUEST_APPLIED',
        'Your Request Has Been Applied: {{FormName}} - {{RequestId}}',
        '<!DOCTYPE html>
<html>
<head>
    <meta charset="utf-8">
    <meta name="viewport" content="width=device-width, initial-scale=1.0">
    <title>Request Applied</title>
    <style>
        body { font-family: ''Segoe UI'', Tahoma, Geneva, Verdana, sans-serif; line-height: 1.6; color: #333; margin: 0; padding: 20px; background-color: #f8f9fa; }
        .container { max-width: 600px; margin: 0 auto; background: white; border-radius: 8px; box-shadow: 0 2px 10px rgba(0,0,0,0.1); overflow: hidden; }
        .header { background: linear-gradient(135deg, #198754, #157347); color: white; padding: 30px 20px; text-align: center; }
        .header h1 { margin: 0; font-size: 24px; font-weight: 600; }
        .content { padding: 30px 20px; }
        .success-banner { background: #d1e7dd; border: 1px solid #badbcc; color: #0f5132; padding: 15px; border-radius: 6px; margin-bottom: 20px; text-align: center; font-weight: 500; }
        .info-grid { display: grid; gap: 15px; margin: 20px 0; }
        .info-item { background: #f8f9fa; padding: 15px; border-radius: 6px; border-left: 4px solid #198754; }
        .info-label { font-weight: 600; color: #495057; font-size: 14px; margin-bottom: 5px; }
        .info-value { color: #212529; font-size: 16px; }
        .action-button { display: inline-block; background: #198754; color: white; padding: 12px 24px; text-decoration: none; border-radius: 6px; font-weight: 500; margin: 20px 0; }
        .footer { background: #f8f9fa; padding: 20px; text-align: center; color: #6c757d; font-size: 14px; border-top: 1px solid #dee2e6; }
    </style>
</head>
<body>
    <div class="container">
        <div class="header">
            <h1>Request Applied Successfully</h1>
            <p style="margin: 10px 0 0 0; opacity: 0.9;">Your changes are now live in the database</p>
        </div>
        <div class="content">
            <div class="success-banner">
                Great news! Your request has been approved and successfully applied to the database.
            </div>
            <div class="info-grid">
                <div class="info-item">
                    <div class="info-label">Form Name</div>
                    <div class="info-value">{{FormName}}</div>
                </div>
                <div class="info-item">
                    <div class="info-label">Request ID</div>
                    <div class="info-value">{{RequestId}}</div>
                </div>
                <div class="info-item">
                    <div class="info-label">Request Type</div>
                    <div class="info-value">{{RequestType}}</div>
                </div>
                <div class="info-item">
                    <div class="info-label">Applied Date</div>
                    <div class="info-value">{{AppliedDate}}</div>
                </div>
            </div>
            <div style="text-align: center;">
                <a href="{{RequestUrl}}" class="action-button">View Request Details</a>
            </div>
            <div style="background: #cff4fc; border: 1px solid #9eeaf9; color: #055160; padding: 15px; border-radius: 6px; margin-top: 20px;">
                <strong>What happened:</strong> Your requested changes have been written to the database. The data is now available for use in downstream systems.
            </div>
        </div>
        <div class="footer">
            <p>This is an automated notification from {{SystemName}}.<br>
            Please do not reply to this email.</p>
        </div>
    </div>
</body>
</html>',
        1
    );
    PRINT 'Added REQUEST_APPLIED notification template.';
END
ELSE
BEGIN
    PRINT 'REQUEST_APPLIED notification template already exists.';
END
GO

PRINT 'Migration 020 completed successfully.';
GO
