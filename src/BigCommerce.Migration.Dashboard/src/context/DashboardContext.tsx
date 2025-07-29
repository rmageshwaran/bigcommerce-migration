import React, { createContext, useContext, useReducer, useEffect } from 'react';
import type { ReactNode } from 'react';
import type { 
  MigrationProgress, 
  SystemHealthData, 
  SignalRConnection,
  DashboardError 
} from '../types';
import { getSignalRService } from '../services/signalRService';
import { getApiService } from '../services/apiService';
import { notificationService } from '../services/notificationService';

/**
 * Dashboard State Interface
 * 
 * Manages the complete state of the migration dashboard including:
 * - SignalR connection status and real-time updates
 * - API connection status
 * - Active migration data and progress
 * - UI loading states and error handling
 * - Polling fallback mechanism for when SignalR fails
 * 
 * This state is shared across all dashboard components and provides
 * a single source of truth for real-time migration monitoring.
 */
interface DashboardState {
  // Connection status
  signalRConnection: SignalRConnection;
  apiConnected: boolean;
  
  // Data
  activeMigrations: Map<string, MigrationProgress>;
  cancelledMigrations: Set<string>;
  systemHealth: SystemHealthData | null;
  
  // UI State
  isLoading: boolean;
  errors: DashboardError[];
  
  // Settings
  autoRefreshEnabled: boolean;
  refreshInterval: number;
  
  // Polling fallback
  isPollingEnabled: boolean;
  pollingInterval: number;
  lastPollingUpdate: Date | null;
}

/**
 * Dashboard Actions
 * 
 * Defines all possible actions that can be dispatched to update the dashboard state.
 * Actions are handled by the dashboardReducer to ensure immutable state updates.
 * 
 * Key action categories:
 * - Connection management (SignalR, API)
 * - Data updates (migrations, system health)
 * - UI state (loading, errors)
 * - Settings (refresh intervals, polling)
 */
type DashboardAction =
  | { type: 'SET_LOADING'; payload: boolean }
  | { type: 'SET_SIGNALR_CONNECTION'; payload: SignalRConnection }
  | { type: 'SET_API_CONNECTED'; payload: boolean }
  | { type: 'UPDATE_MIGRATION_PROGRESS'; payload: MigrationProgress }
  | { type: 'REMOVE_MIGRATION'; payload: string }
  | { type: 'ADD_CANCELLED_MIGRATION'; payload: string }
  | { type: 'REMOVE_CANCELLED_MIGRATION'; payload: string }
  | { type: 'UPDATE_SYSTEM_HEALTH'; payload: SystemHealthData }
  | { type: 'ADD_ERROR'; payload: DashboardError }
  | { type: 'REMOVE_ERROR'; payload: string }
  | { type: 'CLEAR_ERRORS' }
  | { type: 'SET_AUTO_REFRESH'; payload: boolean }
  | { type: 'SET_REFRESH_INTERVAL'; payload: number }
  | { type: 'SET_POLLING_ENABLED'; payload: boolean }
  | { type: 'SET_POLLING_INTERVAL'; payload: number }
  | { type: 'UPDATE_LAST_POLLING'; payload: Date }
  | { type: 'RESET_STATE' };

// Initial State
const initialState: DashboardState = {
  signalRConnection: {
    connectionId: '',
    isConnected: false,
    connectionState: 'Disconnected'
  },
  apiConnected: false,
  activeMigrations: new Map(),
  cancelledMigrations: new Set(),
  systemHealth: null,
  isLoading: false,
  errors: [],
  autoRefreshEnabled: true,
  refreshInterval: 30000, // 30 seconds
  
  // Polling fallback
  isPollingEnabled: false,
  pollingInterval: 10000, // 10 seconds for polling fallback
  lastPollingUpdate: null
};

