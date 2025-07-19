/**
 * Utility functions for date parsing and formatting
 */

/**
 * Safely parse a date from various input formats
 * @param dateInput - Date string, number, or Date object
 * @returns Valid Date object or null if parsing fails
 */
export function safeParseDate(dateInput: string | number | Date | null | undefined): Date | null {
  if (!dateInput) {
    return null;
  }

  try {
    const date = new Date(dateInput);
    
    // Check if the date is valid
    if (isNaN(date.getTime())) {
      console.warn('Invalid date input:', dateInput);
      return null;
    }
    
    return date;
  } catch (error) {
    console.warn('Error parsing date:', dateInput, error);
    return null;
  }
}

/**
 * Format a date for display with fallback
 * @param dateInput - Date string, number, or Date object
 * @param fallback - Fallback text if date is invalid
 * @returns Formatted date string or fallback
 */
export function formatDate(dateInput: string | number | Date | null | undefined, fallback: string = 'Unknown'): string {
  const date = safeParseDate(dateInput);
  
  if (!date) {
    return fallback;
  }
  
  return date.toLocaleString();
}

/**
 * Format a date as relative time (e.g., "2 minutes ago")
 * @param dateInput - Date string, number, or Date object
 * @param fallback - Fallback text if date is invalid
 * @returns Relative time string or fallback
 */
export function formatRelativeTime(dateInput: string | number | Date | null | undefined, fallback: string = 'Unknown'): string {
  const date = safeParseDate(dateInput);
  
  if (!date) {
    return fallback;
  }
  
  const now = new Date();
  const diff = now.getTime() - date.getTime();
  const seconds = Math.floor(diff / 1000);
  const minutes = Math.floor(seconds / 60);
  const hours = Math.floor(minutes / 60);
  const days = Math.floor(hours / 24);
  
  if (seconds < 60) {
    return 'Just now';
  } else if (minutes < 60) {
    return `${minutes} minute${minutes !== 1 ? 's' : ''} ago`;
  } else if (hours < 24) {
    return `${hours} hour${hours !== 1 ? 's' : ''} ago`;
  } else {
    return `${days} day${days !== 1 ? 's' : ''} ago`;
  }
}

/**
 * Format a date as ISO string
 * @param dateInput - Date string, number, or Date object
 * @returns ISO string or empty string if invalid
 */
export function formatISO(dateInput: string | number | Date | null | undefined): string {
  const date = safeParseDate(dateInput);
  return date ? date.toISOString() : '';
} 