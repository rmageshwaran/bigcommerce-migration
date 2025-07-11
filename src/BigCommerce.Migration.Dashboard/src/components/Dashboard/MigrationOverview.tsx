import React from 'react';
import {
  Box,
  Grid,
  Card,
  CardContent,
  Typography,
  LinearProgress,
  Chip,
  Alert,
  IconButton,
  Tooltip,
  Divider,
  CircularProgress,
  Button,
  Stack,
} from '@mui/material';
import {
  Refresh as RefreshIcon,
  Warning as WarningIcon,
  Error as ErrorIcon,
  CheckCircle as CheckCircleIcon,
  Speed as SpeedIcon,
  Timeline as TimelineIcon,
  Storage as StorageIcon,
} from '@mui/icons-material';
import { useDashboard } from '../../context/DashboardContext';
import type { MigrationProgress, SystemStatus } from '../../types';
import { ProgressChart } from '../Charts/ProgressChart';
import { EntityProgressGrid } from '../Charts/EntityProgressGrid';
import { notificationService } from '../../services/notificationService';
import ExportButton from '../Export/ExportButton';

interface MetricCardProps {
  title: string;
  value: string | number;
  subtitle?: string;
  icon: React.ReactNode;
  color?: 'primary' | 'secondary' | 'success' | 'warning' | 'error';
}

const MetricCard: React.FC<MetricCardProps> = ({ 
  title, 
  value, 
  subtitle, 
  icon, 
  color = 'primary' 
}) => (
  <Card sx={{ height: '100%' }}>
    <CardContent>
      <Box sx={{ display: 'flex', alignItems: 'center', mb: 2 }}>
        <Box sx={{ color: `${color}.main`, mr: 1 }}>
          {icon}
        </Box>
        <Typography variant="h6" component="div">
          {title}
        </Typography>
      </Box>
      <Typography variant="h4" component="div" sx={{ mb: 1 }}>
        {value}
      </Typography>
      {subtitle && (
        <Typography variant="body2" color="text.secondary">
          {subtitle}
        </Typography>
      )}
    </CardContent>
  </Card>
);

interface ProgressCardProps {
  migration: MigrationProgress;
}

