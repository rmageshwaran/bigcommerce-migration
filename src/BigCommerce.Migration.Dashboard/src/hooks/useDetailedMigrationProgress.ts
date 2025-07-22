import { useState, useCallback, useEffect, useRef } from 'react';
import { getSignalRService } from '../services/signalRService';
import { notificationService } from '../services/notificationService';

// Enhanced types for detailed progress tracking
export interface DetailedMigrationProgress {
  // Base progress information
  migrationId: string;
  status: string;
  totalEntities: number;
  processedEntities: number;
  successfulEntities: number;
  failedEntities: number;
  overallProgressPercentage: number;
  startTime: Date;
  lastUpdated: Date;
  elapsedTime: number;
  estimatedTimeRemaining: number;
  entitiesPerSecond: number;
  errorRate: number;
  currentPhase: string;
  currentEntity: string;

  // Enhanced detailed information
  currentProcessing: ProcessingContext;
  batchProgress: BatchProgressSummary;
  remainingWork: RemainingWorkload;
  performance: RealTimeMetrics;
}

export interface ProcessingContext {
  currentEntity: string;
  currentBatchNumber: number;
  currentBatch: CurrentBatchDetails;
  currentPhase: string;
  currentActivity: string;
  currentBatchStartTime: Date;
  estimatedBatchCompletion: Date;
}

export interface CurrentBatchDetails {
  batchNumber: number;
  batchSize: number;
  processedInBatch: number;
  batchProgressPercentage: number;
  batchProcessingSpeed: number;
  batchElapsedTime: number;
  estimatedBatchTimeRemaining: number;
}

export interface BatchProgressSummary {
  totalBatches: number;
  completedBatches: number;
  processingBatches: number;
  remainingBatches: number;
  batchCompletionPercentage: number;
  entityBatches: { [entityType: string]: EntityBatchProgress };
}

export interface EntityBatchProgress {
  entityType: string;
  totalBatches: number;
  completedBatches: number;
  currentBatch: number;
  remainingBatches: number;
  batchSize: number;
  completionPercentage: number;
}

export interface RemainingWorkload {
  remainingEntities: number;
  remainingBatches: number;
  estimatedTimeRemaining: number;
  remainingByEntityType: { [entityType: string]: number };
  remainingBatchesByEntityType: { [entityType: string]: number };
}

export interface RealTimeMetrics {
  currentProcessingSpeed: number;
  averageProcessingSpeed: number;
  peakProcessingSpeed: number;
  currentApiCallRate: number;
  currentErrorRate: number;
  performanceTrend: 'improving' | 'stable' | 'declining';
  lastCalculation: Date;
}

export interface DetailedMigrationEvent {
  id: string;
  type: 'DetailedProgress' | 'ProcessingContext' | 'BatchStarted' | 'BatchProgress' | 'BatchCompleted' | 'RemainingWorkload' | 'PerformanceMetrics' | 'EntityPhaseTransition' | 'MigrationMilestone';
  timestamp: Date;
  data: any;
  message: string;
}

export interface MilestoneEvent {
  migrationId: string;
  milestone: number;
  message: string;
  timeToMilestone: number;
  entitiesProcessed: number;
  averageSpeed: number;
  estimatedTimeToCompletion: number;
}

export interface UseDetailedMigrationProgressOptions {
  migrationId: string;
  autoConnect?: boolean;
  enableNotifications?: boolean;
  enablePerformanceTracking?: boolean;
  pollInterval?: number;
}

export interface UseDetailedMigrationProgressState {
  // Core progress data
  progress: DetailedMigrationProgress | null;
  
  // Connection state
  isConnected: boolean;
  connectionState: 'disconnected' | 'connecting' | 'connected' | 'reconnecting';
  lastHeartbeat: Date | null;
  
