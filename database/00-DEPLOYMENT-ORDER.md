# Database Deployment Order

## For New Database Installations

Run the SQL scripts in this exact order for a clean deployment:

1. `requestr-schema.sql` - Main application schema (with integer enums)
2. `sample-data.sql` - Sample data for testing
3. `workflow-schema.sql` - Workflow system schema (with integer enums)
4. `workflow-performance-optimizations.sql` - Workflow performance improvements
5. `performance-optimizations.sql` - General performance improvements
6. `add-bulk-request-support.sql` - Bulk operations support (with integer enums)
7. `bulk-request-items-migration.sql` - Bulk request items table
8. `add-workflow-formrequest-id-to-bulk.sql` - Add workflow form request ID to bulk requests

## For Existing Database Upgrades

If you have an existing database, apply these migrations in order:

1. `bulk-request-items-migration.sql` - Add BulkFormRequestItems table
2. `add-workflow-formrequest-id-to-bulk.sql` - Add workflow IDs to bulk requests

If you have an existing database with string-based enums, also run:
3. `enum-standardization-migration.sql` - Convert existing string enums to integers

**Note**: All new schema files now use integer-based enum storage by default. The enum migration script is only needed for databases created before July 13, 2025.