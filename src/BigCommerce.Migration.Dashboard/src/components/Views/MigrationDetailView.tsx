import React, { useState, useEffect, useRef } from 'react';
import { useParams, useNavigate } from 'react-router-dom';
import {
  Box,
  Card,
  CardContent,
  Typography,
  Button,
  IconButton,
  Table,
  TableHead,
  TableBody,
  TableRow,
  TableCell,
  Chip,
  CircularProgress,
  Alert,
  Stack,
  TableContainer,
  Paper,
} from '@mui/material';
import {
  ArrowBack as ArrowBackIcon,
  Error as ErrorIcon,
  CheckCircle as CheckCircleIcon,
  Schedule as ScheduleIcon,
  Download as DownloadIcon,
} from '@mui/icons-material';
import { format } from 'date-fns';
import { apiService } from '../../services/apiService';
import type { MigrationStatus } from '../../types';
import Collapse from '@mui/material/Collapse';
import Pagination from '@mui/material/Pagination';
import Tooltip from '@mui/material/Tooltip';

interface MigrationDetail {
  migrationId: string;
  sourceStore: string;
  destinationStore: string;
  startedAt: string;
  completedAt: string;
  status: MigrationStatus;
  totalEntities: number;
  processedEntities: number;
  successfulEntities: number;
  failedEntities: number;
  percentageCompleted: number;
  entities: string[];
}

interface MigrationError {
  entityId: string;
  entityName?: string;
  entityType: string;
  error: string;
  processedAt: string;
  requestPayloadBlobUrl?: string;
  responsePayloadBlobUrl?: string;
}

interface EntitySummary {
  entityType: string;
  totalCount: number;
  processedCount: number;
  successCount: number;
  failureCount: number;
}

// Status configuration matching history page
const statusOptions: { value: MigrationStatus; label: string; color: 'default' | 'primary' | 'secondary' | 'error' | 'info' | 'success' | 'warning' }[] = [
  { value: 'pending', label: 'Pending', color: 'default' },
  { value: 'running', label: 'Running', color: 'info' },
  { value: 'completed', label: 'Completed', color: 'success' },
  { value: 'failed', label: 'Failed', color: 'error' },
  { value: 'cancelled', label: 'Cancelled', color: 'warning' },
];

