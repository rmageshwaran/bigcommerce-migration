/**
 * @deprecated This component has been superseded by EnhancedMigrationDashboard.
 * Please use EnhancedMigrationDashboard instead for better features including:
 * - Batch-level progress tracking
 * - Current processing context  
 * - Performance trends and milestones
 * - More detailed real-time metrics
 * 
 * This component will be removed in a future version.
 * Migration: Replace `RealTimeMigrationDashboard` with `EnhancedMigrationDashboard`
 */

import React, { useState, useEffect, useMemo } from 'react';
import {
  Box,
  Grid,
  Card,
  CardContent,
  Typography,
  Button,
  IconButton,
  Chip,
  Alert,
  Snackbar,
  CircularProgress,
  Stack,
  Divider,
  Tab,
  Tabs,
  Badge,
  Tooltip,
  useTheme,
  alpha
} from '@mui/material';
import {
  Refresh as RefreshIcon,
  Fullscreen as FullscreenIcon,
  FullscreenExit as FullscreenExitIcon,
  PlayArrow as PlayIcon,
  Pause as PauseIcon,
  Stop as StopIcon,
  Notifications as NotificationsIcon,
  NotificationsOff as NotificationsOffIcon,
  Speed as PerformanceIcon,
  Timeline as TimelineIcon,
  Error as ErrorIcon,
  CheckCircle as SuccessIcon,
  Warning as WarningIcon
} from '@mui/icons-material';
import { format, formatDistanceToNow } from 'date-fns';

// Import our custom components
import { RealTimeProgressBar } from '../Progress/RealTimeProgressBar';
import { RealTimeEntityGrid } from '../Progress/RealTimeEntityGrid';
import { useRealTimeMigrationProgress } from '../../hooks/useRealTimeMigrationProgress';
import type { MigrationProgress } from '../../types';

interface RealTimeMigrationDashboardProps {
  migrationId: string;
  autoConnect?: boolean;
  enableNotifications?: boolean;
  onMigrationComplete?: (migrationId: string) => void;
  onMigrationError?: (migrationId: string, error: any) => void;
}

interface TabPanelProps {
  children?: React.ReactNode;
  index: number;
  value: number;
}

const TabPanel: React.FC<TabPanelProps> = ({ children, value, index, ...other }) => {
  return (
    <div
      role="tabpanel"
      hidden={value !== index}
      id={`migration-tabpanel-${index}`}
      aria-labelledby={`migration-tab-${index}`}
      {...other}
    >
      {value === index && (
        <Box sx={{ p: 3 }}>
          {children}
        </Box>
      )}
    </div>
  );
};

const PerformanceMetricsCard: React.FC<{
  metrics: any;
  performanceHistory: any[];
}> = ({ metrics, performanceHistory }) => {
  const theme = useTheme();

  if (!metrics) {
    return (
      <Card>
        <CardContent>
          <Typography variant="h6" gutterBottom display="flex" alignItems="center" gap={1}>
            <PerformanceIcon />
            Performance Metrics
          </Typography>
          <Typography color="textSecondary">
            Performance data will appear once migration starts
          </Typography>
        </CardContent>
      </Card>
    );
  }

  const improvementColor = metrics.improvementPercentage > 50 ? 'success.main' : 
                           metrics.improvementPercentage > 20 ? 'warning.main' : 'error.main';

  return (
    <Card>
      <CardContent>
        <Typography variant="h6" gutterBottom display="flex" alignItems="center" gap={1}>
          <PerformanceIcon />
          Performance Metrics
          <Chip
            size="small"
            label={metrics.efficiencyTrend?.toUpperCase()}
            color={
              metrics.efficiencyTrend === 'improving' ? 'success' :
              metrics.efficiencyTrend === 'declining' ? 'error' : 'default'
            }
            variant="outlined"
          />
        </Typography>

        <Grid container spacing={2}>
          <Grid item xs={6} md={3}>
            <Box textAlign="center">
              <Typography variant="h4" color={improvementColor} fontWeight="600">
                +{metrics.improvementPercentage.toFixed(0)}%
              </Typography>
              <Typography variant="caption" color="textSecondary">
                Performance Improvement
              </Typography>
            </Box>
          </Grid>
          
          <Grid item xs={6} md={3}>
            <Box textAlign="center">
              <Typography variant="h4" fontWeight="600">
                {metrics.averageRate.toFixed(1)}
              </Typography>
              <Typography variant="caption" color="textSecondary">
                Avg Rate (entities/sec)
              </Typography>
            </Box>
          </Grid>
          
          <Grid item xs={6} md={3}>
            <Box textAlign="center">
              <Typography variant="h4" color="success.main" fontWeight="600">
                {metrics.peakRate.toFixed(1)}
              </Typography>
              <Typography variant="caption" color="textSecondary">
                Peak Rate (entities/sec)
              </Typography>
            </Box>
          </Grid>
          
          <Grid item xs={6} md={3}>
            <Box textAlign="center">
              <Typography variant="h4" fontWeight="600">
                {performanceHistory.length}
              </Typography>
              <Typography variant="caption" color="textSecondary">
                Data Points
              </Typography>
            </Box>
          </Grid>
        </Grid>

        {metrics.improvementPercentage > 50 && (
          <Alert severity="success" sx={{ mt: 2 }}>
            🚀 Excellent performance! Our optimizations are delivering {metrics.improvementPercentage.toFixed(0)}% improvement over baseline.
          </Alert>
        )}
      </CardContent>
    </Card>
  );
};

