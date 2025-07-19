import React, { useState, useMemo } from 'react';
import {
  Box,
  Card,
  CardContent,
  Typography,
  LinearProgress,
  Chip,
  Grid,
  Table,
  TableBody,
  TableCell,
  TableContainer,
  TableHead,
  TableRow,
  IconButton,
  Tooltip,
  Avatar,
  Stack,
  Collapse,
  Badge,
  useTheme,
  alpha
} from '@mui/material';
import {
  ExpandMore as ExpandMoreIcon,
  ExpandLess as ExpandLessIcon,
  PlayArrow as RunningIcon,
  CheckCircle as CompletedIcon,
  Error as ErrorIcon,
  Schedule as PendingIcon,
  Speed as PerformanceIcon,
  TrendingUp as TrendingUpIcon,
  Timeline as TimelineIcon,
  Assessment as MetricsIcon,
  Refresh as RefreshIcon
} from '@mui/icons-material';
import { format, formatDistanceToNow } from 'date-fns';
import type { EntityProgress, MigrationProgress } from '../../types';

interface EntityStatusExtended extends EntityProgress {
  batchesCompleted?: number;
  currentBatch?: number;
  totalBatches?: number;
  averageTimePerEntity?: number;
  estimatedCompletion?: Date;
  lastBatchTime?: Date;
  performanceRating?: 'excellent' | 'good' | 'average' | 'poor';
}

interface RealTimeEntityGridProps {
  migrationId: string;
  entityProgress: Record<string, EntityProgress>;
  overallProgress?: MigrationProgress;
  showPerformanceMetrics?: boolean;
  showBatchDetails?: boolean;
  onEntityClick?: (entityType: string) => void;
  onRefresh?: () => void;
}

const getEntityStatusConfig = (status: string) => {
  switch (status.toLowerCase()) {
    case 'running':
    case 'in_progress':
      return {
        color: 'primary' as const,
        icon: <RunningIcon />,
        label: 'Running',
        bgColor: '#e3f2fd',
        textColor: '#1976d2'
      };
    case 'completed':
    case 'finished':
      return {
        color: 'success' as const,
        icon: <CompletedIcon />,
        label: 'Completed',
        bgColor: '#e8f5e8',
        textColor: '#2e7d32'
      };
    case 'failed':
    case 'error':
      return {
        color: 'error' as const,
        icon: <ErrorIcon />,
        label: 'Failed',
        bgColor: '#ffebee',
        textColor: '#d32f2f'
      };
    case 'pending':
    case 'waiting':
    default:
      return {
        color: 'default' as const,
        icon: <PendingIcon />,
        label: 'Pending',
        bgColor: '#f5f5f5',
        textColor: '#757575'
      };
  }
};

const getPerformanceRating = (entity: EntityStatusExtended): EntityStatusExtended['performanceRating'] => {
  if (!entity.averageTimePerEntity) return 'average';
  
  // Performance thresholds (in seconds per entity)
  if (entity.averageTimePerEntity < 0.5) return 'excellent';
  if (entity.averageTimePerEntity < 1.0) return 'good';
  if (entity.averageTimePerEntity < 2.0) return 'average';
  return 'poor';
};

const getPerformanceColor = (rating: EntityStatusExtended['performanceRating']) => {
  switch (rating) {
    case 'excellent': return '#4caf50';
    case 'good': return '#8bc34a';
    case 'average': return '#ff9800';
    case 'poor': return '#f44336';
    default: return '#757575';
  }
};

const calculateEntityMetrics = (entity: EntityProgress): EntityStatusExtended => {
  const processedCount = entity.processedCount || 0;
  const totalCount = entity.totalCount || 0;
  const processingTime = entity.processingTime || 0;
  
  const averageTimePerEntity = processedCount > 0 ? processingTime / processedCount : 0;
  const remainingEntities = Math.max(0, totalCount - processedCount);
  const estimatedCompletion = averageTimePerEntity > 0 && remainingEntities > 0
    ? new Date(Date.now() + (remainingEntities * averageTimePerEntity * 1000))
    : undefined;

  const performanceRating = getPerformanceRating({ ...entity, averageTimePerEntity });

  return {
    ...entity,
    averageTimePerEntity,
    estimatedCompletion,
    performanceRating,
    batchesCompleted: Math.floor(processedCount / 10), // Assuming batch size of 10
    currentBatch: Math.floor(processedCount / 10) + 1,
    totalBatches: Math.ceil(totalCount / 10)
  };
};

