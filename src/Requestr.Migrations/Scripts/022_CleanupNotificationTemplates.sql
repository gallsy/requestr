-- Clean up notification templates to only have the 3 essential templates
-- This migration ensures we only have:
-- 1. WORKFLOW_STEP_PENDING - Sent to approvers when a step needs their attention
-- 2. REQUEST_APPROVED - Sent to requestor when workflow completes successfully
-- 3. REQUEST_REJECTED - Sent to requestor if the request is rejected

-- First, drop all existing notification templates
DELETE FROM NotificationTemplates;
PRINT 'Cleared all existing notification templates.';
GO

-- 1. WORKFLOW_STEP_PENDING - Orange theme (sent to approvers)
INSERT INTO NotificationTemplates (Name, TemplateKey, Subject, Body, IsEnabled)
VALUES (
    'Workflow Step Pending',
    'WORKFLOW_STEP_PENDING',
    'Action Required: {{WorkflowStepName}} - Request {{RequestId}}',
    '<!DOCTYPE html>
<html>
<head>
    <meta charset="utf-8">
    <meta name="viewport" content="width=device-width, initial-scale=1.0">
    <style>
        body { font-family: ''Segoe UI'', Tahoma, Geneva, Verdana, sans-serif; line-height: 1.6; color: #333; margin: 0; padding: 20px; background-color: #f5f5f5; }
        .container { max-width: 600px; margin: 0 auto; background: white; border-radius: 8px; overflow: hidden; border: 1px solid #ddd; }
        .header { background: #001947; color: white; padding: 24px; text-align: center; }
        .header h1 { margin: 0; font-size: 22px; font-weight: 600; }
        .content { padding: 24px; }
        table { width: 100%; border-collapse: collapse; margin: 16px 0; }
        td { padding: 10px 0; vertical-align: top; }
        td.label { color: #666; width: 140px; font-weight: 500; }
        td.value { color: #333; }
        .divider { border-top: 1px solid #eee; }
        .btn { display: inline-block; background: #001947; color: white; padding: 12px 28px; text-decoration: none; border-radius: 6px; font-weight: 500; margin-top: 16px; }
        .footer { background: #f9f9f9; padding: 16px 24px; text-align: center; color: #888; font-size: 13px; border-top: 1px solid #eee; }
    </style>
</head>
<body>
    <div class="container">
        <div class="header">
            <h1>Action Required</h1>
        </div>
        <div class="content">
            <table>
                <tr class="divider"><td class="label">Form</td><td class="value">{{FormName}}</td></tr>
                <tr class="divider"><td class="label">Request ID</td><td class="value">{{RequestId}}</td></tr>
                <tr class="divider"><td class="label">Requested by</td><td class="value">{{CreatingUser}}</td></tr>
                <tr class="divider"><td class="label">Comment</td><td class="value">{{RequestComments}}</td></tr>
            </table>
            <div style="text-align: center;">
                <a href="{{RequestUrl}}" class="btn">Review Request</a>
            </div>
        </div>
        <div class="footer">
            Automated notification from {{SystemName}}
        </div>
    </div>
</body>
</html>',
    1
);
PRINT 'Added WORKFLOW_STEP_PENDING template.';
GO

-- 2. REQUEST_APPROVED - Green theme (sent to requestor when workflow completes successfully)
INSERT INTO NotificationTemplates (Name, TemplateKey, Subject, Body, IsEnabled)
VALUES (
    'Request Approved',
    'REQUEST_APPROVED',
    'Request Approved: {{FormName}} - {{RequestId}}',
    '<!DOCTYPE html>
<html>
<head>
    <meta charset="utf-8">
    <meta name="viewport" content="width=device-width, initial-scale=1.0">
    <style>
        body { font-family: ''Segoe UI'', Tahoma, Geneva, Verdana, sans-serif; line-height: 1.6; color: #333; margin: 0; padding: 20px; background-color: #f5f5f5; }
        .container { max-width: 600px; margin: 0 auto; background: white; border-radius: 8px; overflow: hidden; border: 1px solid #ddd; }
        .header { background: #001947; color: white; padding: 24px; text-align: center; }
        .header h1 { margin: 0; font-size: 22px; font-weight: 600; }
        .content { padding: 24px; }
        table { width: 100%; border-collapse: collapse; margin: 16px 0; }
        td { padding: 10px 0; vertical-align: top; }
        td.label { color: #666; width: 140px; font-weight: 500; }
        td.value { color: #333; }
        .divider { border-top: 1px solid #eee; }
        .btn { display: inline-block; background: #001947; color: white; padding: 12px 28px; text-decoration: none; border-radius: 6px; font-weight: 500; margin-top: 16px; }
        .footer { background: #f9f9f9; padding: 16px 24px; text-align: center; color: #888; font-size: 13px; border-top: 1px solid #eee; }
    </style>
</head>
<body>
    <div class="container">
        <div class="header">
            <h1>Request Approved</h1>
        </div>
        <div class="content">
            <p style="margin-top: 0;">Your request has been approved and the changes have been applied.</p>
            <table>
                <tr><td class="label">Form</td><td class="value">{{FormName}}</td></tr>
                <tr class="divider"><td class="label">Request ID</td><td class="value">{{RequestId}}</td></tr>
                <tr class="divider"><td class="label">Approved by</td><td class="value">{{ApproverName}}</td></tr>
                <tr class="divider"><td class="label">Comments</td><td class="value">{{ApproverComments}}</td></tr>
            </table>
            <div style="text-align: center;">
                <a href="{{RequestUrl}}" class="btn">View Details</a>
            </div>
        </div>
        <div class="footer">
            Automated notification from {{SystemName}}
        </div>
    </div>
</body>
</html>',
    1
);
PRINT 'Added REQUEST_APPROVED template.';
GO

-- 3. REQUEST_REJECTED - Red theme (sent to requestor if the request is rejected)
INSERT INTO NotificationTemplates (Name, TemplateKey, Subject, Body, IsEnabled)
VALUES (
    'Request Rejected',
    'REQUEST_REJECTED',
    'Request Cancelled: {{FormName}} - {{RequestId}}',
    '<!DOCTYPE html>
<html>
<head>
    <meta charset="utf-8">
    <meta name="viewport" content="width=device-width, initial-scale=1.0">
    <style>
        body { font-family: ''Segoe UI'', Tahoma, Geneva, Verdana, sans-serif; line-height: 1.6; color: #333; margin: 0; padding: 20px; background-color: #f5f5f5; }
        .container { max-width: 600px; margin: 0 auto; background: white; border-radius: 8px; overflow: hidden; border: 1px solid #ddd; }
        .header { background: #001947; color: white; padding: 24px; text-align: center; }
        .header h1 { margin: 0; font-size: 22px; font-weight: 600; }
        .content { padding: 24px; }
        table { width: 100%; border-collapse: collapse; margin: 16px 0; }
        td { padding: 10px 0; vertical-align: top; }
        td.label { color: #666; width: 140px; font-weight: 500; }
        td.value { color: #333; }
        .divider { border-top: 1px solid #eee; }
        .btn { display: inline-block; background: #001947; color: white; padding: 12px 28px; text-decoration: none; border-radius: 6px; font-weight: 500; margin-top: 16px; }
        .footer { background: #f9f9f9; padding: 16px 24px; text-align: center; color: #888; font-size: 13px; border-top: 1px solid #eee; }
    </style>
</head>
<body>
    <div class="container">
        <div class="header">
            <h1>Request Cancelled</h1>
        </div>
        <div class="content">
            <p style="margin-top: 0;">Your request has been cancelled.</p>
            <table>
                <tr><td class="label">Form</td><td class="value">{{FormName}}</td></tr>
                <tr class="divider"><td class="label">Request ID</td><td class="value">{{RequestId}}</td></tr>
                <tr class="divider"><td class="label">Cancelled by</td><td class="value">{{ApproverName}}</td></tr>
                <tr class="divider"><td class="label">Reason</td><td class="value">{{ApproverComments}}</td></tr>
            </table>
            <div style="text-align: center;">
                <a href="{{RequestUrl}}" class="btn">View Details</a>
            </div>
        </div>
        <div class="footer">
            Automated notification from {{SystemName}}
        </div>
    </div>
</body>
</html>',
    1
);
PRINT 'Added REQUEST_REJECTED template.';
GO

PRINT 'Notification templates cleanup complete - only 3 essential templates remain.';
GO
