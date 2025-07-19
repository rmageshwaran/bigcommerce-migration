import React, { useState, useEffect, useMemo } from 'react';
import {
  Box,
  LinearProgress,
  Typography,
  Card,
  CardContent,
  Chip,
  Stack,
  IconButton,
  Tooltip,
  Grid,
  Avatar,
  Fade,
  useTheme,
  alpha
} from '@mui/material';
import {
  PlayArrow as PlayIcon,
  Pause as PauseIcon,
  CheckCircle as CompletedIcon,
  Error as ErrorIcon,
  Cancel as CancelledIcon,
  Schedule as TimeIcon,
  Speed as PerformanceIcon,
  TrendingUp as TrendingUpIcon,
  Warning as WarningIcon
} from '@mui/icons-material';
import { format, formatDistanceToNow } from 'date-fns';
import type { MigrationProgress, MigrationStatus } from '../../types';

type ProgressBarSize = 'small' | 'medium' | 'large';

interface RealTimeProgressBarProps {
  migrationId: string;
  progress: MigrationProgress;
  showDetails?: boolean;
  showPerformanceMetrics?: boolean;
  showTimeEstimates?: boolean;
  onStatusClick?: (migrationId: string) => void;
  size?: ProgressBarSize;
}

const getStatusConfig = (status: MigrationStatus) => {
  switch (status) {
    case 'running':
      return {
        color: 'primary' as const,
        icon: <PlayIcon />,
        label: 'Running',
        bgColor: '#e3f2fd',
        textColor: '#1976d2'
      };
    case 'completed':
      return {
        color: 'success' as const,
        icon: <CompletedIcon />,
        label: 'Completed',
        bgColor: '#e8f5e8',
        textColor: '#2e7d32'
      };
    case 'failed':
      return {
        color: 'error' as const,
        icon: <ErrorIcon />,
        label: 'Failed',
        bgColor: '#ffebee',
        textColor: '#d32f2f'
      };
    case 'cancelled':
      return {
        color: 'warning' as const,
        icon: <CancelledIcon />,
        label: 'Cancelled',
        bgColor: '#fff3e0',
        textColor: '#f57c00'
      };
    case 'pending':
      return {
        color: 'default' as const,
        icon: <PauseIcon />,
        label: 'Pending',
        bgColor: '#f5f5f5',
        textColor: '#757575'
      };
    default:
      return {
        color: 'default' as const,
        icon: <PauseIcon />,
        label: 'Unknown',
        bgColor: '#f5f5f5',
        textColor: '#757575'
      };
  }
};

const formatDuration = (seconds: number): string => {
  if (seconds < 60) {
    return `${Math.round(seconds)}s`;
  } else if (seconds < 3600) {
    return `${Math.round(seconds / 60)}m ${Math.round(seconds % 60)}s`;
  } else {
    const hours = Math.floor(seconds / 3600);
    const minutes = Math.round((seconds % 3600) / 60);
    return `${hours}h ${minutes}m`;
  }
};

const formatRate = (rate: number): string => {
  if (rate >= 1000) {
    return `${(rate / 1000).toFixed(1)}k/s`;
  }
  return `${rate.toFixed(1)}/s`;
};

const calculatePerformanceImprovement = (currentRate: number, baselineRate: number = 100): number => {
  return ((currentRate - baselineRate) / baselineRate) * 100;
};

