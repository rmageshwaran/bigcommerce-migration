import axios from 'axios';
import type { AxiosInstance, AxiosRequestConfig, AxiosResponse } from 'axios';
import config from '../config/environment';
import type {
  MigrationProgress,
  SystemHealthData,
  QueueStatusData,
  MigrationStatistics,
  ApiResponse,
  PaginatedResponse
} from '../types';

// ===== API ENDPOINT MAPPING =====
/*
 * Frontend API Service → Azure Functions Endpoint Mapping
 * 
 * MAIN MIGRATION API (MigrationHttpFunctions.cs):
 * - POST   /api/migrations                              → startMigration()
 * - GET    /api/migrations                              → getAllMigrations()
 * - POST   /api/migrations/{id}/cancel                  → cancelMigration()
 * - GET    /api/migrations/history                      → getMigrationHistory()
 * - GET    /api/migrations/{id}/entities                → getMigrationEntityBreakdown()
 * - GET    /api/migrations/{id}/entities/{type}/errors  → getMigrationEntityErrors()
 * - GET    /api/migrations/latest/{storeId}             → getLatestMigrationForStore()
 * 
 * QUERY API (MigrationQueryFunctions.cs):
 * - GET    /api/migrations/{id}/status                  → getMigrationStatus()
 * - GET    /api/migrations/{id}/details                 → getMigrationProgress()
 * - GET    /api/query/migrations                        → queryMigrations()
 * 
 * DASHBOARD API (DashboardFunctions.cs):
 * - GET    /api/dashboard/migrations                    → getActiveMigrations()
 * - GET    /api/dashboard/migrations/{id}/status        → useMigrationProgress hook
 * - GET    /api/dashboard/health                        → getSystemHealth() + useSystemHealth hook
 * - GET    /api/dashboard/statistics                    → getPerformanceStats() + getMigrationStatistics()
 * - GET    /api/dashboard/queues                        → getQueueStatus()
 * 
 * MANAGEMENT API (MigrationManagementFunctions.cs):
 * - POST   /api/management/migrations                   → Alternative start endpoint
 * - DELETE /api/management/migrations/{id}             → Alternative cancel endpoint
 * 
 * SIGNALR API (SignalRFunctions.cs):
 * - POST   /api/negotiate                               → testSignalRConnection()
 * - POST   /api/signalr/migration-progress              → broadcastProgress()
 */

export interface ApiConfig {
  baseURL: string;
  timeout: number;
  maxRetries: number;
  retryDelay: number;
}

// Additional type definitions for better type safety
export interface MigrationRequest {
  entities: string[];
  sourceStore: {
    storeId: string;
    accessToken: string;
    channelId: string;
    baseUrl?: string;
  };
  destinationStore: {
    storeId: string;
    accessToken: string;
    channelId: string;
    baseUrl?: string;
  };
  settings?: {
    maxApiCallsPerSecond?: number;
    enableAdaptiveBatching?: boolean;
    logLevel?: string;
    requestTimeoutSeconds?: number;
    maxRetries?: number;
  };
}

export interface MigrationEntityError {
  entityId: string;
  entityType: string;
  errorMessage: string;
  timestamp: string;
}

export interface MigrationEntityBreakdown {
  entityType: string;
  totalCount: number;
  processedCount: number;
  successCount: number;
  failureCount: number;
}

export class ApiService {
  private client: AxiosInstance;
  private config: ApiConfig;

  constructor(apiConfig: Partial<ApiConfig> = {}) {
    // Use environment configuration with override options
    this.config = {
      baseURL: apiConfig.baseURL || config.api.baseUrl,
      timeout: apiConfig.timeout || config.api.timeout,
      maxRetries: apiConfig.maxRetries || config.api.retryAttempts,
      retryDelay: apiConfig.retryDelay || 1000 // 1 second
    };

    // Create axios instance with backend-specific configuration
    this.client = axios.create({
      baseURL: this.config.baseURL,
      timeout: this.config.timeout,
      headers: {
        'Content-Type': 'application/json',
        // Add Azure Functions authentication if API key is available
        ...(config.auth.apiKey && config.auth.apiKey !== 'your-azure-functions-api-key-here' && {
          'Authorization': `Bearer ${config.auth.apiKey}`,
          'x-functions-key': config.auth.apiKey
        })
      },
    });

    this.setupInterceptors();
    
    // Log configuration in development
    if (config.features.enableDebugLogging) {
      console.log('🔌 API Service initialized:', {
        baseURL: this.config.baseURL,
        isDevelopment: config.isDevelopment,
        hasApiKey: !!config.auth.apiKey && config.auth.apiKey !== 'your-azure-functions-api-key-here',
        timeout: this.config.timeout,
        proxyMode: config.isDevelopment ? 'Vite Proxy' : 'Direct'
      });
    }
  }

