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
  LinearProgress,
  Tooltip,
  useTheme,
  alpha,
  Paper,
  Dialog,
  DialogTitle,
  DialogContent,
  DialogActions
} from '@mui/material';
import {
  Refresh as RefreshIcon,
  Fullscreen as FullscreenIcon,
  FullscreenExit as FullscreenExitIcon,
  PlayArrow as PlayIcon,
  Stop as StopIcon,
  Speed as PerformanceIcon,
  Timeline as TimelineIcon,
  Error as ErrorIcon,
  CheckCircle as SuccessIcon,
  Warning as WarningIcon,
  ViewModule as BatchIcon,
  TrendingUp as TrendingUpIcon,
  TrendingDown as TrendingDownIcon,
  TrendingFlat as TrendingFlatIcon,
  ArrowBack as ArrowBackIcon
} from '@mui/icons-material';
import { format, formatDistanceToNow } from 'date-fns';
import { useNavigate } from 'react-router-dom';

// Import our enhanced hook
import { useDetailedMigrationProgress } from '../../hooks/useDetailedMigrationProgress';
import type { DetailedMigrationProgress, ProcessingContext, BatchProgressSummary, RemainingWorkload, RealTimeMetrics } from '../../hooks/useDetailedMigrationProgress';
import { apiService } from '../../services/apiService';

interface EnhancedMigrationDashboardProps {
  migrationId: string;
  autoConnect?: boolean;
  enableNotifications?: boolean;
  onMigrationComplete?: (migrationId: string) => void;
  onMigrationError?: (migrationId: string, error: any) => void;
}

