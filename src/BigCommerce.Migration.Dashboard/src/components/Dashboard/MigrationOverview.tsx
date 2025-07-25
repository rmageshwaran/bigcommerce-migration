import React, { useState, useEffect } from 'react';
import {
  Box,
  Card,
  CardContent,
  Typography,
  Chip,
  Alert,
  Button,
  Stack
} from '@mui/material';
import {
  Refresh as RefreshIcon,
  Error as ErrorIcon,
  CheckCircle as SuccessIcon,
  PlayArrow as StartIcon,
  BugReport as DebugIcon
} from '@mui/icons-material';
import { CircularProgress } from '@mui/material';
import { useDashboard } from '../../context/DashboardContext';
import { formatDate, formatRelativeTime } from '../../utils/dateUtils';
import { useNavigate } from 'react-router-dom';

/**
 * MigrationOverview Component
 * 
 * Main dashboard view that displays active migrations with real-time status indicators.
 * This component implements several key features for optimal user experience:
 * 
 * Key Features:
 * - Real-time connection status for each migration
 * - Loading states and success indicators for group joining
 * - Polling status display when SignalR is unavailable
 * - Enhanced navigation with guaranteed group joining
 * - Comprehensive error handling and user feedback
 * 
 * Implementation Tasks:
 * - Task 4: Show connection status in Active Migrations section
 * - Task 5: Ensure group joined before navigating to detail view
 * - Task 6: Cleanup SignalR groups/listeners on unmount
 */