  /**
   * Set up axios interceptors for request/response handling
   */
  private setupInterceptors(): void {
    // Request interceptor
    this.client.interceptors.request.use(
      (requestConfig) => {
        if (config.features.enableDebugLogging) {
          console.log(`🔍 API Request: ${requestConfig.method?.toUpperCase()} ${requestConfig.url}`);
        }
        return requestConfig;
      },
      (error) => {
        console.error('❌ API Request Error:', error);
        return Promise.reject(error);
      }
    );

    // Response interceptor with Azure Functions error handling
    this.client.interceptors.response.use(
      (response) => {
        if (config.features.enableDebugLogging) {
          console.log(`✅ API Response: ${response.status} ${response.config.url}`);
        }
        return response;
      },
      async (error) => {
        if (config.features.enableDebugLogging) {
          console.error('❌ API Response Error:', error);
        }
        
        // Handle Azure Functions specific errors
        if (error.response?.status === 401) {
          console.error('🔒 Authentication failed - check API key configuration');
        }
        
        if (error.response?.status === 403) {
          console.error('🚫 Authorization failed - API key may lack required permissions');
        }
        
        // Retry logic for failed requests (but not for auth errors)
        if (error.config && !error.config._retry && this.shouldRetry(error)) {
          error.config._retry = true;
          error.config._retryCount = (error.config._retryCount || 0) + 1;
          
          if (error.config._retryCount <= this.config.maxRetries) {
            console.log(`🔄 Retrying request (${error.config._retryCount}/${this.config.maxRetries})`);
            
            // Wait before retrying
            await this.delay(this.config.retryDelay * error.config._retryCount);
            
            return this.client(error.config);
          }
        }
        
        return Promise.reject(this.formatError(error));
      }
    );
  }

  /**
   * Determine if a request should be retried
   */
  private shouldRetry(error: any): boolean {
    // Don't retry authentication or authorization errors
    if (error.response?.status === 401 || error.response?.status === 403) {
      return false;
    }
    
    return (
      error.code === 'ECONNABORTED' || // Timeout
      error.code === 'ENOTFOUND' || // Network error
      error.code === 'ECONNRESET' || // Connection reset
      (error.response && error.response.status >= 500) // Server errors
    );
  }

  /**
   * Format error for consistent error handling
   */
  private formatError(error: any): Error {
    if (error.response) {
      // Server responded with error status
      const message = error.response.data?.message || error.response.statusText || 'Server Error';
      return new Error(`API Error (${error.response.status}): ${message}`);
    } else if (error.request) {
      // Request was made but no response received
      return new Error('Network Error: No response from server');
    } else {
      // Something else happened
      return new Error(`Request Error: ${error.message}`);
    }
  }

  /**
   * Delay utility for retry logic
   */
  private delay(ms: number): Promise<void> {
    return new Promise(resolve => setTimeout(resolve, ms));
  }

  /**
   * Generic GET request
   */
  private async get<T>(url: string, config?: AxiosRequestConfig): Promise<T> {
    const response: AxiosResponse<T> = await this.client.get(url, config);
    return response.data;
  }

  /**
   * Generic POST request
   */
  private async post<T>(url: string, data?: any, config?: AxiosRequestConfig): Promise<T> {
    const response: AxiosResponse<T> = await this.client.post(url, data, config);
    return response.data;
  }

  /**
   * Generic PUT request
   */
  private async put<T>(url: string, data?: any, config?: AxiosRequestConfig): Promise<T> {
    const response: AxiosResponse<T> = await this.client.put(url, data, config);
    return response.data;
  }

  /**
   * Generic DELETE request
   */
  private async delete<T>(url: string, config?: AxiosRequestConfig): Promise<T> {
    const response: AxiosResponse<T> = await this.client.delete(url, config);
    return response.data;
  }

  // ===== BACKEND INTEGRATION API METHODS =====
  
  /**
   * Start a new migration
   * Maps to: POST /api/migrations (Main HTTP API in MigrationHttpFunctions)
   */
  public async startMigration(migrationRequest: MigrationRequest): Promise<ApiResponse<string>> {
    return this.post<ApiResponse<string>>('/migrations', migrationRequest);
  }

