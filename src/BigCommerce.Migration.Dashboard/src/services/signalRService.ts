import * as signalR from '@microsoft/signalr';
import config from '../config/environment';
import type { 
  SignalRConnection, 
  SignalRMessage, 
  MigrationProgress,
  SystemHealthData 
} from '../types';

type ConnectionState = 'Disconnected' | 'Connecting' | 'Connected' | 'Disconnecting';

export class SignalRService {
  private connection: signalR.HubConnection | null = null;
  private connectionState: ConnectionState = 'Disconnected';
  private reconnectAttempts = 0;
  private maxReconnectAttempts: number;
  private reconnectInterval = 5000; // 5 seconds
  private listeners: Map<string, Set<(data: any) => void>> = new Map();
  private hubUrl: string;
  private currentMigrationId: string | null = null;

  constructor(hubUrl?: string) {
    // Use environment configuration with optional override
    // Connect to Azure Functions negotiate endpoint which handles Azure SignalR service
    this.hubUrl = hubUrl || config.signalR.hubUrl;
    this.maxReconnectAttempts = config.signalR.reconnectAttempts;
    
    if (config.features.enableDebugLogging) {
      console.log('🔌 SignalR Service initialized:', {
        hubUrl: this.hubUrl,
        maxReconnectAttempts: this.maxReconnectAttempts,
        hasApiKey: !!config.auth.apiKey
      });
    }
    
    // Connection will be initialized when connect() is called
  }

  /**
   * 🛡️ Safe timestamp creation with validation
   */
  private createSafeTimestamp(timestampValue?: any): Date {
    try {
      const value = timestampValue || Date.now();
      const date = new Date(value);
      // Validate the date is not invalid
      if (isNaN(date.getTime())) {
        return new Date();
      }
      return date;
    } catch {
      return new Date();
    }
  }

  /**
   * Initialize SignalR connection with backend integration
   */
  private async initializeConnection(): Promise<void> {
    console.log('🔌 Initializing SignalR connection...');
    
    const maxAttempts = 3;
    let attempt = 1;
    
    while (attempt <= maxAttempts) {
      try {
        // Step 1: Get SignalR connection info from our custom negotiate endpoint
        // Construct full URL for cross-origin requests
        const negotiateUrl = this.hubUrl.startsWith('http') 
          ? this.hubUrl 
          : `http://localhost:7071${this.hubUrl}`;
        
        console.log(`🔗 Negotiating SignalR connection at: ${negotiateUrl} (attempt ${attempt}/${maxAttempts})`);
        
        const negotiateResponse = await fetch(negotiateUrl, {
          method: 'POST',
          headers: {
            'Content-Type': 'application/json'
            // Remove x-functions-key since the endpoint is AuthorizationLevel.Anonymous
          },
          body: JSON.stringify({})
        });

        if (!negotiateResponse.ok) {
          throw new Error(`Failed to negotiate SignalR connection: ${negotiateResponse.status} ${negotiateResponse.statusText}`);
        }

        const connectionInfo = await negotiateResponse.json();
        console.log('✅ Got SignalR connection info:', connectionInfo);

        // Step 2: Create SignalR connection using the provided URL and access token
        this.connection = new signalR.HubConnectionBuilder()
          .withUrl(connectionInfo.Url, {
            accessTokenFactory: () => connectionInfo.AccessToken,
            skipNegotiation: true, // We already negotiated manually
            transport: signalR.HttpTransportType.WebSockets, // Must use only WebSockets when skipping negotiation
            withCredentials: false
          })
          .configureLogging(signalR.LogLevel.Debug)
          .build();

        this.setupEventHandlers();
        return; // Success - exit the retry loop
        
      } catch (error) {
        console.error(`❌ SignalR initialization attempt ${attempt}/${maxAttempts} failed:`, error);
        
        if (attempt === maxAttempts) {
          console.error('❌ All SignalR initialization attempts failed');
          throw error;
        }
        
        // Wait before retrying (exponential backoff)
        const delay = 1000 * Math.pow(2, attempt - 1); // 1s, 2s, 4s
        console.log(`⏳ Retrying SignalR initialization in ${delay}ms...`);
        await new Promise(resolve => setTimeout(resolve, delay));
        attempt++;
      }
    }
  }