// Reducer
function dashboardReducer(state: DashboardState, action: DashboardAction): DashboardState {
  switch (action.type) {
    case 'SET_LOADING':
      return { ...state, isLoading: action.payload };
      
    case 'SET_SIGNALR_CONNECTION':
      return { ...state, signalRConnection: action.payload };
      
    case 'SET_API_CONNECTED':
      return { ...state, apiConnected: action.payload };
      
    case 'UPDATE_MIGRATION_PROGRESS':
      // Don't add cancelled migrations back to active state
      if (state.cancelledMigrations.has(action.payload.migrationId)) {
        console.log('🚫 Skipping update for cancelled migration:', action.payload.migrationId);
        return state;
      }
      const newMigrations = new Map(state.activeMigrations);
      newMigrations.set(action.payload.migrationId, action.payload);
      return { ...state, activeMigrations: newMigrations };
      
    case 'REMOVE_MIGRATION':
      const filteredMigrations = new Map(state.activeMigrations);
      filteredMigrations.delete(action.payload);
      return { ...state, activeMigrations: filteredMigrations };
      
    case 'ADD_CANCELLED_MIGRATION':
      const newCancelledMigrations = new Set(state.cancelledMigrations);
      newCancelledMigrations.add(action.payload);
      return { ...state, cancelledMigrations: newCancelledMigrations };
      
    case 'REMOVE_CANCELLED_MIGRATION':
      const updatedCancelledMigrations = new Set(state.cancelledMigrations);
      updatedCancelledMigrations.delete(action.payload);
      return { ...state, cancelledMigrations: updatedCancelledMigrations };
      
    case 'UPDATE_SYSTEM_HEALTH':
      return { ...state, systemHealth: action.payload };
      
    case 'ADD_ERROR':
      return { 
        ...state, 
        errors: [...state.errors.filter(e => e.code !== action.payload.code), action.payload] 
      };
      
    case 'REMOVE_ERROR':
      return { 
        ...state, 
        errors: state.errors.filter(e => e.code !== action.payload) 
      };
      
    case 'CLEAR_ERRORS':
      return { ...state, errors: [] };
      
    case 'SET_AUTO_REFRESH':
      return { ...state, autoRefreshEnabled: action.payload };
      
    case 'SET_REFRESH_INTERVAL':
      return { ...state, refreshInterval: action.payload };
      
    case 'SET_POLLING_ENABLED':
      return { ...state, isPollingEnabled: action.payload };
      
    case 'SET_POLLING_INTERVAL':
      return { ...state, pollingInterval: action.payload };
      
    case 'UPDATE_LAST_POLLING':
      return { ...state, lastPollingUpdate: action.payload };
      
    case 'RESET_STATE':
      return { ...initialState };
      
    default:
      return state;
  }
}

// Context Interface
interface DashboardContextType {
  state: DashboardState;
  dispatch: React.Dispatch<DashboardAction>;
  
  // Actions
  connectServices: () => Promise<void>;
  disconnectServices: () => Promise<void>;
  refreshData: () => Promise<void>;
  joinMigrationGroup: (migrationId: string) => Promise<void>;
  leaveMigrationGroup: (migrationId: string) => Promise<void>;
  removeMigration: (migrationId: string) => void;
  addCancelledMigration: (migrationId: string) => void;
  removeCancelledMigration: (migrationId: string) => void;
  clearErrors: () => void;
  addError: (error: DashboardError) => void;
  removeError: (errorCode: string) => void;
  
  // Polling fallback
  startPolling: () => void;
  stopPolling: () => void;
  setPollingInterval: (interval: number) => void;
}

// Create Context
const DashboardContext = createContext<DashboardContextType | undefined>(undefined);

// Provider Props
interface DashboardProviderProps {
  children: ReactNode;
  config?: {
    signalRUrl?: string;
    apiBaseUrl?: string;
    autoConnect?: boolean;
  };
}

