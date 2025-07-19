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

export interface ApiConfig {
  baseURL: string;
  timeout: number;
  maxRetries: number;
  retryDelay: number;
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
        ...(config.auth.apiKey && {
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
        hasApiKey: !!config.auth.apiKey,
        timeout: this.config.timeout
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
   * Maps to: POST /api/migrations/start
   */
  public async startMigration(migrationRequest: any): Promise<ApiResponse<string>> {
    return this.post<ApiResponse<string>>('/api/migrations/start', migrationRequest);
  }

  /**
   * Get migration status by ID  
   * Maps to: GET /api/migrations/{id}/status
   */
  public async getMigrationStatus(migrationId: string): Promise<MigrationProgress> {
    return this.get<MigrationProgress>(`/api/migrations/${migrationId}/status`);
  }

  /**
   * Get detailed migration progress
   * Maps to: GET /api/migrations/{id}/progress
   */
  public async getMigrationProgress(migrationId: string): Promise<MigrationProgress> {
    return this.get<MigrationProgress>(`/api/migrations/${migrationId}/progress`);
  }

  /**
   * Cancel a migration
   * Maps to: POST /api/migrations/{id}/cancel  
   */
  public async cancelMigration(migrationId: string): Promise<ApiResponse<void>> {
    return this.post<ApiResponse<void>>(`/api/migrations/${migrationId}/cancel`);
  }

  /**
   * Get list of active migrations
   * Maps to: GET /api/dashboard/active-migrations
   */
  public async getActiveMigrations(): Promise<PaginatedResponse<MigrationProgress>> {
    return this.get<PaginatedResponse<MigrationProgress>>('/api/dashboard/active-migrations');
  }

  /**
   * Get system health data
   * Maps to: GET /api/dashboard/system-health
   */
  public async getSystemHealth(): Promise<SystemHealthData> {
    return this.get<SystemHealthData>('/api/dashboard/system-health');
  }

  /**
   * Get performance statistics
   * Maps to: GET /api/dashboard/performance-stats
   */
  public async getPerformanceStats(): Promise<any> {
    return this.get<any>('/api/dashboard/performance-stats');
  }

  /**
   * Get queue status data
   * Maps to: GET /api/dashboard/queue-status  
   */
  public async getQueueStatus(): Promise<QueueStatusData> {
    return this.get<QueueStatusData>('/api/dashboard/queue-status');
  }

  /**
   * Get migration statistics
   * Maps to: GET /api/dashboard/migration-stats
   */
  public async getMigrationStatistics(): Promise<MigrationStatistics> {
    return this.get<MigrationStatistics>('/api/dashboard/migration-stats');
  }

  // ===== SIGNALR INTEGRATION HELPERS =====

  /**
   * Test SignalR connectivity
   * Maps to: POST /api/signalr/negotiate
   */
  public async testSignalRConnection(): Promise<any> {
    return this.post<any>('/api/signalr/negotiate');
  }

  /**
   * Manually trigger progress broadcast (for testing)
   * Maps to: POST /api/signalr/migration-progress
   */
  public async broadcastProgress(migrationId: string, progressData: any): Promise<void> {
    return this.post<void>('/api/signalr/migration-progress', { migrationId, ...progressData });
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