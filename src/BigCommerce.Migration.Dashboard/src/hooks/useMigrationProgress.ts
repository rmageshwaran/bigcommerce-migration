import { useState, useEffect, useCallback } from 'react';
import type { MigrationProgress } from '../types';
import { getSignalRService } from '../services/signalRService';

interface UseMigrationProgressOptions {
  migrationId: string;
  autoConnect?: boolean;
}

interface UseMigrationProgressReturn {
  progress: MigrationProgress | null;
  isLoading: boolean;
  error: string | null;
  isConnected: boolean;
  refresh: () => Promise<void>;
  joinMigrationGroup: () => Promise<void>;
  leaveMigrationGroup: () => Promise<void>;
}

export const useMigrationProgress = (
  options: UseMigrationProgressOptions
): UseMigrationProgressReturn => {
  const { migrationId, autoConnect = true } = options;
  const [progress, setProgress] = useState<MigrationProgress | null>(null);
  const [isLoading, setIsLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [isConnected, setIsConnected] = useState(false);
  
  const signalRService = getSignalRService();

  // Handle migration started events (Phase 3: Simplified Events)
  const handleMigrationStarted = useCallback((eventData: any) => {
    if (eventData.migrationId === migrationId) {
      console.log('🚀 useMigrationProgress: Migration Started', eventData);
      setProgress({
        migrationId: eventData.migrationId,
        status: 'running',
        startTime: new Date(eventData.startDateTime),
        lastUpdated: new Date(),
        elapsedTime: 0,
        estimatedTimeRemaining: 0,
        totalEntities: eventData.entities?.reduce((sum: number, entity: any) => sum + entity.totalCount, 0) || 0,
        processedEntities: 0,
        successfulEntities: 0,
        failedEntities: 0,
        skippedEntities: 0,
        cancelledEntities: 0,
        overallProgressPercentage: 0,
        entityProgress: {},
        currentPhase: 'initializing',
        currentEntity: '',
        entitiesPerSecond: 0,
        errorRate: 0
      });
      setIsLoading(false);
    }
  }, [migrationId]);

  // Handle chunk progress updates (Phase 3: Simplified Events)
  const handleChunkProgress = useCallback((eventData: any) => {
    if (eventData.migrationId === migrationId) {
      console.log('📊 useMigrationProgress: Chunk Progress', eventData);
      
      setProgress(prev => {
        if (!prev) return prev;
        
        // 🎯 COMPONENT PROGRESS: Filter out product-components - only track individual components
        if (eventData.entityType?.toLowerCase() === 'product-components') {
          console.log('🎯 [useMigrationProgress] Filtering out product-components chunk progress - individual component tracking used instead');
          return prev; // Don't update for product-components
        }

        // Update entity progress - use correct field names from SignalR events
        const updatedEntityProgress = { ...prev.entityProgress };
        updatedEntityProgress[eventData.entityType] = {
          entityType: eventData.entityType,
          totalCount: eventData.totalEntitiesForType,
          // 🚨 FIX: Use correct field names from SignalR (totalProcessed, totalSuccess, etc.)
          processedCount: eventData.totalProcessed, // Already includes all processed (success + failed + skipped + cancelled)
          successCount: eventData.totalSuccess,     // Already calculated as successful only
          failureCount: eventData.totalFailed,      // Failed entities
          skippedCount: eventData.totalSkipped,     // Skipped entities  
          cancelledCount: eventData.totalCancelled, // Cancelled entities
          progressPercentage: eventData.progressPercentage,
          status: eventData.status,
          startTime: prev.startTime,
          processingTime: eventData.processingTimeMs / 1000
        };
        
        // Calculate overall progress
        const totalEntities = Object.values(updatedEntityProgress).reduce((sum, entity) => sum + entity.totalCount, 0);
        const processedEntities = Object.values(updatedEntityProgress).reduce((sum, entity) => sum + entity.processedCount, 0);
        const successfulEntities = Object.values(updatedEntityProgress).reduce((sum, entity) => sum + entity.successCount, 0);
        const failedEntities = Object.values(updatedEntityProgress).reduce((sum, entity) => sum + entity.failureCount, 0);
        
        return {
          ...prev,
          entityProgress: updatedEntityProgress,
          totalEntities,
          processedEntities,
          successfulEntities,
          failedEntities,
          overallProgressPercentage: totalEntities > 0 ? (processedEntities / totalEntities) * 100 : 0,
          currentEntity: eventData.entityType,
          lastUpdated: new Date(),
          elapsedTime: Date.now() - prev.startTime.getTime()
        };
      });
    }
  }, [migrationId]);

  // Handle migration completion events (Phase 3: Simplified Events)
  const handleMigrationCompleted = useCallback((eventData: any) => {
    if (eventData.migrationId === migrationId) {
      console.log('✅ useMigrationProgress: Migration Completed', eventData);
      
      setProgress(prev => prev ? {
        ...prev,
        status: eventData.status.toLowerCase().includes('cancelled') ? 'cancelled' : 
               eventData.status.toLowerCase().includes('failed') ? 'failed' : 'completed',
        overallProgressPercentage: 100,
        processedEntities: eventData.totalProcessedEntities,
        successfulEntities: eventData.totalProcessedEntities - eventData.totalFailedEntities,
        failedEntities: eventData.totalFailedEntities,
        lastUpdated: new Date(),
        elapsedTime: eventData.durationMs
      } : null);
    }
  }, [migrationId]);

  // Handle error events (Phase 3: Simplified Events)
  const handleErrorEvent = useCallback((eventData: any) => {
    if (eventData.migrationId === migrationId) {
      console.warn('❌ useMigrationProgress: Error Event', eventData);
      setError(`${eventData.errorType}: ${eventData.errorMessage}`);
    }
  }, [migrationId]);

  // Legacy event handlers (for backward compatibility)
  const handleLegacyProgressUpdate = useCallback((progressData: MigrationProgress) => {
    if (progressData.migrationId === migrationId) {
      console.log('🔄 useMigrationProgress: Legacy Progress Update', progressData);
      setProgress(progressData);
      setIsLoading(false);
    }
  }, [migrationId]);

  // Handle connection state changes
  const handleConnectionStateChange = useCallback((connectionData: any) => {
    setIsConnected(connectionData.state === 'Connected');
    if (connectionData.error) {
      setError(connectionData.error.message || 'Connection error');
    } else {
      setError(null);
    }
  }, []);

  // Join migration monitoring group
  const joinMigrationGroup = useCallback(async () => {
    try {
      await signalRService.joinMigrationGroup(migrationId);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to join migration group');
    }
  }, [signalRService, migrationId]);

  // Leave migration monitoring group
  const leaveMigrationGroup = useCallback(async () => {
    try {
      await signalRService.leaveMigrationGroup(migrationId);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to leave migration group');
    }
  }, [signalRService, migrationId]);

  // Refresh migration progress data
  const refresh = useCallback(async () => {
    setIsLoading(true);
    setError(null);
    
    try {
      // Fetch current migration status from API
              const response = await fetch(`/api/dashboard/migrations/${migrationId}/status`);
      if (!response.ok) {
        throw new Error(`HTTP ${response.status}: ${response.statusText}`);
      }
      
      const data = await response.json();
      setProgress(data);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to fetch migration progress');
      setProgress(null);
    } finally {
      setIsLoading(false);
    }
  }, [migrationId]);

  // Set up SignalR listeners and connection
  useEffect(() => {
    // Subscribe to simplified SignalR events (Phase 3)
    const startedUnsubscribe = signalRService.on('migration-started', handleMigrationStarted);
    const chunkProgressUnsubscribe = signalRService.on('chunk-progress', handleChunkProgress);
    const completedUnsubscribe = signalRService.on('migration-completed', handleMigrationCompleted);
    const errorUnsubscribe = signalRService.on('error', handleErrorEvent);
    
    // Legacy event subscriptions (for backward compatibility)
    const legacyProgressUnsubscribe = signalRService.on('MigrationProgressUpdated', handleLegacyProgressUpdate);
    const connectionUnsubscribe = signalRService.on('connectionStateChanged', handleConnectionStateChange);

    // Initial connection state
    const connectionState = signalRService.getConnectionState();
    setIsConnected(connectionState.isConnected);

    // Auto-connect and join migration group if requested
    const initializeConnection = async () => {
      if (autoConnect) {
        try {
          if (!signalRService.isConnected()) {
            await signalRService.connect();
          }
          await joinMigrationGroup();
          await refresh(); // Load initial data
        } catch (err) {
          setError(err instanceof Error ? err.message : 'Failed to initialize connection');
          setIsLoading(false);
        }
      } else {
        await refresh(); // Load initial data even without SignalR
      }
    };

    initializeConnection();

    // Cleanup function
    return () => {
      startedUnsubscribe();
      chunkProgressUnsubscribe();
      completedUnsubscribe();
      errorUnsubscribe();
      legacyProgressUnsubscribe();
      connectionUnsubscribe();
      
      // Leave migration group on cleanup
      if (signalRService.isConnected()) {
        leaveMigrationGroup().catch(console.error);
      }
    };
  }, [
    signalRService,
    autoConnect,
    handleMigrationStarted,
    handleChunkProgress,
    handleMigrationCompleted,
    handleErrorEvent,
    handleLegacyProgressUpdate,
    handleConnectionStateChange,
    joinMigrationGroup,
    leaveMigrationGroup,
    refresh
  ]);

  return {
    progress,
    isLoading,
    error,
    isConnected,
    refresh,
    joinMigrationGroup,
    leaveMigrationGroup
  };
}; 