const ProgressCard: React.FC<ProgressCardProps> = ({ migration }) => {
  const getStatusColor = (status: string): 'default' | 'primary' | 'secondary' | 'error' | 'info' | 'success' | 'warning' => {
    switch (status) {
      case 'completed': return 'success';
      case 'failed': return 'error';
      case 'cancelled': return 'warning';
      case 'running': return 'primary';
      default: return 'default';
    }
  };

  const formatTime = (seconds: number): string => {
    const hours = Math.floor(seconds / 3600);
    const minutes = Math.floor((seconds % 3600) / 60);
    const secs = Math.floor(seconds % 60);
    
    if (hours > 0) {
      return `${hours}h ${minutes}m ${secs}s`;
    } else if (minutes > 0) {
      return `${minutes}m ${secs}s`;
    } else {
      return `${secs}s`;
    }
  };

  return (
    <Card sx={{ mb: 2 }}>
      <CardContent>
        <Box sx={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', mb: 2 }}>
          <Typography variant="h6" component="div">
            Migration {migration.migrationId.slice(-8)}
          </Typography>
          <Chip 
            label={migration.status} 
            color={getStatusColor(migration.status)}
            size="small"
          />
        </Box>
        
        <Box sx={{ mb: 2 }}>
          <Box sx={{ display: 'flex', justifyContent: 'space-between', mb: 1 }}>
            <Typography variant="body2">
              Overall Progress
            </Typography>
            <Typography variant="body2">
              {migration.overallProgressPercentage.toFixed(1)}%
            </Typography>
          </Box>
          <LinearProgress 
            variant="determinate" 
            value={migration.overallProgressPercentage} 
            sx={{ height: 8, borderRadius: 4 }}
          />
        </Box>

        <Grid container spacing={2}>
          <Grid item xs={6}>
            <Typography variant="body2" color="text.secondary">
              Processed: {migration.processedEntities.toLocaleString()} / {migration.totalEntities.toLocaleString()}
            </Typography>
          </Grid>
          <Grid item xs={6}>
            <Typography variant="body2" color="text.secondary">
              Success Rate: {migration.processedEntities > 0 ? 
                ((migration.successfulEntities / migration.processedEntities) * 100).toFixed(1) : 0}%
            </Typography>
          </Grid>
          <Grid item xs={6}>
            <Typography variant="body2" color="text.secondary">
              Speed: {migration.entitiesPerSecond.toFixed(1)} entities/sec
            </Typography>
          </Grid>
          <Grid item xs={6}>
            <Typography variant="body2" color="text.secondary">
              Elapsed: {formatTime(migration.elapsedTime)}
            </Typography>
          </Grid>
        </Grid>

        {migration.currentEntity && (
          <Box sx={{ mt: 2 }}>
            <Typography variant="body2" color="text.secondary">
              Current: {migration.currentEntity} ({migration.currentPhase})
            </Typography>
          </Box>
        )}
      </CardContent>
    </Card>
  );
};

interface SystemHealthCardProps {
  status: SystemStatus;
  isConnected: boolean;
}

const SystemHealthCard: React.FC<SystemHealthCardProps> = ({ status, isConnected }) => {
  const getHealthIcon = () => {
    if (!isConnected) {
      return <ErrorIcon color="error" />;
    }
    switch (status) {
      case 'healthy': return <CheckCircleIcon color="success" />;
      case 'warning': return <WarningIcon color="warning" />;
      case 'error': return <ErrorIcon color="error" />;
      default: return <WarningIcon color="warning" />;
    }
  };

  const getHealthColor = (): 'success' | 'warning' | 'error' => {
    if (!isConnected) return 'error';
    switch (status) {
      case 'healthy': return 'success';
      case 'warning': return 'warning';
      case 'error': return 'error';
      default: return 'warning';
    }
  };

  const getHealthText = () => {
    if (!isConnected) return 'Disconnected';
    switch (status) {
      case 'healthy': return 'All Systems Operational';
      case 'warning': return 'Some Issues Detected';
      case 'error': return 'System Error';
      default: return 'Unknown Status';
    }
  };

  return (
    <Card>
      <CardContent>
        <Box sx={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between' }}>
          <Box sx={{ display: 'flex', alignItems: 'center' }}>
            {getHealthIcon()}
            <Box sx={{ ml: 2 }}>
              <Typography variant="h6">System Health</Typography>
              <Typography variant="body2" color="text.secondary">
                {getHealthText()}
              </Typography>
            </Box>
          </Box>
          <Chip 
            label={isConnected ? status : 'offline'} 
            color={getHealthColor()}
            size="small"
          />
        </Box>
      </CardContent>
    </Card>
  );
};

export const MigrationOverview: React.FC = () => {
  const { state, refreshData } = useDashboard();
  const { 
    activeMigrations, 
    systemHealth, 
    signalRConnection, 
    isLoading, 
    errors,
    apiConnected 
  } = state;

  const migrationList = Array.from(activeMigrations.values());
  const totalMigrations = migrationList.length;
  const runningMigrations = migrationList.filter(m => m.status === 'running').length;
  const totalEntitiesProcessed = migrationList.reduce((sum, m) => sum + m.processedEntities, 0);
  const averageSpeed = migrationList.reduce((sum, m) => sum + m.entitiesPerSecond, 0) / Math.max(totalMigrations, 1);

  const handleRefresh = async () => {
    try {
      await refreshData();
    } catch (error) {
      console.error('Failed to refresh data:', error);
    }
  };

  // Notification testing functions
  const testNotifications = {
    success: () => {
      notificationService.success('Migration Started', 'Products migration has started successfully');
    },
    error: () => {
      notificationService.error('Migration Failed', 'Connection to BigCommerce API failed');
    },
    warning: () => {
      notificationService.warning('Rate Limit Warning', 'Approaching API rate limits');
    },
    info: () => {
      notificationService.info('System Update', 'Dashboard updated to version 2.1.0');
    },
    migrationComplete: () => {
      notificationService.migrationCompleted('mig_12345', 'Products Migration', '2h 15m');
    },
    migrationFailed: () => {
      notificationService.migrationFailed('mig_12346', 'Categories Migration', 'Invalid API credentials');
    },
    systemAlert: () => {
      notificationService.systemAlert('System Maintenance', 'Scheduled maintenance in 15 minutes');
    },
    batchComplete: () => {
      notificationService.batchCompleted('mig_12347', 'Products', 150, 500);
    }
  };

  return (
    <Box sx={{ p: 3 }}>
      {/* Header */}
      <Box sx={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', mb: 3 }}>
        <Typography variant="h4" component="h1">
          Migration Overview
        </Typography>
        <Box sx={{ display: 'flex', gap: 1 }}>
          <ExportButton
            dataType="migrations"
            title="Migration Overview Report"
            variant="icon"
            size="medium"
          />
          <Tooltip title="Refresh Data">
            <IconButton 
              onClick={handleRefresh} 
              disabled={isLoading}
              color="primary"
            >
              {isLoading ? <CircularProgress size={24} /> : <RefreshIcon />}
            </IconButton>
          </Tooltip>
        </Box>
      </Box>

      {/* Error Alerts */}
      {errors.length > 0 && (
        <Box sx={{ mb: 3 }}>
          {errors.slice(0, 3).map((error) => (
            <Alert 
              key={error.code} 
              severity="error" 
              sx={{ mb: 1 }}
            >
              {error.message}
            </Alert>
          ))}
        </Box>
      )}

      {/* Key Metrics */}
      <Grid container spacing={3} sx={{ mb: 3 }}>
        <Grid item xs={12} sm={6} md={3}>
          <MetricCard
            title="Active Migrations"
            value={runningMigrations}
            subtitle={`${totalMigrations} total`}
            icon={<TimelineIcon />}
            color="primary"
          />
        </Grid>
        <Grid item xs={12} sm={6} md={3}>
          <MetricCard
            title="Entities Processed"
            value={totalEntitiesProcessed.toLocaleString()}
            subtitle="All migrations"
            icon={<StorageIcon />}
            color="success"
          />
        </Grid>
        <Grid item xs={12} sm={6} md={3}>
          <MetricCard
            title="Average Speed"
            value={averageSpeed.toFixed(1)}
            subtitle="entities/second"
            icon={<SpeedIcon />}
            color="info"
          />
        </Grid>
        <Grid item xs={12} sm={6} md={3}>
          <MetricCard
            title="Connection Status"
            value={signalRConnection.isConnected ? 'Connected' : 'Disconnected'}
            subtitle={`API: ${apiConnected ? 'Connected' : 'Disconnected'}`}
            icon={signalRConnection.isConnected ? <CheckCircleIcon /> : <ErrorIcon />}
            color={signalRConnection.isConnected ? 'success' : 'error'}
          />
        </Grid>
      </Grid>

      <Grid container spacing={3}>
        {/* System Health */}
        <Grid item xs={12} md={4}>
          <SystemHealthCard 
            status={systemHealth?.status || 'error'} 
            isConnected={signalRConnection.isConnected && apiConnected}
          />
        </Grid>

        {/* Active Migrations */}
        <Grid item xs={12} md={8}>
          <Card>
            <CardContent>
              <Typography variant="h6" component="div" sx={{ mb: 2 }}>
                Active Migrations
              </Typography>
              <Divider sx={{ mb: 2 }} />
              
              {isLoading && migrationList.length === 0 ? (
                <Box sx={{ display: 'flex', justifyContent: 'center', p: 4 }}>
                  <CircularProgress />
                </Box>
              ) : migrationList.length === 0 ? (
                <Box sx={{ textAlign: 'center', p: 4 }}>
                  <Typography variant="body1" color="text.secondary">
                    No active migrations
                  </Typography>
                  <Typography variant="body2" color="text.secondary">
                    Start a migration to see real-time progress here
                  </Typography>
                </Box>
              ) : (
                <Box sx={{ maxHeight: 400, overflow: 'auto' }}>
                  {migrationList.map((migration) => (
                    <ProgressCard key={migration.migrationId} migration={migration} />
                  ))}
                </Box>
              )}
            </CardContent>
          </Card>
        </Grid>
      </Grid>

      {/* Progress Analytics Charts */}
      {migrationList.length > 0 && (
        <Box sx={{ mt: 3 }}>
          <ProgressChart height={400} showControls={true} />
        </Box>
      )}

      {/* Entity Progress Grid */}
      {migrationList.length > 0 && (
        <Box sx={{ mt: 3 }}>
          <EntityProgressGrid height={500} showPagination={true} />
        </Box>
      )}
    </Box>
  );
}; 