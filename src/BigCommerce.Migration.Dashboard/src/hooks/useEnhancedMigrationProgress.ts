import { useState, useEffect, useCallback, useRef } from 'react';
import { getSignalRService } from '../services/signalRService';
import { apiService } from '../services/apiService';
import { useDashboard } from '../context/DashboardContext';
import type { 
  EnhancedMigrationDisplayData, 
  EntityDisplayData,
  MigrationStartedEvent,
  EntityStartedEvent,
  EntityChunkProgressEvent,
  EntityCompletedEvent,
  MigrationCompletedEvent,
  ErrorProgressEvent,
  MigrationStatus
} from '../types';

/**
 * Calculate overall progress across all entities
 */
const calculateOverallProgress = (entities: EntityDisplayData[]): { percentage: number } => {
  if (entities.length === 0) return { percentage: 0 };
  
  let totalExpectedEntities = 0;
  let totalProcessedEntities = 0;
  
  entities.forEach(entity => {
    if (entity.showTotalCount && entity.totalCount > 0) {
      totalExpectedEntities += entity.totalCount;
      totalProcessedEntities += entity.processedCount;
    } else {
      // For dynamic discovery phases, consider progress based on status
      if (entity.status?.toLowerCase() === 'completed') {
        totalExpectedEntities += entity.processedCount;
        totalProcessedEntities += entity.processedCount;
      } else if (entity.processedCount > 0) {
        // Assume at least some work needs to be done
        totalExpectedEntities += Math.max(entity.processedCount * 1.1, entity.processedCount + 10);
        totalProcessedEntities += entity.processedCount;
      }
    }
  });
  
  const percentage = totalExpectedEntities > 0 ? (totalProcessedEntities / totalExpectedEntities) * 100 : 0;
  return { percentage: Math.min(percentage, 100) };
};

/**
 * Calculate total counts across all entities
 */
const calculateTotalCounts = (entities: EntityDisplayData[]): {
  totalProcessed: number;
  totalSuccess: number;
  totalFailed: number;
  totalSkipped: number;
  totalCancelled: number;
} => {
  return entities.reduce((totals, entity) => ({
    totalProcessed: totals.totalProcessed + entity.processedCount,
    totalSuccess: totals.totalSuccess + entity.successCount,
    totalFailed: totals.totalFailed + entity.failedCount,
    totalSkipped: totals.totalSkipped + entity.skippedCount,
    totalCancelled: totals.totalCancelled + (entity.cancelledCount || 0)
  }), {
    totalProcessed: 0,
    totalSuccess: 0,
    totalFailed: 0,
    totalSkipped: 0,
    totalCancelled: 0
  });
};

interface UseEnhancedMigrationProgressState {
  migrationData: EnhancedMigrationDisplayData | null;
  isConnected: boolean;
  isLoading: boolean;
  error: string | null;
  lastUpdated: Date | null;
  recentEvents: Array<{
    type: string;
    message: string;
    timestamp: Date;
  }>;
}

export interface UseEnhancedMigrationProgressReturn extends UseEnhancedMigrationProgressState {
  refreshData: () => Promise<void>;
  clearError: () => void;
}

/**
 * Enhanced hook for migration progress using simplified SignalR events
 * Phase 3: Handles migration-started, chunk-progress, migration-completed, and error events
 */
