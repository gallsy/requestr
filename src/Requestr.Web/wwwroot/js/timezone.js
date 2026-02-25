// Returns the browser's IANA timezone identifier (e.g. "Australia/Sydney", "Europe/London")
window.getUserTimezone = () => Intl.DateTimeFormat().resolvedOptions().timeZone;
