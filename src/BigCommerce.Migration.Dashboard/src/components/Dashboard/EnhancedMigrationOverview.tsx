import React from 'react';
import {
  Box,
  Card,
  CardContent,
  Typography,
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
    const statusLower = status?.toLowerCase() || '';
    switch (statusLower) {
      case 'completed': return <SuccessIcon color="success" />;
      case 'failed': return <ErrorIcon color="error" />;
      case 'cancelled': return <CancelledIcon color="warning" />;
      case 'processing': 
      case 'inprogress':
      case 'in_progress':
      case 'in-progress': return <ProcessingIcon color="primary" />;
      default: return <PendingIcon color="disabled" />;
    }
  };

  const getStatusColor = (status: string): "default" | "primary" | "secondary" | "error" | "info" | "success" | "warning" => {
    const statusLower = status?.toLowerCase() || '';
    switch (statusLower) {
      case 'completed': return 'success';
      case 'failed': return 'error';
      case 'cancelled': return 'warning';
      case 'processing':
      case 'inprogress': 
      case 'in_progress':
      case 'in-progress': return 'primary';
      default: return 'default';
    }
  };

  const formatEntityCounts = (entity: EntityDisplayData) => {
    console.log(`🔢 [DEBUG] formatEntityCounts called with entity:`, entity);
    const { processedCount, totalCount, successCount, failedCount, skippedCount, cancelledCount, showTotalCount } = entity;
    
    // Check if we should show total count based on backend flag
    const shouldShowTotal = showTotalCount !== false; // Default to true if not specified
    const totalDisplay = shouldShowTotal ? (totalCount === 0 ? '???' : totalCount.toLocaleString()) : '';
    
    const processedDisplay = processedCount.toLocaleString();
    const successDisplay = successCount.toLocaleString();
    const failedDisplay = failedCount.toLocaleString();
    const skippedDisplay = skippedCount.toLocaleString();
    const cancelledDisplay = (cancelledCount || 0).toLocaleString();
    
    // Include cancelled count in display if > 0
    const detailCounts = cancelledCount && cancelledCount > 0 
      ? `✅ ${successDisplay}  ❌ ${failedDisplay}  ⏭️ ${skippedDisplay}  🚫 ${cancelledDisplay}`
      : `✅ ${successDisplay}  ❌ ${failedDisplay}  ⏭️ ${skippedDisplay}`;
    
    const result = shouldShowTotal 
      ? `${processedDisplay}/${totalDisplay}  |  ${detailCounts}`
      : detailCounts;
    
    console.log(`🔢 [DEBUG] formatEntityCounts result:`, result);
    return result;
  };



  return (
    <Card>
      <CardContent>
        {/* Header with Live Updates and Refresh */}
        <Stack direction="row" justifyContent="space-between" alignItems="center" sx={{ mb: 3 }}>
          <Typography variant="h5" fontWeight="bold" gutterBottom>
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
        </Stack>

        {/* Migration Details */}
        <Grid container spacing={2} sx={{ mb: 3 }}>
          <Grid item xs={12} md={6}>
            <Typography variant="h6" fontWeight="medium" sx={{ mb: 1 }}>
              <strong>Migration ID:</strong> {migrationData.migrationId}
            </Typography>
            <Typography variant="h6" fontWeight="medium" sx={{ mb: 1 }}>
              <strong>Source Store:</strong> {migrationData.sourceStore}
            </Typography>
            <Typography variant="h6" fontWeight="medium" sx={{ mb: 1 }}>
              <strong>Destination Store:</strong> {migrationData.destinationStore}
            </Typography>
            <Typography variant="h6" fontWeight="medium" sx={{ mb: 1 }}>
              <strong>Migration Start:</strong> {formatDate(new Date(migrationData.startDateTime))}
            </Typography>
          </Grid>
          <Grid item xs={12} md={6}>
            <Typography variant="h6" fontWeight="medium" sx={{ mb: 1 }}>
              <strong>Migration Status:</strong> 
              <Chip 
                size="medium" 
                label={(() => {
                  const statusLower = migrationData.status?.toLowerCase() || '';
                  switch (statusLower) {
                    case 'completed': return 'MIGRATION COMPLETED';
                    case 'running': 
                    case 'inprogress':
                    case 'in_progress': 
                    case 'in-progress': return 'MIGRATION IN PROGRESS';
                    case 'failed': return 'MIGRATION FAILED';
                    case 'cancelled': return 'MIGRATION CANCELLED';
                    default: return migrationData.status.toUpperCase();
                  }
                })()} 
                color={getStatusColor(migrationData.status)}
                variant={migrationData.status?.toLowerCase() === 'completed' ? 'filled' : 'outlined'}
                sx={{ ml: 1, height: 32, fontWeight: 'bold', textTransform: 'uppercase' }}
              />
            </Typography>
            <Typography variant="h6" fontWeight="medium" sx={{ mb: 1 }}>
              <strong>Last Updated:</strong> {formatRelativeTime(lastUpdated || migrationData.lastUpdated)}
            </Typography>
            {migrationData.estimatedEndTime && (
              <Typography variant="h6" fontWeight="medium" sx={{ mb: 1 }}>
                <strong>Expected End Time:</strong> {formatDate(new Date(migrationData.estimatedEndTime))}
              </Typography>
            )}
          </Grid>
        </Grid>

        {/* Overall Summary */}
        <Typography variant="h6" sx={{ mb: 3, color: 'text.secondary', fontWeight: 'medium' }}>
          Processed: {migrationData.totalProcessed.toLocaleString()}  |  
          Success: {migrationData.totalSuccess.toLocaleString()}  |  
          Failed: {migrationData.totalFailed.toLocaleString()}  |  
          Skipped: {migrationData.totalSkipped.toLocaleString()}
          {migrationData.totalCancelled && migrationData.totalCancelled > 0 && (
            <>  |  Cancelled: {migrationData.totalCancelled.toLocaleString()}</>
          )}
        </Typography>

        <Divider sx={{ mb: 3 }} />

        {/* Entities Section - Simplified */}
        <Typography variant="h5" gutterBottom fontWeight="bold">
          Entities
        </Typography>

        {migrationData.entities && migrationData.entities.length > 0 ? (
          migrationData.entities.map((entity, index) => (
            <Stack key={entity.entityType} direction="row" justifyContent="space-between" alignItems="center" sx={{ mb: 2, p: 2, border: '1px solid', borderColor: 'divider', borderRadius: 1 }}>
              <Stack direction="row" alignItems="center" spacing={2}>
                <Typography variant="h6" fontWeight="medium" sx={{ textTransform: 'capitalize', minWidth: '200px' }}>
                  {entity.entityType}
                </Typography>
                <Box sx={{ fontSize: '1.5rem' }}>
                  {getStatusIcon(entity.status)}
                </Box>
              </Stack>
              
              <Typography variant="h6" fontWeight="medium" color="text.primary">
                {formatEntityCounts(entity)}
              </Typography>
            </Stack>
          ))
        ) : (
          <Typography variant="body1" color="text.secondary" sx={{ textAlign: 'center', py: 3 }}>
            {['running', 'inprogress', 'in_progress', 'in-progress', 'processing'].includes(migrationData.status?.toLowerCase() || '') ? 
              'Entity discovery in progress...' : 
              'No entities discovered yet'}
          </Typography>
        )}

        {/* Error Display */}
        {error && (
          <Typography variant="body1" color="error" sx={{ mt: 2 }}>
            {error}
          </Typography>
        )}
      </CardContent>
    </Card>
  );
};