-- Form Sections and Grid Layout Enhancement
-- This migration adds support for form sections and grid-based field positioning

-- Create FormSections table
CREATE TABLE FormSections (
    Id INT PRIMARY KEY IDENTITY(1,1),
    FormDefinitionId INT NOT NULL,
    Name NVARCHAR(255) NOT NULL,
    Description NVARCHAR(MAX) NULL,
    DisplayOrder INT NOT NULL DEFAULT 0,
    IsCollapsible BIT DEFAULT 0,
    DefaultExpanded BIT DEFAULT 1,
    VisibilityCondition NVARCHAR(MAX) NULL,
    MaxColumns INT DEFAULT 12,
    CreatedAt DATETIME2 DEFAULT GETUTCDATE(),
    UpdatedAt DATETIME2 DEFAULT GETUTCDATE(),
    CreatedBy NVARCHAR(255) NULL,
    UpdatedBy NVARCHAR(255) NULL,
    CONSTRAINT FK_FormSections_FormDefinitions FOREIGN KEY (FormDefinitionId) REFERENCES FormDefinitions(Id) ON DELETE CASCADE
);

-- Add grid positioning columns to FormFields
ALTER TABLE FormFields ADD GridRow INT DEFAULT 1;
ALTER TABLE FormFields ADD GridColumn INT DEFAULT 1;
ALTER TABLE FormFields ADD GridColumnSpan INT DEFAULT 6;
ALTER TABLE FormFields ADD FormSectionId INT NULL;

-- Add foreign key constraint for FormSectionId
ALTER TABLE FormFields ADD CONSTRAINT FK_FormFields_FormSections 
    FOREIGN KEY (FormSectionId) REFERENCES FormSections(Id) ON DELETE NO ACTION;

-- Create indexes for performance
CREATE INDEX IX_FormSections_FormDefinitionId_DisplayOrder ON FormSections(FormDefinitionId, DisplayOrder);
CREATE INDEX IX_FormFields_FormSectionId_GridRow_GridColumn ON FormFields(FormSectionId, GridRow, GridColumn);

-- Migrate existing forms to use sections
-- This creates a default section for each existing form and assigns all fields to it
INSERT INTO FormSections (FormDefinitionId, Name, DisplayOrder, MaxColumns)
SELECT DISTINCT 
    fd.Id,
    CASE 
        WHEN fd.Name IS NOT NULL AND fd.Name != '' THEN fd.Name + ' Fields'
        ELSE 'Form Fields'
    END,
    0,
    12
FROM FormDefinitions fd
WHERE fd.Id IN (SELECT DISTINCT FormDefinitionId FROM FormFields);

-- Update existing FormFields to reference the default section and set grid positions
UPDATE ff
SET 
    FormSectionId = fs.Id,
    GridRow = ((ff.DisplayOrder - 1) / 2) + 1,  -- 2 fields per row
    GridColumn = CASE WHEN (ff.DisplayOrder - 1) % 2 = 0 THEN 1 ELSE 7 END,  -- First field starts at column 1, second at column 7
    GridColumnSpan = 6  -- Each field spans 6 columns (half width)
FROM FormFields ff
INNER JOIN FormSections fs ON ff.FormDefinitionId = fs.FormDefinitionId
WHERE ff.FormSectionId IS NULL;

-- Add some sample data comments
-- The migration preserves existing form layouts by:
-- 1. Creating a default section for each form
-- 2. Arranging existing fields in a 2-column grid (6 columns each)
-- 3. Maintaining the original DisplayOrder by converting it to grid positions
