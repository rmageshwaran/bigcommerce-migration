# 📋 **Feature: Migration Details Page Redesign - Entity Type Grid with Expandable Errors**

**Feature ID:** MIG-UI-001  
**Created:** 2024-12-10  
**Status:** Planning  
**Priority:** Medium  
**Estimated Effort:** 8-12 days  

---

## **Overview**
Redesign the migration details page to display entity types in a grid format with success/failure counts, and allow users to expand each row to view paginated errors for that specific entity type.

### **Current Problem**
- Single flat list showing all errors mixed together
- Hard to see which entity types have issues
- Poor user experience when dealing with multiple entity types
- No clear overview of success/failure counts per entity type

### **Proposed Solution**
- **Grid view** with one row per entity type
- **Expandable rows** showing entity-specific errors with pagination
- **Clear success/failure counts** per entity type
- **Better visual hierarchy** and user experience

---

## **UI Mockup Structure**

```
┌─────────────────────────────────────────────────────────────────┐
│ Migration Details: 55b8a34b-9c34-4284-9457-a36b13a3df7e         │
├─────────────────────────────────────────────────────────────────┤
│ Migration Status: ✅ Completed  |  Total: 6  |  Success: 0       │
└─────────────────────────────────────────────────────────────────┘

┌─────────────────────────────────────────────────────────────────┐
│ Entity Type Summary                                              │
├────────────────┬─────────────┬─────────────┬─────────────┬──────┤
│ Entity Type    │ Success     │ Failed      │ Total       │ ▼    │
├────────────────┼─────────────┼─────────────┼─────────────┼──────┤
│ 🏷️  Brands      │     0       │     6       │     6       │  ▼   │ ← Expandable
├────────────────┼─────────────┼─────────────┼─────────────┼──────┤
│ 📁 Categories   │     5       │     2       │     7       │  ▶   │ ← Collapsed
└────────────────┴─────────────┴─────────────┴─────────────┴──────┘

┌─────────────────────────────────────────────────────────────────┐
│ 🏷️ Brands Errors (6 failed entities)                           │
├─────────────────────────────────────────────────────────────────┤
│ Name              │ Error                          │ Time        │
├───────────────────┼────────────────────────────────┼─────────────┤
│ Yummie (1458)     │ API returned Conflict          │ 17:31:29    │
│ Zodiac (1415)     │ API returned Conflict          │ 17:31:28    │
│ Wacoal (1408)     │ API returned Conflict          │ 17:31:28    │
└───────────────────┴────────────────────────────────┴─────────────┘
│ Page 1 of 2  [<] [1] [2] [>]                                   │
└─────────────────────────────────────────────────────────────────┘
```

---

## **Phase 1: Backend API Foundation** 
*Estimated Time: 2-3 days*

### **1.1 Analysis & Design**
- [ ] **Task 1.1.1**: Analyze current error data structure in OpenSearch/logs
- [ ] **Task 1.1.2**: Design new API response models for entity summary
- [ ] **Task 1.1.3**: Design pagination model for entity-specific errors
- [ ] **Task 1.1.4**: Document API contracts (request/response schemas)

### **1.2 Migration Entity Summary Endpoint**
- [ ] **Task 1.2.1**: Create `MigrationEntitySummaryResponse` model
- [ ] **Task 1.2.2**: Create `EntityTypeSummary` model with counts
- [ ] **Task 1.2.3**: Implement `GET /api/migrations/{id}/entity-summary` endpoint
- [ ] **Task 1.2.4**: Add logic to aggregate success/failure counts by entity type
- [ ] **Task 1.2.5**: Write unit tests for the new endpoint
- [ ] **Task 1.2.6**: Test endpoint manually via Postman/HTTP file

#### **API Contract: Migration Entity Summary**
```typescript
GET /api/migrations/{migrationId}/entity-summary

Response:
{
  "migrationId": "55b8a34b-9c34-4284-9457-a36b13a3df7e",
  "entitySummaries": [
    {
      "entityType": "brands",
      "successCount": 0,
      "failedCount": 6,
      "totalCount": 6
    },
    {
      "entityType": "categories", 
      "successCount": 5,
      "failedCount": 2,
      "totalCount": 7
    }
  ]
}
```

### **1.3 Entity-Specific Errors Endpoint**
- [ ] **Task 1.3.1**: Create `EntityErrorsPaginatedResponse` model  
- [ ] **Task 1.3.2**: Implement `GET /api/migrations/{id}/entities/{entityType}/errors` endpoint
- [ ] **Task 1.3.3**: Add pagination parameters (page, pageSize)
- [ ] **Task 1.3.4**: Filter errors by entity type in the query logic
- [ ] **Task 1.3.5**: Write unit tests for pagination and filtering
- [ ] **Task 1.3.6**: Test endpoint manually with different entity types

