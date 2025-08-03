import React, { useState, useEffect } from 'react';
import {
  Box,
  Card,
  CardContent,
  Typography,
  Chip,
  Alert,
  Button,
  Stack,
  Collapse,
  IconButton,
  Tooltip,

} from '@mui/material';
import {
  Refresh as RefreshIcon,
  Error as ErrorIcon,
  CheckCircle as SuccessIcon,
  PlayArrow as StartIcon,
  Stop as StopIcon
} from '@mui/icons-material';
import { CircularProgress } from '@mui/material';
import { useDashboard } from '../../context/DashboardContext';
import { formatDate, formatRelativeTime } from '../../utils/dateUtils';
import { useNavigate, useLocation } from 'react-router-dom';
import { apiService } from '../../services/apiService';
import { getSignalRService } from '../../services/signalRService';
import { notificationService } from '../../services/notificationService';
import { LiveCancellationDialog } from './LiveCancellationDialog';
import type { LiveCancellationRequest } from '../../types';
import { CancellationScope } from '../../types';

/**
 * MigrationOverview Component
 * 
 * Main dashboard view that displays active migrations with real-time status indicators.
 * This component implements several key features for optimal user experience:
 * 
 * Key Features:
 * - Real-time connection status for each migration
 * - Loading states and success indicators for group joining
 * - Enhanced navigation with guaranteed group joining
 * - Comprehensive error handling and user feedback
 * - Debug information controlled from top navigation
 */
