import React from 'react';
import {
  Box,
  Chip,
  Tooltip,
  Typography,
  useTheme,
  alpha
} from '@mui/material';
import {
  Circle as LiveIcon,
  Refresh as UpdateIcon,
  Schedule as ClockIcon,
  Warning as StaleIcon
} from '@mui/icons-material';
import { formatDistanceToNow, format } from 'date-fns';

interface RealTimeIndicatorProps {
  lastUpdated?: Date | null;
  isConnected?: boolean;
  isPolling?: boolean;
  showLabel?: boolean;
  size?: 'small' | 'medium' | 'large';
}

/**
 * 🆕 TASK 4.2: Real-Time Data Indicator
 * 
 * Shows users when progress data is fresh from the new incremental progress system
 * Provides visual feedback about data freshness and connection status
 */
export const RealTimeIndicator: React.FC<RealTimeIndicatorProps> = ({
  lastUpdated,
  isConnected = false,
  isPolling = false,
  showLabel = true,
  size = 'medium'
}) => {
  const theme = useTheme();

  // Calculate data freshness
  const dataFreshness = React.useMemo(() => {
    if (!lastUpdated) {
      return { status: 'unknown', message: 'No data available', color: 'grey' };
    }

    const ageMs = Date.now() - lastUpdated.getTime();
    const ageSeconds = ageMs / 1000;

    if (ageSeconds < 5) {
      return { 
        status: 'live', 
        message: 'Live data (real-time aggregated)', 
        color: 'success' as const,
        icon: <LiveIcon sx={{ fontSize: 'inherit' }} />
      };
    } else if (ageSeconds < 30) {
      return { 
        status: 'fresh', 
        message: `Updated ${formatDistanceToNow(lastUpdated)} ago`, 
        color: 'primary' as const,
        icon: <UpdateIcon sx={{ fontSize: 'inherit' }} />
      };
    } else if (ageSeconds < 120) {
      return { 
        status: 'recent', 
        message: `Updated ${formatDistanceToNow(lastUpdated)} ago`, 
        color: 'warning' as const,
        icon: <ClockIcon sx={{ fontSize: 'inherit' }} />
      };
    } else {
      return { 
        status: 'stale', 
        message: `Stale data - updated ${formatDistanceToNow(lastUpdated)} ago`, 
        color: 'error' as const,
        icon: <StaleIcon sx={{ fontSize: 'inherit' }} />
      };
    }
  }, [lastUpdated]);

  // Size configuration
  const sizeConfig = {
    small: { fontSize: '0.75rem', chipSize: 'small' as const, iconSize: 12 },
    medium: { fontSize: '0.875rem', chipSize: 'medium' as const, iconSize: 16 },
    large: { fontSize: '1rem', chipSize: 'medium' as const, iconSize: 20 }
  };

  const config = sizeConfig[size];

  // Connection status indicator
  const connectionStatus = React.useMemo(() => {
    if (isConnected) {
      return {
        label: 'Connected',
        color: 'success' as const,
        icon: <LiveIcon sx={{ fontSize: config.iconSize }} />
      };
    } else if (isPolling) {
      return {
        label: 'Polling',
        color: 'warning' as const,
        icon: <UpdateIcon sx={{ fontSize: config.iconSize }} />
      };
    } else {
      return {
        label: 'Disconnected',
        color: 'error' as const,
        icon: <StaleIcon sx={{ fontSize: config.iconSize }} />
      };
    }
  }, [isConnected, isPolling, config.iconSize]);

  return (
    <Box display="flex" alignItems="center" gap={1}>
      {/* Data Freshness Indicator */}
      <Tooltip 
        title={
          <Box>
            <Typography variant="caption" fontWeight="bold">
              Data Freshness
            </Typography>
            <Typography variant="caption" display="block">
              {dataFreshness.message}
            </Typography>
            <Typography variant="caption" display="block" sx={{ mt: 0.5, opacity: 0.8 }}>
              🚀 Enhanced with real-time incremental progress
            </Typography>
          </Box>
        }
        arrow
      >
        <Chip
          icon={dataFreshness.icon}
          label={showLabel ? dataFreshness.status.toUpperCase() : undefined}
          color={dataFreshness.color as 'success' | 'primary' | 'warning' | 'error'}
          size={config.chipSize}
          variant={dataFreshness.status === 'live' ? 'filled' : 'outlined'}
          sx={{
            fontSize: config.fontSize,
            animation: dataFreshness.status === 'live' ? 'pulse 2s infinite' : 'none',
            '@keyframes pulse': {
              '0%': { opacity: 1 },
              '50%': { opacity: 0.7 },
              '100%': { opacity: 1 }
            }
          }}
        />
      </Tooltip>

      {/* Connection Status Indicator */}
      <Tooltip 
        title={
          <Box>
            <Typography variant="caption" fontWeight="bold">
              Connection Status
            </Typography>
            <Typography variant="caption" display="block">
              {isConnected ? 'SignalR connected for real-time updates' : 
               isPolling ? 'Fallback polling active' : 
               'No connection - data may be stale'}
            </Typography>
          </Box>
        }
        arrow
      >
        <Chip
          icon={connectionStatus.icon}
          label={showLabel ? connectionStatus.label : undefined}
          color={connectionStatus.color}
          size={config.chipSize}
          variant="outlined"
          sx={{
            fontSize: config.fontSize,
            opacity: 0.8
          }}
        />
      </Tooltip>

      {/* Last Updated Time (for debugging) */}
      {lastUpdated && size !== 'small' && (
        <Typography 
          variant="caption" 
          color="textSecondary"
          sx={{ 
            fontSize: config.fontSize,
            opacity: 0.6
          }}
        >
          {format(lastUpdated, 'HH:mm:ss')}
        </Typography>
      )}
    </Box>
  );
};