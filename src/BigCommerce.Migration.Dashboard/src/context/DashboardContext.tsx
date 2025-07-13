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

// Dashboard State Interface
interface DashboardState {
  // Connection status
  signalRConnection: SignalRConnection;
  apiConnected: boolean;
  
  // Data
  activeMigrations: Map<string, MigrationProgress>;
  systemHealth: SystemHealthData | null;
  
  // UI State
  isLoading: boolean;
  errors: DashboardError[];
  
  // Settings
  autoRefreshEnabled: boolean;
  refreshInterval: number;
}

// Dashboard Actions
type DashboardAction =
  | { type: 'SET_LOADING'; payload: boolean }
  | { type: 'SET_SIGNALR_CONNECTION'; payload: SignalRConnection }
  | { type: 'SET_API_CONNECTED'; payload: boolean }
  | { type: 'UPDATE_MIGRATION_PROGRESS'; payload: MigrationProgress }
  | { type: 'REMOVE_MIGRATION'; payload: string }
  | { type: 'UPDATE_SYSTEM_HEALTH'; payload: SystemHealthData }
  | { type: 'ADD_ERROR'; payload: DashboardError }
  | { type: 'REMOVE_ERROR'; payload: string }
  | { type: 'CLEAR_ERRORS' }
  | { type: 'SET_AUTO_REFRESH'; payload: boolean }
  | { type: 'SET_REFRESH_INTERVAL'; payload: number }
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
  systemHealth: null,
  isLoading: false,
  errors: [],
  autoRefreshEnabled: true,
  refreshInterval: 30000 // 30 seconds
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
      const newMigrations = new Map(state.activeMigrations);
      newMigrations.set(action.payload.migrationId, action.payload);
      return { ...state, activeMigrations: newMigrations };
      
    case 'REMOVE_MIGRATION':
      const filteredMigrations = new Map(state.activeMigrations);
      filteredMigrations.delete(action.payload);
      return { ...state, activeMigrations: filteredMigrations };
      
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
  clearErrors: () => void;
  addError: (error: DashboardError) => void;
  removeError: (errorCode: string) => void;
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

  const signalRService = getSignalRService(config.signalRUrl);
  const apiService = getApiService(config.apiBaseUrl ? { baseURL: config.apiBaseUrl } : undefined);

  // Connect to services
  const connectServices = async (): Promise<void> => {
    try {
      dispatch({ type: 'SET_LOADING', payload: true });
      
      // Test API connection
      const apiConnected = await apiService.testConnection();
      dispatch({ type: 'SET_API_CONNECTED', payload: apiConnected });
      
      // Connect SignalR
      if (!signalRService.isConnected()) {
        await signalRService.connect();
      }
      
      // Initial data load
      await refreshData();
      
    } catch (error) {
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

  // Refresh all data
  const refreshData = async (): Promise<void> => {
    try {
      // Fetch system health
      const healthData = await apiService.getSystemHealth();
      dispatch({ type: 'UPDATE_SYSTEM_HEALTH', payload: healthData });
      
      // Fetch active migrations
      const migrationsResponse = await apiService.getActiveMigrations();
      migrationsResponse.data.forEach(migration => {
        dispatch({ type: 'UPDATE_MIGRATION_PROGRESS', payload: migration });
      });
      
    } catch (error) {
      addError({
        code: 'DATA_REFRESH_FAILED',
        message: 'Failed to refresh dashboard data',
        details: error instanceof Error ? error.message : 'Unknown error',
        timestamp: new Date()
      });
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
      dispatch({ type: 'UPDATE_MIGRATION_PROGRESS', payload: progress });
    });

    // Migration status updates (completed, failed, cancelled, etc.)
    const statusUnsubscribe = signalRService.on('MigrationStatus', (statusData) => {
      // Handle different types of migration status updates
      if (statusData.Status === 'completed') {
        // Calculate duration if possible (fallback to "just now" if no duration data)
        const duration = statusData.Data?.duration || 'just now';
        const migrationName = statusData.Data?.name || `Migration ${statusData.MigrationId}`;
        
        notificationService.migrationCompleted(
          statusData.MigrationId,
          migrationName,
          duration
        );
        
        // Update migration progress to show completion
        const completedProgress: MigrationProgress = {
          migrationId: statusData.MigrationId,
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
      } else if (statusData.Status === 'failed') {
        const migrationName = statusData.Data?.name || `Migration ${statusData.MigrationId}`;
        const errorMessage = statusData.Error?.message || statusData.Data?.error || 'Unknown error occurred';
        
        notificationService.migrationFailed(
          statusData.MigrationId,
          migrationName,
          errorMessage
        );
      } else if (statusData.Status === 'cancelled') {
        const migrationName = statusData.Data?.name || `Migration ${statusData.MigrationId}`;
        
        notificationService.migrationCancelled(
          statusData.MigrationId,
          migrationName
        );
      } else if (statusData.Status === 'started') {
        const migrationName = statusData.Data?.name || `Migration ${statusData.MigrationId}`;
        
        notificationService.migrationStarted(
          statusData.MigrationId,
          migrationName
        );
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
      connectionUnsubscribe();
      progressUnsubscribe();
      statusUnsubscribe();
      healthUnsubscribe();
      errorUnsubscribe();
    };
  }, [autoConnect]);

  // Context value
  const contextValue: DashboardContextType = {
    state,
    dispatch,
    connectServices,
    disconnectServices,
    refreshData,
    joinMigrationGroup,
    leaveMigrationGroup,
    clearErrors,
    addError,
    removeError
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