  /**
   * 🎯 CENTRALIZED SIGNALR: Add UI-specific properties to migration progress events
   * Backend now sends camelCase via SignalRMessageConverter - no transformation needed
   */
  private enrichMigrationProgressEvent(eventData: any): any {
    // Calculate enhanced properties for UI
    const totalEntities = eventData.totalEntities || 0;
    const processedEntities = eventData.processedEntities || 0;
    const failedEntities = eventData.failedEntities || 0;
    const successfulEntities = processedEntities - failedEntities;
    const overallProgress = eventData.overallProgress || 0;
    
    const elapsedTime = eventData.elapsedTime || 0;
    const entitiesPerSecond = elapsedTime > 0 ? processedEntities / (elapsedTime / 1000) : 0;

    return {
      ...eventData, // Use data directly from backend (already in camelCase)
      // Add calculated/enhanced properties for UI
      successfulEntities,
      // ✅ Backend now sends correct progress percentage in sync with entity counts
      overallProgressPercentage: overallProgress,
      entitiesPerSecond,
      errorRate: processedEntities > 0 ? (failedEntities / processedEntities) * 100 : 0,
      lastUpdated: new Date(),
      
      // Enhanced nested structures for detailed dashboard
      currentProcessing: {
        currentEntity: eventData.currentEntityType || 'entities',
        currentActivity: 'Processing entities',
        currentBatchNumber: Math.ceil(processedEntities / 50) || 1,
        currentBatch: {
          batchProgressPercentage: (overallProgress % 10) * 10,
          batchSize: 50,
          processedInBatch: (processedEntities % 50),
          batchProcessingSpeed: entitiesPerSecond
        }
      },
      
      batchProgress: {
        totalBatches: Math.ceil(totalEntities / 50) || 1,
        completedBatches: Math.floor(processedEntities / 50) || 0
      },
      
      remainingWork: {
        remainingEntities: totalEntities - processedEntities,
        remainingBatches: Math.ceil((totalEntities - processedEntities) / 50) || 0,
        // Safe numeric parsing for estimatedTimeRemaining
        estimatedTimeRemaining: (() => {
          const timeValue = eventData.estimatedTimeRemaining;
          if (typeof timeValue === 'number' && !isNaN(timeValue) && timeValue >= 0) {
            return timeValue;
          }
          // Calculate fallback based on remaining entities and current speed
          const remainingEntities = totalEntities - processedEntities;
          return entitiesPerSecond > 0 ? Math.ceil(remainingEntities / entitiesPerSecond) : 0;
        })()
      },
      
      performance: {
        currentProcessingSpeed: entitiesPerSecond,
        averageProcessingSpeed: entitiesPerSecond,
        performanceTrend: entitiesPerSecond > 5 ? 'improving' : entitiesPerSecond > 2 ? 'stable' : 'declining'
      }
    };
  }

  /**
   * 🎯 CENTRALIZED SIGNALR: Add UI-specific properties to migration status events
   * Backend now sends camelCase via SignalRMessageConverter - no transformation needed
   */
  private enrichMigrationStatusEvent(eventData: any): any {
    // Safe timestamp parsing with validation
    let timestamp: Date;
    try {
      const timestampValue = eventData.timestamp || Date.now();
      timestamp = new Date(timestampValue);
      // Validate the date is not invalid
      if (isNaN(timestamp.getTime())) {
        timestamp = new Date();
      }
    } catch {
      timestamp = new Date();
    }

    return {
      ...eventData, // Use data directly from backend (already in camelCase)
      // Ensure consistent status format
      status: (eventData.status || 'unknown').toLowerCase(),
      timestamp
    };
  }

  // 🚨 GLOBAL COORDINATION CLEANUP: Sub-batch enrichment methods removed
  // These methods are no longer needed since we use coordinated MigrationProgress events
  // from the global ParallelProgressAggregator instead of individual sub-batch events