#### **API Contract: Entity-Specific Errors**
```typescript
GET /api/migrations/{migrationId}/entities/{entityType}/errors?page=1&pageSize=10

Response:
{
  "migrationId": "55b8a34b-9c34-4284-9457-a36b13a3df7e",
  "entityType": "brands",
  "errors": [
    {
      "entityId": "1458",
      "entityName": "Yummie",
      "error": "API returned Conflict",
      "processedAt": "2025-07-21T17:31:29Z",
      "requestPayloadBlobUrl": "...",
      "responsePayloadBlobUrl": "..."
    }
  ],
  "pagination": {
    "currentPage": 1,
    "totalPages": 2,
    "pageSize": 10,
    "totalItems": 6
  }
}
```

---

## **Phase 2: Frontend Data Layer** 
*Estimated Time: 1-2 days*

### **2.1 API Service Layer**
- [ ] **Task 2.1.1**: Create TypeScript interfaces for new API responses
- [ ] **Task 2.1.2**: Add `getMigrationEntitySummary()` to API service
- [ ] **Task 2.1.3**: Add `getEntityErrors()` with pagination to API service
- [ ] **Task 2.1.4**: Create mock data for development/testing
- [ ] **Task 2.1.5**: Write unit tests for API service methods

#### **TypeScript Interfaces**
```typescript
interface EntityTypeSummary {
  entityType: string;
  successCount: number;
  failedCount: number;
  totalCount: number;
  errors?: EntityError[];
  isExpanded?: boolean;
  errorsPagination?: {
    currentPage: number;
    totalPages: number;
    pageSize: number;
  };
}

interface MigrationSummary {
  migrationId: string;
  status: string;
  entitySummaries: EntityTypeSummary[];
  overallStats: {
    totalSuccess: number;
    totalFailed: number;
    totalEntities: number;
  };
}
```

### **2.2 State Management**
- [ ] **Task 2.2.1**: Design state structure for entity summaries
- [ ] **Task 2.2.2**: Design state for expanded rows and error data
- [ ] **Task 2.2.3**: Create hooks for managing expansion state
- [ ] **Task 2.2.4**: Create hooks for pagination state per entity type
- [ ] **Task 2.2.5**: Write tests for state management hooks

---

## **Phase 3: UI Components Foundation** 
*Estimated Time: 2-3 days*

### **3.1 Entity Summary Grid Component**
- [ ] **Task 3.1.1**: Create `EntitySummaryGrid` component skeleton
- [ ] **Task 3.1.2**: Implement table structure with MUI Table components
- [ ] **Task 3.1.3**: Add entity type column with icons
- [ ] **Task 3.1.4**: Add success/failed/total count columns with chips
- [ ] **Task 3.1.5**: Add expand/collapse icon column
- [ ] **Task 3.1.6**: Style the grid for responsive design

### **3.2 Entity Error Details Component**
- [ ] **Task 3.2.1**: Create `EntityErrorDetails` component skeleton
- [ ] **Task 3.2.2**: Implement error table with entity name, error, time columns
- [ ] **Task 3.2.3**: Add pagination component using MUI Pagination
- [ ] **Task 3.2.4**: Add Request/Response download buttons
- [ ] **Task 3.2.5**: Style the error details section
- [ ] **Task 3.2.6**: Add loading states and error handling

### **3.3 Utility Components**
- [ ] **Task 3.3.1**: Create entity type icon mapping function
- [ ] **Task 3.3.2**: Create status chip component with colors
- [ ] **Task 3.3.3**: Create time formatting utility
- [ ] **Task 3.3.4**: Write unit tests for utility functions

#### **Entity Type Icons**
```tsx
const getEntityIcon = (entityType: string) => {
  const iconMap = {
    brands: '🏷️',
    categories: '📁', 
    products: '📦',
    variants: '🔧',
    images: '🖼️'
  };
  return iconMap[entityType] || '📄';
};
```

---

## **Phase 4: Integration & Interaction Logic** 
*Estimated Time: 2-3 days*

### **4.1 Row Expansion Logic**
- [ ] **Task 4.1.1**: Implement row click handler for expand/collapse
- [ ] **Task 4.1.2**: Add smooth expand/collapse animations using MUI Collapse
- [ ] **Task 4.1.3**: Load error data on first expansion (lazy loading)
- [ ] **Task 4.1.4**: Handle multiple rows expanded simultaneously
- [ ] **Task 4.1.5**: Add keyboard navigation support
- [ ] **Task 4.1.6**: Write integration tests for expansion behavior

### **4.2 Pagination Integration**
- [ ] **Task 4.2.1**: Implement page change handler for entity errors
- [ ] **Task 4.2.2**: Maintain pagination state per entity type
- [ ] **Task 4.2.3**: Add loading indicators during page changes
- [ ] **Task 4.2.4**: Handle pagination edge cases (empty results, errors)
- [ ] **Task 4.2.5**: Write tests for pagination behavior