export const EnhancedMigrationDashboard: React.FC<EnhancedMigrationDashboardProps> = ({
  migrationId,
  autoConnect = true,
  enableNotifications = true,
  onMigrationComplete,
  onMigrationError
}) => {
  const theme = useTheme();
  const navigate = useNavigate();
  const [isFullscreen, setIsFullscreen] = useState(false);
  const [snackbarOpen, setSnackbarOpen] = useState(false);
  const [snackbarMessage, setSnackbarMessage] = useState('');
  const [snackbarSeverity, setSnackbarSeverity] = useState<'success' | 'error' | 'warning' | 'info'>('info');
  const [cancelDialogOpen, setCancelDialogOpen] = useState(false);
  const [isCancelling, setIsCancelling] = useState(false);

  // Use our enhanced detailed progress hook
  const {
    progress,
    isConnected,
    connectionState,
    lastHeartbeat,
    recentEvents,
    milestones,
    errors,
    isLoading,
    lastUpdated,
    connect,
    disconnect,
    reconnect,
    refresh,
    clearHistory,
    clearErrors
  } = useDetailedMigrationProgress({
    migrationId,
    autoConnect,
    enableNotifications,
    enablePerformanceTracking: true,
    pollInterval: 0 // Temporarily disable polling to test if this is causing refreshes
  });

  // Handle migration completion
  useEffect(() => {
    if (progress?.status === 'completed' && onMigrationComplete) {
      onMigrationComplete(migrationId);
      setSnackbarMessage('🎉 Migration completed successfully!');
      setSnackbarSeverity('success');
      setSnackbarOpen(true);
    }
  }, [progress?.status, migrationId, onMigrationComplete]);

  // Handle migration errors
  useEffect(() => {
    if (progress?.status === 'failed' && onMigrationError) {
      onMigrationError(migrationId, { status: 'failed' });
      setSnackbarMessage('❌ Migration failed. Check error details.');
      setSnackbarSeverity('error');
      setSnackbarOpen(true);
    }
  }, [progress?.status, migrationId, onMigrationError]);

  // Show milestone notifications
  useEffect(() => {
    if (milestones.length > 0) {
      const latestMilestone = milestones[0];
      setSnackbarMessage(`🎯 ${latestMilestone.milestone}% Complete! ${latestMilestone.message}`);
      setSnackbarSeverity('info');
      setSnackbarOpen(true);
    }
  }, [milestones]);

  // Toggle fullscreen mode
  const handleFullscreenToggle = () => {
    setIsFullscreen(!isFullscreen);
  };

  // Handle cancel migration
  const handleCancelClick = () => {
    setCancelDialogOpen(true);
  };

  const handleCancelConfirm = async () => {
    setIsCancelling(true);
    try {
      // Cancel the migration via API
      await apiService.cancelMigration(migrationId);
      
      // Leave SignalR group to stop receiving real-time updates
      console.log('🚪 Leaving SignalR group for cancelled migration:', migrationId);
      disconnect();
      
      setSnackbarMessage('🛑 Migration cancelled successfully!');
      setSnackbarSeverity('warning');
      setSnackbarOpen(true);
      setCancelDialogOpen(false);
      
      // Navigate to history page to see the cancelled migration
      console.log('📄 Navigating to history page...');
      navigate('/history');
      
    } catch (error) {
      setSnackbarMessage(`❌ Failed to cancel migration: ${error instanceof Error ? error.message : 'Unknown error'}`);
      setSnackbarSeverity('error');
      setSnackbarOpen(true);
    } finally {
      setIsCancelling(false);
    }
  };

  const handleCancelDialogClose = () => {
    if (!isCancelling) {
      setCancelDialogOpen(false);
    }
  };

  // Calculate display values
  const displayValues = useMemo(() => {
    if (!progress) return null;

    return {
      overallProgress: progress.overallProgressPercentage || 0,
      currentEntity: progress.currentProcessing?.currentEntity || progress.currentEntity || 'Unknown',
      currentBatch: progress.currentProcessing?.currentBatchNumber || 0,
      currentActivity: progress.currentProcessing?.currentActivity || 'Initializing...',
      totalBatches: progress.batchProgress?.totalBatches || 0,
      completedBatches: progress.batchProgress?.completedBatches || 0,
      remainingBatches: progress.remainingWork?.remainingBatches || 0,
      remainingEntities: progress.remainingWork?.remainingEntities || 0,
      currentSpeed: progress.performance?.currentProcessingSpeed || progress.entitiesPerSecond || 0,
      averageSpeed: progress.performance?.averageProcessingSpeed || 0,
      performanceTrend: progress.performance?.performanceTrend || 'stable',
      batchProgress: progress.currentProcessing?.currentBatch?.batchProgressPercentage || 0,
      batchSize: progress.currentProcessing?.currentBatch?.batchSize || 0,
      processedInBatch: progress.currentProcessing?.currentBatch?.processedInBatch || 0,
      batchSpeed: progress.currentProcessing?.currentBatch?.batchProcessingSpeed || 0,
      estimatedCompletion: progress.remainingWork?.estimatedTimeRemaining || 0
    };
  }, [progress]);

  if (isLoading && !progress) {
    return (
      <Box display="flex" justifyContent="center" alignItems="center" height="400px">
        <Stack alignItems="center" spacing={2}>
          <CircularProgress size={48} />
          <Typography variant="h6">Loading enhanced migration data...</Typography>
          <Typography color="textSecondary">
            Connecting to real-time updates for migration {migrationId}
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
      {/* Top-level page header */}
      <Box display="flex" justifyContent="space-between" alignItems="center" sx={{ mb: 2 }}>
        <Box>
          <Typography variant="h4" fontWeight="600">
            Enhanced Migration Dashboard
          </Typography>
          <Typography variant="body1" color="textSecondary">
            Advanced real-time monitoring with batch-level tracking and performance analytics.
          </Typography>
        </Box>
        <Tooltip title="Back to Migration Overview">
          <Button
            variant="outlined"
            size="medium"
            startIcon={<ArrowBackIcon />}
            onClick={() => navigate('/')}
            sx={{ 
              borderColor: 'primary.main',
              color: 'primary.main',
              '&:hover': {
                backgroundColor: theme => alpha(theme.palette.primary.main, 0.04)
              }
            }}
          >
            Back To Overview
          </Button>
        </Tooltip>
      </Box>

      {/* Header with Enhanced Controls */}
      <Card sx={{ mb: 2 }}>
        <CardContent>
          <Box display="flex" justifyContent="space-between" alignItems="center">
            <Box>
              <Typography variant="subtitle1" color="textSecondary" sx={{ fontFamily: 'monospace', fontSize: '0.9rem' }}>
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
                <Typography variant="caption" color="textSecondary">
                  Events: {recentEvents.length} | Milestones: {milestones.length}
                </Typography>
              </Box>
            </Box>



            <Stack direction="row" spacing={1}>
              <Tooltip title="Refresh data">
                <IconButton onClick={refresh} disabled={isLoading}>
                  <RefreshIcon />
                </IconButton>
              </Tooltip>

              {/* Cancel button - show for active migrations (immediate) and running migrations (after data loads) */}
              {(() => {
                console.log('🔍 Enhanced Dashboard - Progress status:', progress?.status, 'MigrationId:', migrationId, 'IsLoading:', isLoading);
                
                // Don't show cancel button for completed, failed, or cancelled migrations
                const isCompleted = progress?.status === 'completed' || progress?.status === 'failed' || progress?.status === 'cancelled';
                if (isCompleted) {
                  console.log('🚫 Enhanced Dashboard - Migration is completed/failed/cancelled - hiding cancel button');
                  return false;
                }
                
                // Show cancel button if:
                // 1. We have a migrationId (indicating this is an active migration) OR
                // 2. Progress data shows running/progress status
                const hasActiveMigration = !!migrationId;
                const hasRunningStatus = progress?.status === 'running' || progress?.status === 'in_progress' || progress?.status === 'inprogress' || progress?.status?.toLowerCase().includes('progress');
                
                return hasActiveMigration || hasRunningStatus;
              })() ? (
                <Tooltip title="Cancel migration">
                  <Button
                    variant="outlined"
                    color="error"
                    size="small"
                    startIcon={<StopIcon />}
                    onClick={handleCancelClick}
                    disabled={isCancelling}
                  >
                    {isCancelling ? 'Cancelling...' : 'Cancel'}
                  </Button>
                </Tooltip>
              ) : null}
              
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

      {displayValues ? (
        <>
          {/* Main Progress Bar with Enhanced Details */}
          <Card sx={{ mb: 3 }}>
            <CardContent>
              <Typography variant="h6" gutterBottom>
                Overall Migration Progress
              </Typography>
              
              <Box position="relative" mb={2}>
                <LinearProgress
                  variant="determinate"
                  value={displayValues.overallProgress}
                  sx={{
                    height: 12,
                    borderRadius: 6,
                    backgroundColor: alpha(theme.palette.primary.main, 0.1),
                    '& .MuiLinearProgress-bar': {
                      borderRadius: 6,
                      background: `linear-gradient(90deg, ${theme.palette.primary.main} 0%, ${alpha(theme.palette.primary.main, 0.8)} 100%)`
                    }
                  }}
                />
                
                <Typography
                  variant="body2"
                  sx={{
                    position: 'absolute',
                    top: '50%',
                    left: '50%',
                    transform: 'translate(-50%, -50%)',
                    fontWeight: 600,
                    color: displayValues.overallProgress > 50 ? 'white' : theme.palette.primary.main,
                    textShadow: displayValues.overallProgress > 50 ? '0 1px 2px rgba(0,0,0,0.3)' : 'none'
                  }}
                >
                  {displayValues.overallProgress.toFixed(1)}%
                </Typography>
              </Box>

              <Grid container spacing={2}>
                <Grid item xs={12} md={6}>
                  <Typography variant="body2" color="textSecondary">
                    <strong>Total Progress:</strong> {progress?.processedEntities || 0} / {progress?.totalEntities || 0} entities
                  </Typography>
                  <Typography variant="body2" color="textSecondary">
                    <strong>Success Rate:</strong> {progress?.processedEntities ? ((progress.successfulEntities / progress.processedEntities) * 100).toFixed(1) : 0}%
                  </Typography>
                </Grid>
                <Grid item xs={12} md={6}>
                  <Typography variant="body2" color="textSecondary">
                    <strong>Elapsed Time:</strong> {Math.floor((progress?.elapsedTime || 0) / 60)}m {Math.floor((progress?.elapsedTime || 0) % 60)}s
                  </Typography>
                  <Typography variant="body2" color="textSecondary">
                    <strong>Estimated Remaining:</strong> {Math.floor(displayValues.estimatedCompletion / 60)}m {Math.floor(displayValues.estimatedCompletion % 60)}s
                  </Typography>
                </Grid>
              </Grid>
            </CardContent>
          </Card>

          {/* Current Processing Context */}
          <Card sx={{ mb: 3 }}>
            <CardContent>
              <Typography variant="h6" gutterBottom>
                <BatchIcon sx={{ mr: 1, verticalAlign: 'middle' }} />
                Current Processing Status
              </Typography>
              
              <Grid container spacing={3}>
                <Grid item xs={12} md={6}>
                  <Paper sx={{ p: 2, bgcolor: alpha(theme.palette.primary.main, 0.05) }}>
                    <Typography variant="subtitle2" gutterBottom>
                      Current Entity
                    </Typography>
                    <Typography variant="h6" color="primary">
                      {displayValues.currentEntity}
                    </Typography>
                    <Typography variant="body2" color="textSecondary">
                      {displayValues.currentActivity}
                    </Typography>
                  </Paper>
                </Grid>
                
                <Grid item xs={12} md={6}>
                  <Paper sx={{ p: 2, bgcolor: alpha(theme.palette.info.main, 0.05) }}>
                    <Typography variant="subtitle2" gutterBottom>
                      Current Batch Progress
                    </Typography>
                    <Typography variant="h6" color="info.main">
                      Batch {displayValues.currentBatch}
                    </Typography>
                    <Box display="flex" alignItems="center" gap={1}>
                      <LinearProgress
                        variant="determinate"
                        value={displayValues.batchProgress}
                        sx={{ flexGrow: 1, height: 6, borderRadius: 3 }}
                      />
                      <Typography variant="caption">
                        {displayValues.batchProgress.toFixed(1)}%
                      </Typography>
                    </Box>
                    <Typography variant="body2" color="textSecondary">
                      {displayValues.processedInBatch}/{displayValues.batchSize} entities
                    </Typography>
                  </Paper>
                </Grid>
              </Grid>
            </CardContent>
          </Card>

          {/* Batch Summary and Remaining Work */}
          <Grid container spacing={3} sx={{ mb: 3 }}>
            <Grid item xs={12} md={6}>
              <Card>
                <CardContent>
                  <Typography variant="h6" gutterBottom>
                    Batch Summary
                  </Typography>
                  
                  <Stack spacing={2}>
                    <Box display="flex" justifyContent="space-between">
                      <Typography variant="body2" color="textSecondary">
                        Total Batches:
                      </Typography>
                      <Typography variant="body2" fontWeight="600">
                        {displayValues.totalBatches}
                      </Typography>
                    </Box>
                    
                    <Box display="flex" justifyContent="space-between">
                      <Typography variant="body2" color="textSecondary">
                        Completed Batches:
                      </Typography>
                      <Typography variant="body2" fontWeight="600" color="success.main">
                        {displayValues.completedBatches}
                      </Typography>
                    </Box>
                    
                    <Box display="flex" justifyContent="space-between">
                      <Typography variant="body2" color="textSecondary">
                        Remaining Batches:
                      </Typography>
                      <Typography variant="body2" fontWeight="600" color="warning.main">
                        {displayValues.remainingBatches}
                      </Typography>
                    </Box>
                    
                    <Divider />
                    
                    <Box>
                      <Typography variant="subtitle2" gutterBottom>
                        Batch Completion Rate
                      </Typography>
                      <LinearProgress
                        variant="determinate"
                        value={displayValues.totalBatches > 0 ? (displayValues.completedBatches / displayValues.totalBatches) * 100 : 0}
                        sx={{ height: 8, borderRadius: 4 }}
                      />
                      <Typography variant="caption" color="textSecondary">
                        {displayValues.totalBatches > 0 ? ((displayValues.completedBatches / displayValues.totalBatches) * 100).toFixed(1) : 0}% of batches completed
                      </Typography>
                    </Box>
                  </Stack>
                </CardContent>
              </Card>
            </Grid>
            
            <Grid item xs={12} md={6}>
              <Card>
                <CardContent>
                  <Typography variant="h6" gutterBottom>
                    Remaining Work
                  </Typography>
                  
                  <Stack spacing={2}>
                    <Box display="flex" justifyContent="space-between">
                      <Typography variant="body2" color="textSecondary">
                        Remaining Entities:
                      </Typography>
                      <Typography variant="body2" fontWeight="600" color="warning.main">
                        {displayValues.remainingEntities.toLocaleString()}
                      </Typography>
                    </Box>
                    
                    <Box display="flex" justifyContent="space-between">
                      <Typography variant="body2" color="textSecondary">
                        Remaining Batches:
                      </Typography>
                      <Typography variant="body2" fontWeight="600" color="warning.main">
                        {displayValues.remainingBatches}
                      </Typography>
                    </Box>
                    
                    <Box display="flex" justifyContent="space-between">
                      <Typography variant="body2" color="textSecondary">
                        Estimated Time:
                      </Typography>
                      <Typography variant="body2" fontWeight="600" color="info.main">
                        {Math.floor(displayValues.estimatedCompletion / 3600)}h {Math.floor((displayValues.estimatedCompletion % 3600) / 60)}m
                      </Typography>
                    </Box>
                    
                    <Divider />
                    
                    <Box>
                      <Typography variant="subtitle2" gutterBottom>
                        Completion Estimate
                      </Typography>
                      <Typography variant="body2" color="textSecondary">
                        At current speed ({displayValues.currentSpeed.toFixed(1)} entities/sec)
                      </Typography>
                      <Typography variant="caption" color="textSecondary">
                        Expected completion: {format(new Date(Date.now() + displayValues.estimatedCompletion * 1000), 'PPpp')}
                      </Typography>
                    </Box>
                  </Stack>
                </CardContent>
              </Card>
            </Grid>
          </Grid>

          {/* Performance Metrics */}
          <Card sx={{ mb: 3 }}>
            <CardContent>
              <Typography variant="h6" gutterBottom>
                <PerformanceIcon sx={{ mr: 1, verticalAlign: 'middle' }} />
                Real-Time Performance
              </Typography>
              
              <Grid container spacing={3}>
                <Grid item xs={12} md={3}>
                  <Box textAlign="center">
                    <Typography variant="h4" color="primary" fontWeight="600">
                      {displayValues.currentSpeed.toFixed(1)}
                    </Typography>
                    <Typography variant="caption" color="textSecondary">
                      Current Speed (entities/sec)
                    </Typography>
                  </Box>
                </Grid>
                
                <Grid item xs={12} md={3}>
                  <Box textAlign="center">
                    <Typography variant="h4" color="info.main" fontWeight="600">
                      {displayValues.averageSpeed.toFixed(1)}
                    </Typography>
                    <Typography variant="caption" color="textSecondary">
                      Average Speed (entities/sec)
                    </Typography>
                  </Box>
                </Grid>
                
                <Grid item xs={12} md={3}>
                  <Box textAlign="center">
                    <Typography variant="h4" color="success.main" fontWeight="600">
                      {displayValues.batchSpeed.toFixed(1)}
                    </Typography>
                    <Typography variant="caption" color="textSecondary">
                      Current Batch Speed
                    </Typography>
                  </Box>
                </Grid>
                
                <Grid item xs={12} md={3}>
                  <Box textAlign="center" display="flex" flexDirection="column" alignItems="center">
                    <Box display="flex" alignItems="center" gap={1}>
                      {displayValues.performanceTrend === 'improving' && <TrendingUpIcon color="success" />}
                      {displayValues.performanceTrend === 'declining' && <TrendingDownIcon color="error" />}
                      {displayValues.performanceTrend === 'stable' && <TrendingFlatIcon color="info" />}
                      <Typography 
                        variant="h6" 
                        color={
                          displayValues.performanceTrend === 'improving' ? 'success.main' :
                          displayValues.performanceTrend === 'declining' ? 'error.main' : 'info.main'
                        }
                        sx={{ textTransform: 'capitalize' }}
                      >
                        {displayValues.performanceTrend}
                      </Typography>
                    </Box>
                    <Typography variant="caption" color="textSecondary">
                      Performance Trend
                    </Typography>
                  </Box>
                </Grid>
              </Grid>
            </CardContent>
          </Card>

          {/* Recent Events */}
          <Card>
            <CardContent>
              <Typography variant="h6" gutterBottom>
                <TimelineIcon sx={{ mr: 1, verticalAlign: 'middle' }} />
                Recent Events
              </Typography>
              
              <Box maxHeight={300} overflow="auto">
                {recentEvents.length > 0 ? (
                  <Stack spacing={1}>
                    {recentEvents.slice(0, 20).map((event) => (
                      <Box 
                        key={event.id}
                        display="flex" 
                        justifyContent="space-between" 
                        alignItems="center"
                        sx={{ 
                          p: 1, 
                          bgcolor: alpha(theme.palette.primary.main, 0.02),
                          borderRadius: 1,
                          borderLeft: `3px solid ${
                            event.type === 'MigrationMilestone' ? theme.palette.success.main :
                            event.type === 'BatchCompleted' ? theme.palette.info.main :
                            event.type === 'BatchStarted' ? theme.palette.warning.main :
                            theme.palette.grey[400]
                          }`
                        }}
                      >
                        <Typography variant="body2">
                          {event.message}
                        </Typography>
                        <Typography variant="caption" color="textSecondary">
                          {formatDistanceToNow(event.timestamp, { addSuffix: true })}
                        </Typography>
                      </Box>
                    ))}
                  </Stack>
                ) : (
                  <Typography color="textSecondary" textAlign="center" py={2}>
                    No recent events
                  </Typography>
                )}
              </Box>
            </CardContent>
          </Card>
        </>
      ) : (
        <Box display="flex" justifyContent="center" alignItems="center" height="400px">
          <Stack alignItems="center" spacing={2}>
            <Typography variant="h6">Enhanced Migration Data Unavailable</Typography>
            <Typography color="textSecondary">
              Showing basic migration info. Real-time updates might be delayed or unavailable.
            </Typography>
            <Typography variant="body2" color="textSecondary">
              Total Progress: {progress?.processedEntities || 0} / {progress?.totalEntities || 0} entities
            </Typography>
            <Typography variant="body2" color="textSecondary">
              Elapsed Time: {Math.floor((progress?.elapsedTime || 0) / 60)}m {Math.floor((progress?.elapsedTime || 0) % 60)}s
            </Typography>
            <Typography variant="body2" color="textSecondary">
              Estimated Remaining: {Math.floor(progress?.remainingWork?.estimatedTimeRemaining || 0) / 60}m {Math.floor(progress?.remainingWork?.estimatedTimeRemaining || 0) % 60}s
            </Typography>
          </Stack>
        </Box>
      )}

      {/* Cancel Confirmation Dialog */}
      <Dialog
        open={cancelDialogOpen}
        onClose={handleCancelDialogClose}
        maxWidth="sm"
        fullWidth
      >
        <DialogTitle>
          <Box display="flex" alignItems="center" gap={1}>
            <StopIcon color="error" />
            Cancel Migration
          </Box>
        </DialogTitle>
        <DialogContent>
          <Typography gutterBottom>
            Are you sure you want to cancel this migration?
          </Typography>
          <Typography variant="body2" color="textSecondary" gutterBottom>
            <strong>Migration ID:</strong> {migrationId}
          </Typography>
          <Typography variant="body2" color="textSecondary" gutterBottom>
            <strong>Current Progress:</strong> {displayValues?.overallProgress?.toFixed(1) || 0}% ({progress?.processedEntities || 0}/{progress?.totalEntities || 0} entities)
          </Typography>
          <Alert severity="warning" sx={{ mt: 2 }}>
            <Typography variant="body2">
              <strong>This action cannot be undone.</strong> The migration will stop gracefully after completing the current batch, 
              but all progress will be lost and you'll need to restart the migration from the beginning.
            </Typography>
          </Alert>
        </DialogContent>
        <DialogActions>
          <Button 
            onClick={handleCancelDialogClose} 
            disabled={isCancelling}
          >
            Keep Running
          </Button>
          <Button 
            onClick={handleCancelConfirm} 
            color="error" 
            variant="contained"
            disabled={isCancelling}
            startIcon={isCancelling ? <CircularProgress size={16} /> : <StopIcon />}
          >
            {isCancelling ? 'Cancelling...' : 'Cancel Migration'}
          </Button>
        </DialogActions>
      </Dialog>

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
          sx={{ width: '100%' }}
        >
          {snackbarMessage}
        </Alert>
      </Snackbar>
    </Box>
  );
}; 