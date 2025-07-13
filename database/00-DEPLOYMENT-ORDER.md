# Database Deployment Order

## For New Database Installations

Run the SQL scripts in this exact order for a clean deployment:

1. `requestr-schema.sql` - Main application schema (with integer enums)
2. `sample-data.sql` - Sample data for testing
3. `workflow-schema.sql` - Workflow system schema (with integer enums)
4. `workflow-performance-optimizations.sql` - Workflow performance improvements
5. `performance-optimizations.sql` - General performance improvements
6. `add-bulk-request-support.sql` - Bulk operations support (with integer enums)

## For Existing Database Upgrades

If you have an existing database with string-based enums, run this additional script:

7. `enum-standardization-migration.sql` - Convert existing string enums to integers

**Note**: All new schema files now use integer-based enum storage by default. The migration script is only needed for databases created before July 13, 2025.