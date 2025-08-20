import { useState, useEffect, useCallback, useRef } from 'react';
import { getSignalRService } from '../services/signalRService';
import { apiService } from '../services/apiService';
import type { 
  EnhancedMigrationDisplayData, 
  EntityDisplayData,
  MigrationStartedEvent,
  EntityStartedEvent,
  EntityChunkProgressEvent,
  MigrationCompletedEvent,
  ErrorProgressEvent,
  MigrationStatus
} from '../types';

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
  const hasJoinedGroup = useRef(false);

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
    
    console.log(`🔍 [DEBUG] event.entities:`, entities);
    console.log(`🔍 [DEBUG] event.migrationId:`, migrationId);
    
    // Initialize entities with count = 0 (will be updated by entity-started events)
    // Handle case where entities might be undefined and filter out invalid entities
    const entitiesData: EntityDisplayData[] = (entities || [])
      .filter(entity => {
        // 🔧 FIX: Handle both camelCase and PascalCase for entity type
        const entityType = entity.entityType;
        if (!entityType || entityType === 'undefined' || entityType === 'null') {
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
                sourceStore: event.sourceStore || 'Source Store',
        destinationStore: event.destinationStore || 'Destination Store',
        startDateTime: event.startDateTime || new Date().toISOString(),
        estimatedEndTime: event.estimatedEndTime,
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
  }, [addRecentEvent]);

  /**
   * Handle entity started event - updates specific entity with discovered count
   */
  const handleEntityStarted = useCallback((event: EntityStartedEvent) => {
    console.log('🎯 [DEBUG] Enhanced Migration Progress: Entity Started', event);
    console.log(`🔍 [DEBUG] event.entityType:`, event.entityType);
    console.log(`🔍 [DEBUG] event.totalCount:`, event.totalCount);
    
    setState(prev => {
      if (!prev.migrationData) return prev;
      
      const existingEntityIndex = prev.migrationData.entities.findIndex(e => e.entityType === event.entityType);
      
      let updatedEntities;
      if (existingEntityIndex >= 0) {
        // Update existing entity
        updatedEntities = prev.migrationData.entities.map((entity, index) => 
          index === existingEntityIndex ? {
            ...entity,
            totalCount: event.totalCount, // Update with discovered count
            status: event.totalCount === 0 ? 'pending' as const : 'pending' as const // Keep pending until chunk processing starts
          } : entity
        );
      } else {
        // Add new entity (for component types)
        const newEntity: EntityDisplayData = {
          entityType: event.entityType,
          totalCount: event.totalCount,
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

    const countDisplay = event.totalCount === 0 ? 'starting...' : `${event.totalCount} entities discovered`;
    addRecentEvent('entity-started', `${event.entityType}: ${countDisplay}`);
  }, [addRecentEvent]);

  /**
   * Handle entity chunk progress event
   */
  const handleEntityChunkProgress = useCallback((event: EntityChunkProgressEvent) => {
    console.log('📊 [DEBUG] Enhanced Migration Progress: Chunk Progress', event);
    
    // 🔧 FIX: Use correct property names from actual chunk events
    const entityType = event.entityType || (event as any).EntityType;
    const totalProcessed = (event as any).totalProcessed || (event as any).TotalProcessed || 0;
    const totalSuccess = (event as any).totalSuccess || (event as any).TotalSuccess || 0;
    const totalFailed = (event as any).totalFailed || (event as any).TotalFailed || 0;
    const progressPercentage = (event as any).progressPercentage || (event as any).ProgressPercentage || 0;
    
    console.log(`🔍 [DEBUG] event.entityType:`, entityType);
    console.log(`🔍 [DEBUG] event.totalProcessed:`, totalProcessed);
    console.log(`🔍 [DEBUG] event.totalSuccess:`, totalSuccess);
    console.log(`🔍 [DEBUG] event.progressPercentage:`, progressPercentage);
    console.log(`🔍 [DEBUG] Full event object:`, JSON.stringify(event, null, 2));
    
    setState(prev => {
      if (!prev.migrationData) return prev;

      // 🚨 SAFETY CHECK: Ensure event has valid data
      // Note: Backend sends "EntityType" with capital E, not "entityType"
      const entityType = event.entityType || (event as any).EntityType;
      if (!entityType) {
        console.warn('🚫 [DEBUG] handleEntityChunkProgress: Missing EntityType/entityType, skipping update');
        return prev;
      }

      // Update the specific entity data
      const updatedEntities = prev.migrationData.entities.map(entity => {
        if (entity.entityType === entityType) {
          // 🔧 FIX: Use TotalEntitiesForType from discovery (transmitted via SignalR)
          const totalFromEvent = (event as any).totalEntitiesForType || (event as any).TotalEntitiesForType;
          let totalCount = totalFromEvent || entity.totalCount;
          
          // 🎯 PROGRESSIVE DISCOVERY: For component types, total may be unknown (0) - show as ???
          if (totalCount === 0) {
            // Check if this is a component type with progressive discovery
            const isComponentType = ['options', 'modifiers', 'images', 'reviews', 'product-components'].includes(entityType);
            
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
            progressPercentage: progressPercentage,
            status: (progressPercentage >= 100 ? 'completed' : 'processing') as 'processing' | 'completed' | 'cancelled' | 'failed' | 'pending',
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
          lastUpdated: new Date()
        },
        lastUpdated: new Date()
      };
    });

    addRecentEvent('chunk-progress', `${event.entityType}: Chunk ${event.chunkNumber}/${event.totalChunks} completed (${event.processedInChunk} entities)`);
  }, [addRecentEvent]);

  /**
   * Handle migration completed event
   */
  const handleMigrationCompleted = useCallback((event: MigrationCompletedEvent) => {
    console.log('✅ Enhanced Migration Progress: Migration Completed', event);
    
    setState(prev => {
      if (!prev.migrationData) return prev;

      const finalStatus: MigrationStatus = event.status.toLowerCase().includes('cancelled') ? 'cancelled' :
                                         event.status.toLowerCase().includes('failed') ? 'failed' : 'completed';

      return {
        ...prev,
        migrationData: {
          ...prev.migrationData,
          status: finalStatus,
          overallProgress: 100,
          totalProcessed: event.totalProcessedEntities,
          totalSuccess: event.totalProcessedEntities - event.totalFailedEntities,
          totalFailed: event.totalFailedEntities,
          lastUpdated: new Date()
        },
        lastUpdated: new Date()
      };
    });

    addRecentEvent('migration-completed', `Migration ${event.status}: ${event.totalProcessedEntities} entities processed`);
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
   * Handle connection state changes
   */
  const handleConnectionStateChange = useCallback((connectionData: { state: string; error?: any }) => {
    const isConnected = connectionData.state === 'Connected';
    setState(prev => ({ ...prev, isConnected }));
    
    if (isConnected && !hasJoinedGroup.current) {
      // Auto-join migration group when connected
      signalRService.current.joinMigrationGroup(migrationId)
        .then(() => {
          hasJoinedGroup.current = true;
          console.log(`✅ Enhanced Migration Progress: Joined group for migration ${migrationId}`);
        })
        .catch(error => {
          console.error(`❌ Enhanced Migration Progress: Failed to join group for migration ${migrationId}:`, error);
        });
    }
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
          if (!entityType || entityType === 'undefined' || entityType === 'null') {
            console.warn('🚫 [DEBUG] Filtering out entity with invalid entityType:', entityType);
            return false;
          }
          return true;
        })
        .map(([entityType, progress]) => ({
          entityType,
          totalCount: progress?.totalCount || 0,
          processedCount: progress?.processedCount || 0,
          successCount: progress?.successCount || 0,
          failedCount: progress?.failureCount || 0,
          skippedCount: progress?.skippedCount || 0,
          progressPercentage: progress?.progressPercentage || 0,
          status: (progress?.status === 'completed' ? 'completed' :
                  progress?.status === 'failed' ? 'failed' :
                  progress?.status === 'cancelled' ? 'cancelled' :
                  (progress?.processedCount || 0) > 0 ? 'processing' : 'pending') as 'processing' | 'completed' | 'cancelled' | 'failed' | 'pending',
          currentChunk: 0,
          totalChunks: 0,
          processingSpeed: (progress?.processingTime || 0) > 0 ? (progress?.processedCount || 0) / progress.processingTime : 0
        }));

      console.log(`🔄 [DEBUG] Processing entities from entityProgress:`, migrationData?.entityProgress);
      console.log(`📊 [DEBUG] Transformed entities:`, entities);

      const enhancedData: EnhancedMigrationDisplayData = {
        migrationId: migrationData?.migrationId || migrationId,
        sourceStore: migrationData?.sourceStore || 'Source Store', // Fallback
        destinationStore: migrationData?.destinationStore || 'Destination Store', // Fallback  
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
  }, [migrationId]);

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
    const unsubscribeCompleted = signalR.on('migration-completed', handleMigrationCompleted);
    const unsubscribeError = signalR.on('error', handleError);
    const unsubscribeConnectionState = signalR.on('connectionStateChanged', handleConnectionStateChange);

    // 🔧 FIX: Get initial connection state
    const initialConnectionState = signalR.getConnectionState();
    setState(prev => ({ 
      ...prev, 
      isConnected: initialConnectionState.isConnected 
    }));
    
    console.log('🔌 [ENHANCED] Initial SignalR connection state:', {
      isConnected: initialConnectionState.isConnected,
      connectionState: initialConnectionState.connectionState
    });

    // Initial data load
    refreshData();

    // Cleanup function
    return () => {
      unsubscribeStarted();
      unsubscribeEntityStarted();
      unsubscribeChunkProgress();
      unsubscribeCompleted();
      unsubscribeError();
      unsubscribeConnectionState();
    };
  }, [migrationId, handleMigrationStarted, handleEntityStarted, handleEntityChunkProgress, handleMigrationCompleted, handleError, handleConnectionStateChange, refreshData]);

  return {
    ...state,
    refreshData,
    clearError
  };
};