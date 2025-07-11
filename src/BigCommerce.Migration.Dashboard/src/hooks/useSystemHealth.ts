import { useState, useEffect, useCallback } from 'react';
import type { SystemHealthData } from '../types';
import { getSignalRService } from '../services/signalRService';

interface UseSystemHealthOptions {
  autoRefresh?: boolean;
  refreshInterval?: number; // in milliseconds
}

interface UseSystemHealthReturn {
  healthData: SystemHealthData | null;
  isLoading: boolean;
  error: string | null;
  isConnected: boolean;
  refresh: () => Promise<void>;
  startAutoRefresh: () => void;
  stopAutoRefresh: () => void;
}

export const useSystemHealth = (
  options: UseSystemHealthOptions = {}
): UseSystemHealthReturn => {
  const { autoRefresh = true, refreshInterval = 30000 } = options; // 30 seconds default
  const [healthData, setHealthData] = useState<SystemHealthData | null>(null);
  const [isLoading, setIsLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [isConnected, setIsConnected] = useState(false);
  const [refreshTimer, setRefreshTimer] = useState<NodeJS.Timeout | null>(null);
  
  const signalRService = getSignalRService();

  // Handle system health updates from SignalR
  const handleHealthUpdate = useCallback((health: SystemHealthData) => {
    setHealthData(health);
    setIsLoading(false);
    setError(null);
  }, []);

  // Handle connection state changes
  const handleConnectionStateChange = useCallback((connectionData: any) => {
    setIsConnected(connectionData.state === 'Connected');
    if (connectionData.error) {
      setError(connectionData.error.message || 'Connection error');
    }
  }, []);

  // Fetch system health data from API
  const refresh = useCallback(async () => {
    setIsLoading(true);
    setError(null);
    
    try {
      const response = await fetch('/api/dashboard/health');
      if (!response.ok) {
        throw new Error(`HTTP ${response.status}: ${response.statusText}`);
      }
      
      const data = await response.json();
      setHealthData(data);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to fetch system health');
      setHealthData(null);
    } finally {
      setIsLoading(false);
    }
  }, []);

  // Start auto-refresh timer
  const startAutoRefresh = useCallback(() => {
    if (refreshTimer) {
      clearInterval(refreshTimer);
    }
    
    const timer = setInterval(() => {
      refresh();
    }, refreshInterval);
    
    setRefreshTimer(timer);
  }, [refresh, refreshInterval, refreshTimer]);

  // Stop auto-refresh timer
  const stopAutoRefresh = useCallback(() => {
    if (refreshTimer) {
      clearInterval(refreshTimer);
      setRefreshTimer(null);
    }
  }, [refreshTimer]);

  // Set up SignalR listeners and initial data fetch
  useEffect(() => {
    // Subscribe to SignalR events
    const healthUnsubscribe = signalRService.on('systemHealth', handleHealthUpdate);
    const connectionUnsubscribe = signalRService.on('connectionStateChanged', handleConnectionStateChange);

    // Initial connection state
    const connectionState = signalRService.getConnectionState();
    setIsConnected(connectionState.isConnected);

    // Initial data fetch
    refresh();

    // Set up auto-refresh if enabled
    if (autoRefresh) {
      startAutoRefresh();
    }

    // Cleanup function
    return () => {
      healthUnsubscribe();
      connectionUnsubscribe();
      stopAutoRefresh();
    };
  }, [
    signalRService,
    handleHealthUpdate,
    handleConnectionStateChange,
    refresh,
    autoRefresh,
    startAutoRefresh,
    stopAutoRefresh
  ]);

  return {
    healthData,
    isLoading,
    error,
    isConnected,
    refresh,
    startAutoRefresh,
    stopAutoRefresh
  };
}; 