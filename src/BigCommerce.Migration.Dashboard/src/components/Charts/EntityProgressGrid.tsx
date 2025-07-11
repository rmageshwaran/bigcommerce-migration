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
import type { EntityProgress } from '../../types';

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
  height = 600,
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

  const handleChangePage = (event: unknown, newPage: number) => {
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

  const getStatusColor = (status: string) => {
    switch (status) {
      case 'completed':
        return theme.palette.success.main;
      case 'failed':
        return theme.palette.error.main;
      case 'cancelled':
        return theme.palette.action.disabled;
      case 'running':
        return theme.palette.primary.main;
      default:
        return theme.palette.warning.main;
    }
  };

  const getProgressColor = (percentage: number) => {
    if (percentage >= 100) return 'success';
    if (percentage >= 75) return 'info';
    if (percentage >= 50) return 'warning';
    return 'error';
  };

  const formatEntityType = (entityType: string) => {
    return entityType.charAt(0).toUpperCase() + entityType.slice(1);
  };

  const paginatedRows = showPagination 
    ? entityRows.slice(page * rowsPerPage, page * rowsPerPage + rowsPerPage)
    : entityRows;

  return (
    <Card>
      <CardContent>
        <Box sx={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', mb: 2 }}>
          <Typography variant="h6" component="div">
            Entity Progress Details
          </Typography>
          
          <Box sx={{ display: 'flex', alignItems: 'center', gap: 1 }}>
            <Typography variant="body2" color="text.secondary">
              {entityRows.length} entities
            </Typography>
                         <IconButton 
               size="small" 
               onClick={() => refreshData()}
               title="Refresh data"
             >
              <RefreshIcon />
            </IconButton>
          </Box>
        </Box>

        <TableContainer sx={{ height: height - 120 }}>
          <Table stickyHeader>
            <TableHead>
              <TableRow>
                <TableCell>Migration</TableCell>
                <TableCell>Entity Type</TableCell>
                <TableCell align="right">Progress</TableCell>
                <TableCell align="right">Count</TableCell>
                <TableCell align="right">Success</TableCell>
                <TableCell align="right">Errors</TableCell>
                <TableCell align="right">Speed</TableCell>
                <TableCell align="right">ETA</TableCell>
                <TableCell align="center">Status</TableCell>
              </TableRow>
            </TableHead>
            <TableBody>
              {paginatedRows.map((row, index) => (
                <TableRow 
                  key={`${row.migrationId}-${row.entityType}`}
                  sx={{ 
                    '&:nth-of-type(odd)': { 
                      backgroundColor: alpha(theme.palette.action.hover, 0.04) 
                    } 
                  }}
                >
                  <TableCell>
                    <Typography variant="body2" sx={{ fontFamily: 'monospace' }}>
                      {row.migrationId.slice(-8)}
                    </Typography>
                  </TableCell>
                  
                  <TableCell>
                    <Typography variant="body2" sx={{ fontWeight: 'medium' }}>
                      {formatEntityType(row.entityType)}
                    </Typography>
                  </TableCell>
                  
                  <TableCell align="right">
                    <Box sx={{ minWidth: 120 }}>
                      <Box sx={{ display: 'flex', alignItems: 'center', mb: 0.5 }}>
                        <Typography variant="body2" sx={{ minWidth: 45 }}>
                          {row.progressPercentage.toFixed(1)}%
                        </Typography>
                      </Box>
                      <LinearProgress 
                        variant="determinate" 
                        value={row.progressPercentage}
                        color={getProgressColor(row.progressPercentage)}
                        sx={{ height: 6, borderRadius: 3 }}
                      />
                    </Box>
                  </TableCell>
                  
                  <TableCell align="right">
                    <Typography variant="body2">
                      {row.processedCount.toLocaleString()} / {row.totalCount.toLocaleString()}
                    </Typography>
                  </TableCell>
                  
                  <TableCell align="right">
                    <Typography variant="body2" color="success.main">
                      {row.successCount.toLocaleString()}
                    </Typography>
                  </TableCell>
                  
                  <TableCell align="right">
                    <Typography 
                      variant="body2" 
                      color={row.errorCount > 0 ? "error.main" : "text.secondary"}
                    >
                      {row.errorCount.toLocaleString()}
                    </Typography>
                  </TableCell>
                  
                  <TableCell align="right">
                    <Typography variant="body2">
                      {row.processingSpeed.toFixed(1)}/s
                    </Typography>
                  </TableCell>
                  
                  <TableCell align="right">
                    <Typography variant="body2" color="text.secondary">
                      {row.status === 'completed' ? 'Done' : row.estimatedTimeRemaining}
                    </Typography>
                  </TableCell>
                  
                  <TableCell align="center">
                    <Chip
                      icon={getStatusIcon(row.status)}
                      label={row.status.charAt(0).toUpperCase() + row.status.slice(1)}
                      size="small"
                      sx={{
                        backgroundColor: alpha(getStatusColor(row.status), 0.1),
                        color: getStatusColor(row.status),
                        border: `1px solid ${alpha(getStatusColor(row.status), 0.3)}`,
                      }}
                    />
                  </TableCell>
                </TableRow>
              ))}
              
              {paginatedRows.length === 0 && (
                <TableRow>
                  <TableCell colSpan={9} align="center">
                    <Typography variant="body2" color="text.secondary" sx={{ py: 4 }}>
                      No migration entities to display
                    </Typography>
                  </TableCell>
                </TableRow>
              )}
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