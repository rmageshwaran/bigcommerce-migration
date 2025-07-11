import axios from 'axios';
import type { AxiosInstance, AxiosRequestConfig, AxiosResponse } from 'axios';
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

  constructor(config: Partial<ApiConfig> = {}) {
    this.config = {
      baseURL: config.baseURL || '/api/dashboard',
      timeout: config.timeout || 10000, // 10 seconds
      maxRetries: config.maxRetries || 3,
      retryDelay: config.retryDelay || 1000 // 1 second
    };

    this.client = axios.create({
      baseURL: this.config.baseURL,
      timeout: this.config.timeout,
      headers: {
        'Content-Type': 'application/json',
      },
    });

    this.setupInterceptors();
  }

  /**
   * Set up axios interceptors for request/response handling
   */
  private setupInterceptors(): void {
    // Request interceptor
    this.client.interceptors.request.use(
      (config) => {
        // Add authentication headers if needed
        // const token = getAuthToken();
        // if (token) {
        //   config.headers.Authorization = `Bearer ${token}`;
        // }
        
        console.log(`API Request: ${config.method?.toUpperCase()} ${config.url}`);
        return config;
      },
      (error) => {
        console.error('API Request Error:', error);
        return Promise.reject(error);
      }
    );

    // Response interceptor
    this.client.interceptors.response.use(
      (response) => {
        console.log(`API Response: ${response.status} ${response.config.url}`);
        return response;
      },
      async (error) => {
        console.error('API Response Error:', error);
        
        // Retry logic for failed requests
        if (error.config && !error.config._retry && this.shouldRetry(error)) {
          error.config._retry = true;
          error.config._retryCount = (error.config._retryCount || 0) + 1;
          
          if (error.config._retryCount <= this.config.maxRetries) {
            console.log(`Retrying request (${error.config._retryCount}/${this.config.maxRetries})`);
            
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
      const message = error.response.data?.message || error.response.statusText || 'API Error';
      return new Error(`HTTP ${error.response.status}: ${message}`);
    } else if (error.request) {
      // Request was made but no response received
      return new Error('Network Error: No response from server');
    } else {
      // Error in request setup
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

  // Migration API methods
  
  /**
   * Get migration status by ID
   */
  public async getMigrationStatus(migrationId: string): Promise<MigrationProgress> {
    return this.get<MigrationProgress>(`/migrations/${migrationId}/status`);
  }

  /**
   * Get list of active migrations
   */
  public async getActiveMigrations(): Promise<PaginatedResponse<MigrationProgress>> {
    return this.get<PaginatedResponse<MigrationProgress>>('/migrations');
  }

  /**
   * Start a new migration
   */
  public async startMigration(migrationRequest: any): Promise<ApiResponse<string>> {
    return this.post<ApiResponse<string>>('/migrations/start', migrationRequest);
  }

  /**
   * Cancel a migration
   */
  public async cancelMigration(migrationId: string): Promise<ApiResponse<void>> {
    return this.post<ApiResponse<void>>(`/migrations/${migrationId}/cancel`);
  }

  /**
   * Pause a migration
   */
  public async pauseMigration(migrationId: string): Promise<ApiResponse<void>> {
    return this.post<ApiResponse<void>>(`/migrations/${migrationId}/pause`);
  }

  /**
   * Resume a migration
   */
  public async resumeMigration(migrationId: string): Promise<ApiResponse<void>> {
    return this.post<ApiResponse<void>>(`/migrations/${migrationId}/resume`);
  }

  // System Health API methods

  /**
   * Get system health status
   */
  public async getSystemHealth(): Promise<SystemHealthData> {
    return this.get<SystemHealthData>('/health');
  }

  /**
   * Get queue status and metrics
   */
  public async getQueueStatus(): Promise<QueueStatusData> {
    return this.get<QueueStatusData>('/queues');
  }

  /**
   * Get migration statistics
   */
  public async getMigrationStatistics(timeRange?: { startDate: Date; endDate: Date }): Promise<MigrationStatistics> {
    const params = timeRange ? {
      startDate: timeRange.startDate.toISOString(),
      endDate: timeRange.endDate.toISOString()
    } : {};
    
    return this.get<MigrationStatistics>('/statistics', { params });
  }

  // Configuration and utility methods

  /**
   * Test API connectivity
   */
  public async testConnection(): Promise<boolean> {
    try {
      await this.get('/health');
      return true;
    } catch (error) {
      console.error('API connection test failed:', error);
      return false;
    }
  }

  /**
   * Update API configuration
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
    // Clear any internal caches if implemented
    console.log('API Service reset');
  }
}

// Singleton instance
let apiServiceInstance: ApiService | null = null;

/**
 * Get API service singleton instance
 */
export const getApiService = (config?: Partial<ApiConfig>): ApiService => {
  if (!apiServiceInstance) {
    apiServiceInstance = new ApiService(config);
  } else if (config) {
    apiServiceInstance.updateConfig(config);
  }
  return apiServiceInstance;
};

/**
 * Initialize API service with configuration
 */
export const initializeApiService = (config?: Partial<ApiConfig>): ApiService => {
  return getApiService(config);
}; 