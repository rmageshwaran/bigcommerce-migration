import React from 'react';
import {
  Box,
  Card,
  CardContent,
  Typography,
  LinearProgress,
  Grid,
  Chip,
  Stack,
  Divider,
  Tooltip,
  IconButton
} from '@mui/material';
import {
  CheckCircle as SuccessIcon,
  Error as ErrorIcon,
  Schedule as PendingIcon,
  PlayArrow as ProcessingIcon,
  Cancel as CancelledIcon,
  Refresh as RefreshIcon
} from '@mui/icons-material';
import { useEnhancedMigrationProgress } from '../../hooks/useEnhancedMigrationProgress';
import { formatDate, formatRelativeTime } from '../../utils/dateUtils';
import type { EntityDisplayData } from '../../types';

interface EnhancedMigrationOverviewProps {
  migrationId: string;
  onRefresh?: () => void;
}

/**
 * Enhanced Migration Overview Component for Phase 3
 * Displays clean entity progress with the design from the improvement plan
 * Uses simplified SignalR events for real-time updates
 */
export const EnhancedMigrationOverview: React.FC<EnhancedMigrationOverviewProps> = ({ 
  migrationId, 
  onRefresh 
}) => {
  console.log(`🖥️ [DEBUG] EnhancedMigrationOverview rendered with migrationId: "${migrationId}"`);
  const { migrationData, isConnected, isLoading, error, lastUpdated, refreshData } = useEnhancedMigrationProgress(migrationId);
  
  console.log(`🖥️ [DEBUG] EnhancedMigrationOverview state:`, {
    migrationData: migrationData ? {
      migrationId: migrationData.migrationId,
      entitiesLength: migrationData.entities?.length,
      entities: migrationData.entities
    } : null,
    isConnected,
    isLoading,
    error
  });

  if (isLoading && !migrationData) {
    return (
      <Card>
        <CardContent>
          <Typography>Loading migration overview...</Typography>
        </CardContent>
      </Card>
    );
  }

  if (error && !migrationData) {
    return (
      <Card>
        <CardContent>
          <Typography color="error">Error loading migration: {error}</Typography>
        </CardContent>
      </Card>
    );
  }

  if (!migrationData) {
    return (
      <Card>
        <CardContent>
          <Typography>No migration data available</Typography>
        </CardContent>
      </Card>
    );
  }

  const handleRefresh = () => {
    refreshData();
    onRefresh?.();
  };

  const getStatusIcon = (status: string) => {
    switch (status) {
      case 'completed': return <SuccessIcon color="success" />;
      case 'failed': return <ErrorIcon color="error" />;
      case 'cancelled': return <CancelledIcon color="warning" />;
      case 'processing': return <ProcessingIcon color="primary" />;
      default: return <PendingIcon color="disabled" />;
    }
  };

  const getStatusColor = (status: string): "default" | "primary" | "secondary" | "error" | "info" | "success" | "warning" => {
    switch (status) {
      case 'completed': return 'success';
      case 'failed': return 'error';
      case 'cancelled': return 'warning';
      case 'processing': return 'primary';
      default: return 'default';
    }
  };

  const formatEntityCounts = (entity: EntityDisplayData) => {
    console.log(`🔢 [DEBUG] formatEntityCounts called with entity:`, entity);
    const { processedCount, totalCount, successCount, failedCount, skippedCount } = entity;
    const totalDisplay = totalCount === 0 ? '???' : totalCount;
    const result = `${processedCount}/${totalDisplay} | ✅${successCount} ❌${failedCount} ⏭️${skippedCount}`;
    console.log(`🔢 [DEBUG] formatEntityCounts result:`, result);
    return result;
  };

  const formatProcessingSpeed = (speed: number) => {
    if (speed < 1) return `${(speed * 60).toFixed(1)}/min`;
    return `${speed.toFixed(1)}/sec`;
  };

  return (
    <Card>
      <CardContent>
        {/* Header Section */}
        <Box sx={{ mb: 3 }}>
          <Box sx={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', mb: 2 }}>
            <Typography variant="h6" gutterBottom>
              Migration Overview
            </Typography>
            <Stack direction="row" spacing={1} alignItems="center">
              <Chip
                size="small"
                icon={isConnected ? <SuccessIcon /> : <ErrorIcon />}
                label={isConnected ? 'Live Updates' : 'No Connection'}
                color={isConnected ? 'success' : 'error'}
                variant="outlined"
              />
              <Tooltip title="Refresh Data">
                <IconButton size="small" onClick={handleRefresh}>
                  <RefreshIcon />
                </IconButton>
              </Tooltip>
            </Stack>
          </Box>

          {/* Migration Details */}
          <Grid container spacing={2} sx={{ mb: 2 }}>
            <Grid item xs={12} md={6}>
              <Typography variant="body2">
                <strong>Migration ID:</strong> {migrationData.migrationId}
              </Typography>
              <Typography variant="body2">
                <strong>Source Store:</strong> {migrationData.sourceStore} → {migrationData.destinationStore}
              </Typography>
              <Typography variant="body2">
                <strong>Migration Start:</strong> {formatDate(new Date(migrationData.startDateTime))}
              </Typography>
            </Grid>
            <Grid item xs={12} md={6}>
              {migrationData.estimatedEndTime && (
                <Typography variant="body2">
                  <strong>Expected End Time:</strong> {formatDate(new Date(migrationData.estimatedEndTime))}
                </Typography>
              )}
              <Typography variant="body2">
                <strong>Status:</strong> 
                <Chip 
                  size="small" 
                  label={migrationData.status} 
                  color={getStatusColor(migrationData.status)}
                  sx={{ ml: 1, height: 20 }}
                />
              </Typography>
              <Typography variant="body2">
                <strong>Last Updated:</strong> {formatRelativeTime(lastUpdated || migrationData.lastUpdated)}
              </Typography>
            </Grid>
          </Grid>

          {/* Overall Progress Bar */}
          <Box sx={{ mb: 2 }}>
            <Box sx={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', mb: 1 }}>
              <Typography variant="body2" fontWeight="medium">
                Overall Progress
              </Typography>
              <Typography variant="body2" fontWeight="medium">
                {migrationData.overallProgress.toFixed(1)}%
              </Typography>
            </Box>
            <LinearProgress 
              variant="determinate" 
              value={migrationData.overallProgress} 
              sx={{ 
                height: 8, 
                borderRadius: 4,
                backgroundColor: 'grey.200',
                '& .MuiLinearProgress-bar': {
                  borderRadius: 4
                }
              }}
            />
            <Typography variant="body2" sx={{ mt: 1, color: 'text.secondary' }}>
              Processed: {migrationData.totalProcessed.toLocaleString()} | 
              Success: {migrationData.totalSuccess.toLocaleString()} | 
              Failed: {migrationData.totalFailed.toLocaleString()} | 
              Skipped: {migrationData.totalSkipped.toLocaleString()}
            </Typography>
          </Box>
        </Box>

        <Divider sx={{ mb: 3 }} />

        {/* Entities Section */}
        <Box>
          <Typography variant="h6" gutterBottom>
            Entities
          </Typography>

          {migrationData.entities && migrationData.entities.length > 0 ? (
            migrationData.entities.map((entity, index) => (
            <Box key={entity.entityType} sx={{ mb: 3 }}>
              {/* Entity Header */}
              <Box sx={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', mb: 1 }}>
                <Box sx={{ display: 'flex', alignItems: 'center', gap: 1 }}>
                  <Typography variant="subtitle1" fontWeight="medium" sx={{ textTransform: 'capitalize' }}>
                    {entity.entityType}
                  </Typography>
                  {getStatusIcon(entity.status)}
                  <Chip 
                    size="small" 
                    label={entity.status} 
                    color={getStatusColor(entity.status)}
                    variant="outlined"
                    sx={{ height: 20, fontSize: '0.75rem', textTransform: 'capitalize' }}
                  />
                </Box>
                <Typography variant="body2" fontWeight="medium">
                  {entity.progressPercentage.toFixed(1)}%
                </Typography>
              </Box>

              {/* Entity Progress Bar */}
              <LinearProgress 
                variant="determinate" 
                value={entity.progressPercentage} 
                sx={{ 
                  height: 6, 
                  borderRadius: 3,
                  backgroundColor: 'grey.200',
                  '& .MuiLinearProgress-bar': {
                    borderRadius: 3,
                    backgroundColor: entity.status === 'completed' ? 'success.main' :
                                   entity.status === 'failed' ? 'error.main' :
                                   entity.status === 'cancelled' ? 'warning.main' :
                                   'primary.main'
                  }
                }}
              />

              {/* Entity Details */}
              <Box sx={{ mt: 1, display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
                <Typography variant="body2" sx={{ color: 'text.secondary' }}>
                  {formatEntityCounts(entity)}
                </Typography>
                <Box sx={{ display: 'flex', gap: 2 }}>
                  {entity.currentChunk !== undefined && entity.totalChunks !== undefined && entity.totalChunks > 0 && (
                    <Typography variant="body2" sx={{ color: 'text.secondary' }}>
                      Chunk {entity.currentChunk}/{entity.totalChunks}
                    </Typography>
                  )}
                  {entity.processingSpeed !== undefined && entity.processingSpeed > 0 && (
                    <Typography variant="body2" sx={{ color: 'text.secondary' }}>
                      {formatProcessingSpeed(entity.processingSpeed)}
                    </Typography>
                  )}
                </Box>
              </Box>

              {/* Add divider between entities except for the last one */}
              {index < migrationData.entities.length - 1 && (
                <Divider sx={{ mt: 2 }} />
              )}
            </Box>
            ))
          ) : (
            <Box sx={{ textAlign: 'center', py: 3, color: 'text.secondary' }}>
              <Typography variant="body2">
                {migrationData.status === 'running' ? 
                  'Entity discovery in progress...' : 
                  'No entities discovered yet'}
              </Typography>
            </Box>
          )}
        </Box>

        {/* Error Display */}
        {error && (
          <Box sx={{ mt: 2 }}>
            <Typography variant="body2" color="error">
              {error}
            </Typography>
          </Box>
        )}
      </CardContent>
    </Card>
  );
};