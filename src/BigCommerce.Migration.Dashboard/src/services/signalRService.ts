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
   * Transform backend data (PascalCase) to frontend data (camelCase)
   */
  private transformBackendProgress(backendData: any): any {
    if (!backendData) return null;

    const totalEntities = backendData.TotalEntities || backendData.totalEntities || 0;
    const processedEntities = backendData.ProcessedEntities || backendData.processedEntities || 0;
    const successfulEntities = backendData.SuccessfulEntities || backendData.successfulEntities || processedEntities; // Default to processed if not specified
    const failedEntities = backendData.FailedEntities || backendData.failedEntities || 0;
    const overallProgress = backendData.OverallProgress || backendData.overallProgressPercentage || 0;
    const entitiesPerSecond = backendData.EntitiesPerSecond || backendData.entitiesPerSecond || 0;
    
    // Calculate elapsed time (simple calculation based on progress)
    const estimatedTotalTime = entitiesPerSecond > 0 ? (totalEntities / entitiesPerSecond) : 0;
    const elapsedTime = estimatedTotalTime > 0 ? (estimatedTotalTime * (overallProgress / 100)) : 0;
    const estimatedTimeRemaining = estimatedTotalTime > elapsedTime ? (estimatedTotalTime - elapsedTime) : 0;

    return {
      migrationId: backendData.MigrationId || backendData.migrationId || '',
      status: (backendData.Status || backendData.status || 'unknown').toLowerCase(),
      startTime: backendData.StartTime || backendData.startTime || new Date(),
      lastUpdated: new Date(),
      elapsedTime: Math.round(elapsedTime),
      estimatedTimeRemaining: Math.round(estimatedTimeRemaining),
      totalEntities,
      processedEntities,
      successfulEntities,
      failedEntities,
      overallProgressPercentage: overallProgress,
      entityProgress: backendData.EntityProgress || backendData.entityProgress || {},
      currentPhase: backendData.CurrentPhase || backendData.currentPhase || 'Processing',
      currentEntity: backendData.CurrentEntity || backendData.currentEntity || backendData.CurrentEntityType || 'entities',
      entitiesPerSecond,
      errorRate: processedEntities > 0 ? (failedEntities / processedEntities) * 100 : 0,
      
      // Enhanced nested structures for detailed dashboard
      currentProcessing: {
        currentEntity: backendData.CurrentEntity || backendData.currentEntity || backendData.CurrentEntityType || 'entities',
        currentActivity: backendData.CurrentActivity || backendData.currentActivity || 'Processing entities',
        currentBatchNumber: backendData.CurrentBatchNumber || backendData.currentBatchNumber || Math.ceil(processedEntities / 50) || 1,
        currentBatch: {
          batchProgressPercentage: backendData.BatchProgress || backendData.batchProgress || (overallProgress % 10) * 10,
          batchSize: backendData.BatchSize || backendData.batchSize || 50,
          processedInBatch: backendData.ProcessedInBatch || backendData.processedInBatch || (processedEntities % 50),
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
        estimatedTimeRemaining: Math.round(estimatedTimeRemaining)
      },
      
      performance: {
        currentProcessingSpeed: entitiesPerSecond,
        averageProcessingSpeed: entitiesPerSecond,
        performanceTrend: entitiesPerSecond > 5 ? 'improving' : entitiesPerSecond > 2 ? 'stable' : 'declining'
      }
    };
  }

  /**
   * Transform SubBatchStarted event from backend format to frontend format
   */
  private transformSubBatchStartedEvent(backendEvent: any): any {
    return {
      migrationId: backendEvent.MigrationId || backendEvent.migrationId,
      parentBatchNumber: backendEvent.ParentBatchNumber || backendEvent.parentBatchNumber,
      subBatchNumber: backendEvent.SubBatchNumber || backendEvent.subBatchNumber,
      totalSubBatches: backendEvent.TotalSubBatches || backendEvent.totalSubBatches,
      entityType: backendEvent.EntityType || backendEvent.entityType,
      entitiesInBatch: backendEvent.EntitiesInBatch || backendEvent.entitiesInBatch,
      timestamp: new Date(backendEvent.Timestamp || backendEvent.timestamp || Date.now()),
      // UI event list format
      type: 'SubBatchStarted',
      message: `Sub-batch ${backendEvent.SubBatchNumber || 1}/${backendEvent.TotalSubBatches || 1} started for ${backendEvent.EntityType || 'entities'}`,
      id: `subbatch-started-${backendEvent.ParentBatchNumber || 1}-${backendEvent.SubBatchNumber || 1}-${Date.now()}`
    };
  }

  /**
   * Transform SubBatchCompleted event from backend format to frontend format
   */
  private transformSubBatchCompletedEvent(backendEvent: any): any {
    const successRate = backendEvent.TotalEntities > 0 
      ? ((backendEvent.SuccessfulEntities || 0) / backendEvent.TotalEntities * 100).toFixed(1)
      : '0.0';
    
    // 🔍 DEBUG: Log the backend event data to see what we're receiving
    console.log('🎯 DEBUG: SubBatchCompleted backend event data:', {
      CumulativeSuccessfulEntities: backendEvent.CumulativeSuccessfulEntities,
      CumulativeFailedEntities: backendEvent.CumulativeFailedEntities,
      TotalMigrationEntities: backendEvent.TotalMigrationEntities,
      ProgressPercentage: backendEvent.ProgressPercentage
    });
    
    return {
      migrationId: backendEvent.MigrationId || backendEvent.migrationId,
      parentBatchNumber: backendEvent.ParentBatchNumber || backendEvent.parentBatchNumber,
      subBatchNumber: backendEvent.SubBatchNumber || backendEvent.subBatchNumber,
      totalSubBatches: backendEvent.TotalSubBatches || backendEvent.totalSubBatches,
      successfulEntities: backendEvent.SuccessfulEntities || 0,
      failedEntities: backendEvent.FailedEntities || 0,
      totalEntities: backendEvent.TotalEntities || 0,
      entityType: backendEvent.EntityType || backendEvent.entityType,
      processingTime: backendEvent.ProcessingTime || backendEvent.processingTime,
      completedAt: new Date(backendEvent.CompletedAt || backendEvent.completedAt || Date.now()),
      errors: backendEvent.Errors || backendEvent.errors || [],
      cumulativeSuccessfulEntities: backendEvent.CumulativeSuccessfulEntities || 0,
      cumulativeFailedEntities: backendEvent.CumulativeFailedEntities || 0,
      totalMigrationEntities: backendEvent.TotalMigrationEntities || 0,
      progressPercentage: backendEvent.ProgressPercentage || 0,
      estimatedTimeRemaining: backendEvent.EstimatedTimeRemaining || backendEvent.estimatedTimeRemaining || '00:00:00',
      timestamp: new Date(backendEvent.Timestamp || backendEvent.timestamp || Date.now()),
      // UI event list format
      type: 'SubBatchCompleted',
      message: `Sub-batch ${backendEvent.SubBatchNumber || 1}/${backendEvent.TotalSubBatches || 1} completed: ${backendEvent.SuccessfulEntities || 0}/${backendEvent.TotalEntities || 0} ${backendEvent.EntityType || 'entities'} (${successRate}% success)`,
      id: `subbatch-completed-${backendEvent.ParentBatchNumber || 1}-${backendEvent.SubBatchNumber || 1}-${Date.now()}`
    };
  }

  /**
   * Transform SubBatchMigrationProgressEvent from backend format to frontend format
   */
  private transformSubBatchProgressEvent(backendEvent: any): any {
    return {
      migrationId: backendEvent.MigrationId || backendEvent.migrationId,
      totalPages: backendEvent.TotalPages || backendEvent.totalPages || 0,
      completedPages: backendEvent.CompletedPages || backendEvent.completedPages || 0,
      totalSubBatches: backendEvent.TotalSubBatches || backendEvent.totalSubBatches || 0,
      completedSubBatches: backendEvent.CompletedSubBatches || backendEvent.completedSubBatches || 0,
      totalSuccessfulEntities: backendEvent.TotalSuccessfulEntities || backendEvent.totalSuccessfulEntities || 0,
      totalFailedEntities: backendEvent.TotalFailedEntities || backendEvent.totalFailedEntities || 0,
      totalExpectedEntities: backendEvent.TotalExpectedEntities || backendEvent.totalExpectedEntities || 0,
      processingRate: backendEvent.ProcessingRate || backendEvent.processingRate || 0,
      overallProgressPercentage: backendEvent.OverallProgressPercentage || backendEvent.overallProgressPercentage || 0,
      estimatedTimeRemaining: backendEvent.EstimatedTimeRemaining || backendEvent.estimatedTimeRemaining,
      updatedAt: new Date(backendEvent.UpdatedAt || backendEvent.updatedAt || Date.now()),
      elapsedTime: backendEvent.ElapsedTime || backendEvent.elapsedTime,
      recentErrors: backendEvent.RecentErrors || backendEvent.recentErrors || [],
      performanceMetrics: backendEvent.PerformanceMetrics || backendEvent.performanceMetrics || {},
      timestamp: new Date(backendEvent.Timestamp || backendEvent.timestamp || Date.now()),
      // UI event list format
      type: 'SubBatchProgress',
      message: `Migration progress: ${(backendEvent.OverallProgressPercentage || 0).toFixed(1)}% - ${backendEvent.CompletedSubBatches || 0}/${backendEvent.TotalSubBatches || 0} sub-batches completed`,
      id: `subbatch-progress-${backendEvent.CompletedSubBatches || 0}-${Date.now()}`
    };
  }

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

    // Core Progress Events (Queue-based from backend)
    this.connection.on('MigrationProgressUpdated', (backendProgress: any) => {
      console.log('🎯 DEBUG: Received MigrationProgressUpdated (raw):', backendProgress);
      const transformedProgress = this.transformBackendProgress(backendProgress);
      console.log('🎯 DEBUG: Transformed to frontend format:', transformedProgress);
      this.notifyListeners('migrationProgress', transformedProgress);
    });

    this.connection.on('MigrationStatusChanged', (backendStatus: any) => {
      console.log('🎯 DEBUG: Received MigrationStatusChanged (raw):', backendStatus);
      const transformedStatus = {
        migrationId: backendStatus.MigrationId || backendStatus.migrationId,
        status: (backendStatus.Status || backendStatus.status || 'unknown').toLowerCase(),
        message: backendStatus.Message || backendStatus.message,
        data: backendStatus.Data || backendStatus.data,
        error: backendStatus.Error || backendStatus.error
      };
      console.log('🎯 DEBUG: Transformed status:', transformedStatus);
      this.notifyListeners('MigrationStatus', transformedStatus);
    });

    this.connection.on('EntityProgressUpdated', (entityProgress: any) => {
      console.log('🎯 DEBUG: Received EntityProgressUpdated:', entityProgress);
      this.notifyListeners('DetailedProgress', entityProgress);
      // Also notify entity-specific listeners
      this.notifyListeners('entityUpdate', entityProgress);
    });

    this.connection.on('BatchProgressUpdated', (batchProgress: any) => {
      console.log('🎯 DEBUG: Received BatchProgressUpdated:', batchProgress);
      this.notifyListeners('BatchStarted', batchProgress);
      this.notifyListeners('BatchProgress', batchProgress);
      this.notifyListeners('BatchCompleted', batchProgress);
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

    // Sub-batch Progress Events (New configurable sub-batch optimization)
    this.connection.on('SubBatchStarted', (subBatchEvent: any) => {
      console.log('🎯 DEBUG: Received SubBatchStarted:', subBatchEvent);
      const transformedEvent = this.transformSubBatchStartedEvent(subBatchEvent);
      console.log('🎯 DEBUG: Transformed SubBatchStarted:', transformedEvent);
      this.notifyListeners('subBatchStarted', transformedEvent);
      // Also notify as a general event for UI event lists
      this.notifyListeners('DetailedProgress', transformedEvent);
    });

    this.connection.on('SubBatchCompleted', (subBatchEvent: any) => {
      console.log('🎯 DEBUG: Received SubBatchCompleted:', subBatchEvent);
      const transformedEvent = this.transformSubBatchCompletedEvent(subBatchEvent);
      console.log('🎯 DEBUG: Transformed SubBatchCompleted:', transformedEvent);
      this.notifyListeners('subBatchCompleted', transformedEvent);
      // Also notify as a general event for UI event lists
      this.notifyListeners('DetailedProgress', transformedEvent);
    });

    this.connection.on('SubBatchMigrationProgress', (subBatchProgressEvent: any) => {
      console.log('🎯 DEBUG: Received SubBatchMigrationProgress:', subBatchProgressEvent);
      const transformedEvent = this.transformSubBatchProgressEvent(subBatchProgressEvent);
      console.log('🎯 DEBUG: Transformed SubBatchMigrationProgress:', transformedEvent);
      this.notifyListeners('subBatchProgress', transformedEvent);
      // Also notify as a general event for UI event lists
      this.notifyListeners('DetailedProgress', transformedEvent);
    });

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

    if (this.connectionState === 'Connected') {
      return;
    }

    if (this.connectionState === 'Connecting') {
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
        if (error.message.includes('CORS') || error.message.includes('cross-origin')) {
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
      isConnected: this.connectionState === 'Connected',
      lastConnected: this.connectionState === 'Connected' ? new Date() : undefined,
      connectionState: this.connectionState
    };
  }

  /**
   * Check if connection is active
   */
  public isConnected(): boolean {
    return this.connectionState === 'Connected';
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