const EntityProgressRow: React.FC<{
  entityType: string;
  entity: EntityStatusExtended;
  showPerformanceMetrics: boolean;
  showBatchDetails: boolean;
  onEntityClick?: (entityType: string) => void;
}> = ({ entityType, entity, showPerformanceMetrics, showBatchDetails, onEntityClick }) => {
  const [expanded, setExpanded] = useState(false);
  const theme = useTheme();
  const statusConfig = getEntityStatusConfig(entity.status);

  const handleRowClick = () => {
    if (onEntityClick) {
      onEntityClick(entityType);
    }
  };

  const handleExpandClick = (e: React.MouseEvent) => {
    e.stopPropagation();
    setExpanded(!expanded);
  };

  return (
    <>
      <TableRow
        hover
        onClick={handleRowClick}
        sx={{
          cursor: onEntityClick ? 'pointer' : 'default',
          '&:hover': {
            backgroundColor: alpha(statusConfig.bgColor, 0.1)
          }
        }}
      >
        {/* Entity Type with Icon */}
        <TableCell>
          <Box display="flex" alignItems="center" gap={1}>
            <Avatar
              sx={{
                width: 32,
                height: 32,
                bgcolor: statusConfig.textColor,
                '& .MuiSvgIcon-root': { fontSize: '1rem' }
              }}
            >
              {statusConfig.icon}
            </Avatar>
            <Box>
              <Typography variant="subtitle2" fontWeight="600">
                {entityType}
              </Typography>
              <Typography variant="caption" color="textSecondary">
                {format(entity.startTime, 'HH:mm:ss')}
              </Typography>
            </Box>
          </Box>
        </TableCell>

        {/* Status */}
        <TableCell>
          <Chip
            size="small"
            label={statusConfig.label}
            color={statusConfig.color}
            variant="filled"
            sx={{ fontWeight: 600 }}
          />
        </TableCell>

        {/* Progress Bar */}
        <TableCell sx={{ minWidth: 150 }}>
          <Box>
            <Box display="flex" justifyContent="space-between" alignItems="center" mb={0.5}>
              <Typography variant="body2" fontWeight="600">
                {entity.progressPercentage.toFixed(1)}%
              </Typography>
              <Typography variant="caption" color="textSecondary">
                {entity.processedCount.toLocaleString()} / {entity.totalCount.toLocaleString()}
              </Typography>
            </Box>
            <LinearProgress
              variant="determinate"
              value={entity.progressPercentage}
              sx={{
                height: 6,
                borderRadius: 3,
                backgroundColor: alpha(statusConfig.textColor, 0.1),
                '& .MuiLinearProgress-bar': {
                  borderRadius: 3,
                  backgroundColor: statusConfig.textColor
                }
              }}
            />
          </Box>
        </TableCell>

        {/* Success/Failure Count */}
        <TableCell>
          <Stack direction="row" spacing={1}>
            <Box display="flex" alignItems="center" gap={0.5}>
              <Badge
                badgeContent={entity.successCount}
                color="success"
                max={999999}
                sx={{
                  '& .MuiBadge-badge': {
                    fontSize: '0.75rem',
                    height: 20,
                    minWidth: 20
                  }
                }}
              >
                <CompletedIcon fontSize="small" sx={{ color: 'success.main' }} />
              </Badge>
            </Box>
            {entity.failureCount > 0 && (
              <Box display="flex" alignItems="center" gap={0.5}>
                <Badge
                  badgeContent={entity.failureCount}
                  color="error"
                  max={999999}
                  sx={{
                    '& .MuiBadge-badge': {
                      fontSize: '0.75rem',
                      height: 20,
                      minWidth: 20
                    }
                  }}
                >
                  <ErrorIcon fontSize="small" sx={{ color: 'error.main' }} />
                </Badge>
              </Box>
            )}
          </Stack>
        </TableCell>

        {/* Performance Metrics */}
        {showPerformanceMetrics && (
          <TableCell>
            <Box display="flex" alignItems="center" gap={1}>
              <Box
                sx={{
                  width: 8,
                  height: 8,
                  borderRadius: '50%',
                  backgroundColor: getPerformanceColor(entity.performanceRating)
                }}
              />
              <Typography variant="body2" fontWeight="600">
                {entity.averageTimePerEntity ? `${entity.averageTimePerEntity.toFixed(2)}s` : 'N/A'}
              </Typography>
              <Typography variant="caption" color="textSecondary">
                /entity
              </Typography>
            </Box>
          </TableCell>
        )}

        {/* Estimated Completion */}
        <TableCell>
          <Typography variant="body2">
            {entity.estimatedCompletion
              ? formatDistanceToNow(entity.estimatedCompletion, { addSuffix: true })
              : 'Calculating...'
            }
          </Typography>
        </TableCell>

        {/* Expand/Collapse Button */}
        <TableCell>
          <IconButton size="small" onClick={handleExpandClick}>
            {expanded ? <ExpandLessIcon /> : <ExpandMoreIcon />}
          </IconButton>
        </TableCell>
      </TableRow>

      {/* Expanded Details */}
      <TableRow>
        <TableCell colSpan={showPerformanceMetrics ? 7 : 6} sx={{ py: 0 }}>
          <Collapse in={expanded} timeout="auto" unmountOnExit>
            <Box sx={{ margin: 2 }}>
              <Grid container spacing={2}>
                {/* Batch Information */}
                {showBatchDetails && (
                  <Grid item xs={12} md={6}>
                    <Card variant="outlined">
                      <CardContent sx={{ p: 2 }}>
                        <Typography variant="subtitle2" gutterBottom display="flex" alignItems="center" gap={1}>
                          <TimelineIcon fontSize="small" />
                          Batch Progress
                        </Typography>
                        <Box display="flex" justifyContent="space-between" mb={1}>
                          <Typography variant="body2">Current Batch:</Typography>
                          <Typography variant="body2" fontWeight="600">
                            {entity.currentBatch} / {entity.totalBatches}
                          </Typography>
                        </Box>
                        <Box display="flex" justifyContent="space-between" mb={1}>
                          <Typography variant="body2">Batches Completed:</Typography>
                          <Typography variant="body2" fontWeight="600">
                            {entity.batchesCompleted}
                          </Typography>
                        </Box>
                        <LinearProgress
                          variant="determinate"
                          value={(entity.batchesCompleted || 0) / (entity.totalBatches || 1) * 100}
                          sx={{ mt: 1 }}
                        />
                      </CardContent>
                    </Card>
                  </Grid>
                )}

                {/* Performance Details */}
                {showPerformanceMetrics && (
                  <Grid item xs={12} md={6}>
                    <Card variant="outlined">
                      <CardContent sx={{ p: 2 }}>
                        <Typography variant="subtitle2" gutterBottom display="flex" alignItems="center" gap={1}>
                          <MetricsIcon fontSize="small" />
                          Performance Metrics
                        </Typography>
                        <Box display="flex" justifyContent="space-between" mb={1}>
                          <Typography variant="body2">Rating:</Typography>
                          <Chip
                            size="small"
                            label={entity.performanceRating?.toUpperCase()}
                            sx={{
                              backgroundColor: getPerformanceColor(entity.performanceRating),
                              color: 'white',
                              fontWeight: 600,
                              fontSize: '0.7rem'
                            }}
                          />
                        </Box>
                        <Box display="flex" justifyContent="space-between" mb={1}>
                          <Typography variant="body2">Processing Time:</Typography>
                          <Typography variant="body2" fontWeight="600">
                            {(entity.processingTime / 60).toFixed(1)} min
                          </Typography>
                        </Box>
                        <Box display="flex" justifyContent="space-between">
                          <Typography variant="body2">Success Rate:</Typography>
                          <Typography variant="body2" fontWeight="600" color="success.main">
                            {((entity.successCount / Math.max(entity.processedCount, 1)) * 100).toFixed(1)}%
                          </Typography>
                        </Box>
                      </CardContent>
                    </Card>
                  </Grid>
                )}

                {/* Timeline Information */}
                <Grid item xs={12}>
                  <Card variant="outlined">
                    <CardContent sx={{ p: 2 }}>
                      <Typography variant="subtitle2" gutterBottom display="flex" alignItems="center" gap={1}>
                        <TimelineIcon fontSize="small" />
                        Timeline
                      </Typography>
                      <Grid container spacing={2}>
                        <Grid item xs={4}>
                          <Typography variant="caption" color="textSecondary">Started</Typography>
                          <Typography variant="body2" fontWeight="600">
                            {format(entity.startTime, 'MMM dd, HH:mm:ss')}
                          </Typography>
                        </Grid>
                        {entity.endTime && (
                          <Grid item xs={4}>
                            <Typography variant="caption" color="textSecondary">Completed</Typography>
                            <Typography variant="body2" fontWeight="600">
                              {format(entity.endTime, 'MMM dd, HH:mm:ss')}
                            </Typography>
                          </Grid>
                        )}
                        <Grid item xs={4}>
                          <Typography variant="caption" color="textSecondary">Duration</Typography>
                          <Typography variant="body2" fontWeight="600">
                            {formatDistanceToNow(entity.startTime)}
                          </Typography>
                        </Grid>
                      </Grid>
                    </CardContent>
                  </Card>
                </Grid>
              </Grid>
            </Box>
          </Collapse>
        </TableCell>
      </TableRow>
    </>
  );
};