  // Real-time events
  recentEvents: DetailedMigrationEvent[];
  milestones: MilestoneEvent[];
  
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

export interface UseDetailedMigrationProgressActions {
  connect: () => Promise<void>;
  disconnect: () => void;
  reconnect: () => Promise<void>;
  refresh: () => Promise<void>;
  clearHistory: () => void;
  clearErrors: () => void;
  onDetailedProgress: (callback: (progress: DetailedMigrationProgress) => void) => () => void;
  onProcessingContext: (callback: (context: ProcessingContext) => void) => () => void;
  onBatchEvent: (callback: (event: any) => void) => () => void;
  onMilestone: (callback: (milestone: MilestoneEvent) => void) => () => void;
}

export const useDetailedMigrationProgress = (
  options: UseDetailedMigrationProgressOptions
): UseDetailedMigrationProgressState & UseDetailedMigrationProgressActions => {
  const {
    migrationId,
    autoConnect = true,
    enableNotifications = true,
    enablePerformanceTracking = true,
    pollInterval = 30000
  } = options;

  // State management
  const [state, setState] = useState<UseDetailedMigrationProgressState>({
    progress: null,
    isConnected: false,
    connectionState: 'disconnected',
    lastHeartbeat: null,
    recentEvents: [],
    milestones: [],
    errors: [],
    isLoading: true,
    lastUpdated: null
  });

  // Refs for cleanup and tracking
  const signalRService = useRef(getSignalRService());
  const unsubscribeCallbacks = useRef<Array<() => void>>([]);
  const eventIdCounter = useRef(0);
  const pollTimer = useRef<NodeJS.Timeout | null>(null);

  // Helper function to generate unique event IDs
  const generateEventId = useCallback(() => {
    return `detailed_event_${migrationId}_${Date.now()}_${++eventIdCounter.current}`;
  }, [migrationId]);

  // Add event to recent events list
  const addEvent = useCallback((
    type: DetailedMigrationEvent['type'],
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
        ...prev.recentEvents.slice(0, 99) // Keep last 100 events
      ]
    }));
  }, [generateEventId]);

  // Add error to error list
  const addError = useCallback((message: string, details?: any) => {
    setState(prev => ({
      ...prev,
      errors: [
        {
          id: generateEventId(),
          timestamp: new Date(),
          message,
          details
        },
        ...prev.errors.slice(0, 49) // Keep last 50 errors
      ]
    }));
  }, [generateEventId]);

  // Handle detailed progress updates
  const handleDetailedProgress = useCallback((event: any) => {
    if (event.MigrationId !== migrationId && event.migrationId !== migrationId) return;

    const progress = event.Progress || event.progress;
    if (!progress) return;

    setState(prev => ({
      ...prev,
      progress: {
        migrationId: progress.migrationId || progress.MigrationId,
        status: progress.status || progress.Status,
        totalEntities: progress.totalEntities || progress.TotalEntities || 0,
        processedEntities: progress.processedEntities || progress.ProcessedEntities || 0,
        successfulEntities: progress.successfulEntities || progress.SuccessfulEntities || 0,
        failedEntities: progress.failedEntities || progress.FailedEntities || 0,
        overallProgressPercentage: progress.overallProgressPercentage || progress.OverallProgressPercentage || 0,
        startTime: new Date(progress.startTime || progress.StartTime),
        lastUpdated: new Date(progress.lastUpdated || progress.LastUpdated || Date.now()),
        elapsedTime: progress.elapsedTime || progress.ElapsedTime || 0,
        estimatedTimeRemaining: progress.estimatedTimeRemaining || progress.EstimatedTimeRemaining || 0,
        entitiesPerSecond: progress.entitiesPerSecond || progress.EntitiesPerSecond || 0,
        errorRate: progress.errorRate || progress.ErrorRate || 0,
        currentPhase: progress.currentPhase || progress.CurrentPhase || '',
        currentEntity: progress.currentEntity || progress.CurrentEntity || '',
        currentProcessing: progress.currentProcessing || progress.CurrentProcessing || {},
        batchProgress: progress.batchProgress || progress.BatchProgress || {},
        remainingWork: progress.remainingWork || progress.RemainingWork || {},
        performance: progress.performance || progress.Performance || {}
      },
      isLoading: false,
      lastUpdated: new Date(),
      lastHeartbeat: new Date()
    }));

    addEvent('DetailedProgress', event, `Detailed progress: ${(progress.overallProgressPercentage || 0).toFixed(1)}%`);

    if (enableNotifications && progress.status === 'completed') {
      notificationService.success('Migration Complete', `Migration ${migrationId} completed successfully!`);
    }
  }, [migrationId, addEvent, enableNotifications]);

  // Handle processing context updates
  const handleProcessingContext = useCallback((event: any) => {
    if (event.MigrationId !== migrationId && event.migrationId !== migrationId) return;

    const context = event.Context || event.context;
    if (!context) return;

    setState(prev => ({
      ...prev,
      progress: prev.progress ? {
        ...prev.progress,
        currentProcessing: context
      } : prev.progress,
      lastUpdated: new Date(),
      lastHeartbeat: new Date()
    }));

    addEvent('ProcessingContext', event, 
      `Now processing: ${context.currentEntity} batch ${context.currentBatchNumber} (${context.currentActivity})`);
  }, [migrationId, addEvent]);

  // Handle batch events (started, progress, completed)
  const handleBatchEvent = useCallback((eventType: string, event: any) => {
    if (event.MigrationId !== migrationId && event.migrationId !== migrationId) return;

    const entityType = event.EntityType || event.entityType;
    const batchNumber = event.BatchNumber || event.batchNumber || event.BatchDetails?.batchNumber;

    setState(prev => ({
      ...prev,
      lastUpdated: new Date(),
      lastHeartbeat: new Date()
    }));

    let message = '';
    switch (eventType) {
      case 'BatchStarted':
        message = `Started ${entityType} batch ${batchNumber}`;
        break;
      case 'BatchProgress':
        const progressPct = event.BatchProgress?.batchProgressPercentage || event.Summary?.progressPercentage || 0;
        message = `${entityType} batch ${batchNumber}: ${progressPct.toFixed(1)}% complete`;
        break;
      case 'BatchCompleted':
        const successful = event.Summary?.successfulEntities || 0;
        const total = event.Summary?.entitiesProcessed || 0;
        message = `Completed ${entityType} batch ${batchNumber}: ${successful}/${total} successful`;
        break;
    }

    addEvent(eventType as any, event, message);

    if (enableNotifications && eventType === 'BatchCompleted') {
      const summary = event.Summary;
      if (summary && summary.successfulEntities === summary.entitiesProcessed) {
        notificationService.success('Batch Complete', message);
      } else if (summary && summary.failedEntities > 0) {
        notificationService.warning('Batch Complete with Errors', 
          `${message} (${summary.failedEntities} failed)`);
      }
    }
  }, [migrationId, addEvent, enableNotifications]);

  // Handle remaining workload updates
  const handleRemainingWorkload = useCallback((event: any) => {
    if (event.MigrationId !== migrationId && event.migrationId !== migrationId) return;

    const remainingWork = event.RemainingWork || event.remainingWork;
    if (!remainingWork) return;

    setState(prev => ({
      ...prev,
      progress: prev.progress ? {
        ...prev.progress,
        remainingWork: remainingWork
      } : prev.progress,
      lastUpdated: new Date(),
      lastHeartbeat: new Date()
    }));

    addEvent('RemainingWorkload', event, 
      `${remainingWork.remainingEntities} entities, ${remainingWork.remainingBatches} batches remaining`);
  }, [migrationId, addEvent]);

  // Handle performance metrics updates
  const handlePerformanceMetrics = useCallback((event: any) => {
    if (event.MigrationId !== migrationId && event.migrationId !== migrationId) return;

    const metrics = event.Metrics || event.metrics;
    if (!metrics) return;

    setState(prev => ({
      ...prev,
      progress: prev.progress ? {
        ...prev.progress,
        performance: metrics
      } : prev.progress,
      lastUpdated: new Date(),
      lastHeartbeat: new Date()
    }));

    if (enablePerformanceTracking) {
      addEvent('PerformanceMetrics', event, 
        `Performance: ${metrics.currentProcessingSpeed?.toFixed(1)} entities/sec (${metrics.performanceTrend})`);
    }
  }, [migrationId, addEvent, enablePerformanceTracking]);

  // Handle migration milestones
  const handleMilestone = useCallback((event: any) => {
    if (event.MigrationId !== migrationId && event.migrationId !== migrationId) return;

    const milestone = event.Milestone || event.milestone;
    const milestoneData = event.MilestoneData || event.milestoneData;

    const milestoneEvent: MilestoneEvent = {
      migrationId,
      milestone,
      message: event.Message || milestoneData?.message || `${milestone}% complete`,
      timeToMilestone: milestoneData?.timeToMilestone || 0,
      entitiesProcessed: milestoneData?.entitiesProcessed || 0,
      averageSpeed: milestoneData?.averageSpeed || 0,
      estimatedTimeToCompletion: milestoneData?.estimatedTimeToCompletion || 0
    };

    setState(prev => ({
      ...prev,
      milestones: [milestoneEvent, ...prev.milestones.slice(0, 9)], // Keep last 10 milestones
      lastUpdated: new Date(),
      lastHeartbeat: new Date()
    }));

    addEvent('MigrationMilestone', event, milestoneEvent.message);

    if (enableNotifications) {
      notificationService.info(`${milestone}% Complete!`, milestoneEvent.message);
    }
  }, [migrationId, addEvent, enableNotifications]);

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

  // Connection management
  const connect = useCallback(async () => {
    try {
      setState(prev => ({ ...prev, connectionState: 'connecting' }));
      
      await signalRService.current.connect();
      await signalRService.current.joinMigrationGroup(migrationId);
      
      // Set up event listeners for all detailed events
      const unsubscribeDetailedProgress = signalRService.current.on('DetailedProgress', handleDetailedProgress);
      const unsubscribeProcessingContext = signalRService.current.on('ProcessingContext', handleProcessingContext);
      const unsubscribeBatchStarted = signalRService.current.on('BatchStarted', (event: any) => handleBatchEvent('BatchStarted', event));
      const unsubscribeBatchProgress = signalRService.current.on('BatchProgress', (event: any) => handleBatchEvent('BatchProgress', event));
      const unsubscribeBatchCompleted = signalRService.current.on('BatchCompleted', (event: any) => handleBatchEvent('BatchCompleted', event));
      const unsubscribeRemainingWorkload = signalRService.current.on('RemainingWorkload', handleRemainingWorkload);
      const unsubscribePerformanceMetrics = signalRService.current.on('PerformanceMetrics', handlePerformanceMetrics);
      const unsubscribeMilestone = signalRService.current.on('MigrationMilestone', handleMilestone);
      const unsubscribeConnectionState = signalRService.current.on('connectionStateChanged', handleConnectionStateChange);
      
      unsubscribeCallbacks.current = [
        unsubscribeDetailedProgress,
        unsubscribeProcessingContext,
        unsubscribeBatchStarted,
        unsubscribeBatchProgress,
        unsubscribeBatchCompleted,
        unsubscribeRemainingWorkload,
        unsubscribePerformanceMetrics,
        unsubscribeMilestone,
        unsubscribeConnectionState
      ];
      
    } catch (error) {
      addError('Failed to connect to real-time updates', error);
      setState(prev => ({ ...prev, connectionState: 'disconnected' }));
    }
  }, [migrationId, handleDetailedProgress, handleProcessingContext, handleBatchEvent, 
      handleRemainingWorkload, handlePerformanceMetrics, handleMilestone, handleConnectionStateChange, addError]);

  // Disconnect from SignalR
  const disconnect = useCallback(() => {
    try {
      // Clean up event listeners
      unsubscribeCallbacks.current.forEach(unsubscribe => {
        try {
          unsubscribe();
        } catch (error) {
          console.warn('Error unsubscribing from SignalR event:', error);
        }
      });
      unsubscribeCallbacks.current = [];

      // Leave migration group and disconnect
      signalRService.current.leaveMigrationGroup(migrationId);
      signalRService.current.disconnect();

      setState(prev => ({ 
        ...prev, 
        isConnected: false, 
        connectionState: 'disconnected',
        lastHeartbeat: null 
      }));
    } catch (error) {
      addError('Error during disconnect', error);
    }
  }, [migrationId, addError]);

  // Reconnect to SignalR
  const reconnect = useCallback(async () => {
    disconnect();
    await new Promise(resolve => setTimeout(resolve, 1000)); // Brief delay
    await connect();
  }, [disconnect, connect]);

  // Refresh migration data
  const refresh = useCallback(async () => {
    setState(prev => ({ ...prev, isLoading: true }));
    
    try {
      const response = await fetch(`/api/dashboard/migrations/${migrationId}/detailed-status`);
      if (!response.ok) {
        throw new Error(`HTTP ${response.status}: ${response.statusText}`);
      }
      
      const data = await response.json();
      handleDetailedProgress({ Progress: data });
    } catch (error) {
      addError('Failed to fetch detailed migration progress', error);
    } finally {
      setState(prev => ({ ...prev, isLoading: false }));
    }
  }, [migrationId, handleDetailedProgress, addError]);

  // Clear event history
  const clearHistory = useCallback(() => {
    setState(prev => ({ ...prev, recentEvents: [], milestones: [] }));
  }, []);

  // Clear errors
  const clearErrors = useCallback(() => {
    setState(prev => ({ ...prev, errors: [] }));
  }, []);

  // Event subscription helpers
  const onDetailedProgress = useCallback((callback: (progress: DetailedMigrationProgress) => void) => {
    const unsubscribe = signalRService.current.on('DetailedProgress', (event: any) => {
      const progress = event.Progress || event.progress;
      if (progress && (event.MigrationId === migrationId || event.migrationId === migrationId)) {
        callback(progress);
      }
    });
    return unsubscribe;
  }, [migrationId]);

  const onProcessingContext = useCallback((callback: (context: ProcessingContext) => void) => {
    const unsubscribe = signalRService.current.on('ProcessingContext', (event: any) => {
      const context = event.Context || event.context;
      if (context && (event.MigrationId === migrationId || event.migrationId === migrationId)) {
        callback(context);
      }
    });
    return unsubscribe;
  }, [migrationId]);

  const onBatchEvent = useCallback((callback: (event: any) => void) => {
    const batchEvents = ['BatchStarted', 'BatchProgress', 'BatchCompleted'];
    const unsubscribes = batchEvents.map(eventName =>
      signalRService.current.on(eventName, (event: any) => {
        if (event.MigrationId === migrationId || event.migrationId === migrationId) {
          callback({ ...event, eventType: eventName });
        }
      })
    );
    
    return () => unsubscribes.forEach(unsubscribe => unsubscribe());
  }, [migrationId]);

  const onMilestone = useCallback((callback: (milestone: MilestoneEvent) => void) => {
    const unsubscribe = signalRService.current.on('MigrationMilestone', (event: any) => {
      if (event.MigrationId === migrationId || event.migrationId === migrationId) {
        const milestoneData = event.MilestoneData || event.milestoneData;
        callback({
          migrationId,
          milestone: event.Milestone || event.milestone,
          message: event.Message || milestoneData?.message || '',
          timeToMilestone: milestoneData?.timeToMilestone || 0,
          entitiesProcessed: milestoneData?.entitiesProcessed || 0,
          averageSpeed: milestoneData?.averageSpeed || 0,
          estimatedTimeToCompletion: milestoneData?.estimatedTimeToCompletion || 0
        });
      }
    });
    return unsubscribe;
  }, [migrationId]);

  // Auto-connect and polling setup
  useEffect(() => {
    if (autoConnect) {
      connect();
      
      // Set up fallback polling
      if (pollInterval > 0) {
        pollTimer.current = setInterval(() => {
          if (!state.isConnected) {
            refresh();
          }
        }, pollInterval);
      }
    }

    return () => {
      if (pollTimer.current) {
        clearInterval(pollTimer.current);
      }
      disconnect();
    };
  }, [autoConnect, connect, disconnect, refresh, pollInterval]);

  // Cleanup on unmount
  useEffect(() => {
    return () => {
      disconnect();
    };
  }, [disconnect]);

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
    onDetailedProgress,
    onProcessingContext,
    onBatchEvent,
    onMilestone
  };
}; 