  /**
   * Get migration status by ID  
   * Maps to: GET /api/migrations/{id}/status (MigrationQueryFunctions)
   */
  public async getMigrationStatus(migrationId: string): Promise<MigrationProgress> {
    return this.get<MigrationProgress>(`/migrations/${migrationId}/status`);
  }

  /**
   * Get detailed migration progress
   * Maps to: GET /api/migrations/{id}/details (MigrationQueryFunctions)
   */
  public async getMigrationProgress(migrationId: string): Promise<MigrationProgress> {
    return this.get<MigrationProgress>(`/migrations/${migrationId}/details`);
  }

  /**
   * Cancel a migration
   * Maps to: POST /api/migrations/{id}/cancel (MigrationHttpFunctions)
   */
  public async cancelMigration(migrationId: string): Promise<ApiResponse<void>> {
    return this.post<ApiResponse<void>>(`/migrations/${migrationId}/cancel`);
  }

  /**
   * Get list of active migrations
   * Maps to: GET /api/dashboard/migrations (DashboardFunctions)
   */
  public async getActiveMigrations(): Promise<PaginatedResponse<MigrationProgress>> {
    return this.get<PaginatedResponse<MigrationProgress>>('/dashboard/migrations');
  }

  /**
   * Get system health data
   * Maps to: GET /api/dashboard/health (DashboardFunctions)
   */
  public async getSystemHealth(): Promise<SystemHealthData> {
    return this.get<SystemHealthData>('/dashboard/health');
  }

  /**
   * Get performance statistics
   * Maps to: GET /api/dashboard/statistics (DashboardFunctions)
   */
  public async getPerformanceStats(): Promise<any> {
    return this.get<any>('/dashboard/statistics');
  }

  /**
   * Get queue status data
   * Maps to: GET /api/dashboard/queues (DashboardFunctions)
   */
  public async getQueueStatus(): Promise<QueueStatusData> {
    return this.get<QueueStatusData>('/dashboard/queues');
  }

  /**
   * Get migration statistics
   * Maps to: GET /api/dashboard/statistics (DashboardFunctions)
   */
  public async getMigrationStatistics(): Promise<MigrationStatistics> {
    return this.get<MigrationStatistics>('/dashboard/statistics');
  }

  // ===== ADDITIONAL MIGRATION API METHODS =====

  /**
   * Get all migrations with filtering
   * Maps to: GET /api/migrations (MigrationHttpFunctions)
   */
  public async getAllMigrations(queryParams?: Record<string, string>): Promise<PaginatedResponse<MigrationProgress>> {
    const params = queryParams ? new URLSearchParams(queryParams).toString() : '';
    const url = params ? `/migrations?${params}` : '/migrations';
    return this.get<PaginatedResponse<MigrationProgress>>(url);
  }

  /**
   * Get migration history
   * Maps to: GET /api/migrations/history (MigrationHttpFunctions)
   */
  public async getMigrationHistory(queryParams?: Record<string, string>): Promise<PaginatedResponse<any>> {
    const params = queryParams ? new URLSearchParams(queryParams).toString() : '';
    const url = params ? `/migrations/history?${params}` : '/migrations/history';
    return this.get<PaginatedResponse<any>>(url);
  }

  /**
   * Get migration entity breakdown
   * Maps to: GET /api/migrations/{id}/entities (MigrationHttpFunctions)
   */
  public async getMigrationEntityBreakdown(migrationId: string): Promise<MigrationEntityBreakdown[]> {
    return this.get<MigrationEntityBreakdown[]>(`/migrations/${migrationId}/entities`);
  }

  /**
   * Get migration entity errors
   * Maps to: GET /api/migrations/{id}/entities/{entityType}/errors (MigrationHttpFunctions)
   */
  public async getMigrationEntityErrors(migrationId: string, entityType: string): Promise<MigrationEntityError[]> {
    return this.get<MigrationEntityError[]>(`/migrations/${migrationId}/entities/${entityType}/errors`);
  }

  /**
   * Query migrations with advanced filtering
   * Maps to: GET /api/query/migrations (MigrationQueryFunctions)
   */
  public async queryMigrations(queryParams?: Record<string, string>): Promise<PaginatedResponse<MigrationProgress>> {
    const params = queryParams ? new URLSearchParams(queryParams).toString() : '';
    const url = params ? `/query/migrations?${params}` : '/query/migrations';
    return this.get<PaginatedResponse<MigrationProgress>>(url);
  }