export const RealTimeEntityGrid: React.FC<RealTimeEntityGridProps> = ({
  migrationId,
  entityProgress,
  overallProgress,
  showPerformanceMetrics = true,
  showBatchDetails = true,
  onEntityClick,
  onRefresh
}) => {
  const theme = useTheme();

  // Calculate enhanced metrics for each entity
  const enhancedEntities = useMemo(() => {
    return Object.entries(entityProgress).map(([entityType, entity]) => ({
      entityType,
      entity: calculateEntityMetrics(entity)
    }));
  }, [entityProgress]);

  // Sort entities by status (running first, then by progress)
  const sortedEntities = useMemo(() => {
    return enhancedEntities.sort((a, b) => {
      // Priority order: running > pending > completed > failed
      const statusPriority: Record<string, number> = {
        'running': 1,
        'in_progress': 1,
        'pending': 2,
        'waiting': 2,
        'completed': 3,
        'finished': 3,
        'failed': 4,
        'error': 4
      };

      const aPriority = statusPriority[a.entity.status.toLowerCase()] || 5;
      const bPriority = statusPriority[b.entity.status.toLowerCase()] || 5;

      if (aPriority !== bPriority) {
        return aPriority - bPriority;
      }

      // If same priority, sort by progress (descending)
      return b.entity.progressPercentage - a.entity.progressPercentage;
    });
  }, [enhancedEntities]);

  if (enhancedEntities.length === 0) {
    return (
      <Card>
        <CardContent sx={{ textAlign: 'center', py: 4 }}>
          <Typography color="textSecondary">
            No entity progress data available for migration {migrationId}
          </Typography>
        </CardContent>
      </Card>
    );
  }

  return (
    <Card>
      <CardContent sx={{ p: 0 }}>
        {/* Header */}
        <Box sx={{ p: 2, borderBottom: `1px solid ${theme.palette.divider}` }}>
          <Box display="flex" justifyContent="space-between" alignItems="center">
            <Typography variant="h6" fontWeight="600">
              Entity Progress Details
            </Typography>
            <Box display="flex" alignItems="center" gap={1}>
              {overallProgress && (
                <Typography variant="body2" color="textSecondary">
                  Overall: {overallProgress.overallProgressPercentage?.toFixed(1)}%
                </Typography>
              )}
              {onRefresh && (
                <Tooltip title="Refresh data">
                  <IconButton size="small" onClick={onRefresh}>
                    <RefreshIcon />
                  </IconButton>
                </Tooltip>
              )}
            </Box>
          </Box>
        </Box>

        {/* Entity Table */}
        <TableContainer>
          <Table>
            <TableHead>
              <TableRow>
                <TableCell>Entity Type</TableCell>
                <TableCell>Status</TableCell>
                <TableCell>Progress</TableCell>
                <TableCell>Results</TableCell>
                {showPerformanceMetrics && <TableCell>Performance</TableCell>}
                <TableCell>ETA</TableCell>
                <TableCell width={48}></TableCell>
              </TableRow>
            </TableHead>
            <TableBody>
              {sortedEntities.map(({ entityType, entity }) => (
                <EntityProgressRow
                  key={entityType}
                  entityType={entityType}
                  entity={entity}
                  showPerformanceMetrics={showPerformanceMetrics}
                  showBatchDetails={showBatchDetails}
                  onEntityClick={onEntityClick}
                />
              ))}
            </TableBody>
          </Table>
        </TableContainer>
      </CardContent>
    </Card>
  );
}; 