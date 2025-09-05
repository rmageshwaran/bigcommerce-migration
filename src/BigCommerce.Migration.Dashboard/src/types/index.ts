// System Status Types
export type SystemStatus = 'healthy' | 'warning' | 'error';
export type MigrationStatus = 'pending' | 'queued' | 'running' | 'in_progress' | 'inprogress' | 'completed' | 'failed' | 'cancelled';

// Migration Data Types
export interface MigrationProgress {
  migrationId: string;
  status: MigrationStatus;
  startTime: Date;
  lastUpdated: Date;
  elapsedTime: number; // in seconds
  estimatedTimeRemaining: number; // in seconds
  totalEntities: number;
  processedEntities: number;
  successfulEntities: number;
  failedEntities: number;
  skippedEntities: number;
  cancelledEntities: number;  // 🚨 CANCELLATION FIX: Add cancelledEntities
  overallProgressPercentage: number;
  entityProgress: Record<string, EntityProgress>;
  currentPhase: string;
  currentEntity: string;
  entitiesPerSecond: number;
  errorRate: number;
  // Additional properties for enhanced display
  sourceStore?: string;
  destinationStore?: string;
  estimatedEndTime?: Date;
}

export interface EntityProgress {
  entityType: string;
  totalCount: number;
  processedCount: number;
  successCount: number;
  failureCount: number;
  skippedCount: number;
  cancelledCount: number;  // 🚨 CANCELLATION FIX: Add cancelledCount
  progressPercentage: number;
  status: string;
  startTime: Date;
  endTime?: Date;
  processingTime: number; // in seconds
}

export interface MigrationCancellationResponse {
  migrationId: string;
  status: string;
  message: string;
  cancelledAt: string;
}

export interface ChannelMapping {
  sourceChannel: string;
  destinationChannel: string;
}

export interface MigrationRequest {
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
  entities: string[];
  channelMapping?: ChannelMapping[];
  settings?: {
    maxApiCallsPerSecond?: number;
    enableAdaptiveBatching?: boolean;
    logLevel?: string;
    requestTimeoutSeconds?: number;
    maxRetries?: number;
  };
}

export interface EntityConfiguration {
  categories: boolean;
  products: boolean;
  brands: boolean;
  variants: boolean;
  images: boolean;
  modifiers: boolean;
  options: boolean;
  reviews: boolean;
  batchSizes: Record<string, number>;
}

export interface MigrationOptions {
  continueOnError: boolean;
  skipExisting: boolean;
  validateData: boolean;
  enableLogging: boolean;
}

// System Health Types
export interface SystemHealthData {
  status: SystemStatus;
  timestamp: string | Date; // Allow both string (from API) and Date (parsed)
  services: ServiceHealthStatus;
  systemMetrics: SystemMetrics;
}

export interface ServiceHealthStatus {
  database: ServiceHealth;
  storage: ServiceHealth;
  rateLimit: ServiceHealth;
  signalR: ServiceHealth;
}

export interface ServiceHealth {
  status: SystemStatus;
  responseTime?: string;
  error?: string;
  metadata?: Record<string, any>;
}

export interface SystemMetrics {
  activeMigrations: number;
  totalProcessedEntities: number;
  averageProcessingSpeed: number;
  errorRate: number;
  uptimeSeconds: number;
  memoryUsage?: number;
  cpuUsage?: number;
}

// Queue Types
export interface QueueStatusData {
  queues: Record<string, QueueMetrics>;
  totalActiveMessages: number;
  totalDeadLetterMessages: number;
  processingRate: number;
  timestamp: Date;
}

export interface QueueMetrics {
  activeMessages: number;
  deadLetterMessages: number;
  processingRate?: number;
  avgProcessingTime?: number;
}

// Statistics Types
export interface MigrationStatistics {
  totalMigrations: number;
  completedMigrations: number;
  failedMigrations: number;
  averageProcessingTime: number; // in seconds
  totalEntitiesProcessed: number;
  averageEntitiesPerSecond: number;
  errorRate: number;
  entityStatistics: Record<string, EntityStatistics>;
  timeRange: TimeRange;
  timestamp: Date;
}