  /**
   * Set up connection event handlers
   * 
   * ✅ EVENT MAPPING: Backend Queue Events → Frontend Internal Events
   * Backend sends:           Frontend translates to:
   * - MigrationProgressUpdated  → 'migrationProgress'
   * - MigrationStatusChanged    → 'MigrationStatus'  
   * - EntityProgressUpdated     → 'DetailedProgress' + 'entityUpdate'
   * - BatchProgressUpdated      → 'BatchStarted' + 'BatchProgress' + 'BatchCompleted'
   * - ErrorOccurred             → 'error'
   * 
   * This translation layer allows frontend hooks to use consistent internal 
   * event names regardless of backend changes.
   */
  private setupEventHandlers(): void {
    if (!this.connection) return;

    // Connection state events
    this.connection.onclose((error) => {
      this.connectionState = 'Disconnected';
      console.error('SignalR connection closed:', error);
      this.notifyListeners('connectionStateChanged', { 
        state: this.connectionState, 
        error 
      });
    });

    this.connection.onreconnecting((error) => {
      this.connectionState = 'Connecting';
      console.warn('SignalR reconnecting:', error);
      this.notifyListeners('connectionStateChanged', { 
        state: this.connectionState, 
        error 
      });
    });

    this.connection.onreconnected((connectionId) => {
      this.connectionState = 'Connected';
      this.reconnectAttempts = 0;
      console.info('SignalR reconnected:', connectionId);
      this.notifyListeners('connectionStateChanged', { 
        state: this.connectionState, 
        connectionId 
      });
    });

    // 🎯 PHASE 3: Simplified SignalR Events (Primary) - Fixed hub method names
    this.connection.on('MigrationStarted', (eventData: any) => {
      console.log('🚀 SignalR: Migration Started', eventData);
      this.notifyListeners('migration-started', eventData);
    });

    this.connection.on('EntityStarted', (eventData: any) => {
      console.log('🎯 SignalR: Entity Started', eventData);
      this.notifyListeners('entity-started', eventData);
    });

    this.connection.on('EntityChunkProgress', (eventData: any) => {
      console.log('📊 [DEBUG] SignalR: Entity Chunk Progress received', eventData);
      console.log('📊 [DEBUG] EventData structure:', {
        entityType: eventData?.entityType,
        cumulativeProcessed: eventData?.cumulativeProcessed,
        totalEntitiesForType: eventData?.totalEntitiesForType,
        allKeys: eventData ? Object.keys(eventData) : []
      });
      this.notifyListeners('chunk-progress', eventData);
      // Also forward to legacy listeners for backward compatibility
      this.notifyListeners('entityUpdate', eventData);
      this.notifyListeners('migrationProgress', eventData);
    });

    this.connection.on('EntityCompleted', (eventData: any) => {
      console.log('✅ [DEBUG] SignalR: Entity Completed received', eventData);
      console.log('✅ [DEBUG] EntityCompleted structure:', {
        entityType: eventData?.entityType,
        totalProcessed: eventData?.totalProcessed,
        totalSuccess: eventData?.totalSuccess,
        totalFailed: eventData?.totalFailed,
        allKeys: eventData ? Object.keys(eventData) : []
      });
      this.notifyListeners('entity-completed', eventData);
    });

    this.connection.on('MigrationCompleted', (eventData: any) => {
      console.log('✅ SignalR: Migration Completed', eventData);
      this.notifyListeners('migration-completed', eventData);
      // Also forward to legacy listeners for backward compatibility
      this.notifyListeners('MigrationStatus', eventData);
    });

    this.connection.on('ErrorProgress', (eventData: any) => {
      console.warn('❌ SignalR: Error Event', eventData);
      this.notifyListeners('error', eventData);
    });

    this.connection.on('migrationCancelled', (eventData: any) => {
      console.warn('🛑 SignalR: Migration Cancelled', eventData);
      this.notifyListeners('migrationCancelled', eventData);
    });

    // 🎯 LEGACY: Core Progress Events (for backward compatibility)
    this.connection.on('MigrationProgressUpdated', (eventData: any) => {
      console.log('🔄 DEBUG: Received MigrationProgressUpdated (legacy)', eventData);
      const enrichedProgress = this.enrichMigrationProgressEvent(eventData);
      console.log('🔄 DEBUG: Enriched MigrationProgress:', enrichedProgress);
      this.notifyListeners('migrationProgress', enrichedProgress);
    });

    this.connection.on('MigrationStatusChanged', (eventData: any) => {
      console.log('🎯 DEBUG: Received MigrationStatusChanged:', eventData);
      const enrichedStatus = this.enrichMigrationStatusEvent(eventData);
      console.log('🎯 DEBUG: Enriched MigrationStatus:', enrichedStatus);
      this.notifyListeners('MigrationStatus', enrichedStatus);
    });

    this.connection.on('EntityProgressUpdated', (eventData: any) => {
      console.log('🎯 DEBUG: Received EntityProgressUpdated:', eventData);
      
      // 🚨 STATUS FIX: Forward completion status to DashboardContext
      if (eventData.status === 'completed') {
        console.log('🎉 STATUS-FIX: Forwarding EntityProgressUpdated completion status to MigrationStatus listener');
        this.notifyListeners('MigrationStatus', {
          migrationId: eventData.migrationId,
          status: 'completed',
          data: eventData
        });
      }
      
      // Entity events can be used directly - already in camelCase from backend
      this.notifyListeners('DetailedProgress', eventData);
      this.notifyListeners('entityUpdate', eventData);
    });

    this.connection.on('BatchProgressUpdated', (eventData: any) => {
      console.log('🎯 DEBUG: Received BatchProgressUpdated:', eventData);
      // Batch events can be used directly - already in camelCase from backend
      this.notifyListeners('BatchStarted', eventData);
      this.notifyListeners('BatchProgress', eventData);
      this.notifyListeners('BatchCompleted', eventData);
    });

    this.connection.on('EntityCompleted', (eventData: any) => {
      console.log('✅ DEBUG: Received EntityCompleted:', eventData);
      
      // Forward entity completion event to the entity-completed listener
      this.notifyListeners('entity-completed', eventData);
    });

    this.connection.on('ErrorOccurred', (errorEvent: any) => {
      console.log('🎯 DEBUG: Received ErrorOccurred:', errorEvent);
      this.notifyListeners('error', { 
        message: errorEvent.Message || errorEvent.message || 'Migration error occurred',
        severity: errorEvent.Severity || 'error',
        entityType: errorEvent.EntityType,
        entityId: errorEvent.EntityId,
        details: errorEvent.Details,
        timestamp: new Date() 
      });
    });

    // 🚨 GLOBAL COORDINATION: Sub-batch events now handled by global aggregator
    // Individual sub-batch events are reported to ParallelProgressAggregator which sends coordinated MigrationProgress events
    // This eliminates the 50→192 jump issue by ensuring proper cross-batch coordination
    
    // 🎯 NOTE: Sub-batch event subscriptions REMOVED - UI now relies on coordinated MigrationProgress events only
    // This prevents duplicate/conflicting events and ensures smooth progress: 5→10→15...→192

    // Legacy event handlers (keeping for backward compatibility during migration)
    // TODO: Remove these after confirming queue-based events work correctly
    this.connection.on('PerformanceMetrics', (metrics: any) => {
      console.log('🎯 DEBUG: Received PerformanceMetrics (legacy):', metrics);
      this.notifyListeners('PerformanceMetrics', metrics);
    });

    this.connection.on('RemainingWorkload', (workload: any) => {
      console.log('🎯 DEBUG: Received RemainingWorkload (legacy):', workload);
      this.notifyListeners('RemainingWorkload', workload);
    });

    // System Health Events (independent of queue system)
    this.connection.on('SystemHealth', (healthData: SystemHealthData) => {
      console.log('🎯 DEBUG: Received SystemHealth:', healthData);
      this.notifyListeners('systemHealth', healthData);
    });

    this.connection.on('systemHealth', (healthData: SystemHealthData) => {
      console.log('🎯 DEBUG: Received systemHealth (lowercase):', healthData);
      this.notifyListeners('systemHealth', healthData);
    });

    // Generic Error Handler (fallback)
    this.connection.on('Error', (error: string) => {
      console.error('SignalR server error (generic):', error);
      this.notifyListeners('error', { message: error, timestamp: new Date() });
    });

    this.connection.on('Connected', (connectionId: string) => {
      console.info('SignalR connected with ID:', connectionId);
      this.notifyListeners('connected', { connectionId });
    });

    this.connection.on('JoinedMigrationGroup', (migrationId: string) => {
      this.notifyListeners('joinedMigrationGroup', { migrationId });
    });

    this.connection.on('LeftMigrationGroup', (migrationId: string) => {
      this.notifyListeners('leftMigrationGroup', { migrationId });
    });
  }

