/**
 * @deprecated This hook has been superseded by useDetailedMigrationProgress.
 * Please use useDetailedMigrationProgress instead for enhanced features including:
 * - Batch-level progress tracking
 * - Current processing context
 * - Performance trends and milestones
 * - More detailed real-time metrics
 * 
 * This hook will be removed in a future version.
 * Migration: Replace `useRealTimeMigrationProgress` with `useDetailedMigrationProgress`
 */

import { useState, useEffect, useCallback, useRef } from 'react';
import type { 
  MigrationProgress, 
  EntityProgress, 
  MigrationStatus,
  SignalRConnection 
} from '../types';
import { getSignalRService } from '../services/signalRService';
import { notificationService } from '../services/notificationService';

interface RealTimeMigrationProgressOptions {
  migrationId: string;
  autoConnect?: boolean;
  enableNotifications?: boolean;
  enablePerformanceTracking?: boolean;
  pollInterval?: number; // Fallback polling interval in ms
}

interface PerformanceMetrics {
  averageRate: number;
  peakRate: number;
  improvementPercentage: number;
  efficiencyTrend: 'improving' | 'stable' | 'declining';
  lastUpdated: Date;
}

interface EntityStatusMap {
  [entityType: string]: {
    status: 'pending' | 'running' | 'completed' | 'failed';
    progress: EntityProgress;
    lastUpdate: Date;
    estimatedCompletion?: Date;
  };
}

interface RealTimeMigrationProgressState {
  // Core progress data
  progress: MigrationProgress | null;
  entityStatus: EntityStatusMap;
  
  // Connection state
  isConnected: boolean;
  connectionState: 'disconnected' | 'connecting' | 'connected' | 'reconnecting';
  lastHeartbeat: Date | null;
  
  // Performance tracking
  performanceMetrics: PerformanceMetrics | null;
  performanceHistory: Array<{ timestamp: Date; rate: number; progress: number }>;
  
  // Real-time events
  recentEvents: Array<{
    id: string;
    type: 'progress' | 'status' | 'entity' | 'batch' | 'error';
    timestamp: Date;
    data: any;
    message: string;
  }>;
  
  // Error handling
  errors: Array<{
    id: string;
    timestamp: Date;
    message: string;
    details?: any;
  }>;
  
  // UI state
  isLoading: boolean;
  lastUpdated: Date | null;
}

interface RealTimeMigrationProgressActions {
  // Connection management
  connect: () => Promise<void>;
  disconnect: () => Promise<void>;
  reconnect: () => Promise<void>;
  
  // Data refresh
  refresh: () => Promise<void>;
  clearHistory: () => void;
  clearErrors: () => void;
  
  // Event handlers
  onProgressUpdate: (callback: (progress: MigrationProgress) => void) => () => void;
  onStatusChange: (callback: (status: MigrationStatus) => void) => () => void;
  onEntityUpdate: (callback: (entityType: string, status: EntityProgress) => void) => () => void;
  onError: (callback: (error: any) => void) => () => void;
}

