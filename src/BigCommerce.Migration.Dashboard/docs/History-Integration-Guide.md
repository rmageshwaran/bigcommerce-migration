# History & Rollback Tab Integration Guide

## Overview

The History & Rollback tab has been successfully integrated with real API data from the BigCommerce Migration backend. This integration replaces the previous mock data implementation with a fully functional search and filtering system.

## Features Implemented

### 1. Real API Integration
- **Endpoint**: `GET /api/migrations/history`
- **Service**: `apiService.getMigrationHistory()`
- **Response Format**: Paginated response with migration details

### 2. Advanced Filtering
- **Date Range**: From/To date pickers with proper date formatting
- **Request ID**: Search by migration ID (supports partial matching)
- **Status**: Filter by migration status (Pending, Running, Completed, Failed, Cancelled)
- **Source Store**: Filter by source store ID
- **Destination Store**: Filter by destination store ID
- **Entity Type**: Filter by specific entity types (brands, categories, products, etc.)

### 3. Search Functionality
- **Apply Filter Button**: Executes search with current filter criteria
- **Reset Button**: Clears all filters and resets to default state
- **Real-time Updates**: Results update immediately when filters are applied

### 4. Enhanced UI Components
- **Loading States**: Shows loading spinner during API calls
- **Error Handling**: Displays error messages for failed API calls
- **Pagination**: Full pagination support with page navigation
- **Status Indicators**: Color-coded status chips for easy identification
- **Action Buttons**: View details and export functionality

## API Integration Details

### Request Parameters
```typescript
interface FilterState {
  startDate: Date | null;
  endDate: Date | null;
  requestId: string;
  status: MigrationStatus | '';
  sourceStore: string;
  destinationStore: string;
  entityType: string;
}
```

### Response Structure
```typescript
interface MigrationHistoryItem {
  migrationId: string;
  sourceStore: string;
  destinationStore: string;
  startedAt: string;
  completedAt: string;
  status: MigrationStatus;
  totalEntities: number;
  processedEntities: number;
  successfulEntities: number;
  failedEntities: number;
  percentageCompleted: number;
  entities: string[];
}
```

### API Endpoint Mapping
- **Frontend**: `HistoryView` component
- **API Service**: `apiService.getMigrationHistory()`
- **Backend**: `GET /api/migrations/history` (MigrationHttpFunctions.cs)

## Component Structure

### HistoryView Component
```typescript
// Location: src/components/Views/HistoryView.tsx
export const HistoryView: React.FC = () => {
  // State management for filters, pagination, and data
  // API integration with error handling
  // UI rendering with Material-UI components
}
```

### Key Features
1. **Filter Management**: Comprehensive filtering system with date pickers
2. **API Integration**: Real-time data fetching with proper error handling
3. **Pagination**: Server-side pagination with navigation controls
4. **Responsive Design**: Mobile-friendly layout with responsive breakpoints
5. **Accessibility**: Proper ARIA labels and keyboard navigation

## Usage Instructions

### 1. Accessing the History Tab
- Navigate to `http://localhost:3000/history`
- The tab is available in the main navigation menu

### 2. Using Filters
1. **Date Range**: Select start and end dates using the date pickers
2. **Request ID**: Enter a migration ID to search for specific migrations
3. **Status**: Choose from dropdown to filter by migration status
4. **Store IDs**: Enter source or destination store IDs
5. **Entity Type**: Select specific entity types to filter results

### 3. Search Operations
- Click "Apply Filter" to execute search with current criteria
- Click "Clear All" to reset all filters to default values
- Use the refresh button to reload current data

### 4. Viewing Details
- Click on any migration ID to view detailed information
- Use action buttons for additional operations (view details, export)

## Error Handling

### API Errors
- Network errors are displayed as alerts
- Failed requests show appropriate error messages
- Loading states prevent multiple simultaneous requests

### Validation
- Date range validation ensures logical date ordering
- Input sanitization prevents invalid API requests
- Graceful handling of empty or invalid responses

## Performance Considerations

### Optimization Features
- **Debounced Search**: Prevents excessive API calls during typing
- **Pagination**: Limits data transfer with configurable page sizes
- **Caching**: Browser-level caching for improved performance
- **Loading States**: Prevents UI blocking during data fetching

### Default Settings
- **Page Size**: 50 items per page
- **Date Range**: Last 7 days by default
- **Auto-refresh**: Disabled to prevent unnecessary API calls

## Testing

### Test Coverage
- Component rendering tests
- API integration tests
- Filter functionality tests
- Error handling tests
- User interaction tests

### Test Files
- `src/components/Views/__tests__/HistoryView.test.tsx`
- `src/components/Views/__tests__/HistoryView.simple.test.tsx`

## Future Enhancements

### Planned Features
1. **Export Functionality**: CSV/Excel export of migration data
2. **Advanced Analytics**: Charts and graphs for migration trends
3. **Bulk Operations**: Multi-select and bulk actions
4. **Real-time Updates**: WebSocket integration for live updates
5. **Advanced Filtering**: Saved filter presets and custom filters

### API Enhancements
1. **Sorting**: Server-side sorting by various fields
2. **Advanced Search**: Full-text search across migration data
3. **Aggregation**: Summary statistics and metrics
4. **Audit Trail**: Detailed change history and rollback capabilities

## Troubleshooting

### Common Issues
1. **No Data Displayed**: Check API connectivity and filter settings
2. **Loading Errors**: Verify backend service is running
3. **Filter Not Working**: Ensure proper date format and valid inputs
4. **Pagination Issues**: Check API response format and pagination parameters

### Debug Information
- Check browser console for API request/response logs
- Verify network connectivity to backend services
- Review API endpoint configuration in `apiService.ts`

## Configuration

### Environment Variables
- `VITE_API_BASE_URL`: Backend API base URL
- `VITE_API_TIMEOUT`: API request timeout (default: 30000ms)
- `VITE_ENABLE_DEBUG_LOGGING`: Enable detailed API logging

### API Configuration
- Base URL: `http://localhost:7071/api` (development)
- Timeout: 30 seconds
- Retry Logic: Disabled (as per user preferences)
- CORS: Configured for local development

## Conclusion

The History & Rollback tab now provides a fully functional interface for viewing and searching migration history with real data from the BigCommerce Migration backend. The integration follows best practices for React development, includes comprehensive error handling, and provides an excellent user experience with advanced filtering and search capabilities. 