export interface EntityStatistics {
  total: number;
  success: number;
  failure: number;
  averageProcessingTime: number;
}

export interface TimeRange {
  startDate: Date;
  endDate: Date;
}

// Dashboard Component Types
export interface DashboardProps {
  migrationId?: string;
  autoRefresh?: boolean;
  refreshInterval?: number; // in milliseconds
}

export interface ChartDataPoint {
  timestamp: Date;
  value: number;
  label?: string;
}

export interface ProgressChartData {
  labels: string[];
  datasets: {
    label: string;
    data: number[];
    backgroundColor: string;
    borderColor: string;
    borderWidth: number;
  }[];
}

// SignalR Types
export interface SignalRConnection {
  connectionId: string;
  isConnected: boolean;
  lastConnected?: Date;
  connectionState: 'Disconnected' | 'Connecting' | 'Connected' | 'Disconnecting';
}

export interface SignalRMessage {
  type: string;
  data: any;
  timestamp: Date;
}

// API Response Types
export interface ApiResponse<T> {
  data: T;
  success: boolean;
  error?: string;
  timestamp: Date;
}

export interface PaginatedResponse<T> {
  data: T[];
  totalCount: number;
  page: number;
  pageSize: number;
  totalPages: number;
}

export interface MigrationHistoryResponse {
  migrations: any[];
  totalCount: number;
  pageSize: number;
  currentPage: number;
  totalPages: number;
  hasMorePages: boolean;
  message: string;
}

// Error Types
export interface DashboardError {
  code: string;
  message: string;
  details?: string;
  timestamp: Date;
  componentStack?: string;
}

// Configuration Types
export interface DashboardConfig {
  apiBaseUrl: string;
  signalRUrl: string;
  refreshInterval: number;
  maxRetries: number;
  timeout: number;
  enableNotifications: boolean;
  theme: 'light' | 'dark' | 'auto';
}

// Navigation Types
export interface NavigationItem {
  text: string;
  icon: React.ReactNode;
  path: string;
  badge?: number;
  disabled?: boolean;
}

// Filter and Search Types
export interface FilterOptions {
  status?: MigrationStatus[];
  dateRange?: TimeRange;
  entityType?: string[];
  storeHash?: string[];
}

export interface SearchOptions {
  query: string;
  fields: string[];
  caseSensitive: boolean;
}

// Enhanced Migration Progress Types for Real-time Tracking

export interface EnhancedMigrationProgress {
  migrationId: string;
  currentPhase: string;
  currentEntity: string;
  lastUpdated: Date;
  startTime: Date;
  elapsedTime: number; // milliseconds
  currentProcessing: ProcessingContext;
  batchProgress: BatchProgressSummary;
  remainingWork: RemainingWorkload;
  performance: RealTimeMetrics;
}

export interface ProcessingContext {
  currentEntity: string;
  currentBatchNumber: number;
  currentPhase: string;
  currentActivity: string;
  currentBatchStartTime: Date;
  currentBatch: CurrentBatchDetails;
}

export interface CurrentBatchDetails {
  batchNumber: number;
  batchSize: number;
  processedInBatch: number;
  batchProgressPercentage: number;
  batchProcessingSpeed: number; // entities per second
  batchElapsedTime: number; // milliseconds
  estimatedBatchTimeRemaining: number; // milliseconds
}

export interface BatchProgressSummary {
  totalBatches: number;
  completedBatches: number;
  processingBatches: number;
  remainingBatches: number;
  batchCompletionPercentage: number;
}

export interface RemainingWorkload {
  remainingEntities: number;
  remainingBatches: number;
  estimatedTimeRemaining: number; // milliseconds
}

export interface RealTimeMetrics {
  currentProcessingSpeed: number; // entities per second
  averageProcessingSpeed: number; // entities per second
  peakProcessingSpeed: number; // entities per second
  currentApiCallRate: number; // calls per second
  currentErrorRate: number; // errors per second
}

export interface BatchCompletionSummary {
  batchNumber: number;
  entitiesProcessed: number;
  successfulEntities: number;
  failedEntities: number;
  processingDuration: number; // milliseconds
  processingSpeed: number; // entities per second
  errorRate: number;
}