const RecentEventsCard: React.FC<{
  events: any[];
  errors: any[];
}> = ({ events, errors }) => {
  const [selectedTab, setSelectedTab] = useState(0);

  const handleTabChange = (event: React.SyntheticEvent, newValue: number) => {
    setSelectedTab(newValue);
  };

  const recentEvents = events.slice(0, 10);
  const recentErrors = errors.slice(0, 10);

  return (
    <Card>
      <CardContent sx={{ p: 0 }}>
        <Box sx={{ borderBottom: 1, borderColor: 'divider' }}>
          <Tabs value={selectedTab} onChange={handleTabChange}>
            <Tab 
              label={
                <Badge badgeContent={events.length} color="primary" max={99}>
                  Recent Events
                </Badge>
              } 
            />
            <Tab 
              label={
                <Badge badgeContent={errors.length} color="error" max={99}>
                  Errors
                </Badge>
              } 
            />
          </Tabs>
        </Box>

        <TabPanel value={selectedTab} index={0}>
          {recentEvents.length === 0 ? (
            <Typography color="textSecondary" textAlign="center">
              No events recorded yet
            </Typography>
          ) : (
            <Stack spacing={1}>
              {recentEvents.map((event) => (
                <Box key={event.id} display="flex" alignItems="center" gap={1} p={1} sx={{
                  borderRadius: 1,
                  backgroundColor: alpha('#000', 0.02),
                  border: '1px solid',
                  borderColor: 'divider'
                }}>
                  <Chip
                    size="small"
                    label={event.type}
                    color={
                      event.type === 'progress' ? 'primary' :
                      event.type === 'status' ? 'default' :
                      event.type === 'entity' ? 'secondary' :
                      event.type === 'batch' ? 'info' : 'default'
                    }
                    variant="outlined"
                  />
                  <Typography variant="body2" flex={1}>
                    {event.message}
                  </Typography>
                  <Typography variant="caption" color="textSecondary">
                    {format(event.timestamp, 'HH:mm:ss')}
                  </Typography>
                </Box>
              ))}
            </Stack>
          )}
        </TabPanel>

        <TabPanel value={selectedTab} index={1}>
          {recentErrors.length === 0 ? (
            <Typography color="textSecondary" textAlign="center">
              No errors recorded
            </Typography>
          ) : (
            <Stack spacing={1}>
              {recentErrors.map((error) => (
                <Alert key={error.id} severity="error" variant="outlined">
                  <Box display="flex" justifyContent="space-between" alignItems="center" width="100%">
                    <Typography variant="body2">
                      {error.message}
                    </Typography>
                    <Typography variant="caption" color="textSecondary">
                      {format(error.timestamp, 'HH:mm:ss')}
                    </Typography>
                  </Box>
                </Alert>
              ))}
            </Stack>
          )}
        </TabPanel>
      </CardContent>
    </Card>
  );
};

