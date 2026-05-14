-- Add IsUnique column to FormFields table
ALTER TABLE FormFields ADD IsUnique BIT NOT NULL DEFAULT 0;
GO
