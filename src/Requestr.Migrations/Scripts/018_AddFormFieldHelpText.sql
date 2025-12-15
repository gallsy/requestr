-- Migration: Add HelpText column to FormFields table
-- Allows form builders to add tooltip content that displays as an info icon on field labels

ALTER TABLE FormFields ADD HelpText NVARCHAR(500) NULL;
GO
