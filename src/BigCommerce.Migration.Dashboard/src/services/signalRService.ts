import * as signalR from '@microsoft/signalr';
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
  private maxReconnectAttempts = 5;
  private reconnectInterval = 5000; // 5 seconds
  private listeners: Map<string, Set<(data: any) => void>> = new Map();

  constructor(private hubUrl: string) {
    this.initializeConnection();
  }

  /**
   * Initialize SignalR connection with configuration
   */
  private initializeConnection(): void {
    this.connection = new signalR.HubConnectionBuilder()
      .withUrl(this.hubUrl, {
        skipNegotiation: false, // Azure SignalR requires negotiation
        transport: signalR.HttpTransportType.WebSockets,
        accessTokenFactory: () => {
          // Add authentication token if needed
          return '';
        }
      })
      .withAutomaticReconnect({
        nextRetryDelayInMilliseconds: retryContext => {
          if (retryContext.elapsedMilliseconds < 60000) {
            return Math.random() * 10000;
          } else {
            return null; // Stop retrying after 1 minute
          }
        }
      })
      .configureLogging(signalR.LogLevel.Information)
      .build();

    this.setupEventHandlers();
  }

  /**
   * Set up connection event handlers
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

    // Message handlers
    this.connection.on('MigrationProgress', (progress: MigrationProgress) => {
      this.notifyListeners('migrationProgress', progress);
    });

    this.connection.on('MigrationStatus', (status: any) => {
      this.notifyListeners('migrationStatus', status);
    });

    this.connection.on('SystemHealth', (healthData: SystemHealthData) => {
      this.notifyListeners('systemHealth', healthData);
    });

    this.connection.on('Error', (error: string) => {
      console.error('SignalR server error:', error);
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
  public async connect(): Promise<void> {
    if (!this.connection) {
      throw new Error('SignalR connection not initialized');
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
      
      await this.connection.start();
      this.connectionState = 'Connected';
      this.reconnectAttempts = 0;
      
      console.info('SignalR connected successfully');
      this.notifyListeners('connectionStateChanged', { 
        state: this.connectionState,
        connectionId: this.connection.connectionId 
      });
    } catch (error) {
      this.connectionState = 'Disconnected';
      console.error('SignalR connection failed:', error);
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
  public async joinMigrationGroup(migrationId: string, userId = 'dashboard-user'): Promise<void> {
    if (!this.connection || this.connectionState !== 'Connected') {
      throw new Error('SignalR not connected');
    }

    try {
      await this.connection.invoke('JoinMigrationGroup', migrationId, userId);
      console.info(`Joined migration group: ${migrationId}`);
    } catch (error) {
      console.error(`Failed to join migration group ${migrationId}:`, error);
      throw error;
    }
  }

  /**
   * Leave a migration monitoring group
   */
  public async leaveMigrationGroup(migrationId: string): Promise<void> {
    if (!this.connection || this.connectionState !== 'Connected') {
      throw new Error('SignalR not connected');
    }

    try {
      await this.connection.invoke('LeaveMigrationGroup', migrationId);
      console.info(`Left migration group: ${migrationId}`);
    } catch (error) {
      console.error(`Failed to leave migration group ${migrationId}:`, error);
      throw error;
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

/**
 * Get SignalR service singleton instance
 */
export const getSignalRService = (hubUrl?: string): SignalRService => {
  if (!signalRServiceInstance) {
    if (!hubUrl) {
      // Default to localhost for development
      hubUrl = 'http://localhost:7071';
    }
    signalRServiceInstance = new SignalRService(hubUrl);
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