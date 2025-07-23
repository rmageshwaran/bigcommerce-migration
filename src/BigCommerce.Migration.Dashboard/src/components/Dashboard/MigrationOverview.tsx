import React, { useState } from 'react';
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
import { useDashboard } from '../../context/DashboardContext';
import { formatDate, formatRelativeTime } from '../../utils/dateUtils';
import { useNavigate } from 'react-router-dom';

export const MigrationOverview: React.FC = () => {
  const navigate = useNavigate();
  const { state, refreshData, addError } = useDashboard();
  const [isRefreshing, setIsRefreshing] = useState(false);

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

  const {
    activeMigrations,
    systemHealth,
    signalRConnection,
    apiConnected,
    isLoading,
    errors
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
          <Button
            variant="outlined"
            startIcon={<DebugIcon />}
            onClick={handleDebugSignalR}
            size="small"
          >
            Debug SignalR
          </Button>
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
            <Stack direction="row" spacing={2}>
              <Chip
                icon={signalRConnection.isConnected ? <SuccessIcon /> : <ErrorIcon />}
                label={`SignalR: ${signalRConnection.isConnected ? 'Connected' : 'Disconnected'}`}
                color={signalRConnection.isConnected ? 'success' : 'error'}
              />
              <Chip
                icon={apiConnected ? <SuccessIcon /> : <ErrorIcon />}
                label={`API: ${apiConnected ? 'Connected' : 'Disconnected'}`}
                color={apiConnected ? 'success' : 'error'}
              />
            </Stack>
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
                        <Stack direction="row" spacing={1}>
                          <Button
                            variant="contained"
                            size="small"
                            onClick={() => navigate(`/enhanced?migrationId=${migration.migrationId}`)}
                          >
                            View Dashboard
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