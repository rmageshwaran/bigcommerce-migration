/**
 * Environment Configuration
 * Manages environment variables and provides defaults for development
 */

// Environment variable helpers
const getEnvVar = (key: string, defaultValue: string = ''): string => {
  return import.meta.env[key] || defaultValue;
};

const getEnvBoolean = (key: string, defaultValue: boolean = false): boolean => {
  const value = import.meta.env[key];
  if (value === undefined) return defaultValue;
  return value === 'true' || value === '1';
};

const getEnvNumber = (key: string, defaultValue: number = 0): number => {
  const value = import.meta.env[key];
  if (value === undefined) return defaultValue;
  const parsed = parseInt(value, 10);
  return isNaN(parsed) ? defaultValue : parsed;
};

// Environment Configuration
export const config = {
  // Environment
  isDevelopment: import.meta.env.DEV,
  isProduction: import.meta.env.PROD,
  
  // Backend API Configuration
  api: {
    // In development, use relative URLs to work with Vite proxy
    // In production, use the actual Azure Functions URL
    baseUrl: import.meta.env.DEV 
      ? '/api'  // Relative URL for Vite proxy in development
      : getEnvVar('VITE_API_BASE_URL', 'https://your-production-functions.azurewebsites.net/api'),
    timeout: getEnvNumber('VITE_CONNECTION_TIMEOUT', 30000),
    retryAttempts: getEnvNumber('VITE_RETRY_ATTEMPTS', 3),
  },

  // SignalR Configuration
  signalR: {
    // Use Azure SignalR Service for both development and production
    // The Azure Functions backend handles the SignalR service connection
    hubUrl: getEnvVar('VITE_SIGNALR_HUB_URL', 'https://vortexiq-migration-signalr-dev.service.signalr.net'),
    hubName: 'migration', // SignalR hub name
    reconnectAttempts: getEnvNumber('VITE_RECONNECT_ATTEMPTS', 5),
    connectionTimeout: getEnvNumber('VITE_CONNECTION_TIMEOUT', 30000),
  },

  // Authentication
  auth: {
    apiKey: getEnvVar('VITE_API_KEY', 'your-azure-functions-api-key-here'),
    // For development, allow empty API key
    requireApiKey: !import.meta.env.DEV || getEnvBoolean('VITE_REQUIRE_API_KEY', false),
  },

  // Feature Flags
  features: {
    enableNotifications: getEnvBoolean('VITE_ENABLE_NOTIFICATIONS', true),
    enableRealtime: getEnvBoolean('VITE_ENABLE_REALTIME', true),
    enableDebugLogging: getEnvBoolean('VITE_ENABLE_DEBUG_LOGGING', import.meta.env.DEV),
  },

  // Dashboard Configuration
  dashboard: {
    defaultRefreshInterval: getEnvNumber('VITE_DEFAULT_REFRESH_INTERVAL', 10000),
    pollingInterval: getEnvNumber('VITE_POLLING_INTERVAL', 5000),
    maxEventHistory: getEnvNumber('VITE_MAX_EVENT_HISTORY', 100),
    performanceBaseline: getEnvNumber('VITE_PERFORMANCE_BASELINE', 100),
  },
};

// Configuration validation
export const validateConfig = (): { isValid: boolean; errors: string[] } => {
  const errors: string[] = [];

  // Check required configuration
  if (!config.api.baseUrl) {
    errors.push('VITE_API_BASE_URL is required');
  }

  if (!config.signalR.hubUrl) {
    errors.push('VITE_SIGNALR_HUB_URL is required');
  }

  if (config.auth.requireApiKey && !config.auth.apiKey) {
    errors.push('VITE_API_KEY is required for this environment');
  }

  return {
    isValid: errors.length === 0,
    errors
  };
};

// Export configuration for easy access
export default config;

// Development helper to log configuration (only in dev mode)
if (import.meta.env.DEV) {
  console.log('🔧 Dashboard Configuration:', {
    ...config,
    auth: { ...config.auth, apiKey: config.auth.apiKey ? '[REDACTED]' : '[NOT SET]' }
  });
  
  const validation = validateConfig();
  if (!validation.isValid) {
    console.warn('⚠️ Configuration Issues:', validation.errors);
  }
} 