export const MigrationOverview: React.FC = () => {
  const navigate = useNavigate();
  const { state, refreshData, addError, joinMigrationGroup } = useDashboard();
  const [isRefreshing, setIsRefreshing] = useState(false);
  const [joiningGroups, setJoiningGroups] = useState<Set<string>>(new Set());
  const [joinedGroups, setJoinedGroups] = useState<Set<string>>(new Set());

  // Cleanup local state when component unmounts
  useEffect(() => {
    return () => {
      console.log('🧹 Cleaning up MigrationOverview component...');
      setJoiningGroups(new Set());
      setJoinedGroups(new Set());
    };
  }, []);

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

  const handleDebugSignalR = () => {
    navigate('/test/signalr');
  };

  const handleViewDashboard = () => {
    navigate('/enhanced');
  };

  /**
   * Enhanced navigation handler that ensures SignalR group is joined
   * 
   * This function implements Task 5: Ensure group joined before navigating to detail view.
   * It provides a seamless user experience by guaranteeing that real-time updates
   * are available immediately when the user navigates to the migration detail view.
   * 
   * Navigation Flow:
   * 1. Check if SignalR is connected
   * 2. If already joined, navigate immediately
   * 3. If not joined, join the group with loading state
   * 4. Show success state and navigate
   * 5. Handle errors gracefully with fallback navigation
   * 
   * State Management:
   * - Uses joiningGroups to track loading state per migration
   * - Uses joinedGroups to track successful joins
   * - Provides visual feedback during the join process
   * 
   * Error Handling:
   * - Individual group join failures don't prevent navigation
   * - Users are notified of connection issues
   * - Fallback to polling-based updates if needed
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

  const activeMigrationsArray = Array.from(activeMigrations.values());

  return (
    <Box sx={{ flexGrow: 1, p: 3 }}>
      {/* Header Section */}
      <Box sx={{ mb: 3, display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
        <Typography variant="h4" component="h1" gutterBottom>
          Migration Overview
        </Typography>
        <Stack direction="row" spacing={2}>
          <Button
            variant="contained"
            color="primary"
            onClick={handleViewDashboard}
            size="small"
          >
            Migration Dashboard
          </Button>
          {/*<Button
            variant="outlined"
            startIcon={<DebugIcon />}
            onClick={handleDebugSignalR}
            size="small"
          >
            Debug SignalR
          </Button>*/}
          <Button
            variant="outlined"
            startIcon={<RefreshIcon />}
            onClick={handleRefresh}
            disabled={isRefreshing}
          >
            {isRefreshing ? 'Refreshing...' : 'Refresh'}
          </Button>
        </Stack>
      </Box>

      {/* Connection Status */}
      <Box sx={{ mb: 3 }}>
        <Card>
          <CardContent>
            <Typography variant="h6" gutterBottom>
              Connection Status
            </Typography>
            <Stack direction="row" spacing={2} sx={{ mb: 2 }}>
              <Chip
                icon={signalRConnection.isConnected ? <SuccessIcon /> : <ErrorIcon />}
                label={`Real-time Monitoring: ${signalRConnection.isConnected ? 'Connected' : 'Disconnected'}`}
                color={signalRConnection.isConnected ? 'success' : 'error'}
              />
              <Chip
                icon={apiConnected ? <SuccessIcon /> : <ErrorIcon />}
                label={`Backend Service: ${apiConnected ? 'Connected' : 'Disconnected'}`}
                color={apiConnected ? 'success' : 'error'}
              />
              {isPollingEnabled && (
                <Chip
                  icon={<CircularProgress size={16} />}
                  label="Polling Fallback Active"
                  color="info"
                  variant="outlined"
                />
              )}
            </Stack>
            
            {/* Connection Details */}
            <Box sx={{ mt: 2 }}>
              <Typography variant="body2" color="text.secondary" gutterBottom>
                <strong>Real-time Updates Status:</strong>
              </Typography>
              <Typography variant="body2" color="text.secondary" sx={{ mb: 1 }}>
                {signalRConnection.isConnected 
                  ? `✅ Connected to real-time updates. Active migrations will show live progress.`
                  : isPollingEnabled
                  ? `🔄 Real-time connection unavailable. Using polling fallback for updates.`
                  : `⚠️ Real-time connection not available. Migration progress will be updated via polling.`
                }
              </Typography>
              {signalRConnection.connectionState && (
                <Typography variant="caption" color="text.secondary">
                  Connection State: {signalRConnection.connectionState}
                  {signalRConnection.connectionId && ` (ID: ${signalRConnection.connectionId.slice(-8)})`}
                </Typography>
              )}
              {isPollingEnabled && lastPollingUpdate && (
                <Typography variant="caption" color="text.secondary" display="block" sx={{ mt: 0.5 }}>
                  Last Polling Update: {formatRelativeTime(lastPollingUpdate)}
                </Typography>
              )}
            </Box>
          </CardContent>
        </Card>
      </Box>

      {/* System Health */}
      {systemHealth && (
        <Box sx={{ mb: 3 }}>
          <Card>
            <CardContent>
              <Typography variant="h6" gutterBottom>
                System Health
              </Typography>
              <Typography variant="body1">
                Status: {systemHealth.status}
              </Typography>
              <Typography variant="body2" color="text.secondary">
                Last Updated: {formatDate(systemHealth.timestamp)}
              </Typography>
              <Typography variant="body2" color="text.secondary">
                ({formatRelativeTime(systemHealth.timestamp)})
              </Typography>
            </CardContent>
          </Card>
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
                          <Typography variant="subtitle1" gutterBottom>
                            Migration: {migration.migrationId}
                          </Typography>
                          <Typography variant="body2" color="text.secondary" gutterBottom>
                            Status: {migration.status}
                          </Typography>
                          <Typography variant="caption">
                            {migration.processedEntities} / {migration.totalEntities} entities
                          </Typography>
                        </Box>
                        <Stack direction="row" spacing={1} alignItems="center">
                          {/* Connection Status Indicator */}
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
                          <Button
                            variant="contained"
                            size="small"
                            onClick={() => handleNavigateToMigrationDetail(migration.migrationId)}
                            disabled={joiningGroups.has(migration.migrationId)}
                          >
                            {joiningGroups.has(migration.migrationId) ? 'Joining...' : 'View Dashboard'}
                          </Button>
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
    </Box>
  );
}; 