  /**
   * Start the SignalR connection
   */
  public   async connect(): Promise<void> {
    if (!this.connection) {
      await this.initializeConnection();
    }

    if (!this.connection) {
      throw new Error('Failed to initialize SignalR connection');
    }

    if (this.connectionState?.toLowerCase() === 'connected') {
      return;
    }

    if (this.connectionState?.toLowerCase() === 'connecting') {
      return; // Already connecting, don't start another connection attempt
    }

    try {
      this.connectionState = 'Connecting';
      this.notifyListeners('connectionStateChanged', { state: this.connectionState });
      
      console.log('🔄 Starting SignalR connection...');
      await this.connection.start();
      this.connectionState = 'Connected';
      this.reconnectAttempts = 0;
      
      console.log('✅ SignalR connected successfully');
      console.log(`🔗 SignalR connection ID: ${this.connection.connectionId}`);
      
      // Automatically join migration group if we have a current migration ID
      if (this.currentMigrationId) {
        await this.joinMigrationGroup(this.currentMigrationId);
      }
      
      this.notifyListeners('connectionStateChanged', { 
        state: this.connectionState,
        connectionId: this.connection.connectionId 
      });
    } catch (error) {
      this.connectionState = 'Disconnected';
      console.error('❌ SignalR connection failed:', error);
      
      // Log additional error details for CORS debugging
      if (error instanceof Error) {
        console.error('Error name:', error.name);
        console.error('Error message:', error.message);
        console.error('Error stack:', error.stack);
        
        // Check for CORS-specific errors
        if (error.message?.toLowerCase().includes('cors') || error.message?.toLowerCase().includes('cross-origin')) {
          console.error('🚫 CORS ERROR DETECTED - This is a browser security restriction');
          console.error('Hub URL being used:', this.hubUrl);
          console.error('Config API key:', config.auth.apiKey ? 'Present' : 'Missing');
        }
      }
      this.notifyListeners('connectionStateChanged', { 
        state: this.connectionState, 
        error 
      });
      throw error;
    }
  }