export const useRealTimeMigrationProgress = (
  options: RealTimeMigrationProgressOptions
): RealTimeMigrationProgressState & RealTimeMigrationProgressActions => {
  const {
    migrationId,
    autoConnect = true,
    enableNotifications = true,
    enablePerformanceTracking = true,
    pollInterval = 30000 // 30 seconds fallback
  } = options;

  // State management
  const [state, setState] = useState<RealTimeMigrationProgressState>({
    progress: null,
    entityStatus: {},
    isConnected: false,
    connectionState: 'disconnected',
    lastHeartbeat: null,
    performanceMetrics: null,
    performanceHistory: [],
    recentEvents: [],
    errors: [],
    isLoading: true,
    lastUpdated: null
  });

  // Refs for cleanup and tracking
  const signalRService = useRef(getSignalRService());
  const unsubscribeCallbacks = useRef<Array<() => void>>([]);
  const performanceTracker = useRef<{
    rates: number[];
    timestamps: Date[];
    startTime: Date | null;
  }>({
    rates: [],
    timestamps: [],
    startTime: null
  });
  const eventIdCounter = useRef(0);
  const pollTimer = useRef<NodeJS.Timeout | null>(null);

  // Helper function to generate unique event IDs
  const generateEventId = useCallback(() => {
    return `event_${migrationId}_${Date.now()}_${++eventIdCounter.current}`;
  }, [migrationId]);

  // Add event to recent events list
  const addEvent = useCallback((
    type: RealTimeMigrationProgressState['recentEvents'][0]['type'],
    data: any,
    message: string
  ) => {
    setState(prev => ({
      ...prev,
      recentEvents: [
        {
          id: generateEventId(),
          type,
          timestamp: new Date(),
          data,
          message
        },
        ...prev.recentEvents.slice(0, 49) // Keep last 50 events
      ]
    }));
  }, [generateEventId]);

  // Add error to errors list
  const addError = useCallback((message: string, details?: any) => {
    const error = {
      id: generateEventId(),
      timestamp: new Date(),
      message,
      details
    };

    setState(prev => ({
      ...prev,
      errors: [error, ...prev.errors.slice(0, 9)] // Keep last 10 errors
    }));

    if (enableNotifications) {
      notificationService.error('Migration Error', `Migration ${migrationId}: ${message}`);
    }
  }, [generateEventId, migrationId, enableNotifications]);

  // Update performance metrics
  const updatePerformanceMetrics = useCallback((currentProgress: MigrationProgress) => {
    if (!enablePerformanceTracking) return;

    const now = new Date();
    const rate = currentProgress.entitiesPerSecond || 0;
    
    // Update performance tracker
    performanceTracker.current.rates.push(rate);
    performanceTracker.current.timestamps.push(now);
    
    // Keep last 100 data points
    if (performanceTracker.current.rates.length > 100) {
      performanceTracker.current.rates.shift();
      performanceTracker.current.timestamps.shift();
    }

    // Calculate metrics
    const rates = performanceTracker.current.rates;
    const averageRate = rates.reduce((sum, r) => sum + r, 0) / rates.length;
    const peakRate = Math.max(...rates);
    
    // Calculate improvement percentage (comparing to baseline of 100 entities/sec)
    const baselineRate = 100;
    const improvementPercentage = ((averageRate - baselineRate) / baselineRate) * 100;
    
    // Determine efficiency trend (last 10 vs previous 10 data points)
    let efficiencyTrend: PerformanceMetrics['efficiencyTrend'] = 'stable';
    if (rates.length >= 20) {
      const recent = rates.slice(-10).reduce((sum, r) => sum + r, 0) / 10;
      const previous = rates.slice(-20, -10).reduce((sum, r) => sum + r, 0) / 10;
      const change = (recent - previous) / previous;
      
      if (change > 0.05) efficiencyTrend = 'improving';
      else if (change < -0.05) efficiencyTrend = 'declining';
    }

    setState(prev => ({
      ...prev,
      performanceMetrics: {
        averageRate,
        peakRate,
        improvementPercentage,
        efficiencyTrend,
        lastUpdated: now
      },
      performanceHistory: [
        ...prev.performanceHistory,
        {
          timestamp: now,
          rate,
          progress: currentProgress.overallProgressPercentage || 0
        }
      ].slice(-200) // Keep last 200 data points
    }));
  }, [enablePerformanceTracking]);

  // Enhanced event handlers for detailed progress tracking
  const handleDetailedProgress = useCallback((detailedProgress: any) => {
    setState(prev => ({
      ...prev,
      progress: detailedProgress,
      lastUpdated: new Date(),
      isLoading: false
    }));
    
    addEvent('progress', detailedProgress, 
      `Detailed progress update: ${detailedProgress.currentPhase} - ${detailedProgress.currentEntity}`);
    
    if (enablePerformanceTracking && detailedProgress.performance) {
      updatePerformanceData(detailedProgress.performance.currentProcessingSpeed);
    }
  }, [addEvent, enablePerformanceTracking]);

  const handleProcessingContext = useCallback((context: any) => {
    setState(prev => ({
      ...prev,
      entityStatus: {
        ...prev.entityStatus,
        [context.currentEntity]: {
          status: 'running',
          progress: {
            processedCount: 0,
            totalCount: 0,
            successCount: 0,
            failureCount: 0,
            progressPercentage: 0
          },
          lastUpdate: new Date(),
          currentActivity: context.currentActivity,
          currentBatch: context.currentBatchNumber,
          phase: context.currentPhase
        }
      }
    }));
    
    addEvent('entity', context, 
      `Processing: ${context.currentEntity} - ${context.currentActivity}`);
  }, [addEvent]);

  const handleBatchEvent = useCallback((eventType: string, batchEvent: any) => {
    console.log(`🎯 DEBUG: handleBatchEvent called with eventType: ${eventType}`, batchEvent);
    
    const { migrationId: eventMigrationId, entityType, batchDetails, summary } = batchEvent;
    
    if (eventMigrationId !== migrationId) {
      console.log(`🎯 DEBUG: Migration ID mismatch - expected: ${migrationId}, got: ${eventMigrationId}`);
      return;
    }
    
    console.log(`🎯 DEBUG: Processing ${eventType} for ${entityType} in migration ${migrationId}`);
    
    setState(prev => ({
      ...prev,
      entityStatus: {
        ...prev.entityStatus,
        [entityType]: {
          ...prev.entityStatus[entityType],
          status: eventType === 'BatchCompleted' ? 'completed' : 'running',
          lastUpdate: new Date()
        }
      }
    }));
    
    const message = eventType === 'BatchStarted' 
      ? `Started batch ${batchDetails?.batchNumber} for ${entityType}`
      : eventType === 'BatchCompleted'
      ? `Completed batch ${summary?.batchNumber} for ${entityType} (${summary?.successfulEntities}/${summary?.entitiesProcessed} successful)`
      : `Batch progress for ${entityType}`;
    
    console.log(`🎯 DEBUG: Adding event with message: ${message}`);
    addEvent('batch', batchEvent, message);
  }, [migrationId, addEvent]);

  const handleRemainingWorkload = useCallback((workload: any) => {
    setState(prev => ({
      ...prev,
      progress: prev.progress ? {
        ...prev.progress,
        estimatedTimeRemaining: workload.estimatedTimeRemaining || 0
      } : prev.progress
    }));
    
    addEvent('progress', workload, 
      `Remaining: ${workload.remainingEntities} entities, ${Math.round(workload.estimatedTimeRemaining / 1000)}s`);
  }, [addEvent]);

  const handlePerformanceMetrics = useCallback((metrics: any) => {
    setState(prev => ({
      ...prev,
      performanceMetrics: metrics,
      performanceHistory: [
        ...prev.performanceHistory.slice(-29), // Keep last 30 entries
        {
          timestamp: new Date(),
          rate: metrics.currentProcessingSpeed || 0,
          progress: metrics.averageProcessingSpeed || 0
        }
      ]
    }));
    
    if (enablePerformanceTracking) {
      updatePerformanceData(metrics.currentProcessingSpeed);
    }
    
    addEvent('progress', metrics, 
      `Speed: ${metrics.currentProcessingSpeed?.toFixed(1) || 0} entities/s`);
  }, [addEvent, enablePerformanceTracking]);

  const handleMilestone = useCallback((milestone: any) => {
    if (milestone.migrationId !== migrationId && milestone.MigrationId !== migrationId) return;
    
    addEvent('progress', milestone, 
      `Milestone: ${milestone.milestone || milestone.Milestone} - ${milestone.message || milestone.Message}`);
    
    if (enableNotifications) {
      // Show milestone notification
      console.log(`🎯 Migration Milestone: ${milestone.milestone || milestone.Milestone}`);
    }
  }, [migrationId, addEvent, enableNotifications]);

  // Performance tracking helper
  const updatePerformanceData = useCallback((currentSpeed: number) => {
    if (!enablePerformanceTracking) return;
    
    performanceTracker.current.rates.push(currentSpeed || 0);
    performanceTracker.current.timestamps.push(new Date());
    
    // Keep only last 100 measurements
    if (performanceTracker.current.rates.length > 100) {
      performanceTracker.current.rates.shift();
      performanceTracker.current.timestamps.shift();
    }
  }, [enablePerformanceTracking]);

  // Handle migration progress updates
  const handleProgressUpdate = useCallback((progressData: MigrationProgress) => {
    if (progressData.migrationId !== migrationId) return;

    setState(prev => {
      const newEntityStatus = { ...prev.entityStatus };
      
      // Process EntityProgress data from API response if available
      if (progressData.entityProgress) {
        Object.entries(progressData.entityProgress).forEach(([entityType, entityProgress]) => {
          // Map API status to expected status type
          const mapStatus = (status: string): 'pending' | 'running' | 'completed' | 'failed' => {
            switch (status?.toLowerCase()) {
              case 'processing':
              case 'in_progress':
              case 'running':
                return 'running';
              case 'completed':
              case 'finished':
                return 'completed';
              case 'failed':
              case 'error':
                return 'failed';
              default:
                return 'pending';
            }
          };

          const mappedStatus = mapStatus(entityProgress.status || 'pending');

          newEntityStatus[entityType] = {
            status: mappedStatus,
            progress: {
              entityType: entityProgress.entityType || entityType,
              totalCount: entityProgress.totalCount || 0,
              processedCount: entityProgress.processedCount || 0,
              successCount: entityProgress.successCount || 0,
              failureCount: entityProgress.failureCount || 0,
              skippedCount: entityProgress.skippedCount || 0,
              progressPercentage: entityProgress.progressPercentage || 0,
              status: mappedStatus,
              startTime: new Date(entityProgress.startTime || Date.now()),
              endTime: entityProgress.endTime ? new Date(entityProgress.endTime) : undefined,
              processingTime: entityProgress.processingTime || 0
            },
            lastUpdate: new Date()
          };
        });
      }

      return {
        ...prev,
        progress: progressData,
        entityStatus: newEntityStatus,
        isLoading: false,
        lastUpdated: new Date(),
        lastHeartbeat: new Date()
      };
    });

    updatePerformanceMetrics(progressData);
    addEvent('progress', progressData, `Progress: ${progressData.overallProgressPercentage?.toFixed(1)}%`);

    // ✅ FIX: Don't show completion notification here - DashboardContext handles this centrally
    // This prevents duplicate notifications when migration completes
  }, [migrationId, updatePerformanceMetrics, addEvent, enableNotifications]);

  // Handle migration status updates
  const handleStatusUpdate = useCallback((statusData: any) => {
    if (statusData.migrationId !== migrationId && statusData.MigrationId !== migrationId) return;

    setState(prev => ({
      ...prev,
      progress: prev.progress ? { ...prev.progress, status: statusData.status || statusData.Status } : prev.progress,
      lastUpdated: new Date(),
      lastHeartbeat: new Date()
    }));

    addEvent('status', statusData, `Status changed to: ${statusData.status || statusData.Status}`);

    // Handle specific status changes
    if (enableNotifications) {
      switch (statusData.status || statusData.Status) {
        case 'started':
          notificationService.info('Migration Started', `Migration ${migrationId} started`);
          break;
        case 'failed':
          notificationService.error('Migration Failed', `Migration ${migrationId} failed`);
          break;
        case 'cancelled':
          notificationService.warning('Migration Cancelled', `Migration ${migrationId} was cancelled`);
          break;
      }
    }
  }, [migrationId, addEvent, enableNotifications]);

  // Handle entity-specific updates
  const handleEntityUpdate = useCallback((entityData: any) => {
    const entityType = entityData.entityType || entityData.EntityType;
    const phase = entityData.phase || entityData.Phase;
    
    if (!entityType) return;

    setState(prev => {
      const newEntityStatus = { ...prev.entityStatus };
      
      if (!newEntityStatus[entityType]) {
        newEntityStatus[entityType] = {
          status: 'pending',
          progress: {
            entityType,
            totalCount: 0,
            processedCount: 0,
            successCount: 0,
            failureCount: 0,
            skippedCount: 0,
            progressPercentage: 0,
            status: 'pending',
            startTime: new Date(),
            processingTime: 0
          },
          lastUpdate: new Date()
        };
      }

      // Update status based on phase
      switch (phase) {
        case 'started':
        case 'phase_started':
          newEntityStatus[entityType].status = 'running';
          break;
        case 'completed':
        case 'phase_completed':
          newEntityStatus[entityType].status = 'completed';
          break;
        case 'failed':
          newEntityStatus[entityType].status = 'failed';
          break;
      }

      newEntityStatus[entityType].lastUpdate = new Date();

      return {
        ...prev,
        entityStatus: newEntityStatus
      };
    });

    addEvent('entity', entityData, `${entityType}: ${phase}`);
  }, [addEvent]);

  // Handle batch completion updates
  const handleBatchUpdate = useCallback((batchData: any) => {
    addEvent('batch', batchData, `Batch ${batchData.batchNumber || 'N/A'} completed for ${batchData.entityType || 'unknown'}`);
  }, [addEvent]);

  // Handle connection state changes
  const handleConnectionStateChange = useCallback((connectionData: { state: string; error?: any; connectionId?: string }) => {
    setState(prev => ({
      ...prev,
      isConnected: connectionData.state === 'Connected',
      connectionState: connectionData.state.toLowerCase() as any,
      lastHeartbeat: connectionData.state === 'Connected' ? new Date() : prev.lastHeartbeat
    }));

    if (connectionData.error) {
      addError('Connection error', connectionData.error);
    }
  }, [addError]);

  // Handle error events
  const handleErrorEvent = useCallback((errorData: any) => {
    addError(`Error: ${errorData.message}`, errorData.details);
  }, [addError]);

  // Connection management
  const connect = useCallback(async () => {
    try {
      setState(prev => ({ ...prev, connectionState: 'connecting' }));
      
      await signalRService.current.connect();
      await signalRService.current.joinMigrationGroup(migrationId);
      
      // Set up event listeners for basic events
      const unsubscribeProgress = signalRService.current.on('MigrationProgressUpdated', handleProgressUpdate);
      const unsubscribeStatus = signalRService.current.on('MigrationStatusChanged', handleStatusUpdate);
      const unsubscribeBatch = signalRService.current.on('BatchProgressUpdated', (event: any) => handleBatchEvent('BatchProgress', event));
      const unsubscribeEntity = signalRService.current.on('EntityProgressUpdated', handleEntityUpdate);
      const unsubscribeErrors = signalRService.current.on('ErrorOccurred', handleErrorEvent);
      const unsubscribeConnectionState = signalRService.current.on('connectionStateChanged', handleConnectionStateChange);
      
      unsubscribeCallbacks.current = [
        unsubscribeProgress, 
        unsubscribeStatus, 
        unsubscribeBatch,
        unsubscribeEntity,
        unsubscribeErrors,
        unsubscribeConnectionState
      ];
      
    } catch (error) {
      addError('Failed to connect to real-time updates', error);
      setState(prev => ({ ...prev, connectionState: 'disconnected' }));
    }
  }, [migrationId, handleProgressUpdate, handleStatusUpdate, handleBatchEvent, handleEntityUpdate, handleErrorEvent, handleConnectionStateChange, addError]);

  const disconnect = useCallback(async () => {
    try {
      // Clear all subscriptions
      unsubscribeCallbacks.current.forEach(unsubscribe => unsubscribe());
      unsubscribeCallbacks.current = [];
      
      // Clear polling timer
      if (pollTimer.current) {
        clearInterval(pollTimer.current);
        pollTimer.current = null;
      }
      
      // Leave migration group but DON'T disconnect the shared SignalR connection
      // The connection is shared across all dashboard components
      await signalRService.current.leaveMigrationGroup(migrationId);
      
      // Only update local state - don't disconnect the shared connection
      setState(prev => ({ 
        ...prev, 
        isConnected: false, 
        connectionState: 'disconnected',
        lastHeartbeat: null
      }));
    } catch (error) {
      addError('Failed to disconnect properly', error);
    }
  }, [migrationId, addError]);

  const reconnect = useCallback(async () => {
    await disconnect();
    await connect();
  }, [disconnect, connect]);

  // Data refresh (fallback when SignalR is not working)
  const refresh = useCallback(async () => {
    try {
      setState(prev => ({ ...prev, isLoading: true }));
      
      const response = await fetch(`/api/dashboard/migrations/${migrationId}/status`);
      if (!response.ok) {
        throw new Error(`HTTP ${response.status}: ${response.statusText}`);
      }
      
      const data = await response.json();
      handleProgressUpdate(data);
    } catch (error) {
      addError('Failed to refresh migration data', error);
    } finally {
      setState(prev => ({ ...prev, isLoading: false }));
    }
  }, [migrationId, handleProgressUpdate, addError]);

  // Clear functions
  const clearHistory = useCallback(() => {
    setState(prev => ({ 
      ...prev, 
      performanceHistory: [], 
      recentEvents: [] 
    }));
    performanceTracker.current = { rates: [], timestamps: [], startTime: null };
  }, []);

  const clearErrors = useCallback(() => {
    setState(prev => ({ ...prev, errors: [] }));
  }, []);

  // Event subscription helpers
  const onProgressUpdate = useCallback((callback: (progress: MigrationProgress) => void) => {
    const unsubscribe = signalRService.current.on('MigrationProgressUpdated', callback);
    return unsubscribe;
  }, []);

  const onStatusChange = useCallback((callback: (status: MigrationStatus) => void) => {
    const unsubscribe = signalRService.current.on('MigrationStatusChanged', (data: any) => {
      callback(data.status || data.Status);
    });
    return unsubscribe;
  }, []);

  const onEntityUpdate = useCallback((callback: (entityType: string, status: EntityProgress) => void) => {
    const unsubscribe = signalRService.current.on('EntityProgressUpdated', (data: any) => {
      callback(data.entityType, data.progress);
    });
    return unsubscribe;
  }, []);

  const onError = useCallback((callback: (error: any) => void) => {
    const unsubscribe = signalRService.current.on('ErrorOccurred', callback);
    return unsubscribe;
  }, []);

  // Auto-connect and polling setup
  useEffect(() => {
    if (autoConnect) {
      connect();
      
      // Set up fallback polling only when not connected
      pollTimer.current = setInterval(() => {
        if (!state.isConnected) {
          refresh();
        }
      }, pollInterval);
    }

    // Only disconnect if we're actually unmounting or migrationId changes
    return () => {
      if (state.isConnected) {
        disconnect();
      }
    };
  }, [autoConnect, connect, disconnect, pollInterval, refresh, state.isConnected, migrationId]);

  return {
    // State
    ...state,
    
    // Actions
    connect,
    disconnect,
    reconnect,
    refresh,
    clearHistory,
    clearErrors,
    onProgressUpdate,
    onStatusChange,
    onEntityUpdate,
    onError
  };
}; 