  /**
   * Get latest migration for a store
   * Maps to: GET /api/migrations/latest/{storeId} (MigrationHttpFunctions)
   */
  public async getLatestMigrationForStore(storeId: string): Promise<MigrationProgress | null> {
    return this.get<MigrationProgress | null>(`/migrations/latest/${storeId}`);
  }

  // ===== SIGNALR INTEGRATION HELPERS =====

  /**
   * Test SignalR connectivity
   * Maps to: POST /api/negotiate
   */
  public async testSignalRConnection(): Promise<any> {
    return this.post<any>('/negotiate');
  }

  /**
   * Manually trigger progress broadcast (for testing)
   * Maps to: POST /api/signalr/migration-progress
   */
  public async broadcastProgress(migrationId: string, progressData: any): Promise<void> {
    return this.post<void>('/signalr/migration-progress', { migrationId, ...progressData });
  }

  // ===== UTILITY METHODS =====

  /**
   * Test API connectivity with improved diagnostics
   * Maps to: GET /api/health/detailed (MonitoringFunctions)
   */
  public async testConnection(): Promise<boolean> {
    try {
      // Try the main health endpoint first
      await this.get('/health/detailed');
      if (config.features.enableDebugLogging) {
        console.log('✅ API connection successful: /health/detailed');
      }
      return true;
    } catch (error) {
      console.warn('❌ Primary health endpoint failed, trying alternatives...');
      
      // Try alternative endpoints
      const fallbackEndpoints = ['/info', '/swagger', '/'];
      
      for (const endpoint of fallbackEndpoints) {
        try {
          await this.get(endpoint);
          if (config.features.enableDebugLogging) {
            console.log(`✅ API connection successful via fallback: ${endpoint}`);
          }
          return true;
        } catch (fallbackError) {
          console.warn(`❌ Fallback endpoint ${endpoint} also failed`);
        }
      }
      
      // All endpoints failed - provide diagnostics
      console.error('🚨 API Connection Failed - Diagnostics:');
      console.error('  📍 Base URL:', this.config.baseURL);
      console.error('  🔧 Development Mode:', config.isDevelopment);
      console.error('  🔗 Proxy Mode:', config.isDevelopment ? 'Vite Proxy → localhost:7071' : 'Direct');
      console.error('  🔑 Has API Key:', !!config.auth.apiKey && config.auth.apiKey !== 'your-azure-functions-api-key-here');
      console.error('  💡 Troubleshooting:');
      console.error('     1. Make sure Azure Functions are running on localhost:7071');
      console.error('     2. Check if the functions are deployed and accessible');
      console.error('     3. Verify CORS configuration in Azure Functions');
      console.error('     4. Check network connectivity');
      
      console.warn('API connection test failed:', error);
      return false;
    }
  }

  /**
   * Get API information and available endpoints
   * Maps to: GET /api/info (OpenApiFunctions)
   */
  public async getApiInfo(): Promise<any> {
    return this.get<any>('/info');
  }

  /**
   * Update API configuration dynamically
   */
  public updateConfig(newConfig: Partial<ApiConfig>): void {
    this.config = { ...this.config, ...newConfig };
    
    // Update axios instance configuration
    this.client.defaults.baseURL = this.config.baseURL;
    this.client.defaults.timeout = this.config.timeout;
  }

  /**
   * Get current API configuration
   */
  public getConfig(): ApiConfig {
    return { ...this.config };
  }

  /**
   * Clear any cached data or reset state
   */
  public reset(): void {
    console.log('🔄 API Service reset');
  }
}

// Singleton instance with backend configuration
let apiServiceInstance: ApiService | null = null;

/**
 * Get API service singleton instance configured for backend integration
 */
export const getApiService = (apiConfig?: Partial<ApiConfig>): ApiService => {
  if (!apiServiceInstance) {
    apiServiceInstance = new ApiService(apiConfig);
  } else if (apiConfig) {
    apiServiceInstance.updateConfig(apiConfig);
  }
  return apiServiceInstance;
};

/**
 * Initialize API service with backend configuration
 */
export const initializeApiService = (apiConfig?: Partial<ApiConfig>): ApiService => {
  return getApiService(apiConfig);
};

// Export configured instance for easy access
export const apiService = getApiService(); 