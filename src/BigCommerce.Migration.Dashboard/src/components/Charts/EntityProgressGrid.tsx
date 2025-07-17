import React, { useState } from 'react';
import {
  Box,
  Card,
  CardContent,
  Typography,
  LinearProgress,
  Chip,
  IconButton,
  Table,
  TableBody,
  TableCell,
  TableContainer,
  TableHead,
  TableRow,
  TablePagination,
  useTheme,
  alpha,
} from '@mui/material';
import {
  Refresh as RefreshIcon,
  CheckCircle as CheckCircleIcon,
  Error as ErrorIcon,
  Warning as WarningIcon,
  PlayArrow as PlayArrowIcon,
  Pause as PauseIcon,
} from '@mui/icons-material';
import { useDashboard } from '../../context/DashboardContext';

interface EntityProgressGridProps {
  showPagination?: boolean;
  height?: number;
}

interface EntityProgressRow {
  migrationId: string;
  entityType: string;
  totalCount: number;
  processedCount: number;
  successCount: number;
  errorCount: number;
  progressPercentage: number;
  processingSpeed: number;
  status: 'running' | 'completed' | 'failed' | 'cancelled';
  estimatedTimeRemaining: string;
  lastUpdated: Date;
}

export const EntityProgressGrid: React.FC<EntityProgressGridProps> = ({
  showPagination = true,
}) => {
  const theme = useTheme();
  const { state, refreshData } = useDashboard();
  const { activeMigrations } = state;
  const [page, setPage] = useState(0);
  const [rowsPerPage, setRowsPerPage] = useState(10);

  // Convert migrations to entity progress rows
  const getEntityProgressRows = (): EntityProgressRow[] => {
    const rows: EntityProgressRow[] = [];
    
    activeMigrations.forEach((migration) => {
      Object.entries(migration.entityProgress).forEach(([entityType, progress]) => {
        const progressPercentage = progress.totalCount > 0 
          ? (progress.processedCount / progress.totalCount) * 100 
          : 0;
        
        const processingSpeed = migration.entitiesPerSecond * 
          (progress.processedCount / Math.max(1, Object.values(migration.entityProgress).reduce((sum, ep) => sum + ep.processedCount, 0)));
        
        const remaining = progress.totalCount - progress.processedCount;
        const estimatedMinutes = processingSpeed > 0 ? remaining / (processingSpeed * 60) : 0;
        const estimatedTimeRemaining = estimatedMinutes > 60 
          ? `${Math.ceil(estimatedMinutes / 60)}h ${Math.ceil(estimatedMinutes % 60)}m`
          : `${Math.ceil(estimatedMinutes)}m`;

        let status: 'running' | 'completed' | 'failed' | 'cancelled' = 'running';
        if (progressPercentage >= 100) {
          status = 'completed';
        } else if (migration.status === 'failed') {
          status = 'failed';
        } else if (migration.status === 'cancelled') {
          status = 'cancelled';
        }

        rows.push({
          migrationId: migration.migrationId,
          entityType,
          totalCount: progress.totalCount,
          processedCount: progress.processedCount,
          successCount: progress.successCount,
          errorCount: progress.failureCount,
          progressPercentage,
          processingSpeed,
          status,
          estimatedTimeRemaining,
          lastUpdated: migration.lastUpdated,
        });
      });
    });

    return rows.sort((a, b) => {
      // Sort by migration ID first, then by entity type
      if (a.migrationId !== b.migrationId) {
        return a.migrationId.localeCompare(b.migrationId);
      }
      return a.entityType.localeCompare(b.entityType);
    });
  };

  const entityRows = getEntityProgressRows();

  const handleChangePage = (_: unknown, newPage: number) => {
    setPage(newPage);
  };

  const handleChangeRowsPerPage = (event: React.ChangeEvent<HTMLInputElement>) => {
    setRowsPerPage(parseInt(event.target.value, 10));
    setPage(0);
  };

  const getStatusIcon = (status: string) => {
    switch (status) {
      case 'completed':
        return <CheckCircleIcon color="success" />;
      case 'failed':
        return <ErrorIcon color="error" />;
      case 'cancelled':
        return <PauseIcon color="action" />;
      case 'running':
        return <PlayArrowIcon color="primary" />;
      default:
        return <WarningIcon color="warning" />;
    }
  };

  const getStatusColor = (status: string): 'default' | 'primary' | 'secondary' | 'error' | 'info' | 'success' | 'warning' => {
    switch (status) {
      case 'completed':
        return 'success';
      case 'failed':
        return 'error';
      case 'cancelled':
        return 'default';
      case 'running':
        return 'primary';
      default:
        return 'warning';
    }
  };

  const getProgressColor = (percentage: number) => {
    if (percentage >= 100) return theme.palette.success.main;
    if (percentage >= 75) return theme.palette.info.main;
    if (percentage >= 50) return theme.palette.warning.main;
    return theme.palette.error.main;
  };



  const paginatedRows = showPagination 
    ? entityRows.slice(page * rowsPerPage, page * rowsPerPage + rowsPerPage)
    : entityRows;

  return (
    <Card sx={{ height: '100%' }}>
      <CardContent>
        <Box sx={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', mb: 2 }}>
          <Typography variant="h6" component="h2">
            Entity Progress
          </Typography>
          <IconButton onClick={refreshData} size="small">
            <RefreshIcon />
          </IconButton>
        </Box>

        <TableContainer sx={{ maxHeight: 400 }}>
          <Table stickyHeader size="small">
            <TableHead>
              <TableRow>
                <TableCell sx={{ fontWeight: 600 }}>Entity Type</TableCell>
                <TableCell sx={{ fontWeight: 600 }}>Progress</TableCell>
                <TableCell sx={{ fontWeight: 600 }}>Status</TableCell>
                <TableCell sx={{ fontWeight: 600 }}>Speed</TableCell>
                <TableCell sx={{ fontWeight: 600 }}>ETA</TableCell>
              </TableRow>
            </TableHead>
            <TableBody>
              {paginatedRows.map((row) => (
                <TableRow key={`${row.migrationId}-${row.entityType}`}>
                  <TableCell>
                    <Typography variant="body2" sx={{ fontWeight: 500 }}>
                      {row.entityType}
                    </Typography>
                    <Typography variant="caption" color="text.secondary">
                      {row.processedCount} / {row.totalCount}
                    </Typography>
                  </TableCell>
                  <TableCell>
                    <Box sx={{ width: '100%', mr: 1 }}>
                      <LinearProgress
                        variant="determinate"
                        value={row.progressPercentage}
                        sx={{
                          height: 8,
                          borderRadius: 4,
                          backgroundColor: alpha(theme.palette.primary.main, 0.1),
                          '& .MuiLinearProgress-bar': {
                            borderRadius: 4,
                            backgroundColor: getProgressColor(row.progressPercentage),
                          },
                        }}
                      />
                    </Box>
                    <Typography variant="caption" color="text.secondary">
                      {row.progressPercentage.toFixed(1)}%
                    </Typography>
                  </TableCell>
                  <TableCell>
                    <Chip
                      icon={getStatusIcon(row.status)}
                      label={row.status}
                      size="small"
                      color={getStatusColor(row.status)}
                      variant="outlined"
                    />
                  </TableCell>
                  <TableCell>
                    <Typography variant="body2">
                      {row.processingSpeed.toFixed(1)} items/s
                    </Typography>
                  </TableCell>
                  <TableCell>
                    <Typography variant="body2">
                      {row.estimatedTimeRemaining}
                    </Typography>
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        </TableContainer>

        {showPagination && entityRows.length > 0 && (
          <TablePagination
            rowsPerPageOptions={[5, 10, 25, 50]}
            component="div"
            count={entityRows.length}
            rowsPerPage={rowsPerPage}
            page={page}
            onPageChange={handleChangePage}
            onRowsPerPageChange={handleChangeRowsPerPage}
            sx={{ borderTop: 1, borderColor: 'divider' }}
          />
        )}
      </CardContent>
    </Card>
  );
}; 