export interface MilestoneEvent {
  migrationId: string;
  milestone: string;
  message: string;
  timeToMilestone: number; // milliseconds
  entitiesProcessed: number;
  averageSpeed: number;
  estimatedTimeToCompletion: number; // milliseconds
}

// Real-time Event Types
export interface BatchEvent {
  migrationId: string;
  entityType: string;
  batchDetails?: CurrentBatchDetails;
  summary?: BatchCompletionSummary;
  timestamp: Date;
}

// 🚨 GLOBAL COORDINATION CLEANUP: Sub-batch event types removed
// These interfaces are no longer needed since we use coordinated MigrationProgress events
// from the global ParallelProgressAggregator instead of individual sub-batch events

export interface EntityPhaseTransition {
  migrationId: string;
  entityType: string;
  fromPhase: string;
  toPhase: string;
  transitionData: any;
  timestamp: Date;
}

export interface EntityCompletion {
  migrationId: string;
  entityType: string;
  completionData: {
    totalProcessed: number;
    successfulEntities: number;
    failedEntities: number;
    successRate: number;
    completedAt: Date;
  };
  timestamp: Date;
}

// Enhanced Migration Display Types for Phase 3 UI
export interface EnhancedMigrationDisplayData {
  migrationId: string;
  sourceStore: string;
  destinationStore: string;
  startDateTime: string;
  estimatedEndTime?: string;
  overallProgress: number;
  totalProcessed: number;
  totalSuccess: number;
  totalFailed: number;
  totalSkipped: number;
  totalCancelled: number;
  entities: EntityDisplayData[];
  status: MigrationStatus;
  lastUpdated: Date;
}

export interface EntityDisplayData {
  entityType: string;
  totalCount: number;
  processedCount: number;
  successCount: number;
  failedCount: number;
  skippedCount: number;
  cancelledCount?: number; // Optional cancelled entities count
  progressPercentage: number;
  status: 'processing' | 'completed' | 'cancelled' | 'failed' | 'pending';
  currentChunk?: number;
  totalChunks?: number;
  processingSpeed?: number; // entities per second
  showTotalCount?: boolean; // Flag to control whether to display total count (for dynamic discovery phases)
}

// Simplified SignalR Event Types for Phase 3
export interface MigrationStartedEvent {
  migrationId: string;
  sourceStore: string;
  destinationStore: string;
  startDateTime: string;
  estimatedEndTime?: string;
  entities: Array<{
    entityType: string;
    totalCount: number;
    estimatedDuration?: number;
  }>;
}

export interface EntityStartedEvent {
  migrationId: string;
  entityType: string;
  totalCount: number;
  estimatedDurationMs?: number;
  message: string;
  startDateTime: string;
}

export interface EntityChunkProgressEvent {
  migrationId: string;
  entityType: string;
  chunkNumber: number;
  totalChunks: number;
  chunkSize: number;
  processedInChunk: number;
  failedInChunk: number;
  // Cumulative counts across all chunks (matches backend camelCase conversion)
  totalProcessed: number;
  totalSuccess: number;
  totalFailed: number;
  totalSkipped: number;
  totalCancelled: number;
  totalEntitiesForType: number;
  progressPercentage: number;
  status: string;
  showTotalCount: boolean;
  message: string;
  processingTimeMs: number;
}

export interface EntityCompletedEvent {
  migrationId: string;
  entityType: string;
  totalProcessed: number;
  totalSuccess: number;
  totalFailed: number;
  totalSkipped: number;
  totalCancelled: number;
  status: string;
  showTotalCount: boolean;
  processingTimeMs: number; // milliseconds (matches backend field name)
  completedDateTime: string;
  message: string;
}

export interface MigrationCompletedEvent {
  migrationId: string;
  status: string;
  message: string;
  totalProcessedEntities: number;
  totalFailedEntities: number;
  durationMs: number;
  endDateTime: string;
}

export interface ErrorProgressEvent {
  migrationId: string;
  errorType: string;
  errorMessage: string;
  entityType?: string;
  entityId?: string;
  stackTrace?: string;
} 