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

  // Handle migration progress updates from SignalR
  const handleProgressUpdate = useCallback((progressData: MigrationProgress) => {
    if (progressData.migrationId === migrationId) {
      setProgress(progressData);
      setIsLoading(false);
    }
  }, [migrationId]);

  // Handle migration status updates from SignalR
  const handleStatusUpdate = useCallback((statusData: any) => {
    if (statusData.migrationId === migrationId) {
      // Update progress based on status data
      setProgress(prev => prev ? { ...prev, ...statusData } : null);
    }
  }, [migrationId]);

  // Handle entity progress updates from SignalR (when entities complete)
  const handleEntityProgressUpdate = useCallback((entityData: any) => {
    if (entityData.migrationId === migrationId || entityData.MigrationId === migrationId) {
      console.log('🔄 useMigrationProgress: Received EntityProgressUpdated:', entityData);
      
      // If entity status is completed, update the overall migration status
      if (entityData.Status === 'completed' || entityData.status === 'completed') {
        console.log('✅ useMigrationProgress: Entity completed, updating migration status to completed');
        setProgress(prev => prev ? { 
          ...prev, 
          status: 'completed',
          processedEntities: entityData.ProcessedCount || entityData.processedCount || prev.processedEntities,
          totalEntities: entityData.TotalCount || entityData.totalCount || prev.totalEntities
        } : null);
      } else {
        // Update entity counts for other statuses
        setProgress(prev => prev ? { 
          ...prev,
          processedEntities: entityData.ProcessedCount || entityData.processedCount || prev.processedEntities,
          totalEntities: entityData.TotalCount || entityData.totalCount || prev.totalEntities
        } : null);
      }
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
    // Subscribe to SignalR events
    const progressUnsubscribe = signalRService.on('MigrationProgressUpdated', handleProgressUpdate);
    const statusUnsubscribe = signalRService.on('MigrationStatusChanged', handleStatusUpdate);
    const entityProgressUnsubscribe = signalRService.on('EntityProgressUpdated', handleEntityProgressUpdate);
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
      progressUnsubscribe();
      statusUnsubscribe();
      entityProgressUnsubscribe();
      connectionUnsubscribe();
      
      // Leave migration group on cleanup
      if (signalRService.isConnected()) {
        leaveMigrationGroup().catch(console.error);
      }
    };
  }, [
    signalRService,
    autoConnect,
    handleProgressUpdate,
    handleStatusUpdate,
    handleEntityProgressUpdate,
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