// Dashboard Provider Component
export const DashboardProvider: React.FC<DashboardProviderProps> = ({ 
  children, 
  config = {} 
}) => {
  const [state, dispatch] = useReducer(dashboardReducer, initialState);
  const { autoConnect = true } = config;

  const signalRService = getSignalRService(); // Use default configuration
  const apiService = getApiService(config.apiBaseUrl ? { baseURL: config.apiBaseUrl } : undefined);

  /**
   * Connect to services
   * 
   * Initializes connections to both API and SignalR services in parallel for optimal performance.
   * This function is called when the dashboard loads and handles the initial setup of
   * real-time communication channels.
   * 
   * Implementation Details:
   * - Starts API connection test and SignalR connection simultaneously
   * - Uses Promise.all() for parallel execution to minimize connection time
   * - Handles individual service failures gracefully
   * - Only attempts data loading if API connection is successful
   * 
   * Error Handling:
   * - API failures are logged but don't prevent SignalR connection
   * - SignalR failures are logged and user is notified
   * - Connection errors are stored in state for UI display
   */
  const connectServices = async (): Promise<void> => {
    try {
      dispatch({ type: 'SET_LOADING', payload: true });
      
      // Start both API and SignalR connections in parallel for faster initialization
      console.log('🔌 Starting parallel service connections...');
      
      // Start API connection test
      const apiConnectionPromise = apiService.testConnection().catch(error => {
        console.warn('⚠️ API connection test failed:', error);
        return false;
      });
      
      // Start SignalR connection (if not already connected)
      const signalRConnectionPromise = signalRService.isConnected() 
        ? Promise.resolve(true)
        : signalRService.connect().then(() => {
            console.log('✅ SignalR connected successfully');
            return true;
          }).catch(error => {
            console.warn('⚠️ SignalR connection failed:', error);
            addError({
              code: 'SIGNALR_CONNECTION_FAILED',
              message: 'Real-time connection failed',
              details: 'SignalR connection could not be established. Real-time updates will not be available.',
              timestamp: new Date()
            });
            return false;
          });
      
      // Wait for both connections to complete (or fail)
      const [apiConnected, signalRConnected] = await Promise.all([
        apiConnectionPromise,
        signalRConnectionPromise
      ]);
      
      // Update API connection state
      dispatch({ type: 'SET_API_CONNECTED', payload: apiConnected });
      
      if (!apiConnected) {
        addError({
          code: 'API_UNAVAILABLE',
          message: 'Backend API is not available',
          details: 'Azure Functions may not be running on localhost:7071. Please start the backend services.',
          timestamp: new Date()
        });
      }
      
      // Only attempt data loading if API is connected
      if (apiConnected) {
        console.log('📊 Loading initial data...');
        await refreshData();
      } else {
        console.log('⏭️ Skipping data load due to API connection failure');
      }
      
    } catch (error) {
      console.error('🚨 Service connection failed:', error);
      addError({
        code: 'CONNECTION_FAILED',
        message: 'Failed to connect to services',
        details: error instanceof Error ? error.message : 'Unknown error',
        timestamp: new Date()
      });
    } finally {
      dispatch({ type: 'SET_LOADING', payload: false });
    }
  };

  // Disconnect from services
  const disconnectServices = async (): Promise<void> => {
    try {
      await signalRService.disconnect();
      dispatch({ type: 'SET_SIGNALR_CONNECTION', payload: {
        connectionId: '',
        isConnected: false,
        connectionState: 'Disconnected'
      }});
    } catch (error) {
      console.error('Error disconnecting services:', error);
    }
  };

  /**
   * Refresh all data
   * 
   * Fetches the latest migration data and system health information from the API.
   * This function is called on initial load and can be triggered manually by users.
   * 
   * Key Features:
   * - Fetches active migrations and system health in parallel
   * - Pre-joins SignalR groups for all active migrations (Task 3)
   * - Handles API failures gracefully without breaking the dashboard
   * - Updates state with fresh data for real-time monitoring
   * 
   * Group Pre-joining (Task 3):
   * - Automatically joins SignalR groups for all active migrations
   * - Ensures real-time updates are received immediately
   * - Handles group join failures gracefully
   * - Uses Promise.allSettled() for parallel group joining
   */
  const refreshData = async (): Promise<void> => {
    try {
      console.log('🔄 Refreshing dashboard data...');
      
      // Fetch system health (non-critical)
      try {
        const healthData = await apiService.getSystemHealth();
        dispatch({ type: 'UPDATE_SYSTEM_HEALTH', payload: healthData });
        console.log('✅ System health data refreshed');
      } catch (healthError) {
        console.warn('⚠️ Failed to fetch system health:', healthError);
        // Don't throw - system health is not critical
      }
      
      // Fetch active migrations (non-critical)
      try {
        const migrationsResponse = await apiService.getActiveMigrations();
        console.log('📊 Active migrations response:', migrationsResponse);
        
        // Handle response safely - check if data exists and is an array
        if (migrationsResponse && migrationsResponse.data && Array.isArray(migrationsResponse.data)) {
          migrationsResponse.data.forEach(migration => {
            dispatch({ type: 'UPDATE_MIGRATION_PROGRESS', payload: migration });
          });
          console.log('✅ Active migrations data refreshed:', migrationsResponse.data.length, 'migrations');
          
          // Pre-join SignalR groups for all active migrations (Task 3)
          if (signalRService.isConnected()) {
            console.log('🔗 Pre-joining SignalR groups for active migrations...');
            const joinPromises = migrationsResponse.data.map(async (migration) => {
              try {
                await signalRService.joinMigrationGroup(migration.migrationId);
                console.log(`✅ Joined SignalR group for migration: ${migration.migrationId}`);
              } catch (joinError) {
                console.warn(`⚠️ Failed to join SignalR group for migration ${migration.migrationId}:`, joinError);
                // Don't throw - individual group join failures shouldn't break the whole process
              }
            });
            
            // Wait for all group joins to complete (but don't fail if some fail)
            await Promise.allSettled(joinPromises);
            console.log('✅ SignalR group pre-join process completed');
          } else {
            console.log('⚠️ SignalR not connected - skipping group pre-join');
          }
        } else {
          console.warn('⚠️ Active migrations response has unexpected structure:', migrationsResponse);
        }
      } catch (migrationsError) {
        console.warn('⚠️ Failed to fetch active migrations:', migrationsError);
        // Don't throw - migrations data is not critical for initial load
      }
      
    } catch (error) {
      console.warn('⚠️ Data refresh encountered issues:', error);
      // Only add error if it's a critical failure
      if (error instanceof Error && error.message.includes('Network Error')) {
        addError({
          code: 'DATA_REFRESH_FAILED',
          message: 'Failed to refresh dashboard data',
          details: 'Network connectivity issues detected. Some data may be outdated.',
          timestamp: new Date()
        });
      }
      // Don't throw - allow the dashboard to continue functioning
    }
  };

  // Join migration monitoring group
  const joinMigrationGroup = async (migrationId: string): Promise<void> => {
    try {
      await signalRService.joinMigrationGroup(migrationId);
    } catch (error) {
      addError({
        code: 'JOIN_GROUP_FAILED',
        message: `Failed to join migration group ${migrationId}`,
        details: error instanceof Error ? error.message : 'Unknown error',
        timestamp: new Date()
      });
    }
  };

  // Leave migration monitoring group
  const leaveMigrationGroup = async (migrationId: string): Promise<void> => {
    try {
      await signalRService.leaveMigrationGroup(migrationId);
    } catch (error) {
      console.error(`Failed to leave migration group ${migrationId}:`, error);
    }
  };

  // Remove migration from active state (for immediate UI updates after cancellation)
  const removeMigration = (migrationId: string): void => {
    console.log(`🗑️ Removing migration from active state: ${migrationId}`);
    dispatch({ type: 'REMOVE_MIGRATION', payload: migrationId });
    // Also add to cancelled list to prevent background processes from adding it back
    dispatch({ type: 'ADD_CANCELLED_MIGRATION', payload: migrationId });
  };

  // Add migration to cancelled list (prevents background refresh from adding it back)
  const addCancelledMigration = (migrationId: string): void => {
    console.log(`🚫 Adding migration to cancelled list: ${migrationId}`);
    dispatch({ type: 'ADD_CANCELLED_MIGRATION', payload: migrationId });
  };

  // Remove migration from cancelled list (after timeout or manual cleanup)
  const removeCancelledMigration = (migrationId: string): void => {
    console.log(`✅ Removing migration from cancelled list: ${migrationId}`);
    dispatch({ type: 'REMOVE_CANCELLED_MIGRATION', payload: migrationId });
  };

  // Error management
  const clearErrors = (): void => {
    dispatch({ type: 'CLEAR_ERRORS' });
  };

  const addError = (error: DashboardError): void => {
    dispatch({ type: 'ADD_ERROR', payload: error });
  };

  const removeError = (errorCode: string): void => {
    dispatch({ type: 'REMOVE_ERROR', payload: errorCode });
  };

  /**
   * Polling fallback functions
   * 
   * These functions manage the polling fallback mechanism that ensures users
   * continue to receive migration updates even when SignalR connection fails.
   * 
   * Polling Strategy:
   * - Automatically starts when SignalR disconnects
   * - Uses configurable intervals (default: 10 seconds)
   * - Automatically stops when SignalR reconnects
   * - Provides visual feedback to users about polling status
   */
  const startPolling = (): void => {
    dispatch({ type: 'SET_POLLING_ENABLED', payload: true });
    console.log('🔄 Starting polling fallback for migration updates');
  };

  const stopPolling = (): void => {
    dispatch({ type: 'SET_POLLING_ENABLED', payload: false });
    console.log('⏹️ Stopping polling fallback');
  };

  const setPollingInterval = (interval: number): void => {
    dispatch({ type: 'SET_POLLING_INTERVAL', payload: interval });
    console.log(`⏱️ Polling interval set to ${interval}ms`);
  };

  // Set up SignalR event listeners
  useEffect(() => {
    const signalRService = getSignalRService();

    // Connection state listener
    const connectionUnsubscribe = signalRService.on('connectionStateChanged', (connectionData) => {
      dispatch({
        type: 'SET_SIGNALR_CONNECTION',
        payload: {
          connectionId: connectionData.connectionId || '',
          isConnected: connectionData.state === 'Connected',
          lastConnected: connectionData.state === 'Connected' ? new Date() : undefined,
          connectionState: connectionData.state
        }
      });
    });

    // Migration progress updates
    const progressUnsubscribe = signalRService.on('migrationProgress', (progress: MigrationProgress) => {
      console.log('🎯 DashboardContext received migrationProgress:', progress);
      
      // Fix backend status mismatch: detect if migration is actually running despite "queued" status
      const normalizedProgress = { ...progress };
      
      if (progress.status === 'queued' || progress.status === 'pending') {
        // Check if migration is actually running based on data indicators
        const isActuallyRunning = 
          progress.processedEntities > 0 ||
          progress.currentPhase === 'Processing' ||
          progress.overallProgressPercentage > 0 ||
          (progress.currentEntity && progress.currentEntity !== '') ||
          ((progress as any).currentProcessing && Object.keys((progress as any).currentProcessing).length > 0);
          
        if (isActuallyRunning) {
          console.log(`🔧 Status normalization: Backend says "${progress.status}" but migration is actually running`);
          console.log(`📊 Evidence: processedEntities=${progress.processedEntities}, currentPhase="${progress.currentPhase}", progress=${progress.overallProgressPercentage}%`);
          normalizedProgress.status = 'in_progress';
        }
      }
      
      dispatch({ type: 'UPDATE_MIGRATION_PROGRESS', payload: normalizedProgress });
    });

    // Migration status updates (completed, failed, cancelled, etc.)
    const statusUnsubscribe = signalRService.on('MigrationStatus', (statusData) => {
      console.log('🎯 DashboardContext received MigrationStatus:', statusData);
      // Handle different types of migration status updates
      if (statusData.status === 'completed') {
        // Calculate duration if possible (fallback to "just now" if no duration data)
        const duration = statusData.data?.duration || 'just now';
        const migrationName = statusData.data?.name || `Migration ${statusData.migrationId}`;
        
        notificationService.migrationCompleted(
          statusData.migrationId,
          migrationName,
          duration
        );
        
        // Update migration progress to show completion
        const completedProgress: MigrationProgress = {
          migrationId: statusData.migrationId,
          status: 'completed',
          totalEntities: 0,
          processedEntities: 0,
          successfulEntities: 0,
          failedEntities: 0,
          startTime: new Date(),
          lastUpdated: new Date(),
          entitiesPerSecond: 0,
          estimatedTimeRemaining: 0,
          elapsedTime: 0,
          currentEntity: '',
          currentPhase: 'completed',
          overallProgressPercentage: 100,
          entityProgress: {},
          errorRate: 0
        };
        dispatch({ type: 'UPDATE_MIGRATION_PROGRESS', payload: completedProgress });
      } else if (statusData.status === 'failed') {
        const migrationName = statusData.data?.name || `Migration ${statusData.migrationId}`;
        const errorMessage = statusData.error?.message || statusData.data?.error || 'Unknown error occurred';
        
        notificationService.migrationFailed(
          statusData.migrationId,
          migrationName,
          errorMessage
        );
      } else if (statusData.status === 'cancelled') {
        const migrationName = statusData.data?.name || `Migration ${statusData.migrationId}`;
        
        notificationService.migrationCancelled(
          statusData.migrationId,
          migrationName
        );
      } else if (statusData.status === 'started') {
        const migrationName = statusData.data?.name || `Migration ${statusData.migrationId}`;
        
        notificationService.migrationStarted(
          statusData.migrationId,
          migrationName
        );
      }
    });

    // Sub-batch completion progress updates (real-time progress from sub-batch optimization)
    const subBatchCompletedUnsubscribe = signalRService.on('subBatchCompleted', (subBatchData: any) => {
      console.log('🎯 DashboardContext received subBatchCompleted:', subBatchData);
      
      const migrationId = subBatchData.migrationId || subBatchData.MigrationId;
      if (!migrationId) return;
      
      // Don't update cancelled migrations
      if (state.cancelledMigrations.has(migrationId)) {
        console.log('🚫 Skipping sub-batch update for cancelled migration:', migrationId);
        return;
      }
      
      // Get current migration to preserve other data
      const currentMigration = state.activeMigrations.get(migrationId);
      if (!currentMigration) return;
      
      // Create updated progress using cumulative data from sub-batch
      const updatedProgress: MigrationProgress = {
        ...currentMigration,
        migrationId: migrationId,
        totalEntities: subBatchData.totalMigrationEntities || currentMigration.totalEntities,
        processedEntities: subBatchData.cumulativeSuccessfulEntities + (subBatchData.cumulativeFailedEntities || 0),
        successfulEntities: subBatchData.cumulativeSuccessfulEntities || currentMigration.successfulEntities,
        failedEntities: subBatchData.cumulativeFailedEntities || currentMigration.failedEntities,
        overallProgressPercentage: subBatchData.progressPercentage || currentMigration.overallProgressPercentage,
        currentEntity: subBatchData.entityType || currentMigration.currentEntity,
        lastUpdated: new Date(),
        status: subBatchData.progressPercentage >= 100 ? 'completed' : 'in_progress',
        currentPhase: subBatchData.progressPercentage >= 100 ? 'Completed' : 'Processing'
      };
      
      console.log('📊 DashboardContext updating progress from sub-batch:', {
        migrationId,
        totalEntities: updatedProgress.totalEntities,
        processedEntities: updatedProgress.processedEntities,
        successfulEntities: updatedProgress.successfulEntities,
        progressPercentage: updatedProgress.overallProgressPercentage
      });
      
      dispatch({ type: 'UPDATE_MIGRATION_PROGRESS', payload: updatedProgress });
    });

    // Entity progress updates (when individual entities complete)
    const entityProgressUnsubscribe = signalRService.on('EntityProgressUpdated', (entityData: any) => {
      console.log('🔄 DashboardContext received EntityProgressUpdated:', entityData);
      
      const migrationId = entityData.migrationId || entityData.MigrationId;
      if (!migrationId) return;
      
      // If entity status is completed, update the overall migration status
      if (entityData.Status === 'completed' || entityData.status === 'completed') {
        console.log('✅ DashboardContext: Entity completed, updating migration status to completed');
        
        // Update the migration to completed status
        const completedProgress: MigrationProgress = {
          migrationId: migrationId,
          status: 'completed',
          totalEntities: entityData.TotalCount || entityData.totalCount || 0,
          processedEntities: entityData.ProcessedCount || entityData.processedCount || 0,
          successfulEntities: entityData.SuccessCount || entityData.successCount || 0,
          failedEntities: entityData.FailureCount || entityData.failureCount || 0,
          startTime: new Date(),
          lastUpdated: new Date(),
          entitiesPerSecond: 0,
          estimatedTimeRemaining: 0,
          elapsedTime: 0,
          currentEntity: entityData.EntityType || entityData.entityType || '',
          currentPhase: 'completed',
          overallProgressPercentage: 100,
          entityProgress: {},
          errorRate: 0
        };
        dispatch({ type: 'UPDATE_MIGRATION_PROGRESS', payload: completedProgress });
        
        // Show completion notification
        const migrationName = `Migration ${migrationId}`;
        notificationService.migrationCompleted(
          migrationId,
          migrationName,
          entityData.ProcessingTime || 'just completed'
        );
      } else if (migrationId) {
        // Update entity counts for other statuses
        dispatch({ 
          type: 'UPDATE_MIGRATION_PROGRESS', 
          payload: {
            migrationId: migrationId,
            processedEntities: entityData.ProcessedCount || entityData.processedCount,
            totalEntities: entityData.TotalCount || entityData.totalCount,
            lastUpdated: new Date()
          } as MigrationProgress
        });
      }
    });

    // System health updates
    const healthUnsubscribe = signalRService.on('systemHealth', (health: SystemHealthData) => {
      dispatch({ type: 'UPDATE_SYSTEM_HEALTH', payload: health });
    });

    // Error handling
    const errorUnsubscribe = signalRService.on('error', (errorData) => {
      notificationService.error(
        'SignalR Connection Error',
        errorData.message || 'An error occurred with the real-time connection'
      );
    });

    // Auto-connect on mount
    if (autoConnect) {
      connectServices();
    }

    // Cleanup
    return () => {
      console.log('🧹 Cleaning up DashboardContext...');
      
      // Unsubscribe from SignalR events
      connectionUnsubscribe();
      progressUnsubscribe();
      statusUnsubscribe();
      subBatchCompletedUnsubscribe();
      entityProgressUnsubscribe();
      healthUnsubscribe();
      errorUnsubscribe();
      
      // Leave all joined migration groups
      if (signalRService.isConnected()) {
        const activeMigrationsArray = Array.from(state.activeMigrations.keys());
        console.log(`🔗 Leaving ${activeMigrationsArray.length} migration groups on cleanup...`);
        
        // Leave all groups in parallel
        Promise.allSettled(
          activeMigrationsArray.map(migrationId => 
            signalRService.leaveMigrationGroup(migrationId)
          )
        ).then(results => {
          const successful = results.filter(r => r.status === 'fulfilled').length;
          const failed = results.filter(r => r.status === 'rejected').length;
          console.log(`✅ Cleanup completed: ${successful} groups left successfully, ${failed} failed`);
        }).catch(error => {
          console.error('❌ Error during group cleanup:', error);
        });
      }
    };
  }, [autoConnect]);

  /**
   * Automatic fallback to polling when SignalR fails
   * 
   * This effect monitors SignalR connection status and automatically switches
   * between real-time updates and polling fallback to ensure continuous
   * migration progress updates.
   * 
   * Fallback Logic:
   * - When SignalR disconnects and API is available → Start polling
   * - When SignalR reconnects and polling is active → Stop polling
   * - Ensures users always have access to migration updates
   * 
   * This implements Task 7: Fallback to REST API polling if SignalR fails
   */
  useEffect(() => {
    const { signalRConnection, isPollingEnabled } = state;
    
    // If SignalR is disconnected and we're not already polling, start polling
    if (!signalRConnection.isConnected && !isPollingEnabled && state.apiConnected) {
      console.log('🔄 SignalR disconnected - starting polling fallback');
      startPolling();
    }
    
    // If SignalR reconnects and we're polling, stop polling
    if (signalRConnection.isConnected && isPollingEnabled) {
      console.log('✅ SignalR reconnected - stopping polling fallback');
      stopPolling();
    }
  }, [state.signalRConnection.isConnected, state.isPollingEnabled, state.apiConnected]);

  /**
   * Polling effect
   * 
   * Manages the polling timer that fetches migration updates at regular intervals
   * when SignalR is not available. This ensures users continue to receive
   * migration progress updates even during connection issues.
   * 
   * Polling Implementation:
   * - Uses setInterval for regular API calls
   * - Calls refreshData() to fetch latest migration status
   * - Updates lastPollingUpdate timestamp for UI feedback
   * - Handles polling errors gracefully
   * - Cleans up timer on unmount or when polling is disabled
   * 
   * Error Handling:
   * - Polling failures don't break the system
   * - Errors are logged and user is notified
   * - Automatic retry on next polling interval
   */
  useEffect(() => {
    if (!state.isPollingEnabled || !state.apiConnected) {
      return;
    }

    console.log('🔄 Setting up polling interval for migration updates');
    
    const pollingTimer = setInterval(async () => {
      try {
        console.log('🔄 Polling for migration updates...');
        await refreshData();
        dispatch({ type: 'UPDATE_LAST_POLLING', payload: new Date() });
      } catch (error) {
        console.warn('⚠️ Polling failed:', error);
        addError({
          code: 'POLLING_FAILED',
          message: 'Failed to fetch migration updates via polling',
          details: error instanceof Error ? error.message : 'Unknown error',
          timestamp: new Date()
        });
      }
    }, state.pollingInterval);

    return () => {
      console.log('🧹 Cleaning up polling timer');
      clearInterval(pollingTimer);
    };
  }, [state.isPollingEnabled, state.pollingInterval, state.apiConnected]);

  // Context value
  const contextValue: DashboardContextType = {
    state,
    dispatch,
    connectServices,
    disconnectServices,
    refreshData,
    joinMigrationGroup,
    leaveMigrationGroup,
    removeMigration,
    addCancelledMigration,
    removeCancelledMigration,
    clearErrors,
    addError,
    removeError,
    startPolling,
    stopPolling,
    setPollingInterval
  };

  return (
    <DashboardContext.Provider value={contextValue}>
      {children}
    </DashboardContext.Provider>
  );
};

// Custom hook to use dashboard context
export const useDashboard = (): DashboardContextType => {
  const context = useContext(DashboardContext);
  if (context === undefined) {
    throw new Error('useDashboard must be used within a DashboardProvider');
  }
  return context;
}; 