export const RealTimeMigrationDashboard: React.FC<RealTimeMigrationDashboardProps> = ({
  migrationId,
  autoConnect = true,
  enableNotifications = true,
  onMigrationComplete,
  onMigrationError
}) => {
  const theme = useTheme();
  const [isFullscreen, setIsFullscreen] = useState(false);
  const [notificationsEnabled, setNotificationsEnabled] = useState(enableNotifications);
  const [snackbarOpen, setSnackbarOpen] = useState(false);
  const [snackbarMessage, setSnackbarMessage] = useState('');
  const [snackbarSeverity, setSnackbarSeverity] = useState<'success' | 'error' | 'warning' | 'info'>('info');

  // Use our enhanced real-time hook
  const {
    progress,
    entityStatus,
    isConnected,
    connectionState,
    lastHeartbeat,
    performanceMetrics,
    performanceHistory,
    recentEvents,
    errors,
    isLoading,
    lastUpdated,
    connect,
    disconnect,
    reconnect,
    refresh,
    clearHistory,
    clearErrors
  } = useRealTimeMigrationProgress({
    migrationId,
    autoConnect,
    enableNotifications: notificationsEnabled,
    enablePerformanceTracking: true,
    pollInterval: 30000 // 30 seconds fallback polling
  });

  // Handle migration completion
  useEffect(() => {
    if (progress?.status === 'completed' && onMigrationComplete) {
      onMigrationComplete(migrationId);
      setSnackbarMessage('Migration completed successfully!');
      setSnackbarSeverity('success');
      setSnackbarOpen(true);
    }
  }, [progress?.status, migrationId, onMigrationComplete]);

  // Handle migration errors
  useEffect(() => {
    if (progress?.status === 'failed' && onMigrationError) {
      onMigrationError(migrationId, { status: 'failed' });
      setSnackbarMessage('Migration failed. Check error details.');
      setSnackbarSeverity('error');
      setSnackbarOpen(true);
    }
  }, [progress?.status, migrationId, onMigrationError]);

  // Toggle fullscreen mode
  const handleFullscreenToggle = () => {
    setIsFullscreen(!isFullscreen);
  };

  // Toggle notifications
  const handleNotificationsToggle = () => {
    setNotificationsEnabled(!notificationsEnabled);
  };

  // Calculate overall status
  const overallStatus = useMemo(() => {
    if (!progress) return 'loading';
    return progress.status;
  }, [progress]);

  // Calculate statistics
  const statistics = useMemo(() => {
    if (!progress) return null;

    return {
      totalEntities: progress.totalEntities,
      processedEntities: progress.processedEntities,
      successfulEntities: progress.successfulEntities,
      failedEntities: progress.failedEntities,
      successRate: progress.processedEntities > 0 
        ? (progress.successfulEntities / progress.processedEntities) * 100 
        : 0,
      errorRate: progress.errorRate || 0,
      entitiesPerSecond: progress.entitiesPerSecond || 0,
      elapsedTime: progress.elapsedTime || 0,
      estimatedTimeRemaining: progress.estimatedTimeRemaining || 0
    };
  }, [progress]);

  if (isLoading && !progress) {
    return (
      <Box display="flex" justifyContent="center" alignItems="center" height="400px">
        <Stack alignItems="center" spacing={2}>
          <CircularProgress size={48} />
          <Typography variant="h6">Loading migration data...</Typography>
          <Typography color="textSecondary">
            Connecting to migration {migrationId}
          </Typography>
        </Stack>
      </Box>
    );
  }

  return (
    <Box 
      sx={{
        position: isFullscreen ? 'fixed' : 'relative',
        top: isFullscreen ? 0 : 'auto',
        left: isFullscreen ? 0 : 'auto',
        right: isFullscreen ? 0 : 'auto',
        bottom: isFullscreen ? 0 : 'auto',
        zIndex: isFullscreen ? 9999 : 'auto',
        backgroundColor: isFullscreen ? theme.palette.background.default : 'transparent',
        overflow: isFullscreen ? 'auto' : 'visible',
        p: isFullscreen ? 2 : 0
      }}
    >
      {/* Header with Controls */}
      <Card sx={{ mb: 2 }}>
        <CardContent>
          <Box display="flex" justifyContent="space-between" alignItems="center">
            <Box>
              <Typography variant="h5" fontWeight="600" sx={{ fontFamily: 'monospace', fontSize: '1.1rem' }}>
                Migration {migrationId}
              </Typography>
              <Box display="flex" alignItems="center" gap={2} mt={1}>
                <Chip
                  size="small"
                  label={connectionState}
                  color={isConnected ? 'success' : 'error'}
                  icon={isConnected ? <SuccessIcon /> : <ErrorIcon />}
                />
                {lastHeartbeat && (
                  <Typography variant="caption" color="textSecondary">
                    Last update: {formatDistanceToNow(lastHeartbeat, { addSuffix: true })}
                  </Typography>
                )}
              </Box>
            </Box>

            <Stack direction="row" spacing={1}>
              <Tooltip title={notificationsEnabled ? 'Disable notifications' : 'Enable notifications'}>
                <IconButton onClick={handleNotificationsToggle}>
                  {notificationsEnabled ? <NotificationsIcon /> : <NotificationsOffIcon />}
                </IconButton>
              </Tooltip>
              
              <Tooltip title="Refresh data">
                <IconButton onClick={refresh} disabled={isLoading}>
                  <RefreshIcon />
                </IconButton>
              </Tooltip>
              
              <Tooltip title={isFullscreen ? 'Exit fullscreen' : 'Enter fullscreen'}>
                <IconButton onClick={handleFullscreenToggle}>
                  {isFullscreen ? <FullscreenExitIcon /> : <FullscreenIcon />}
                </IconButton>
              </Tooltip>

              {!isConnected && (
                <Button 
                  variant="contained" 
                  onClick={reconnect}
                  startIcon={<PlayIcon />}
                  size="small"
                >
                  Reconnect
                </Button>
              )}
            </Stack>
          </Box>
        </CardContent>
      </Card>

      {/* Main Progress Bar */}
      {progress && (
        <Box mb={3}>
          <RealTimeProgressBar
            migrationId={migrationId}
            progress={progress}
            showDetails={true}
            showPerformanceMetrics={true}
            showTimeEstimates={true}
            size="large"
          />
        </Box>
      )}

      {/* Statistics Cards */}
      {statistics && (
        <Box sx={{ display: 'flex', gap: 2, mb: 3, flexWrap: 'wrap' }}>
          <Box sx={{ flex: { xs: '1 1 45%', sm: '1 1 22%' } }}>
            <Card>
              <CardContent sx={{ textAlign: 'center' }}>
                <Typography variant="h4" fontWeight="600">
                  {statistics.processedEntities.toLocaleString()}
                </Typography>
                <Typography variant="caption" color="textSecondary">
                  Processed Entities
                </Typography>
              </CardContent>
            </Card>
          </Box>
          
          <Box sx={{ flex: { xs: '1 1 45%', sm: '1 1 22%' } }}>
            <Card>
              <CardContent sx={{ textAlign: 'center' }}>
                <Typography variant="h4" color="success.main" fontWeight="600">
                  {statistics.successRate.toFixed(1)}%
                </Typography>
                <Typography variant="caption" color="textSecondary">
                  Success Rate
                </Typography>
              </CardContent>
            </Card>
          </Box>
          
          <Box sx={{ flex: { xs: '1 1 45%', sm: '1 1 22%' } }}>
            <Card>
              <CardContent sx={{ textAlign: 'center' }}>
                <Typography variant="h4" fontWeight="600">
                  {statistics.entitiesPerSecond.toFixed(1)}
                </Typography>
                <Typography variant="caption" color="textSecondary">
                  Entities/Second
                </Typography>
              </CardContent>
            </Card>
          </Box>
          
          <Box sx={{ flex: { xs: '1 1 45%', sm: '1 1 22%' } }}>
            <Card>
              <CardContent sx={{ textAlign: 'center' }}>
                <Typography variant="h4" fontWeight="600">
                  {Math.round(statistics.elapsedTime / 60)}m
                </Typography>
                <Typography variant="caption" color="textSecondary">
                  Elapsed Time
                </Typography>
              </CardContent>
            </Card>
          </Box>
        </Box>
      )}

      {/* Main Content Grid */}
      <Grid container spacing={3}>
        {/* Entity Progress Grid */}
        <Box sx={{ flex: { xs: '1 1 100%', lg: '1 1 65%' } }}>
          <RealTimeEntityGrid
            migrationId={migrationId}
            entityProgress={Object.fromEntries(
              Object.entries(entityStatus).map(([key, value]) => [key, value.progress])
            )}
            overallProgress={progress || undefined}
            showPerformanceMetrics={true}
            showBatchDetails={true}
            onRefresh={refresh}
          />
        </Box>

        {/* Side Panel */}
        <Grid item xs={12} lg={4}>
          <Stack spacing={3}>
            {/* Performance Metrics */}
            <PerformanceMetricsCard
              metrics={performanceMetrics}
              performanceHistory={performanceHistory}
            />

            {/* Recent Events */}
            <RecentEventsCard
              events={recentEvents}
              errors={errors}
            />
          </Stack>
        </Grid>
      </Grid>

      {/* Snackbar for notifications */}
      <Snackbar
        open={snackbarOpen}
        autoHideDuration={6000}
        onClose={() => setSnackbarOpen(false)}
        anchorOrigin={{ vertical: 'bottom', horizontal: 'right' }}
      >
        <Alert 
          onClose={() => setSnackbarOpen(false)} 
          severity={snackbarSeverity}
          variant="filled"
        >
          {snackbarMessage}
        </Alert>
      </Snackbar>
    </Box>
  );
}; 