### **4.3 Main View Integration**
- [ ] **Task 4.3.1**: Update `MigrationDetailView` to use new components
- [ ] **Task 4.3.2**: Replace old error list with new entity summary grid
- [ ] **Task 4.3.3**: Update data fetching logic in main component
- [ ] **Task 4.3.4**: Handle loading and error states
- [ ] **Task 4.3.5**: Write integration tests for complete flow

---

## **Phase 5: Polish & Enhancement** 
*Estimated Time: 1-2 days*

### **5.1 Visual Polish**
- [ ] **Task 5.1.1**: Add hover effects and micro-interactions
- [ ] **Task 5.1.2**: Implement responsive design for mobile devices
- [ ] **Task 5.1.3**: Add empty state when no errors exist
- [ ] **Task 5.1.4**: Add skeleton loading states
- [ ] **Task 5.1.5**: Fine-tune spacing, colors, and typography

### **5.2 Accessibility & UX**
- [ ] **Task 5.2.1**: Add ARIA labels for screen readers
- [ ] **Task 5.2.2**: Ensure keyboard navigation works properly
- [ ] **Task 5.2.3**: Add tooltips for action buttons
- [ ] **Task 5.2.4**: Test with screen reader software
- [ ] **Task 5.2.5**: Add help text/instructions for users

### **5.3 Performance Optimization**
- [ ] **Task 5.3.1**: Implement virtual scrolling for large error lists (if needed)
- [ ] **Task 5.3.2**: Add debouncing for rapid expand/collapse actions
- [ ] **Task 5.3.3**: Optimize re-renders with React.memo where appropriate
- [ ] **Task 5.3.4**: Test performance with large datasets
- [ ] **Task 5.3.5**: Add error boundaries for graceful failure handling

---

## **Phase 6: Documentation & Testing** 
*Estimated Time: 1 day*

### **6.1 Documentation**
- [ ] **Task 6.1.1**: Update API documentation with new endpoints
- [ ] **Task 6.1.2**: Create user guide for new migration details view
- [ ] **Task 6.1.3**: Document component architecture and props
- [ ] **Task 6.1.4**: Add code comments and JSDoc annotations
- [ ] **Task 6.1.5**: Update architecture documentation

### **6.2 Testing & QA**
- [ ] **Task 6.2.1**: Write comprehensive E2E tests
- [ ] **Task 6.2.2**: Test with different migration scenarios (all success, all fail, mixed)
- [ ] **Task 6.2.3**: Test pagination with various page sizes
- [ ] **Task 6.2.4**: Cross-browser testing (Chrome, Firefox, Safari, Edge)
- [ ] **Task 6.2.5**: Mobile device testing

---

## **Technical Considerations**

### **Performance**
- Lazy loading of error details on row expansion
- Pagination to handle large error lists
- Virtual scrolling for extremely large datasets
- Optimized re-renders with React.memo

### **Accessibility**
- ARIA labels for screen readers
- Keyboard navigation support
- Focus management for expanded rows
- High contrast mode compatibility

### **Responsive Design**
- Mobile-first approach
- Collapsible columns on small screens
- Touch-friendly interaction targets
- Optimized for tablet and mobile viewing

---

## **Risk Assessment**

| Risk | Impact | Probability | Mitigation |
|------|--------|-------------|------------|
| Large dataset performance | High | Medium | Implement virtual scrolling, pagination |
| Complex state management | Medium | Medium | Use tested state management patterns |
| Browser compatibility | Low | Low | Comprehensive testing, polyfills |
| API response time | Medium | Low | Add loading states, caching |

---

## **Success Criteria**

- [ ] Users can view entity types in a clean grid format
- [ ] Success/failure counts are clearly displayed per entity type
- [ ] Rows expand smoothly to show entity-specific errors
- [ ] Pagination works correctly for error details
- [ ] Page loads in under 2 seconds for typical datasets
- [ ] All accessibility standards are met
- [ ] No regression in existing functionality

---

## **Dependencies**

### **Technical Dependencies**
- Material-UI v5 (already in project)
- React state management hooks
- Existing API infrastructure
- OpenSearch logging data

### **Team Dependencies**
- Backend developer for API endpoints
- Frontend developer for UI components
- QA engineer for testing
- Product owner for UX approval

---

## **Rollout Plan**

1. **Development**: Complete phases 1-6
2. **Internal Testing**: QA and stakeholder review
3. **Beta Release**: Limited user group testing
4. **Production Release**: Full rollout with monitoring
5. **Post-Release**: Monitor performance and user feedback

---

## **Changelog**

- **2024-12-10**: Initial feature specification created
- **TBD**: Implementation phases to be tracked here

---

**Related Documents:**
- [Migration Details Redesign - Quick Reference](./Migration-Details-Redesign-QuickRef.md)
- [API Documentation](./API-Documentation.md)
- [Component Architecture](./Component-Architecture.md) 