export const MigrationDetailView: React.FC = () => {
  const { requestId } = useParams<{ requestId: string }>();
  const navigate = useNavigate();
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [entitySummary, setEntitySummary] = useState<EntitySummary[]>([]);
  const [entitySummaryLoading, setEntitySummaryLoading] = useState(false);
  const [entitySummaryError, setEntitySummaryError] = useState<string | null>(null);
  const [migrationInfo, setMigrationInfo] = useState<{
    migrationId: string;
    sourceStore: string;
    destinationStore: string;
    status: string;
  } | null>(null);
  const entitySummaryFetchedRef = useRef(false);
  const [expandedEntity, setExpandedEntity] = useState<string | null>(null);
  const [entityErrors, setEntityErrors] = useState<Record<string, MigrationError[]>>({});
  const [entityErrorsLoading, setEntityErrorsLoading] = useState<Record<string, boolean>>({});
  const [entityErrorsError, setEntityErrorsError] = useState<Record<string, string | null>>({});
  const [entityErrorsPage, setEntityErrorsPage] = useState<Record<string, number>>({});
  const [entityErrorsTotal, setEntityErrorsTotal] = useState<Record<string, number>>({});
  const pageSize = 10;

  useEffect(() => {
    if (!requestId) {
      setError('Migration ID is required');
      setLoading(false);
      return;
    }

    if (!entitySummaryFetchedRef.current) {
      fetchEntitySummary();
    }
  }, [requestId]);



  const fetchEntitySummary = async () => {
    if (entitySummaryFetchedRef.current || entitySummaryLoading) {
      return; // Prevent duplicate calls
    }
    
    entitySummaryFetchedRef.current = true;
    setEntitySummaryLoading(true);
    setEntitySummaryError(null);
    try {
      const summaryArr = await apiService.getMigrationEntityBreakdown(requestId!);
      
      // Extract migration info from the response
      setMigrationInfo({
        migrationId: summaryArr.migrationId || requestId!,
        sourceStore: summaryArr.sourceStore || 'Unknown',
        destinationStore: summaryArr.destinationStore || 'Unknown',
        status: summaryArr.status || 'Unknown',
      });
      
      const rawEntities = Array.isArray(summaryArr.entities) ? summaryArr.entities : [];
      
      // Transform the API data to match our UI interface
      const transformedEntities: EntitySummary[] = rawEntities.map((entity: any) => ({
        entityType: entity.entity || 'Unknown',
        totalCount: entity.totalEntities || 0,
        processedCount: (entity.successfulEntities || 0) + (entity.failedEntities || 0) + (entity.skippedEntities || 0),
        successCount: entity.successfulEntities || 0,
        failureCount: entity.failedEntities || 0,
      }));
      
      setEntitySummary(transformedEntities);
    } catch (err) {
      setEntitySummaryError('Failed to fetch entity summary');
      setEntitySummary([]);
      entitySummaryFetchedRef.current = false; // Reset on error to allow retry
    } finally {
      setEntitySummaryLoading(false);
    }
  };

  const handleErrorPageChange = async (entityType: string, page: number) => {
    setEntityErrorsPage((prev) => ({ ...prev, [entityType]: page }));
    setEntityErrorsLoading((prev) => ({ ...prev, [entityType]: true }));
    setEntityErrorsError((prev) => ({ ...prev, [entityType]: null }));
    
    try {
      const errors = await apiService.getMigrationEntityErrors(requestId!, entityType);
      const mappedErrors: MigrationError[] = errors.map((err) => ({
        entityId: err.entityId,
        entityName: err.entityName,
        entityType: err.entityType,
        error: err.errorMessage || 'Unknown error',
        processedAt: err.timestamp,
        requestPayloadBlobUrl: (err as any).requestPayloadBlobUrl,
        responsePayloadBlobUrl: (err as any).responsePayloadBlobUrl,
      }));
      
      setEntityErrors((prev) => ({ ...prev, [entityType]: mappedErrors }));
      setEntityErrorsTotal((prev) => ({ ...prev, [entityType]: mappedErrors.length }));
    } catch (err) {
      setEntityErrorsError((prev) => ({ ...prev, [entityType]: 'Failed to fetch error details' }));
    } finally {
      setEntityErrorsLoading((prev) => ({ ...prev, [entityType]: false }));
    }
  };

  // Update handleRowClick to support pagination
  const handleRowClick = async (entityType: string) => {
    if (expandedEntity === entityType) {
      setExpandedEntity(null);
      return;
    }
    setExpandedEntity(entityType);
    if (!entityErrors[entityType]) {
      setEntityErrorsPage((prev) => ({ ...prev, [entityType]: 1 }));
      await handleErrorPageChange(entityType, 1);
    }
  };

  const getStatusColor = (status: MigrationStatus) => {
    switch (status) {
      case 'completed': return 'success';
      case 'running': return 'info';
      case 'failed': return 'error';
      case 'cancelled': return 'warning';
      case 'pending': return 'default';
      default: return 'default';
    }
  };

  const getStatusIcon = (status: MigrationStatus) => {
    switch (status) {
      case 'completed': return <CheckCircleIcon />;
      case 'running': return <ScheduleIcon />;
      case 'failed': return <ErrorIcon />;
      case 'cancelled': return <ErrorIcon />;
      case 'pending': return <ScheduleIcon />;
      default: return <ScheduleIcon />;
    }
  };

  const formatDate = (dateString: string) => {
    try {
      return format(new Date(dateString), 'EEE, dd-MMM-yyyy');
    } catch {
      return dateString;
    }
  };

  const formatDateTime = (dateString: string) => {
    try {
      return format(new Date(dateString), 'EEE, dd-MMM-yyyy HH:mm:ss');
    } catch {
      return dateString;
    }
  };

  if (error) {
    return (
      <Box sx={{ p: 3 }}>
        <Alert severity="error" sx={{ mb: 2 }}>
          {error}
        </Alert>
        <Button
          variant="outlined"
          startIcon={<ArrowBackIcon />}
          onClick={() => navigate('/history')}
        >
          Back to Migration History
        </Button>
      </Box>
    );
  }

  return (
    <Box sx={{ p: 3 }}>
      {/* Back Link */}
      <Button
        variant="outlined"
        startIcon={<ArrowBackIcon />}
        onClick={() => navigate('/history')}
        sx={{
          mb: 3,
          textTransform: 'none',
          fontWeight: 500,
          borderColor: 'divider',
        }}
      >
        Back to Migration History
      </Button>

      {/* Entity Summary Grid (only new UI) */}
      <Card sx={{ mb: 3 }}>
        <CardContent>
          <Box sx={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', mb: 3 }}>
            <Typography variant="h6" sx={{ fontWeight: 600 }}>
              Entity Summary
            </Typography>
            {migrationInfo && (
              <Chip 
                label={migrationInfo.status.charAt(0).toUpperCase() + migrationInfo.status.slice(1)}
                color={getStatusColor(migrationInfo.status as MigrationStatus)}
                size="small"
                variant="outlined"
              />
            )}
          </Box>
          
          {migrationInfo && (
            <Box sx={{ mb: 3 }}>
              <Box sx={{ 
                display: 'grid', 
                gridTemplateColumns: '1fr 1fr 1fr', 
                gap: 4, 
                mb: 3,
                alignItems: 'start'
              }}>
                <Box>
                  <Typography variant="body2" color="text.secondary" sx={{ fontWeight: 500, mb: 0.5 }}>
                    Migration ID
                  </Typography>
                  <Typography 
                    variant="body1" 
                    sx={{ 
                      fontFamily: 'monospace', 
                      fontSize: '0.875rem', 
                      color: 'primary.main',
                      wordBreak: 'break-all'
                    }}
                  >
                    {migrationInfo.migrationId}
                  </Typography>
                </Box>
                <Box>
                  <Typography variant="body2" color="text.secondary" sx={{ fontWeight: 500, mb: 0.5 }}>
                    Source Store
                  </Typography>
                  <Typography variant="body1" sx={{ fontWeight: 500 }}>
                    {migrationInfo.sourceStore}
                  </Typography>
                </Box>
                <Box>
                  <Typography variant="body2" color="text.secondary" sx={{ fontWeight: 500, mb: 0.5 }}>
                    Destination Store  
                  </Typography>
                  <Typography variant="body1" sx={{ fontWeight: 500 }}>
                    {migrationInfo.destinationStore}
                  </Typography>
                </Box>
              </Box>
            </Box>
          )}
          {entitySummaryLoading ? (
            <CircularProgress size={24} />
          ) : entitySummaryError ? (
            <Alert severity="error">{entitySummaryError}</Alert>
          ) : (Array.isArray(entitySummary) && entitySummary.length === 0) ? (
            <Typography variant="body2" color="text.secondary">No entity summary data available for this migration.</Typography>
          ) : (
            <TableContainer component={Paper}>
              <Table size="small">
                <TableHead>
                  <TableRow>
                    <TableCell sx={{ fontWeight: 600, textAlign: 'center' }}>Entity Type</TableCell>
                    <TableCell sx={{ fontWeight: 600, textAlign: 'center' }}>Total</TableCell>
                    <TableCell sx={{ fontWeight: 600, textAlign: 'center' }}>Success</TableCell>
                    <TableCell sx={{ fontWeight: 600, textAlign: 'center' }}>Failed</TableCell>
                    <TableCell sx={{ fontWeight: 600, textAlign: 'center' }}>Success Rate</TableCell>
                    <TableCell sx={{ fontWeight: 600, textAlign: 'center' }}>Status</TableCell>
                  </TableRow>
                </TableHead>
                <TableBody>
                  {(Array.isArray(entitySummary) ? entitySummary : []).map((entity) => [
                    <TableRow
                      key={`summary-${entity.entityType}`}
                      hover
                      onClick={() => handleRowClick(entity.entityType)}
                      style={{ cursor: 'pointer' }}
                      selected={expandedEntity === entity.entityType}
                      tabIndex={0}
                      role="button"
                      aria-label={`Expand error details for ${entity.entityType}`}
                      onKeyDown={(e) => {
                        if (e.key === 'Enter' || e.key === ' ') handleRowClick(entity.entityType);
                      }}
                                          >
                        <TableCell sx={{ textAlign: 'center' }}>{entity.entityType}</TableCell>
                        <TableCell sx={{ textAlign: 'center' }}>{entity.totalCount}</TableCell>
                        <TableCell sx={{ textAlign: 'center', color: 'success.main', fontWeight: 600 }}>{entity.successCount}</TableCell>
                        <TableCell sx={{ textAlign: 'center', color: 'error.main', fontWeight: 600 }}>{entity.failureCount}</TableCell>
                        <TableCell sx={{ textAlign: 'center' }}>{entity.totalCount > 0 ? `${Math.round((entity.successCount / entity.totalCount) * 100)}%` : '0%'}</TableCell>
                        <TableCell sx={{ textAlign: 'center' }}>
                          <Chip 
                            label={entity.failureCount > 0 ? 'Failed' : entity.successCount > 0 ? 'Completed' : 'Pending'} 
                            color={entity.failureCount > 0 ? 'error' : entity.successCount > 0 ? 'success' : 'default'}
                            size="small"
                            variant="outlined"
                          />
                        </TableCell>
                      </TableRow>,
                    <TableRow key={`expand-${entity.entityType}`}>
                      <TableCell colSpan={6} sx={{ p: 0, border: 0 }}>
                        <Collapse in={expandedEntity === entity.entityType} timeout="auto" unmountOnExit>
                          <Box sx={{ p: 2, backgroundColor: 'background.default' }}>
                            {entityErrorsLoading[entity.entityType] ? (
                              <CircularProgress size={20} />
                            ) : entityErrorsError[entity.entityType] ? (
                              <Alert severity="error">{entityErrorsError[entity.entityType]}</Alert>
                            ) : entityErrors[entity.entityType] && entityErrors[entity.entityType].length > 0 ? (
                              <>
                                <Table size="small">
                                  <TableHead>
                                    <TableRow>
                                      <TableCell>Name</TableCell>
                                      <TableCell>Error</TableCell>
                                      <TableCell>Processed At</TableCell>
                                      <TableCell>Request</TableCell>
                                      <TableCell>Response</TableCell>
                                    </TableRow>
                                  </TableHead>
                                  <TableBody>
                                    {entityErrors[entity.entityType]
                                      .slice(((entityErrorsPage[entity.entityType] || 1) - 1) * pageSize, (entityErrorsPage[entity.entityType] || 1) * pageSize)
                                      .map((err, idx) => (
                                        <TableRow key={`${entity.entityType}-error-${err.entityId || err.entityName || idx}`}>
                                          <TableCell>{err.entityName ? `${err.entityName} (${err.entityId})` : err.entityId}</TableCell>
                                          <TableCell>{err.error}</TableCell>
                                          <TableCell>{formatDateTime(err.processedAt)}</TableCell>
                                          <TableCell>
                                            <Tooltip title="Download/View Request Payload">
                                              <span>
                                                <IconButton
                                                  size="small"
                                                  aria-label="Download or view request payload"
                                                  onClick={() => {
                                                    if (err.requestPayloadBlobUrl) {
                                                      window.open(err.requestPayloadBlobUrl, '_blank');
                                                    } else {
                                                      alert('Request payload not available for this error.');
                                                    }
                                                  }}
                                                  disabled={!err.requestPayloadBlobUrl}
                                                  sx={{ color: err.requestPayloadBlobUrl ? 'primary.main' : 'text.disabled' }}
                                                  tabIndex={0}
                                                >
                                                  <DownloadIcon />
                                                </IconButton>
                                              </span>
                                            </Tooltip>
                                          </TableCell>
                                          <TableCell>
                                            <Tooltip title="Download/View Response Payload">
                                              <span>
                                                <IconButton
                                                  size="small"
                                                  aria-label="Download or view response payload"
                                                  onClick={() => {
                                                    if (err.responsePayloadBlobUrl) {
                                                      window.open(err.responsePayloadBlobUrl, '_blank');
                                                    } else {
                                                      alert('Response payload not available for this error.');
                                                    }
                                                  }}
                                                  disabled={!err.responsePayloadBlobUrl}
                                                  sx={{ color: err.responsePayloadBlobUrl ? 'primary.main' : 'text.disabled' }}
                                                  tabIndex={0}
                                                >
                                                  <DownloadIcon />
                                                </IconButton>
                                              </span>
                                            </Tooltip>
                                          </TableCell>
                                        </TableRow>
                                      ))}
                                  </TableBody>
                                </Table>
                                <Box sx={{ display: 'flex', justifyContent: 'flex-end', mt: 2 }}>
                                  <Pagination
                                    count={Math.ceil((entityErrorsTotal[entity.entityType] || 0) / pageSize)}
                                    page={entityErrorsPage[entity.entityType] || 1}
                                    onChange={(_, page) => handleErrorPageChange(entity.entityType, page)}
                                    size="small"
                                    aria-label={`Pagination for ${entity.entityType} errors`}
                                  />
                                </Box>
                              </>
                            ) : (
                              <Typography variant="body2" color="text.secondary">No errors found for this entity type.</Typography>
                            )}
                          </Box>
                        </Collapse>
                      </TableCell>
                    </TableRow>
                  ])}
                </TableBody>
              </Table>
            </TableContainer>
          )}
        </CardContent>
      </Card>
    </Box>
  );
}; 