export const useEnhancedMigrationProgress = (migrationId: string): UseEnhancedMigrationProgressReturn => {
  const [state, setState] = useState<UseEnhancedMigrationProgressState>({
    migrationData: null,
    isConnected: false,
    isLoading: true,
    error: null,
    lastUpdated: null,
    recentEvents: []
  });

  const signalRService = useRef(getSignalRService());
  
  // Get store information from dashboard context
  const { state: dashboardState } = useDashboard();
  const migrationFromContext = dashboardState.activeMigrations.get(migrationId);
  
  // Debug: Log context data
  console.log(`🏪 [DEBUG] Enhanced Migration Progress: Context data for ${migrationId}:`, {
    foundInContext: !!migrationFromContext,
    sourceStore: migrationFromContext?.sourceStore,
    destinationStore: migrationFromContext?.destinationStore,
    totalActiveMigrations: dashboardState.activeMigrations.size,
    activeMigrationIds: Array.from(dashboardState.activeMigrations.keys())
  });

  /**
   * Add a recent event to the event log
   */
  const addRecentEvent = useCallback((type: string, message: string) => {
    setState(prev => ({
      ...prev,
      recentEvents: [
        { type, message, timestamp: new Date() },
        ...prev.recentEvents.slice(0, 9) // Keep last 10 events
      ]
    }));
  }, []);

  /**
   * Handle migration started event
   */
  const handleMigrationStarted = useCallback((event: MigrationStartedEvent) => {
    console.log('🚀 [DEBUG] Enhanced Migration Progress: Migration Started', event);
    
    // 🔧 FIX: Handle both camelCase and PascalCase for SignalR serialization
    const migrationId = event.migrationId || (event as any).MigrationId;
    const entities = event.entities || (event as any).Entities;
    const sourceStore = event.sourceStore || (event as any).SourceStore || 'Source Store';
    const destinationStore = event.destinationStore || (event as any).DestinationStore || 'Destination Store';
    const startDateTime = event.startDateTime || (event as any).StartDateTime || new Date().toISOString();
    const estimatedEndTime = event.estimatedEndTime || (event as any).EstimatedEndTime;
    
    console.log(`🔍 [DEBUG] event.entities:`, entities);
    console.log(`🔍 [DEBUG] event.migrationId:`, migrationId);
    console.log(`🔍 [DEBUG] event.sourceStore:`, sourceStore);
    console.log(`🔍 [DEBUG] event.destinationStore:`, destinationStore);
    
    // Initialize entities with count = 0 (will be updated by entity-started events)
    // Handle case where entities might be undefined and filter out invalid entities
    const entitiesData: EntityDisplayData[] = (entities || [])
      .filter(entity => {
        // 🔧 FIX: Handle both camelCase and PascalCase for entity type
        const entityType = entity.entityType;
        if (!entityType || entityType?.toLowerCase() === 'undefined' || entityType?.toLowerCase() === 'null') {
          console.warn('🚫 [DEBUG] Filtering out entity with invalid entityType in migration-started:', entityType);
          return false;
        }
        return true;
      })
      .map(entity => ({
        entityType: entity.entityType,
        totalCount: 0, // Start with 0, will be updated by entity-started events
        processedCount: 0,
        successCount: 0,
        failedCount: 0,
        skippedCount: 0,
        progressPercentage: 0,
        status: 'pending' as const,
        currentChunk: 0,
        totalChunks: 0,
        processingSpeed: 0
      }));

    setState(prev => ({
      ...prev,
      migrationData: {
        migrationId: migrationId,
        sourceStore: migrationFromContext?.sourceStore || sourceStore, // Get from context first
        destinationStore: migrationFromContext?.destinationStore || destinationStore, // Get from context first
        startDateTime: startDateTime,
        estimatedEndTime: estimatedEndTime,
        overallProgress: 0,
        totalProcessed: 0,
        totalSuccess: 0,
        totalFailed: 0,
        totalSkipped: 0,
        entities: entitiesData,
        status: 'running' as MigrationStatus,
        lastUpdated: new Date()
      },
      lastUpdated: new Date(),
      isLoading: false
    }));

    addRecentEvent('migration-started', `Migration started: ${entities.length} entity types (discovery in progress)`);
  }, [addRecentEvent, migrationFromContext]);

  /**
   * Handle entity started event - updates specific entity with discovered count
   */
  const handleEntityStarted = useCallback((event: EntityStartedEvent) => {
    console.log('🎯 [DEBUG] Enhanced Migration Progress: Entity Started', event);
    
    // 🔧 FIX: Handle both camelCase and PascalCase for SignalR serialization
    const entityType = event.entityType || (event as any).EntityType;
    const totalCount = event.totalCount ?? (event as any).TotalCount ?? 0;
    const message = event.message || (event as any).Message || '';
    
    console.log(`🔍 [DEBUG] event.entityType:`, entityType);
    console.log(`🔍 [DEBUG] event.totalCount:`, totalCount);
    console.log(`🔍 [DEBUG] event.message:`, message);
    
    setState(prev => {
      if (!prev.migrationData) return prev;
      
      const existingEntityIndex = prev.migrationData.entities.findIndex(e => e.entityType?.toLowerCase() === entityType?.toLowerCase());
      
      let updatedEntities;
      if (existingEntityIndex >= 0) {
        // Update existing entity
        updatedEntities = prev.migrationData.entities.map((entity, index) => 
          index === existingEntityIndex ? {
            ...entity,
            totalCount: totalCount, // Update with discovered count
            status: totalCount === 0 ? 'pending' as const : 'pending' as const // Keep pending until chunk processing starts
          } : entity
        );
      } else {
        // Add new entity (for component types)
        const newEntity: EntityDisplayData = {
          entityType: entityType,
          totalCount: totalCount,
          processedCount: 0,
          successCount: 0,
          failedCount: 0,
          skippedCount: 0,
          progressPercentage: 0,
          status: 'pending' as const,
          currentChunk: 0,
          totalChunks: 0,
          processingSpeed: 0
        };
        updatedEntities = [...prev.migrationData.entities, newEntity];
      }
      
      return {
        ...prev,
        migrationData: {
          ...prev.migrationData,
          entities: updatedEntities,
          lastUpdated: new Date()
        },
        lastUpdated: new Date()
      };
    });

    const countDisplay = totalCount === 0 ? 'starting...' : `${totalCount} entities discovered`;
    addRecentEvent('entity-started', `${entityType}: ${countDisplay}`);
  }, [addRecentEvent]);

  /**
   * Handle entity chunk progress event
   */
  const handleEntityChunkProgress = useCallback((event: EntityChunkProgressEvent) => {
    console.log('📊 [DEBUG] Enhanced Migration Progress: Chunk Progress', event);
    
    setState(prev => {
      if (!prev.migrationData) return prev;

      // 🚨 SAFETY CHECK: Ensure event has valid data
      // ✅ FIX: Use correct property names from SignalR events (camelCase from SignalRMessageConverter)
      const entityType = event.entityType || (event as any).EntityType;
      const totalProcessed = (event as any).totalProcessed || (event as any).TotalProcessed || 0;
      const totalSuccess = (event as any).totalSuccess || (event as any).TotalSuccess || 0;
      const totalFailed = (event as any).totalFailed || (event as any).TotalFailed || 0;
      const totalSkipped = (event as any).totalSkipped || (event as any).TotalSkipped || 0;
      const totalCancelled = (event as any).totalCancelled || (event as any).TotalCancelled || 0;
      const progressPercentage = (event as any).progressPercentage || (event as any).ProgressPercentage || 0;
      const showTotalCount = (event as any).showTotalCount ?? (event as any).ShowTotalCount ?? true; // 🎯 UI FLAG: Get display flag from SignalR
      const totalEntitiesForType = (event as any).totalEntitiesForType || (event as any).TotalEntitiesForType || 0;
      
      console.log(`🔍 [DEBUG] event.entityType:`, entityType);
      console.log(`🔍 [DEBUG] event.totalProcessed:`, totalProcessed);
      console.log(`🔍 [DEBUG] event.totalSuccess:`, totalSuccess);
      console.log(`🔍 [DEBUG] event.progressPercentage:`, progressPercentage);
      console.log(`🔍 [DEBUG] Full event object:`, JSON.stringify(event, null, 2));
      if (!entityType) {
        console.warn('🚫 [DEBUG] handleEntityChunkProgress: Missing EntityType/entityType, skipping update');
        return prev;
      }

      // Update the specific entity data
      const updatedEntities = prev.migrationData.entities.map(entity => {
        if (entity.entityType?.toLowerCase() === entityType?.toLowerCase()) {
          // 🔧 FIX: Use TotalEntitiesForType from discovery (transmitted via SignalR)
          const totalFromEvent = (event as any).totalEntitiesForType || (event as any).TotalEntitiesForType;
          let totalCount = totalFromEvent || entity.totalCount;
          
          // 🎯 PROGRESSIVE DISCOVERY: For component types, total may be unknown (0) - show as ???
          if (totalCount === 0) {
            // Check if this is a component type with progressive discovery
            const isComponentType = ['options', 'modifiers', 'images', 'reviews'].includes(entityType?.toLowerCase() || '');
            
            if (isComponentType) {
              console.log(`📊 [PROGRESSIVE] Component type ${entityType} - total unknown, showing as ???`);
              totalCount = 0; // Keep as 0 to display ???
            } else if (progressPercentage > 0 && totalProcessed > 0) {
              // Only estimate for non-component types
              totalCount = Math.round(totalProcessed / (progressPercentage / 100));
              console.log(`📊 [FALLBACK] Estimated totalCount for ${entityType}: ${totalCount} (from ${totalProcessed} processed at ${progressPercentage}%)`);
            }
          } else if (totalFromEvent > 0) {
            console.log(`📊 [FROM-EVENT] Using totalCount from discovery for ${entityType}: ${totalCount}`);
          }
          
          return {
            ...entity,
            // 🔧 FIX: Use correct property names from chunk events
            totalCount: totalCount,
            processedCount: totalProcessed,
            successCount: totalSuccess,
            failedCount: totalFailed,
            skippedCount: totalSkipped,
            progressPercentage: progressPercentage,
            showTotalCount: showTotalCount, // 🎯 UI FLAG: Set display flag from SignalR event
            status: (() => {
              // 🎯 RESPECT EXISTING STATUS - don't downgrade completed entities
              const currentStatus = entity.status?.toLowerCase() || '';
              
              // Check multiple completion indicators first
              if (progressPercentage >= 100 || 
                  (totalCount > 0 && totalProcessed >= totalCount)) {
                return 'completed';
              }
              
              // If entity is already completed, don't downgrade it to processing
              if (currentStatus === 'completed') {
                return 'completed';
              }
              
              if (totalCancelled > 0) return 'cancelled';
              if (totalFailed > totalSuccess) return 'failed';
              if (totalProcessed > 0) return 'processing';
              return 'pending';
            })() as 'processing' | 'completed' | 'cancelled' | 'failed' | 'pending',
            currentChunk: (event as any).ChunkNumber || 0,
            totalChunks: (event as any).TotalChunks || 0,
            processingSpeed: (event as any).ProcessingTimeMs > 0 ? ((event as any).ProcessedInChunk || 0) / ((event as any).ProcessingTimeMs / 1000) : 0
          };
        }
        return entity;
      });

      // Calculate overall progress
      const totalEntitiesAcrossTypes = updatedEntities.reduce((sum, entity) => sum + entity.totalCount, 0);
      const totalProcessedAcrossTypes = updatedEntities.reduce((sum, entity) => sum + entity.processedCount, 0);
      const totalSuccessAcrossTypes = updatedEntities.reduce((sum, entity) => sum + entity.successCount, 0);
      const totalFailedAcrossTypes = updatedEntities.reduce((sum, entity) => sum + entity.failedCount, 0);
      const totalSkippedAcrossTypes = updatedEntities.reduce((sum, entity) => sum + entity.skippedCount, 0);
      const overallProgress = totalEntitiesAcrossTypes > 0 ? (totalProcessedAcrossTypes / totalEntitiesAcrossTypes) * 100 : 0;

      return {
        ...prev,
        migrationData: {
          ...prev.migrationData,
          entities: updatedEntities,
          overallProgress: Math.round(overallProgress * 100) / 100,
          totalProcessed: totalProcessedAcrossTypes,
          totalSuccess: totalSuccessAcrossTypes,
          totalFailed: totalFailedAcrossTypes,
          totalSkipped: totalSkippedAcrossTypes,
          lastUpdated: new Date()
        },
        lastUpdated: new Date()
      };
    });

    addRecentEvent('chunk-progress', `${event.entityType}: Chunk ${event.chunkNumber}/${event.totalChunks} completed (${event.processedInChunk} entities)`);
  }, [addRecentEvent]);

  /**
   * Handle entity completed event - marks entity phase as definitively completed
   */
  const handleEntityCompleted = useCallback((event: EntityCompletedEvent) => {
    console.log('✅ [DEBUG] Enhanced Migration Progress: Entity Completed', event);
    
    // 🔧 FIX: Handle both camelCase and PascalCase for SignalR serialization
    const entityType = event.entityType || (event as any).EntityType;
    const totalProcessed = event.totalProcessed || (event as any).TotalProcessed || 0;
    const totalSuccess = event.totalSuccess || (event as any).TotalSuccess || 0;
    const totalFailed = event.totalFailed || (event as any).TotalFailed || 0;
    const totalSkipped = event.totalSkipped || (event as any).TotalSkipped || 0;
    const totalCancelled = event.totalCancelled || (event as any).TotalCancelled || 0;
    const status = event.status || (event as any).Status || 'completed';
    const showTotalCount = event.showTotalCount ?? (event as any).ShowTotalCount ?? true;
    
    console.log(`🔍 [DEBUG] event.entityType:`, entityType);
    console.log(`🔍 [DEBUG] event.totalProcessed:`, totalProcessed);
    console.log(`🔍 [DEBUG] event.status:`, status);
    console.log(`🔍 [DEBUG] event.showTotalCount:`, showTotalCount);
    
    setState(prev => {
      if (!prev.migrationData) return prev;
      
      const updatedEntities = prev.migrationData.entities.map(entity => {
        if (entity.entityType.toLowerCase() === (entityType || '').toLowerCase()) {
          return {
            ...entity,
            totalCount: entity.totalCount, // Keep existing total count
            processedCount: totalProcessed || totalSuccess + totalFailed + totalSkipped + totalCancelled,
            successCount: totalSuccess,
            failedCount: totalFailed,
            skippedCount: totalSkipped,
            cancelledCount: totalCancelled,
            progressPercentage: 100, // ✅ Explicitly mark as 100% complete
            showTotalCount: showTotalCount ?? entity.showTotalCount ?? true, // Preserve display flag
            status: status as 'processing' | 'completed' | 'cancelled' | 'failed' | 'pending', // ✅ Use actual status from event
            currentChunk: entity.totalChunks || 0,
            totalChunks: entity.totalChunks || 0,
            processingSpeed: entity.processingSpeed || 0
          };
        }
        return entity;
      });
      
      // Calculate overall progress with completed entity
      const overallProgress = calculateOverallProgress(updatedEntities);
      const totalCounts = calculateTotalCounts(updatedEntities);
      
      return {
        ...prev,
        migrationData: {
          ...prev.migrationData,
          entities: updatedEntities,
          overallProgress: overallProgress.percentage,
          totalProcessed: totalCounts.totalProcessed,
          totalSuccess: totalCounts.totalSuccess,
          totalFailed: totalCounts.totalFailed,
          totalSkipped: totalCounts.totalSkipped,
          totalCancelled: totalCounts.totalCancelled,
          lastUpdated: new Date()
        },
        lastUpdated: new Date()
      };
    });

    addRecentEvent('entity-completed', `Entity ${entityType} completed: ${totalSuccess}/${totalProcessed} successful`);
  }, [addRecentEvent]);

  /**
   * Handle migration completed event
   */
  const handleMigrationCompleted = useCallback((event: MigrationCompletedEvent) => {
    console.log('✅ Enhanced Migration Progress: Migration Completed', event);
    
    // 🔧 FIX: Handle both camelCase and PascalCase for SignalR serialization
    const status = event.status || (event as any).Status || 'completed';
    const message = event.message || (event as any).Message || 'Migration completed';
    const totalProcessedEntities = event.totalProcessedEntities || (event as any).TotalProcessedEntities || 0;
    const totalFailedEntities = event.totalFailedEntities || (event as any).TotalFailedEntities || 0;
    const durationMs = event.durationMs || (event as any).DurationMs || 0;
    const endDateTime = event.endDateTime || (event as any).EndDateTime;
    
    console.log(`🔍 [DEBUG] migration.status:`, status);
    console.log(`🔍 [DEBUG] migration.message:`, message);
    console.log(`🔍 [DEBUG] migration.totalProcessedEntities:`, totalProcessedEntities);
    
    setState(prev => {
      if (!prev.migrationData) return prev;

      const finalStatus: MigrationStatus = (() => {
        const statusLower = status?.toLowerCase() || '';
        if (statusLower.includes('cancelled')) return 'cancelled';
        if (statusLower.includes('failed')) return 'failed';
        return 'completed';
      })();

      return {
        ...prev,
        migrationData: {
          ...prev.migrationData,
          status: finalStatus,
          overallProgress: 100,
          totalProcessed: totalProcessedEntities,
          totalSuccess: totalProcessedEntities - totalFailedEntities,
          totalFailed: totalFailedEntities,
          lastUpdated: new Date()
        },
        lastUpdated: new Date()
      };
    });

    addRecentEvent('migration-completed', `Migration ${status}: ${totalProcessedEntities} entities processed`);
  }, [addRecentEvent]);

  /**
   * Handle error event
   */
  const handleError = useCallback((event: ErrorProgressEvent) => {
    console.warn('❌ Enhanced Migration Progress: Error Event', event);
    
    // 🔧 FIX: Handle both camelCase and PascalCase for SignalR serialization
    const errorType = event.errorType || (event as any).ErrorType || 'Unknown';
    const errorMessage = event.errorMessage || (event as any).ErrorMessage || 'Unknown error';
    
    setState(prev => ({
      ...prev,
      error: `${errorType}: ${errorMessage}`,
      lastUpdated: new Date()
    }));

    addRecentEvent('error', `${errorType}: ${errorMessage}`);
  }, [addRecentEvent]);

  /**
   * Handle migration cancelled event
   */
  const handleMigrationCancelled = useCallback((event: any) => {
    console.warn('🛑 Enhanced Migration Progress: Migration Cancelled', event);
    
    // 🔧 FIX: Handle both camelCase and PascalCase for SignalR serialization
    const migrationId = event.migrationId || event.MigrationId;
    const reason = event.reason || event.Reason || 'Migration was cancelled';
    const cancelledAt = event.cancelledAt || event.CancelledAt || new Date().toISOString();
    const message = event.message || event.Message || 'Migration cancelled';
    
    setState(prev => {
      if (!prev.migrationData) return prev;

      return {
        ...prev,
        migrationData: {
          ...prev.migrationData,
          status: 'cancelled' as MigrationStatus,
          lastUpdated: new Date(cancelledAt)
        },
        lastUpdated: new Date()
      };
    });

    addRecentEvent('migration-cancelled', `Migration cancelled: ${reason}`);
  }, [addRecentEvent]);

  /**
   * Handle connection state changes
   */
  const handleConnectionStateChange = useCallback((connectionData: { state: string; error?: any }) => {
    const isConnected = connectionData.state?.toLowerCase() === 'connected';
    setState(prev => ({ ...prev, isConnected }));
    
    // ✅ OPTIMIZATION: No need to join group here - already pre-joined by dashboard context
    console.log(`🔗 [ENHANCED-DASHBOARD] Connection state changed: ${connectionData.state} for migration ${migrationId} (group already joined)`);
  }, [migrationId]);

  /**
   * Refresh migration data from API
   */
  const refreshData = useCallback(async () => {
    try {
      console.log(`🔄 [DEBUG] refreshData called for migrationId: "${migrationId}"`);
      setState(prev => ({ ...prev, isLoading: true, error: null }));
      
      // Get migration data from API
      console.log(`📡 [DEBUG] Calling apiService.getMigrationProgress with migrationId: "${migrationId}"`);
      const migrationData = await apiService.getMigrationProgress(migrationId);
      console.log(`📊 [DEBUG] Raw API response:`, migrationData);
      
      // Transform API data to our enhanced display format with safe property access
      const entities: EntityDisplayData[] = Object.entries(migrationData?.entityProgress || {})
        .filter(([entityType, progress]) => {
          // 🚨 FILTER OUT: Skip entities with invalid names
          if (!entityType || entityType?.toLowerCase() === 'undefined' || entityType?.toLowerCase() === 'null') {
            console.warn('🚫 [DEBUG] Filtering out entity with invalid entityType:', entityType);
            return false;
          }
          
          // 🎯 COMPONENT PROGRESS: Filter out product-components - only show individual components (options, modifiers, reviews)
          if (entityType?.toLowerCase() === 'product-components') {
            console.log('🎯 [DEBUG] Filtering out product-components - individual component tracking used instead');
            return false;
          }
          
          return true;
        })
        .sort(([entityTypeA], [entityTypeB]) => {
          // 🎯 ENTITY DISPLAY ORDER: Sort entities by processing phase order for better UX
          const entityOrder = [
            'products',              // Phase 1: Core products
            'options',               // Phase 2a: Product options (individual component tracking)
            'modifiers',             // Phase 2b: Product modifiers (individual component tracking)
            'reviews',               // Phase 2c: Product reviews (individual component tracking)
            'product-related',       // Phase 3: Related products updates
            'product-images',        // Phase 4: Product images migration
            'product-channel-assign',// Phase 5: Product channel assignments
            'product-metafields',    // Phase 6: Product metafields
            'variants',              // Phase 7: Product variants
            'categories',            // Other entity types
            'brands',
            'customers',
            'orders'
          ];
          
          const indexA = entityOrder.indexOf(entityTypeA.toLowerCase());
          const indexB = entityOrder.indexOf(entityTypeB.toLowerCase());
          
          // If both entities are in the order list, use that order
          if (indexA !== -1 && indexB !== -1) {
            return indexA - indexB;
          }
          
          // If only one is in the order list, prioritize it
          if (indexA !== -1) return -1;
          if (indexB !== -1) return 1;
          
          // If neither is in the order list, use alphabetical
          return entityTypeA.localeCompare(entityTypeB);
        })
        .map(([entityType, progress]) => ({
          entityType,
          totalCount: progress?.totalCount || 0,
          processedCount: progress?.processedCount || 0,
          successCount: progress?.successCount || 0,
          failedCount: progress?.failureCount || 0,
          skippedCount: progress?.skippedCount || 0,
          progressPercentage: progress?.progressPercentage || 0,
          showTotalCount: (progress as any)?.showTotalCount ?? true, // 🎯 UI FLAG: Get display flag from API response
          status: (() => {
            // 🎯 TRUST BACKEND STATUS FIRST - don't override with calculations
            const statusLower = progress?.status?.toLowerCase() || '';
            
            // First: Trust explicit backend status
            if (statusLower === 'completed') {
              console.log(`✅ [STATUS-TRUST] Using backend status 'completed' for ${entityType}`);
              return 'completed';
            }
            if (statusLower === 'failed') {
              console.log(`❌ [STATUS-TRUST] Using backend status 'failed' for ${entityType}`);
              return 'failed';
            }
            if (statusLower === 'cancelled') {
              console.log(`🚫 [STATUS-TRUST] Using backend status 'cancelled' for ${entityType}`);
              return 'cancelled';
            }
            if (statusLower === 'processing') {
              console.log(`⏳ [STATUS-TRUST] Using backend status 'processing' for ${entityType}`);
              return 'processing';
            }
            if (statusLower === 'pending') {
              console.log(`⏸️ [STATUS-TRUST] Using backend status 'pending' for ${entityType}`);
              return 'pending';
            }
            
            // Only calculate status if backend doesn't provide valid status
            console.log(`⚠️ [STATUS-CALC] Backend status '${progress?.status}' invalid for ${entityType}, calculating...`);
            if ((progress?.progressPercentage || 0) >= 100 ||
                ((progress?.totalCount || 0) > 0 && (progress?.processedCount || 0) >= (progress?.totalCount || 0))) {
              console.log(`📊 [STATUS-CALC] Calculated 'completed' for ${entityType}`);
              return 'completed';
            }
            if ((progress?.processedCount || 0) > 0) {
              console.log(`📊 [STATUS-CALC] Calculated 'processing' for ${entityType}`);
              return 'processing';
            }
            console.log(`📊 [STATUS-CALC] Calculated 'pending' for ${entityType}`);
            return 'pending';
          })() as 'processing' | 'completed' | 'cancelled' | 'failed' | 'pending',
          currentChunk: 0,
          totalChunks: 0,
          processingSpeed: (progress?.processingTime || 0) > 0 ? (progress?.processedCount || 0) / progress.processingTime : 0
        }));

      console.log(`🔄 [DEBUG] Processing entities from entityProgress:`, migrationData?.entityProgress);
      console.log(`📊 [DEBUG] Transformed entities:`, entities);

      const enhancedData: EnhancedMigrationDisplayData = {
        migrationId: migrationData?.migrationId || migrationId,
        sourceStore: migrationFromContext?.sourceStore || migrationData?.sourceStore || 'Source Store', // Get from context first
        destinationStore: migrationFromContext?.destinationStore || migrationData?.destinationStore || 'Destination Store', // Get from context first
        startDateTime: migrationData?.startTime ? new Date(migrationData.startTime).toISOString() : new Date().toISOString(),
        estimatedEndTime: migrationData?.estimatedEndTime ? new Date(migrationData.estimatedEndTime).toISOString() : undefined,
        overallProgress: migrationData?.overallProgressPercentage || 0,
        totalProcessed: migrationData?.processedEntities || 0,
        totalSuccess: migrationData?.successfulEntities || 0,
        totalFailed: migrationData?.failedEntities || 0,
        totalSkipped: migrationData?.skippedEntities || 0,
        entities,
        status: migrationData?.status || 'unknown',
        lastUpdated: migrationData?.lastUpdated ? new Date(migrationData.lastUpdated) : new Date()
      };

      console.log(`✅ [DEBUG] Final enhancedData:`, enhancedData);

      setState(prev => ({
        ...prev,
        migrationData: enhancedData,
        isLoading: false,
        error: null,
        lastUpdated: new Date()
      }));
      
    } catch (error) {
      console.error('Failed to refresh migration data:', error);
      setState(prev => ({
        ...prev,
        error: error instanceof Error ? error.message : 'Failed to refresh data',
        isLoading: false
      }));
    }
  }, [migrationId, migrationFromContext]);

  /**
   * Clear error state
   */
  const clearError = useCallback(() => {
    setState(prev => ({ ...prev, error: null }));
  }, []);

  /**
   * Set up SignalR subscriptions
   */
  useEffect(() => {
    const signalR = signalRService.current;
    
    // Subscribe to simplified events
    const unsubscribeStarted = signalR.on('migration-started', handleMigrationStarted);
    const unsubscribeEntityStarted = signalR.on('entity-started', handleEntityStarted);
    const unsubscribeChunkProgress = signalR.on('chunk-progress', handleEntityChunkProgress);
    const unsubscribeEntityCompleted = signalR.on('entity-completed', handleEntityCompleted); // 🔧 FIX: Use event type for consistency with other events
    const unsubscribeCompleted = signalR.on('migration-completed', handleMigrationCompleted);
    const unsubscribeError = signalR.on('error', handleError);
    const unsubscribeCancelled = signalR.on('migrationCancelled', handleMigrationCancelled);
    const unsubscribeConnectionState = signalR.on('connectionStateChanged', handleConnectionStateChange);

    // 🔧 FIX: Get initial connection state
    const initialConnectionState = signalR.getConnectionState();
    setState(prev => ({ 
      ...prev, 
      isConnected: initialConnectionState.isConnected 
    }));
    
    console.log('🔌 [ENHANCED] Initial SignalR connection state:', {
      isConnected: initialConnectionState.isConnected,
      connectionState: initialConnectionState.connectionState,
      groupPreJoined: 'Groups pre-joined by dashboard context - no delay needed'
    });

    // Initial data load
    refreshData();

    // Cleanup function
    return () => {
      unsubscribeStarted();
      unsubscribeEntityStarted();
      unsubscribeChunkProgress();
      unsubscribeEntityCompleted();
      unsubscribeCompleted();
      unsubscribeError();
      unsubscribeCancelled();
      unsubscribeConnectionState();
    };
  }, [migrationId, migrationFromContext, handleMigrationStarted, handleEntityStarted, handleEntityChunkProgress, handleEntityCompleted, handleMigrationCompleted, handleError, handleMigrationCancelled, handleConnectionStateChange, refreshData]);

  return {
    ...state,
    refreshData,
    clearError
  };
};