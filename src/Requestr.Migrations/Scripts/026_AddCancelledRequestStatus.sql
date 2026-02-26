-- Migration: Add Cancelled status (value 5) to RequestStatus
-- The RequestStatus enum now supports: Pending(0), Approved(1), Rejected(2), Applied(3), Failed(4), Cancelled(5)
-- The FormRequestChangeType enum now supports Cancelled(12)
-- No schema changes needed - Status columns already store integers and accept any int value.
-- This migration is a no-op placeholder for documentation purposes.

PRINT 'RequestStatus.Cancelled (5) and FormRequestChangeType.Cancelled (12) are now supported by the application.'