export const MigrationOverview: React.FC = () => {
  const navigate = useNavigate();
  const location = useLocation();
  const { state, refreshData, addError, clearErrors, joinMigrationGroup, removeMigration, addCancelledMigration, removeCancelledMigration } = useDashboard();
  const [isRefreshing, setIsRefreshing] = useState(false);
  const [joiningGroups, setJoiningGroups] = useState<Set<string>>(new Set());
  const [joinedGroups, setJoinedGroups] = useState<Set<string>>(new Set());
  const [showDebugInfo, setShowDebugInfo] = useState(false);
  
  // Live cancellation state (Task 7.6)
  const [liveCancelDialogOpen, setLiveCancelDialogOpen] = useState(false);
  const [selectedMigrationForCancel, setSelectedMigrationForCancel] = useState<string | null>(null);
  const [isCancelling, setIsCancelling] = useState(false);

  // Listen for debug toggle event from top navigation
  useEffect(() => {
    const handleToggleDebug = () => {
      setShowDebugInfo(prev => !prev);
    };

    window.addEventListener('toggleDebugInfo', handleToggleDebug);
    
    return () => {
      console.log('🧹 Cleaning up MigrationOverview component...');
      setJoiningGroups(new Set());
      setJoinedGroups(new Set());
      window.removeEventListener('toggleDebugInfo', handleToggleDebug);
    };
  }, []);

  // Auto-refresh data when navigating to Home page or when window gains focus
  useEffect(() => {
    console.log('🏠 MigrationOverview: Page visited/focused, refreshing data...');
    
    const handleRefreshData = async () => {
      try {
        await refreshData();
        console.log('✅ MigrationOverview: Auto-refresh completed');
      } catch (error) {
        console.error('❌ MigrationOverview: Auto-refresh failed:', error);
      }
    };

    // Refresh immediately when component mounts or location changes
    handleRefreshData();

    // Also refresh when window gains focus (user comes back to tab)
    const handleWindowFocus = () => {
      console.log('👁️ MigrationOverview: Window focused, refreshing data...');
      handleRefreshData();
    };

    window.addEventListener('focus', handleWindowFocus);

    return () => {
      window.removeEventListener('focus', handleWindowFocus);
    };
  }, [location.pathname]); // Only depend on pathname, not refreshData to avoid infinite loop
  // eslint-disable-next-line react-hooks/exhaustive-deps

  const handleRefresh = async () => {
    setIsRefreshing(true);
    try {
      await refreshData();
    } catch (error) {
      addError({
        code: 'REFRESH_ERROR',
        message: 'Failed to refresh data',
        details: error instanceof Error ? error.message : 'Unknown error',
        timestamp: new Date()
      });
    } finally {
      setIsRefreshing(false);
    }
  };

  const handleStartMigration = () => {
    navigate('/start');
  };

  /**
   * Enhanced navigation handler that ensures SignalR group is joined
   */
  const handleNavigateToMigrationDetail = async (migrationId: string) => {
    try {
      // Check if SignalR is connected
      if (!signalRConnection.isConnected) {
        console.warn('SignalR not connected - navigating without group join');
        navigate(`/enhanced?migrationId=${migrationId}`);
        return;
      }

      // Check if already joined
      if (joinedGroups.has(migrationId)) {
        console.log(`✅ Group already joined for migration: ${migrationId}`);
        navigate(`/enhanced?migrationId=${migrationId}`);
        return;
      }

      // Set loading state
      setJoiningGroups(prev => new Set(prev).add(migrationId));
      console.log(`🔗 Ensuring SignalR group is joined for migration: ${migrationId}`);
      
      // Use the dashboard context to join the group
      await joinMigrationGroup(migrationId);
      
      console.log(`✅ SignalR group confirmed joined for migration: ${migrationId}`);
      
      // Update success state
      setJoinedGroups(prev => new Set(prev).add(migrationId));
      
      // Navigate to the detail view
      navigate(`/enhanced?migrationId=${migrationId}`);
      
    } catch (error) {
      console.error(`❌ Failed to join SignalR group for migration ${migrationId}:`, error);
      
      // Still navigate even if group join fails (fallback to polling)
      addError({
        code: 'GROUP_JOIN_FAILED',
        message: `Failed to join real-time updates for migration ${migrationId.slice(-8)}`,
        details: 'You will still see migration progress, but updates may be delayed.',
        timestamp: new Date()
      });
      
      navigate(`/enhanced?migrationId=${migrationId}`);
    } finally {
      // Clear loading state
      setJoiningGroups(prev => {
        const newSet = new Set(prev);
        newSet.delete(migrationId);
        return newSet;
      });
    }
  };

  // Live cancellation handlers (Task 7.6)
  const handleCancelClick = (migrationId: string) => {
    setSelectedMigrationForCancel(migrationId);
    setLiveCancelDialogOpen(true);
  };

  const handleLiveCancelDialogClose = () => {
    setLiveCancelDialogOpen(false);
    setSelectedMigrationForCancel(null);
  };

  const handleLiveCancelConfirm = async (request: LiveCancellationRequest) => {
    if (!selectedMigrationForCancel) return;

    setIsCancelling(true);
    try {
      // Clear any previous error messages
      clearErrors();

      // Use new live cancellation API with enhanced logging
      console.log('🚫 Live cancelling migration via API:', request);
      const cancelResponse = await apiService.liveCancelMigration(request);
      console.log('✅ Live cancellation API response:', cancelResponse);

      // Check if the API response indicates successful cancellation
      if (cancelResponse?.status === 'cancelled' || cancelResponse?.message?.includes('cancelled successfully')) {
        console.log('✅ Backend confirmed live cancellation was successful');
        
        // Leave SignalR group to stop receiving real-time updates for this migration
        console.log('🚪 Leaving SignalR group for cancelled migration:', selectedMigrationForCancel);
        const signalRService = getSignalRService();
        if (signalRService.isConnected()) {
          await signalRService.leaveMigrationGroup(selectedMigrationForCancel);
        }

        // Directly remove the migration from local active state (don't refresh as backend queue may not have processed yet)
        removeMigration(selectedMigrationForCancel);
        
        // Clear from cancelled list after 10 minutes (give backend queue time to process)
        setTimeout(() => {
          removeCancelledMigration(selectedMigrationForCancel);
          console.log('🧹 Removed migration from cancelled list after timeout:', selectedMigrationForCancel);
        }, 10 * 60 * 1000); // 10 minutes
        
        // Show enhanced success notification with scope information
        const scopeText = request.scope === CancellationScope.Migration ? 'entire migration' : 
                         `${request.scope.toLowerCase()} level`;
        notificationService.success(
          'Live Cancellation Successful', 
          `${scopeText} for migration ${selectedMigrationForCancel.slice(-8)} cancelled successfully!`
        );
      } else {
        // API response doesn't confirm successful cancellation
        console.error('❌ Live cancellation API did not confirm successful cancellation:', cancelResponse);
        notificationService.error('Live Cancellation Failed', 'The backend did not confirm successful cancellation. Please try again.');
        
        setIsCancelling(false);
        setLiveCancelDialogOpen(false);
        setSelectedMigrationForCancel(null);
        return;
      }
      

      console.log('Migration cancelled successfully and removed from active list');
    } catch (err) {
      console.error('Failed to cancel migration:', err);
      addError({
        code: 'CANCEL_MIGRATION_FAILED',
        message: `Failed to cancel migration ${selectedMigrationForCancel.slice(-8)}`,
        details: err instanceof Error ? err.message : 'Unknown error',
        timestamp: new Date()
      });
    } finally {
      setIsCancelling(false);
      setLiveCancelDialogOpen(false);
      setSelectedMigrationForCancel(null);
    }
  };

  const {
    activeMigrations,
    systemHealth,
    signalRConnection,
    apiConnected,
    isLoading,
    errors,
    isPollingEnabled,
    lastPollingUpdate
  } = state;

  // Get active migrations array (cancelled migrations are already filtered at DashboardContext level)
  const activeMigrationsArray = Array.from(activeMigrations.values());

  return (
    <Box sx={{ flexGrow: 1, p: 3 }}>
      {/* Header Section */}
      <Box sx={{ mb: 3, display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
        <Typography variant="h4" component="h1" gutterBottom>
          Migration Overview
        </Typography>
        <Button
          variant="outlined"
          startIcon={<RefreshIcon />}
          onClick={handleRefresh}
          disabled={isRefreshing}
        >
          {isRefreshing ? 'Refreshing...' : 'Refresh'}
        </Button>
      </Box>

      {/* Debug Information (Collapsible) - Controlled from top navigation */}
      <Collapse in={showDebugInfo}>
        <Box sx={{ mb: 3 }}>
          <Card>
            <CardContent>
              <Typography variant="h6" gutterBottom>
                🔍 SignalR Connection Debug Info
              </Typography>
              <Stack direction="row" spacing={2} sx={{ mb: 2 }}>
                <Chip
                  icon={signalRConnection.isConnected ? <SuccessIcon /> : <ErrorIcon />}
                  label={`SignalR: ${signalRConnection.isConnected ? 'Connected' : 'Disconnected'}`}
                  color={signalRConnection.isConnected ? 'success' : 'error'}
                  variant="outlined"
                />
                <Chip
                  icon={apiConnected ? <SuccessIcon /> : <ErrorIcon />}
                  label={`API: ${apiConnected ? 'Connected' : 'Disconnected'}`}
                  color={apiConnected ? 'success' : 'error'}
                  variant="outlined"
                />
                {isPollingEnabled && (
                  <Chip
                    icon={<CircularProgress size={16} />}
                    label="Polling Active"
                    color="info"
                    variant="outlined"
                  />
                )}
              </Stack>
              
              <Box sx={{ display: 'grid', gridTemplateColumns: 'repeat(2, 1fr)', gap: 2, mt: 2 }}>
                <Box>
                  <Typography variant="caption" color="text.secondary">
                    Connection State:
                  </Typography>
                  <Typography variant="body2">
                    {signalRConnection.connectionState || 'Unknown'}
                  </Typography>
                </Box>
                {signalRConnection.connectionId && (
                  <Box>
                    <Typography variant="caption" color="text.secondary">
                      Connection ID:
                    </Typography>
                    <Typography variant="body2" sx={{ fontFamily: 'monospace' }}>
                      {signalRConnection.connectionId.slice(-8)}...
                    </Typography>
                  </Box>
                )}
                {lastPollingUpdate && (
                  <Box>
                    <Typography variant="caption" color="text.secondary">
                      Last Polling Update:
                    </Typography>
                    <Typography variant="body2">
                      {formatRelativeTime(lastPollingUpdate)}
                    </Typography>
                  </Box>
                )}
                <Box>
                  <Typography variant="caption" color="text.secondary">
                    Active Migrations:
                  </Typography>
                  <Typography variant="body2">
                    {activeMigrationsArray.length}
                  </Typography>
                </Box>
              </Box>
              
              <Typography variant="caption" color="text.secondary" sx={{ mt: 2, display: 'block' }}>
                💡 Check browser console (F12) for detailed SignalR logs
              </Typography>
            </CardContent>
          </Card>
        </Box>
      </Collapse>

      {/* Messages/Errors */}
      {errors.length > 0 && (
        <Box sx={{ mb: 3 }}>
          <Typography variant="h6" gutterBottom>
            Recent Messages
          </Typography>
          {errors.map((error, index) => (
            <Alert 
              key={index} 
              severity={error.code.includes('SUCCESS') ? 'success' : 'error'} 
              sx={{ mb: 1 }}
            >
              <Typography variant="subtitle2">{error.message}</Typography>
              {error.details && (
                <Typography variant="body2">{error.details}</Typography>
              )}
            </Alert>
          ))}
        </Box>
      )}

      {/* Active Migrations */}
      <Box sx={{ mb: 3 }}>
        <Card>
          <CardContent>
            <Typography variant="h6" gutterBottom>
              Active Migrations ({activeMigrationsArray.length})
            </Typography>
            
            {isLoading && activeMigrationsArray.length === 0 ? (
              <Typography>Loading migrations...</Typography>
            ) : activeMigrationsArray.length === 0 ? (
              <Alert severity="info">
                <Typography variant="body1" gutterBottom>
                  <strong>No active migrations found.</strong>
                </Typography>
                <Typography variant="body2" paragraph>
                  To see real-time migration progress tracking, you need to start a migration first.
                </Typography>
                <Stack direction="row" spacing={2} alignItems="center">
                  <Button 
                    variant="contained" 
                    size="small" 
                    onClick={handleStartMigration}
                  >
                    Start Migration
                  </Button>
                  <Typography variant="caption" color="text.secondary">
                    This will open the migration configuration form
                  </Typography>
                </Stack>
              </Alert>
            ) : (
              <Box>
                {activeMigrationsArray.map((migration) => (
                  <Card key={migration.migrationId} variant="outlined" sx={{ mb: 2 }}>
                    <CardContent>
                      <Box display="flex" justifyContent="space-between" alignItems="center">
                        <Box>
                          <Box display="flex" alignItems="center" gap={1} sx={{ mb: 1 }}>
                            <Typography variant="subtitle1">
                              Migration: {migration.migrationId}
                            </Typography>
                            {/* Live Updates Status Indicator */}
                            {joiningGroups.has(migration.migrationId) ? (
                              <Chip
                                size="small"
                                icon={<CircularProgress size={16} />}
                                label="Joining..."
                                color="info"
                                variant="outlined"
                                sx={{ 
                                  fontSize: '0.75rem',
                                  height: '24px',
                                  '& .MuiChip-icon': { fontSize: '16px' }
                                }}
                              />
                            ) : joinedGroups.has(migration.migrationId) ? (
                              <Chip
                                size="small"
                                icon={<SuccessIcon />}
                                label="Connected"
                                color="success"
                                variant="outlined"
                                sx={{ 
                                  fontSize: '0.75rem',
                                  height: '24px',
                                  '& .MuiChip-icon': { fontSize: '16px' }
                                }}
                              />
                            ) : (
                              <Chip
                                size="small"
                                icon={signalRConnection.isConnected ? <SuccessIcon /> : <ErrorIcon />}
                                label={signalRConnection.isConnected ? 'Live Updates' : 'No Live Updates'}
                                color={signalRConnection.isConnected ? 'success' : 'warning'}
                                variant="outlined"
                                sx={{ 
                                  fontSize: '0.75rem',
                                  height: '24px',
                                  '& .MuiChip-icon': { fontSize: '16px' }
                                }}
                              />
                            )}
                          </Box>
                          <Typography variant="body2" color="text.secondary" gutterBottom>
                            Status: {migration.status}
                          </Typography>
                          <Typography variant="caption">
                            {(migration.processedEntities ?? 0)} / {(migration.totalEntities ?? 0)} entities
                          </Typography>
                        </Box>
                        <Stack direction="row" spacing={1} alignItems="center">
                          {/* Only show View Dashboard button for active migrations */}
                          {!['completed', 'failed', 'cancelled'].includes(migration.status) && (
                            <Button
                              variant="contained"
                              size="small"
                              onClick={() => handleNavigateToMigrationDetail(migration.migrationId)}
                              disabled={joiningGroups.has(migration.migrationId)}
                            >
                              {joiningGroups.has(migration.migrationId) ? 'Joining...' : 'View Dashboard'}
                            </Button>
                          )}
                          
                          {/* Show status for completed migrations */}
                          {['completed', 'failed', 'cancelled'].includes(migration.status) && (
                            <Chip
                              size="small"
                              label={`Migration ${migration.status}`}
                              color={migration.status === 'completed' ? 'success' : 'error'}
                              variant="outlined"
                              sx={{ 
                                fontSize: '0.75rem',
                                height: '24px',
                                textTransform: 'capitalize'
                              }}
                            />
                          )}
                          {/* Cancel button - show for active migrations (immediate) and running migrations */}
                          {(() => {
                            console.log('🔍 Migration Overview - Migration:', migration.migrationId, 'Status:', migration.status);
                            
                            // Don't show cancel button for completed, failed, or cancelled migrations
                            const isCompleted = migration.status === 'completed' || migration.status === 'failed' || migration.status === 'cancelled';
                            if (isCompleted) {
                              console.log('🚫 Migration is completed/failed/cancelled - hiding cancel button');
                              return false;
                            }
                            
                            // Show cancel button if:
                            // 1. Migration exists in active list (immediate) OR
                            // 2. Status indicates running/progress (after data loads)
                            const isInActiveList = activeMigrations.has(migration.migrationId);
                            const hasRunningStatus = migration.status === 'running' || migration.status === 'in_progress' || migration.status === 'inprogress' || migration.status?.toLowerCase().includes('progress');
                            
                            return isInActiveList || hasRunningStatus;
                          })() && (
                            <Tooltip title="Cancel Migration">
                              <IconButton
                                size="small"
                                color="error"
                                onClick={() => handleCancelClick(migration.migrationId)}
                                disabled={isCancelling || joiningGroups.has(migration.migrationId)}
                                sx={{ 
                                  border: '1px solid',
                                  borderColor: 'error.main',
                                  '&:hover': {
                                    backgroundColor: 'error.light',
                                    color: 'white'
                                  }
                                }}
                              >
                                <StopIcon fontSize="small" />
                              </IconButton>
                            </Tooltip>
                          )}
                        </Stack>
                      </Box>
                    </CardContent>
                  </Card>
                ))}
              </Box>
            )}
          </CardContent>
        </Card>
      </Box>

      {/* Live Cancellation Dialog (Task 7.6) - Simplified */}
      <LiveCancellationDialog
        open={liveCancelDialogOpen}
        migrationId={selectedMigrationForCancel || ''}
        onClose={handleLiveCancelDialogClose}
        onConfirm={handleLiveCancelConfirm}
        isCancelling={isCancelling}
      />
    </Box>
  );
}; 