  /**
   * Stop the SignalR connection
   */
  public async disconnect(): Promise<void> {
    if (!this.connection) return;

    try {
      this.connectionState = 'Disconnecting';
      this.notifyListeners('connectionStateChanged', { state: this.connectionState });
      
      await this.connection.stop();
      this.connectionState = 'Disconnected';
      
      console.info('SignalR disconnected');
      this.notifyListeners('connectionStateChanged', { state: this.connectionState });
    } catch (error) {
      console.error('SignalR disconnect error:', error);
      throw error;
    }
  }

  /**
   * Join a migration monitoring group
   */
  public async joinMigrationGroup(migrationId: string): Promise<void> {
    this.currentMigrationId = migrationId;
    
    if (!this.connection || this.connection.state !== signalR.HubConnectionState.Connected) {
      console.log('🔗 Monitoring migration group: ' + migrationId + ' (will join after connection)');
      return;
    }

    if (!this.connection.connectionId) {
      console.warn('⚠️ No connection ID available for group join');
      return;
    }

    try {
      console.log(`🔗 Joining migration group: ${migrationId}`);
      
      // Call the backend HTTP endpoint to join the group
      const response = await fetch(`${config.api.baseUrl}/signalr/join/${migrationId}?connectionId=${this.connection.connectionId}`, {
        method: 'POST',
        headers: {
          'Content-Type': 'application/json',
          'x-functions-key': config.auth.apiKey
        }
      });

      if (!response.ok) {
        throw new Error(`Failed to join migration group: ${response.status} ${response.statusText}`);
      }

      const result = await response.json();
      console.log(`✅ Successfully joined migration group for migration: ${result.migrationId}`, result);
      console.log(`✅ Ready to receive updates for migration: ${migrationId}`);
      
      // Listen for the confirmation event
      if (this.connection) {
        this.connection.on('JoinedMigrationGroup', (data) => {
          console.log('🎉 Received group join confirmation:', data);
        });
      }
    } catch (error) {
      console.error(`❌ Failed to join migration group ${migrationId}:`, error);
      // Don't throw - this is not critical enough to break the whole connection
    }
  }

