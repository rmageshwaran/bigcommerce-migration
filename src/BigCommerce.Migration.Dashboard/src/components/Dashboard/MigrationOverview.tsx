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
  Dialog,
  DialogTitle,
  DialogContent,
  DialogActions,
  DialogContentText
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
  const [showDebugInfo, setShowDebugInfo] = useState(false);
  
  // Cancel migration state
  const [cancelDialogOpen, setCancelDialogOpen] = useState(false);
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
   * Instant navigation handler - groups are already pre-joined by dashboard context
   */
  const handleNavigateToMigrationDetail = (migrationId: string) => {
    console.log(`🚀 [INSTANT-NAV] Navigating to dashboard for migration: ${migrationId} (group already joined)`);
    navigate(`/enhanced?migrationId=${migrationId}`);
  };

  // Cancel migration handlers
  const handleCancelClick = (migrationId: string) => {
    setSelectedMigrationForCancel(migrationId);
    setCancelDialogOpen(true);
  };

  const handleCancelCancel = () => {
    setCancelDialogOpen(false);
    setSelectedMigrationForCancel(null);
  };

  const handleCancelConfirm = async () => {
    if (!selectedMigrationForCancel) return;

    setIsCancelling(true);
    try {
      // Clear any previous error messages
      clearErrors();

      // Cancel the migration via API with detailed logging
      console.log('🚫 Cancelling migration via API:', selectedMigrationForCancel);
      const cancelResponse = await apiService.cancelMigration(selectedMigrationForCancel);
      console.log('✅ API cancellation response:', cancelResponse);

      // Check if the API response indicates successful cancellation
      if (cancelResponse?.status?.toLowerCase() === 'cancelled' || cancelResponse?.message?.toLowerCase().includes('cancelled successfully')) {
        console.log('✅ Backend confirmed cancellation was successful');
        
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
        
        // Show success notification
        notificationService.success('Migration Cancelled', `Migration ${selectedMigrationForCancel.slice(-8)} cancelled successfully!`);
      } else {
        // API response doesn't confirm successful cancellation
        console.error('❌ Backend API did not confirm successful cancellation:', cancelResponse);
        notificationService.error('Cancellation Failed', 'The backend did not confirm successful cancellation. Please try again.');
        
        setIsCancelling(false);
        setCancelDialogOpen(false);
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
      setCancelDialogOpen(false);
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
  console.log(`🏠 [DEBUG] MigrationOverview activeMigrationsArray:`, activeMigrationsArray.map(m => ({
    migrationId: m.migrationId,
    status: m.status,
    allKeys: Object.keys(m)
  })));

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
              severity={error.code?.toLowerCase().includes('success') ? 'success' : 'error'} 
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
                      {/* Enhanced Migration Display for Active Migrations */}
                      {!['completed', 'failed', 'cancelled'].includes(migration.status?.toLowerCase() || '') ? (
                        <Box>
                          {/* Header with basic info and controls */}
                          <Box display="flex" justifyContent="space-between" alignItems="center" sx={{ mb: 2 }}>
                            <Box>
                              <Typography variant="h6" fontWeight="medium" sx={{ mb: 1 }}>
                                Migration: {migration.migrationId}
                              </Typography>
                              <Typography variant="h6" fontWeight="medium" color="text.secondary" gutterBottom>
                                {migration.sourceStore || 'Source Store'} → {migration.destinationStore || 'Destination Store'}
                              </Typography>
                              <Typography variant="h6" fontWeight="medium" color="text.secondary">
                                Status: {migration.status}
                              </Typography>
                            </Box>
                            <Stack direction="row" spacing={1} alignItems="center">
                              {/* Only show View Dashboard button for active migrations */}
                              <Button
                                variant="contained"
                                size="small"
                                onClick={() => handleNavigateToMigrationDetail(migration.migrationId)}
                              >
                                View Dashboard
                              </Button>
                              
                              {/* Cancel button */}
                              {(() => {
                                const isInActiveList = activeMigrations.has(migration.migrationId);
                                const hasRunningStatus = ['running', 'in_progress', 'inprogress', 'in-progress', 'processing'].includes(migration.status?.toLowerCase() || '');
                                
                                return isInActiveList || hasRunningStatus;
                              })() && (
                                <Tooltip title="Cancel Migration">
                                  <IconButton
                                    size="small"
                                    color="error"
                                    onClick={() => handleCancelClick(migration.migrationId)}
                                    disabled={isCancelling}
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
                          

                        </Box>
                      ) : (
                        /* Simple display for completed/failed/cancelled migrations */
                        <Box display="flex" justifyContent="space-between" alignItems="center">
                          <Box>
                            <Box display="flex" alignItems="center" gap={1} sx={{ mb: 1 }}>
                              <Typography variant="subtitle1">
                                Migration: {migration.migrationId}
                              </Typography>
                            </Box>
                            <Typography variant="body2" color="text.secondary" gutterBottom>
                              Status: {migration.status}
                            </Typography>
                            <Typography variant="caption">
                              {(migration.processedEntities ?? 0)} / {(migration.totalEntities ?? 0)} entities
                            </Typography>
                          </Box>
                          <Stack direction="row" spacing={1} alignItems="center">
                            {/* Show status chip for completed migrations */}
                            <Chip
                              size="small"
                              label={`Migration ${migration.status}`}
                              color={migration.status?.toLowerCase() === 'completed' ? 'success' : 'error'}
                              variant="outlined"
                              sx={{ 
                                fontSize: '0.75rem',
                                height: '24px',
                                textTransform: 'capitalize'
                              }}
                            />
                          </Stack>
                        </Box>
                      )}
                    </CardContent>
                  </Card>
                ))}
              </Box>
            )}
          </CardContent>
        </Card>
      </Box>

      {/* Cancel Confirmation Dialog */}
      <Dialog
        open={cancelDialogOpen}
        onClose={handleCancelCancel}
        maxWidth="sm"
        fullWidth
      >
        <DialogTitle>Cancel Migration</DialogTitle>
        <DialogContent>
          <DialogContentText>
            Are you sure you want to cancel this migration?
            <br /><br />
            <strong>Migration ID:</strong> {selectedMigrationForCancel}
            <br /><br />
            This action cannot be undone. The migration will be stopped and marked as cancelled.
          </DialogContentText>
        </DialogContent>
        <DialogActions>
          <Button
            onClick={handleCancelCancel}
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
    </Box>
  );
}; 