export const RealTimeProgressBar: React.FC<RealTimeProgressBarProps> = ({
  migrationId,
  progress,
  showDetails = true,
  showPerformanceMetrics = true,
  showTimeEstimates = true,
  onStatusClick,
  size = 'medium'
}) => {
  const theme = useTheme();
  const [isAnimating, setIsAnimating] = useState(false);
  const [previousProgress, setPreviousProgress] = useState(0);

  const statusConfig = getStatusConfig(progress.status);
  
  // Memoized calculations
  const calculations = useMemo(() => {
    const currentProgress = progress.overallProgressPercentage || 0;
    const performanceImprovement = calculatePerformanceImprovement(progress.entitiesPerSecond);
    const errorRate = (progress.failedEntities / (progress.processedEntities || 1)) * 100;
    const successRate = 100 - errorRate;
    
    return {
      currentProgress,
      performanceImprovement,
      errorRate,
      successRate,
      isHighPerformance: performanceImprovement > 50, // Our 60%+ optimization threshold
      isLowError: errorRate < 5,
      timeRemaining: progress.estimatedTimeRemaining || 0,
      elapsedTime: progress.elapsedTime || 0
    };
  }, [progress]);

  // Animation effect when progress updates
  useEffect(() => {
    if (calculations.currentProgress !== previousProgress) {
      setIsAnimating(true);
      setPreviousProgress(calculations.currentProgress);
      const timer = setTimeout(() => setIsAnimating(false), 500);
      return () => clearTimeout(timer);
    }
  }, [calculations.currentProgress, previousProgress]);

  const sizeConfig: Record<ProgressBarSize, { height: number; fontSize: string; padding: number }> = {
    small: { height: 6, fontSize: '0.75rem', padding: 1 },
    medium: { height: 8, fontSize: '0.875rem', padding: 2 },
    large: { height: 10, fontSize: '1rem', padding: 3 }
  };

  const config = sizeConfig[size];

  return (
    <Card 
      elevation={2}
      sx={{
        background: `linear-gradient(135deg, ${alpha(statusConfig.bgColor, 0.1)} 0%, ${alpha(theme.palette.background.paper, 0.9)} 100%)`,
        border: `1px solid ${alpha(statusConfig.textColor, 0.2)}`,
        transition: 'all 0.3s ease-in-out',
        '&:hover': {
          elevation: 4,
          transform: 'translateY(-2px)'
        }
      }}
    >
      <CardContent sx={{ p: config.padding, '&:last-child': { pb: config.padding } }}>
        {/* Header with Status and Migration ID */}
        <Box display="flex" justifyContent="space-between" alignItems="center" mb={1}>
          <Box display="flex" alignItems="center" gap={1}>
            <Avatar 
              sx={{ 
                width: 24, 
                height: 24, 
                bgcolor: statusConfig.textColor,
                '& .MuiSvgIcon-root': { fontSize: '0.875rem' }
              }}
            >
              {statusConfig.icon}
            </Avatar>
            <Typography variant="subtitle2" fontWeight="600">
              Migration {migrationId.slice(-8)}
            </Typography>
          </Box>
          
          <Stack direction="row" spacing={1}>
            <Chip
              size="small"
              label={statusConfig.label}
              color={statusConfig.color}
              variant="filled"
              onClick={() => onStatusClick?.(migrationId)}
              sx={{ 
                fontWeight: 600,
                fontSize: config.fontSize,
                cursor: onStatusClick ? 'pointer' : 'default'
              }}
            />
            
            {/* Performance Indicator */}
            {showPerformanceMetrics && calculations.isHighPerformance && (
              <Fade in={true}>
                <Chip
                  size="small"
                  label={`+${calculations.performanceImprovement.toFixed(0)}%`}
                  icon={<TrendingUpIcon />}
                  color="success"
                  variant="outlined"
                  sx={{ fontSize: config.fontSize, fontWeight: 600 }}
                />
              </Fade>
            )}
          </Stack>
        </Box>

        {/* Main Progress Bar */}
        <Box position="relative" mb={showDetails ? 2 : 0}>
          <LinearProgress
            variant="determinate"
            value={calculations.currentProgress}
            sx={{
              height: config.height,
              borderRadius: config.height / 2,
              backgroundColor: alpha(statusConfig.textColor, 0.1),
              '& .MuiLinearProgress-bar': {
                borderRadius: config.height / 2,
                background: progress.status === 'running' 
                  ? `linear-gradient(90deg, ${statusConfig.textColor} 0%, ${alpha(statusConfig.textColor, 0.8)} 100%)`
                  : statusConfig.textColor,
                transition: 'transform 0.5s ease-in-out',
                animation: isAnimating ? 'progressPulse 0.5s ease-in-out' : 'none'
              }
            }}
          />
          
          {/* Progress Percentage Overlay */}
          <Typography
            variant="caption"
            sx={{
              position: 'absolute',
              top: '50%',
              left: '50%',
              transform: 'translate(-50%, -50%)',
              fontWeight: 600,
              color: calculations.currentProgress > 50 ? 'white' : statusConfig.textColor,
              fontSize: config.fontSize,
              textShadow: calculations.currentProgress > 50 ? '0 1px 2px rgba(0,0,0,0.3)' : 'none'
            }}
          >
            {calculations.currentProgress.toFixed(1)}%
          </Typography>
        </Box>

        {/* Detailed Information */}
        {showDetails && (
          <Grid container spacing={2} alignItems="center">
            {/* Entity Progress */}
            <Grid item xs={12} sm={6}>
              <Box display="flex" alignItems="center" gap={1}>
                <Typography variant="body2" color="textSecondary">
                  Progress:
                </Typography>
                <Typography variant="body2" fontWeight="600">
                  {progress.processedEntities.toLocaleString()} / {progress.totalEntities.toLocaleString()}
                </Typography>
                <Typography variant="caption" color="textSecondary">
                  entities
                </Typography>
              </Box>
            </Grid>

            {/* Processing Rate */}
            <Grid item xs={12} sm={6}>
              <Box display="flex" alignItems="center" gap={1}>
                <PerformanceIcon fontSize="small" color="action" />
                <Typography variant="body2" color="textSecondary">
                  Rate:
                </Typography>
                <Typography variant="body2" fontWeight="600" color={calculations.isHighPerformance ? 'success.main' : 'text.primary'}>
                  {formatRate(progress.entitiesPerSecond)}
                </Typography>
              </Box>
            </Grid>

            {/* Time Information */}
            {showTimeEstimates && (
              <>
                <Grid item xs={12} sm={6}>
                  <Box display="flex" alignItems="center" gap={1}>
                    <TimeIcon fontSize="small" color="action" />
                    <Typography variant="body2" color="textSecondary">
                      Elapsed:
                    </Typography>
                    <Typography variant="body2" fontWeight="600">
                      {formatDuration(calculations.elapsedTime)}
                    </Typography>
                  </Box>
                </Grid>

                <Grid item xs={12} sm={6}>
                  <Box display="flex" alignItems="center" gap={1}>
                    <TimeIcon fontSize="small" color="action" />
                    <Typography variant="body2" color="textSecondary">
                      ETA:
                    </Typography>
                    <Typography variant="body2" fontWeight="600">
                      {calculations.timeRemaining > 0 ? formatDuration(calculations.timeRemaining) : 'Calculating...'}
                    </Typography>
                  </Box>
                </Grid>
              </>
            )}

            {/* Success/Error Rates */}
            <Grid item xs={12}>
              <Stack direction="row" spacing={2} alignItems="center">
                <Box display="flex" alignItems="center" gap={0.5}>
                  <CompletedIcon fontSize="small" sx={{ color: 'success.main' }} />
                  <Typography variant="caption" color="success.main" fontWeight="600">
                    {progress.successfulEntities.toLocaleString()} successful
                  </Typography>
                </Box>
                
                {progress.failedEntities > 0 && (
                  <Box display="flex" alignItems="center" gap={0.5}>
                    <ErrorIcon fontSize="small" sx={{ color: 'error.main' }} />
                    <Typography variant="caption" color="error.main" fontWeight="600">
                      {progress.failedEntities.toLocaleString()} failed
                    </Typography>
                  </Box>
                )}
                
                <Typography variant="caption" color="textSecondary">
                  ({calculations.successRate.toFixed(1)}% success rate)
                </Typography>
              </Stack>
            </Grid>

            {/* Current Phase */}
            {progress.currentPhase && (
              <Grid item xs={12}>
                <Typography variant="caption" color="textSecondary">
                  Current Phase: <strong>{progress.currentPhase}</strong>
                  {progress.currentEntity && ` • Processing: ${progress.currentEntity}`}
                </Typography>
              </Grid>
            )}
          </Grid>
        )}

        {/* Last Updated */}
        <Box mt={1} display="flex" justifyContent="space-between" alignItems="center">
          <Typography variant="caption" color="textSecondary">
            Last updated: {formatDistanceToNow(progress.lastUpdated, { addSuffix: true })}
          </Typography>
          
          {progress.status === 'running' && (
            <Box
              sx={{
                width: 8,
                height: 8,
                borderRadius: '50%',
                backgroundColor: 'success.main',
                animation: 'pulse 2s infinite'
              }}
            />
          )}
        </Box>
      </CardContent>
      
      {/* CSS Animations handled by MUI sx prop and theme */}
    </Card>
  );
}; 