  /**
   * Leave a migration monitoring group
   */
  public async leaveMigrationGroup(migrationId: string): Promise<void> {
    if (!this.connection || this.connection.state !== signalR.HubConnectionState.Connected) {
      console.log('🔗 Not connected - cannot leave migration group: ' + migrationId);
      return;
    }

    if (!this.connection.connectionId) {
      console.warn('⚠️ No connection ID available for group leave');
      return;
    }

    try {
      console.log(`🔗 Leaving migration group: ${migrationId}`);
      
      // Call the backend HTTP endpoint to leave the group
      const response = await fetch(`${config.api.baseUrl}/signalr/leave/${migrationId}?connectionId=${this.connection.connectionId}`, {
        method: 'POST',
        headers: {
          'Content-Type': 'application/json',
          'x-functions-key': config.auth.apiKey
        }
      });

      if (!response.ok) {
        throw new Error(`Failed to leave migration group: ${response.status} ${response.statusText}`);
      }

      const result = await response.json();
      console.log(`✅ Successfully left migration group: ${result.groupName}`, result);
      
      // Clear current migration if it matches
      if (this.currentMigrationId === migrationId) {
        this.currentMigrationId = null;
      }
    } catch (error) {
      console.error(`❌ Failed to leave migration group ${migrationId}:`, error);
      // Don't throw - this is not critical enough to break the whole connection
    }
  }

  /**
   * Subscribe to specific message types
   */
  public on(messageType: string, callback: (data: any) => void): () => void {
    if (!this.listeners.has(messageType)) {
      this.listeners.set(messageType, new Set());
    }
    
    this.listeners.get(messageType)!.add(callback);
    
    // Return unsubscribe function
    return () => {
      const typeListeners = this.listeners.get(messageType);
      if (typeListeners) {
        typeListeners.delete(callback);
        if (typeListeners.size === 0) {
          this.listeners.delete(messageType);
        }
      }
    };
  }

  /**
   * Remove all listeners for a message type
   */
  public off(messageType: string): void {
    this.listeners.delete(messageType);
  }

  /**
   * Notify all listeners for a message type
   */
  private notifyListeners(messageType: string, data: any): void {
    const typeListeners = this.listeners.get(messageType);
    if (typeListeners) {
      typeListeners.forEach(callback => {
        try {
          callback(data);
        } catch (error) {
          console.error(`Error in SignalR listener for ${messageType}:`, error);
        }
      });
    }
  }

  /**
   * Get current connection state
   */
  public getConnectionState(): SignalRConnection {
    return {
      connectionId: this.connection?.connectionId || '',
      isConnected: this.connectionState?.toLowerCase() === 'connected',
      lastConnected: this.connectionState?.toLowerCase() === 'connected' ? new Date() : undefined,
      connectionState: this.connectionState
    };
  }

  /**
   * Check if connection is active
   */
  public isConnected(): boolean {
    return this.connectionState?.toLowerCase() === 'connected';
  }

  /**
   * Get connection statistics
   */
  public getConnectionStats() {
    return {
      state: this.connectionState,
      reconnectAttempts: this.reconnectAttempts,
      connectionId: this.connection?.connectionId,
      listenerCount: Array.from(this.listeners.values()).reduce((total, set) => total + set.size, 0)
    };
  }
}

// Singleton instance
let signalRServiceInstance: SignalRService | null = null;

// Reset singleton for debugging (call this to force recreation)
(window as any).resetSignalRService = () => {
  console.log('🔄 Resetting SignalR service singleton');
  signalRServiceInstance = null;
};

/**
 * Get SignalR service singleton instance
 */
export const getSignalRService = (hubUrl?: string): SignalRService => {
  if (!signalRServiceInstance) {
    // Use config value if no hubUrl provided
    const configuredUrl = `${config.api.baseUrl}${config.signalR.hubUrl}`;
    const finalUrl = hubUrl || configuredUrl;
    
    console.log('🔧 SignalR Factory Debug:', {
      passedHubUrl: hubUrl,
      apiBaseUrl: config.api.baseUrl,
      signalRHubUrl: config.signalR.hubUrl,
      configuredUrl: configuredUrl,
      finalUrl: finalUrl
    });
    
    signalRServiceInstance = new SignalRService(finalUrl);
  }
  return signalRServiceInstance;
};

/**
 * Initialize SignalR service
 */
export const initializeSignalR = async (hubUrl?: string): Promise<SignalRService> => {
  const service = getSignalRService(hubUrl);
  await service.connect();
  return service;
}; 