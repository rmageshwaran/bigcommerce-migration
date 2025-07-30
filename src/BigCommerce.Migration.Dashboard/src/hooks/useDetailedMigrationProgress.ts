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

  // Check if SignalR is already connected to determine initial loading state
  const signalRService = useRef(getSignalRService());
  const isInitiallyConnected = signalRService.current.isConnected();
  
  // State management
  const [state, setState] = useState<UseDetailedMigrationProgressState>({
    progress: null,
    isConnected: isInitiallyConnected,
    connectionState: isInitiallyConnected ? 'connected' : 'disconnected',
    lastHeartbeat: isInitiallyConnected ? new Date() : null,
    recentEvents: [],
    milestones: [],
    errors: [],
    isLoading: !isInitiallyConnected, // Only show loading if not already connected
    lastUpdated: null
  });

  // Refs for cleanup and tracking
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

  // Handle detailed progress updates (now receives already-transformed data)
  const handleDetailedProgress = useCallback((progress: any) => {
    console.log('🎯 useDetailedMigrationProgress received transformed progress:', progress);
    console.log('🎯 Current migrationId:', migrationId, 'Progress migrationId:', progress?.migrationId);
    
    if (progress?.migrationId && progress.migrationId !== migrationId) {
      console.log('🎯 Skipping progress - wrong migrationId');
      return;
    }

    if (!progress) {
      console.log('⚠️ useDetailedMigrationProgress: No progress data received');
      return;
    }

    console.log('🎯 Setting progress state:', progress);
    
    // Data is already transformed by SignalR service, just use it directly
    setState(prev => {
      const newState = {
        ...prev,
        progress: {
          ...progress,
          // Ensure we have all required fields for the detailed dashboard
          currentProcessing: progress.currentProcessing || {},
          batchProgress: progress.batchProgress || {},
          remainingWork: progress.remainingWork || {},
          performance: progress.performance || {}
        },
        isLoading: false,
        lastUpdated: new Date(),
        lastHeartbeat: new Date()
      };
      console.log('🎯 New state will be:', newState);
      return newState;
    });

    addEvent('DetailedProgress', progress, `Detailed progress: ${(progress.overallProgressPercentage || 0).toFixed(1)}%`);

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

  // Handle status updates (maps to existing handleDetailedProgress)
  const handleStatusUpdate = useCallback((event: any) => {
    if (event.MigrationId !== migrationId && event.migrationId !== migrationId) return;

    // Map status event to progress update format
    const progressData = {
      migrationId: event.MigrationId || event.migrationId,
      status: event.Status || event.status,
      message: event.Message || event.message,
      timestamp: event.Timestamp || event.timestamp || new Date()
    };

    setState(prev => ({
      ...prev,
      progress: prev.progress ? { ...prev.progress, status: progressData.status } : prev.progress,
      lastUpdated: new Date(),
      lastHeartbeat: new Date()
    }));

    addEvent('MigrationStatus' as any, event, `Status: ${progressData.status} - ${progressData.message || ''}`);
  }, [migrationId, addEvent]);

  // Handle entity updates (maps to batch events)
  const handleEntityUpdate = useCallback((event: any) => {
    if (event.MigrationId !== migrationId && event.migrationId !== migrationId) return;

    const entityType = event.EntityType || event.entityType;
    const status = event.Status || event.status;

    setState(prev => ({
      ...prev,
      lastUpdated: new Date(),
      lastHeartbeat: new Date()
    }));

    const message = `${entityType}: ${event.ProcessedCount || 0}/${event.TotalCount || 0} processed (${status})`;
    addEvent('EntityProgress' as any, event, message);
  }, [migrationId, addEvent]);

  // Handle error events
  const handleErrorEvent = useCallback((event: any) => {
    if (event.MigrationId !== migrationId && event.migrationId !== migrationId) return;

    const errorMessage = event.Message || event.message || 'Unknown error';
    const entityType = event.EntityType || event.entityType || '';
    
    addError(`Error in ${entityType || 'migration'}: ${errorMessage}`, event);
    
    if (enableNotifications) {
      notificationService.error('Migration Error', errorMessage);
    }
  }, [migrationId, addError, enableNotifications]);

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
      console.log('🔄 useDetailedMigrationProgress: Starting connection for migrationId:', migrationId);
      
      // Check if already connected and set up listeners immediately
      if (signalRService.current.isConnected()) {
        console.log('✅ useDetailedMigrationProgress: SignalR already connected, setting up listeners immediately');
        
        // Join migration group (safe to call even if already joined)
        await signalRService.current.joinMigrationGroup(migrationId);
        
        // Set up event listeners immediately
        const unsubscribeProgress = signalRService.current.on('migrationProgress', handleDetailedProgress);
        const unsubscribeStatus = signalRService.current.on('MigrationStatus', handleStatusUpdate);
        const unsubscribeBatchProgress = signalRService.current.on('BatchProgressUpdated', (event: any) => handleBatchEvent('BatchProgress', event));
        const unsubscribeEntityProgress = signalRService.current.on('EntityProgressUpdated', handleEntityUpdate);
        const unsubscribeErrors = signalRService.current.on('ErrorOccurred', handleErrorEvent);
        const unsubscribeConnectionState = signalRService.current.on('connectionStateChanged', handleConnectionStateChange);

        // 🎯 CENTRALIZED SIGNALR: Subscribe to DetailedProgress events from sub-batch operations
        const unsubscribeDetailedProgress = signalRService.current.on('DetailedProgress', (event: any) => {
          console.log('🎯 DEBUG: Received DetailedProgress event (autoConnect):', event);
          
          // Check if this event contains progress data that should update the overall progress
          if (event && (event.MigrationId === migrationId || event.migrationId === migrationId)) {
            // If the event has overall progress data, use it to update the progress state
            if (event.overallProgressPercentage !== undefined || event.overallProgress !== undefined || 
                event.processedEntities !== undefined || event.totalEntities !== undefined) {
              
              console.log('🎯 DEBUG: DetailedProgress contains progress data, updating state (autoConnect)');
              
              // Create a progress object from the event data
              const progressData = {
                migrationId: event.migrationId || migrationId,
                status: event.status || 'in_progress',
                overallProgressPercentage: event.overallProgressPercentage || event.overallProgress || 0,
                totalEntities: event.totalEntities || 0,
                processedEntities: event.processedEntities || 0,
                successfulEntities: event.successfulEntities || 0,
                failedEntities: event.failedEntities || 0,
                currentEntity: event.entityType || event.currentEntity || 'entities',
                entitiesPerSecond: event.entitiesPerSecond || 0,
                errorRate: event.errorRate || 0,
                elapsedTime: event.elapsedTime || 0,
                estimatedTimeRemaining: event.estimatedTimeRemaining || 0,
                // Preserve existing nested structures or create minimal ones
                currentProcessing: event.currentProcessing || {},
                batchProgress: event.batchProgress || {},
                remainingWork: event.remainingWork || {},
                performance: event.performance || {}
              };
              
              handleDetailedProgress(progressData);
            }
          }
        });
        
        unsubscribeCallbacks.current = [
          unsubscribeProgress,
          unsubscribeStatus,
          unsubscribeBatchProgress,
          unsubscribeEntityProgress,
          unsubscribeErrors,
          unsubscribeConnectionState,
          unsubscribeDetailedProgress
        ];
        
        // Update state to connected and not loading
        setState(prev => ({ 
          ...prev, 
          connectionState: 'connected', 
          isConnected: true,
          isLoading: false,
          lastHeartbeat: new Date()
        }));
        
        console.log('✅ useDetailedMigrationProgress: Listeners set up for existing connection');
        
        // Fetch initial migration data and transform to match SignalR format
        console.log('🔄 useDetailedMigrationProgress: Fetching initial migration data...');
        try {
          const { config } = await import('../config/environment');
          const response = await fetch(`${config.api.baseUrl}/dashboard/migrations/${migrationId}/status`);
          if (response.ok) {
            const data = await response.json();
            console.log('✅ useDetailedMigrationProgress: Initial data fetched:', data);
            
            // Helper function to parse time strings like "00:00:20.4497323" to seconds
            const parseTimeStringToSeconds = (timeStr: string): number => {
              if (!timeStr || typeof timeStr !== 'string') return 0;
              try {
                const parts = timeStr.split(':');
                if (parts.length >= 3) {
                  const hours = parseInt(parts[0]) || 0;
                  const minutes = parseInt(parts[1]) || 0;
                  const seconds = parseFloat(parts[2]) || 0;
                  return hours * 3600 + minutes * 60 + seconds;
                }
                return 0;
              } catch {
                return 0;
              }
            };

            // Transform PascalCase API data to camelCase format with nested structures
            const transformedData = {
              migrationId: data.MigrationId || migrationId,
              status: (data.Status || 'unknown').toLowerCase(),
              totalEntities: data.TotalEntities || 0,
              processedEntities: data.ProcessedEntities || 0,
              successfulEntities: data.SuccessfulEntities || 0,
              failedEntities: data.FailedEntities || 0,
              overallProgressPercentage: data.OverallProgress || 0,
              currentEntity: data.CurrentEntity || 'Unknown',
              currentPhase: data.CurrentPhase || 'Processing',
              entitiesPerSecond: data.EntitiesPerSecond || 0,
              errorRate: data.ErrorRate || 0,
              elapsedTime: parseTimeStringToSeconds(data.ElapsedTime),
              estimatedTimeRemaining: parseTimeStringToSeconds(data.EstimatedTimeRemaining),
              startTime: data.StartTime ? new Date(data.StartTime) : new Date(),
              lastUpdated: new Date(),
              
              // Add required nested structures for Enhanced Dashboard
              currentProcessing: {
                currentEntity: data.CurrentEntity || 'Unknown',
                currentActivity: data.CurrentPhase || 'Processing...',
                currentBatchNumber: Math.ceil((data.ProcessedEntities || 0) / 50) || 1,
                currentBatch: {
                  batchProgressPercentage: ((data.OverallProgress || 0) % 10) * 10,
                  batchSize: 50,
                  processedInBatch: (data.ProcessedEntities || 0) % 50,
                  batchProcessingSpeed: data.EntitiesPerSecond || 0
                }
              },
              batchProgress: {
                totalBatches: Math.ceil((data.TotalEntities || 0) / 50) || 1,
                completedBatches: Math.floor((data.ProcessedEntities || 0) / 50) || 0,
                remainingBatches: Math.ceil(((data.TotalEntities || 0) - (data.ProcessedEntities || 0)) / 50) || 0
              },
              remainingWork: {
                remainingEntities: (data.TotalEntities || 0) - (data.ProcessedEntities || 0),
                estimatedTimeRemaining: parseTimeStringToSeconds(data.EstimatedTimeRemaining)
              },
              performance: {
                currentProcessingSpeed: data.EntitiesPerSecond || 0,
                averageProcessingSpeed: data.EntitiesPerSecond || 0,
                performanceTrend: (data.EntitiesPerSecond || 0) > 5 ? 'improving' : (data.EntitiesPerSecond || 0) > 2 ? 'stable' : 'declining'
              }
            };
            
            console.log('🔄 useDetailedMigrationProgress: Transformed API data:', transformedData);
            handleDetailedProgress(transformedData);
          } else {
            console.warn('⚠️ useDetailedMigrationProgress: Failed to fetch initial data:', response.status);
          }
        } catch (error) {
          console.warn('⚠️ useDetailedMigrationProgress: Error fetching initial data:', error);
        }
        
        return;
      }
      
      // Not connected yet, establish connection
      setState(prev => ({ ...prev, connectionState: 'connecting' }));
      
      console.log('🔄 useDetailedMigrationProgress: Calling signalRService.connect()');
      await signalRService.current.connect();
      
      console.log('🔄 useDetailedMigrationProgress: Joining migration group:', migrationId);
      await signalRService.current.joinMigrationGroup(migrationId);
      
      // Set up event listeners for all detailed events (using transformed events)
      console.log('🔄 useDetailedMigrationProgress: Setting up event listeners for transformed events');
      const unsubscribeProgress = signalRService.current.on('migrationProgress', handleDetailedProgress);
      const unsubscribeStatus = signalRService.current.on('MigrationStatus', handleStatusUpdate);
      console.log('✅ useDetailedMigrationProgress: Event listeners set up successfully');
      const unsubscribeBatchProgress = signalRService.current.on('BatchProgressUpdated', (event: any) => handleBatchEvent('BatchProgress', event));
      const unsubscribeEntityProgress = signalRService.current.on('EntityProgressUpdated', handleEntityUpdate);
      const unsubscribeErrors = signalRService.current.on('ErrorOccurred', handleErrorEvent);
      const unsubscribeConnectionState = signalRService.current.on('connectionStateChanged', handleConnectionStateChange);

      // 🎯 CENTRALIZED SIGNALR: Subscribe to DetailedProgress events from sub-batch operations
      const unsubscribeDetailedProgress = signalRService.current.on('DetailedProgress', (event: any) => {
        console.log('🎯 DEBUG: Received DetailedProgress event:', event);
        
        // Check if this event contains progress data that should update the overall progress
        if (event && (event.MigrationId === migrationId || event.migrationId === migrationId)) {
          // If the event has overall progress data, use it to update the progress state
          if (event.overallProgressPercentage !== undefined || event.overallProgress !== undefined || 
              event.processedEntities !== undefined || event.totalEntities !== undefined) {
            
            console.log('🎯 DEBUG: DetailedProgress contains progress data, updating state');
            
            // Create a progress object from the event data
            const progressData = {
              migrationId: event.migrationId || migrationId,
              status: event.status || 'in_progress',
              overallProgressPercentage: event.overallProgressPercentage || event.overallProgress || 0,
              totalEntities: event.totalEntities || 0,
              processedEntities: event.processedEntities || 0,
              successfulEntities: event.successfulEntities || 0,
              failedEntities: event.failedEntities || 0,
              currentEntity: event.entityType || event.currentEntity || 'entities',
              entitiesPerSecond: event.entitiesPerSecond || 0,
              errorRate: event.errorRate || 0,
              elapsedTime: event.elapsedTime || 0,
              estimatedTimeRemaining: event.estimatedTimeRemaining || 0,
              // Preserve existing nested structures or create minimal ones
              currentProcessing: event.currentProcessing || {},
              batchProgress: event.batchProgress || {},
              remainingWork: event.remainingWork || {},
              performance: event.performance || {}
            };
            
            handleDetailedProgress(progressData);
          }
        }
      });
      
      unsubscribeCallbacks.current = [
        unsubscribeProgress,
        unsubscribeStatus,
        unsubscribeBatchProgress,
        unsubscribeEntityProgress,
        unsubscribeErrors,
        unsubscribeConnectionState,
        unsubscribeDetailedProgress
      ];
      
      // Update connection state to connected
      console.log('✅ useDetailedMigrationProgress: Connection and setup complete');
      setState(prev => ({ 
        ...prev, 
        connectionState: 'connected', 
        isConnected: true,
        isLoading: false,
        lastHeartbeat: new Date()
      }));
      
      // Fetch initial migration data and transform
      console.log('🔄 useDetailedMigrationProgress: Fetching initial migration data after new connection...');
      try {
        const { config } = await import('../config/environment');
        const response = await fetch(`${config.api.baseUrl}/dashboard/migrations/${migrationId}/status`);
        if (response.ok) {
          const data = await response.json();
          console.log('✅ useDetailedMigrationProgress: Initial data fetched:', data);
          
          // Helper function to parse time strings like "00:00:20.4497323" to seconds
          const parseTimeStringToSeconds = (timeStr: string): number => {
            if (!timeStr || typeof timeStr !== 'string') return 0;
            try {
              const parts = timeStr.split(':');
              if (parts.length >= 3) {
                const hours = parseInt(parts[0]) || 0;
                const minutes = parseInt(parts[1]) || 0;
                const seconds = parseFloat(parts[2]) || 0;
                return hours * 3600 + minutes * 60 + seconds;
              }
              return 0;
            } catch {
              return 0;
            }
          };

          // Transform PascalCase API data to camelCase format with nested structures
          const transformedData = {
            migrationId: data.MigrationId || migrationId,
            status: (data.Status || 'unknown').toLowerCase(),
            totalEntities: data.TotalEntities || 0,
            processedEntities: data.ProcessedEntities || 0,
            successfulEntities: data.SuccessfulEntities || 0,
            failedEntities: data.FailedEntities || 0,
            overallProgressPercentage: data.OverallProgress || 0,
            currentEntity: data.CurrentEntity || 'Unknown',
            currentPhase: data.CurrentPhase || 'Processing',
            entitiesPerSecond: data.EntitiesPerSecond || 0,
            errorRate: data.ErrorRate || 0,
            elapsedTime: parseTimeStringToSeconds(data.ElapsedTime),
            estimatedTimeRemaining: parseTimeStringToSeconds(data.EstimatedTimeRemaining),
            startTime: data.StartTime ? new Date(data.StartTime) : new Date(),
            lastUpdated: new Date(),
            
            // Add required nested structures for Enhanced Dashboard
            currentProcessing: {
              currentEntity: data.CurrentEntity || 'Unknown',
              currentActivity: data.CurrentPhase || 'Processing...',
              currentBatchNumber: Math.ceil((data.ProcessedEntities || 0) / 50) || 1,
              currentBatch: {
                batchProgressPercentage: ((data.OverallProgress || 0) % 10) * 10,
                batchSize: 50,
                processedInBatch: (data.ProcessedEntities || 0) % 50,
                batchProcessingSpeed: data.EntitiesPerSecond || 0
              }
            },
            batchProgress: {
              totalBatches: Math.ceil((data.TotalEntities || 0) / 50) || 1,
              completedBatches: Math.floor((data.ProcessedEntities || 0) / 50) || 0,
              remainingBatches: Math.ceil(((data.TotalEntities || 0) - (data.ProcessedEntities || 0)) / 50) || 0
            },
            remainingWork: {
              remainingEntities: (data.TotalEntities || 0) - (data.ProcessedEntities || 0),
              estimatedTimeRemaining: parseTimeStringToSeconds(data.EstimatedTimeRemaining)
            },
            performance: {
              currentProcessingSpeed: data.EntitiesPerSecond || 0,
              averageProcessingSpeed: data.EntitiesPerSecond || 0,
              performanceTrend: (data.EntitiesPerSecond || 0) > 5 ? 'improving' : (data.EntitiesPerSecond || 0) > 2 ? 'stable' : 'declining'
            }
          };
          
          console.log('🔄 useDetailedMigrationProgress: Transformed API data:', transformedData);
          handleDetailedProgress(transformedData);
        } else {
          console.warn('⚠️ useDetailedMigrationProgress: Failed to fetch initial data:', response.status);
        }
      } catch (error) {
        console.warn('⚠️ useDetailedMigrationProgress: Error fetching initial data:', error);
      }
      
    } catch (error) {
      addError('Failed to connect to real-time updates', error);
      setState(prev => ({ ...prev, connectionState: 'disconnected', isLoading: false }));
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

      // Leave migration group but DON'T disconnect the shared SignalR connection
      // The connection is shared across all dashboard components
      signalRService.current.leaveMigrationGroup(migrationId);
      
      // Only update local state - don't disconnect the shared connection
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
      const { config } = await import('../config/environment');
      const response = await fetch(`${config.api.baseUrl}/dashboard/migrations/${migrationId}/status`);
      if (!response.ok) {
        throw new Error(`HTTP ${response.status}: ${response.statusText}`);
      }
      
      const data = await response.json();
      console.log('🔄 refresh: Fetched data:', data);
      
      // Helper function to parse time strings like "00:00:20.4497323" to seconds
      const parseTimeStringToSeconds = (timeStr: string): number => {
        if (!timeStr || typeof timeStr !== 'string') return 0;
        try {
          const parts = timeStr.split(':');
          if (parts.length >= 3) {
            const hours = parseInt(parts[0]) || 0;
            const minutes = parseInt(parts[1]) || 0;
            const seconds = parseFloat(parts[2]) || 0;
            return hours * 3600 + minutes * 60 + seconds;
          }
          return 0;
        } catch {
          return 0;
        }
      };

      // Transform PascalCase API data to camelCase format with nested structures
      const transformedData = {
        migrationId: data.MigrationId || migrationId,
        status: (data.Status || 'unknown').toLowerCase(),
        totalEntities: data.TotalEntities || 0,
        processedEntities: data.ProcessedEntities || 0,
        successfulEntities: data.SuccessfulEntities || 0,
        failedEntities: data.FailedEntities || 0,
        overallProgressPercentage: data.OverallProgress || 0,
        currentEntity: data.CurrentEntity || 'Unknown',
        currentPhase: data.CurrentPhase || 'Processing',
        entitiesPerSecond: data.EntitiesPerSecond || 0,
        errorRate: data.ErrorRate || 0,
        elapsedTime: parseTimeStringToSeconds(data.ElapsedTime),
        estimatedTimeRemaining: parseTimeStringToSeconds(data.EstimatedTimeRemaining),
        startTime: data.StartTime ? new Date(data.StartTime) : new Date(),
        lastUpdated: new Date(),
        
        // Add required nested structures for Enhanced Dashboard
        currentProcessing: {
          currentEntity: data.CurrentEntity || 'Unknown',
          currentActivity: data.CurrentPhase || 'Processing...',
          currentBatchNumber: Math.ceil((data.ProcessedEntities || 0) / 50) || 1,
          currentBatch: {
            batchProgressPercentage: ((data.OverallProgress || 0) % 10) * 10,
            batchSize: 50,
            processedInBatch: (data.ProcessedEntities || 0) % 50,
            batchProcessingSpeed: data.EntitiesPerSecond || 0
          }
        },
        batchProgress: {
          totalBatches: Math.ceil((data.TotalEntities || 0) / 50) || 1,
          completedBatches: Math.floor((data.ProcessedEntities || 0) / 50) || 0,
          remainingBatches: Math.ceil(((data.TotalEntities || 0) - (data.ProcessedEntities || 0)) / 50) || 0
        },
        remainingWork: {
          remainingEntities: (data.TotalEntities || 0) - (data.ProcessedEntities || 0),
          estimatedTimeRemaining: parseTimeStringToSeconds(data.EstimatedTimeRemaining)
        },
        performance: {
          currentProcessingSpeed: data.EntitiesPerSecond || 0,
          averageProcessingSpeed: data.EntitiesPerSecond || 0,
          performanceTrend: (data.EntitiesPerSecond || 0) > 5 ? 'improving' : (data.EntitiesPerSecond || 0) > 2 ? 'stable' : 'declining'
        }
      };
      
      handleDetailedProgress(transformedData);
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

  // Auto-connect setup (runs once on mount)
  useEffect(() => {
    console.log('🔄 Connection setup - autoConnect:', autoConnect, 'migrationId:', migrationId);
    if (autoConnect) {
      connect();
    }
    
    return () => {
      console.log('🧹 Cleanup: Disconnecting on unmount');
      disconnect();
    };
  }, [autoConnect, migrationId]); // Stable dependencies

  // Polling setup (separate from connection) - using refs to avoid stale closures
  useEffect(() => {
    console.log('🔄 Polling setup - pollInterval:', pollInterval);
    
    if (pollInterval > 0) {
      pollTimer.current = setInterval(() => {
        // Use a callback to get current state to avoid stale closure
        setState(currentState => {
          console.log('🔄 Polling timer fired - isConnected:', currentState.isConnected);
          if (!currentState.isConnected) {
            console.log('🔄 Not connected, triggering refresh');
            // Call refresh in next tick to avoid state update during render
            setTimeout(() => refresh(), 0);
          } else {
            console.log('🔄 Connected, skipping refresh');
          }
          return currentState; // No state change
        });
      }, pollInterval);
      console.log('✅ Polling timer set up with interval:', pollInterval, 'ms');
    }

    return () => {
      console.log('🧹 Cleanup: Clearing polling timer');
      if (pollTimer.current) {
        clearInterval(pollTimer.current);
        pollTimer.current = null;
      }
    };
  }, [pollInterval]); // Only re-setup when pollInterval changes

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