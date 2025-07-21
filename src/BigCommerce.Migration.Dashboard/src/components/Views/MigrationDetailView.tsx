import React, { useState, useEffect } from 'react';
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

export const MigrationDetailView: React.FC = () => {
  const { requestId } = useParams<{ requestId: string }>();
  const navigate = useNavigate();
  const [migration, setMigration] = useState<MigrationDetail | null>(null);
  const [errors, setErrors] = useState<MigrationError[]>([]);
  const [debugInfo, setDebugInfo] = useState<string>('');
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (!requestId) {
      setError('Migration ID is required');
      setLoading(false);
      return;
    }

    fetchMigrationDetails();
  }, [requestId]);

  const fetchMigrationDetails = async () => {
    try {
      setLoading(true);
      setError(null);

      // Fetch migration details
      const migrationResponse = await apiService.getMigrationProgress(requestId!);
      console.log('✅ Migration details response:', migrationResponse);
      console.log('✅ Migration entities:', (migrationResponse as any).entities);
      console.log('✅ Migration failed entities:', migrationResponse.failedEntities);

      // Transform the response to match our interface
      const response = migrationResponse as any;
      const migrationDetail: MigrationDetail = {
        migrationId: response.migrationId,
        sourceStore: response.sourceStore || 'Unknown',
        destinationStore: response.destinationStore || 'Unknown',
        startedAt: response.createdAt || response.startTime?.toString() || new Date().toISOString(),
        completedAt: response.updatedAt || response.lastUpdated?.toString() || new Date().toISOString(),
        status: response.status,
        totalEntities: response.progress?.totalEntities || response.totalEntities || 0,
        processedEntities: response.progress?.processedEntities || response.processedEntities || 0,
        successfulEntities: response.progress?.completedEntities || response.successfulEntities || 0,
        failedEntities: response.progress?.failedEntities || response.failedEntities || 0,
        percentageCompleted: response.progress?.overallProgress || response.overallProgressPercentage || 0,
        entities: response.entities || Object.keys(response.entityProgress || {}),
      };

      setMigration(migrationDetail);

      // Fetch migration errors if there are any failures
      const failedEntities = response.progress?.failedEntities || response.failedEntities || 0;
      if (failedEntities > 0) {
        console.log('🔍 Fetching errors for failed entities:', failedEntities);
        console.log('🔍 Entity types:', response.entities);
        try {
          // Try to fetch errors for each entity type
          const allErrors: MigrationError[] = [];
          
          for (const entityType of response.entities || []) {
            try {
              console.log(`🔍 Fetching errors for entity type: ${entityType}`);
              const errorsResponse = await apiService.getMigrationEntityErrors(requestId!, entityType);
              console.log(`✅ Raw errors response for ${entityType}:`, errorsResponse);
              
              // Transform errors to match our interface
              if (Array.isArray(errorsResponse)) {
                console.log(`✅ Processing ${errorsResponse.length} errors for ${entityType}`);
                const entityErrors = errorsResponse.map((err: any) => {
                  // Transform Azurite URLs to Functions API URLs for browser access
                  const transformBlobUrl = (blobUrl: string | undefined) => {
                    if (!blobUrl) return undefined;
                    
                    // If it's an Azurite URL, transform it to use the Functions API
                    if (blobUrl.includes('azurite:10000')) {
                      // Extract the path from the Azurite URL
                      const url = new URL(blobUrl);
                      const pathParts = url.pathname.split('/');
                      
                      // The path should be: /devstoreaccount1/migration-payloads/migrationId/requests/requestId_timestamp.json
                      if (pathParts.length >= 6) {
                        const migrationId = pathParts[3];
                        const payloadType = pathParts[4]; // 'requests' or 'responses'
                        const fileName = pathParts[5];
                        
                        // Extract requestId from filename (format: requestId_timestamp.json)
                        const requestId = fileName.split('_')[0];
                        const entityName = 'categories'; // We can extract this from entityType if needed
                        
                        // Create Functions API URL
                        return `http://localhost:7071/api/logs/payload/${migrationId}/${requestId}/${payloadType.slice(0, -1)}/${entityName}`;
                      }
                    }
                    
                    return blobUrl;
                  };
                  
                  return {
                    entityId: err.entityId || 'Unknown',
                    entityName: err.entityName,
                    entityType: err.entityType || entityType,
                    error: err.errorMessage || err.error || 'Unknown error',
                    processedAt: err.timestamp || new Date().toISOString(),
                    requestPayloadBlobUrl: transformBlobUrl(err.requestPayloadBlobUrl),
                    responsePayloadBlobUrl: transformBlobUrl(err.responsePayloadBlobUrl),
                  };
                });
                console.log(`✅ Transformed errors for ${entityType}:`, entityErrors);
                allErrors.push(...entityErrors);
              } else {
                console.warn(`❌ Unexpected response format for ${entityType}:`, errorsResponse);
              }
            } catch (entityError) {
              console.error(`❌ Failed to fetch errors for entity type ${entityType}:`, entityError);
            }
          }
          
          console.log('📊 Total errors found:', allErrors.length);
          console.log('🔍 Final error details:', allErrors);
          
          // Show real errors only - no test data
          if (allErrors.length === 0) {
            console.log('✅ No real errors found - showing clean successful migration');
            setErrors([]);
            setDebugInfo('No errors in this successful migration');
          } else {
            console.log('📊 Using real error data:', allErrors);
            setErrors(allErrors);
            setDebugInfo('Real errors loaded: ' + allErrors.length);
          }
        } catch (error) {
          console.error('❌ Failed to fetch migration errors:', error);
          // Don't fail the whole request if errors can't be fetched
        }
      } else {
        console.log('✅ No failed entities - showing clean successful migration results');
        
        // No test data - show real migration results only
        setErrors([]);
        setDebugInfo('No errors in this successful migration');
      }

    } catch (err) {
      console.error('❌ Failed to fetch migration details:', err);
      setError(err instanceof Error ? err.message : 'Failed to fetch migration details');
    } finally {
      setLoading(false);
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

  const calculateSuccessRate = () => {
    if (!migration || migration.totalEntities === 0) return 0;
    return Math.round((migration.successfulEntities / migration.totalEntities) * 100);
  };

  if (loading) {
    return (
      <Box sx={{ display: 'flex', justifyContent: 'center', alignItems: 'center', height: '400px' }}>
        <CircularProgress />
      </Box>
    );
  }

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

  if (!migration) {
    return (
      <Box sx={{ p: 3 }}>
        <Alert severity="warning" sx={{ mb: 2 }}>
          Migration not found
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

  const successRate = calculateSuccessRate();
  const primaryEntityType = migration.entities.length > 0 ? migration.entities[0] : 'Unknown';

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

      {/* Migration Detail Card */}
      <Card sx={{ mb: 3 }}>
        <CardContent sx={{ p: 3 }}>
          {/* Status and Entity */}
          <Box sx={{ display: 'flex', alignItems: 'center', gap: 2, mb: 3 }}>
            <Chip
              icon={getStatusIcon(migration.status)}
              label={migration.status.charAt(0).toUpperCase() + migration.status.slice(1)}
              color={getStatusColor(migration.status) as any}
              sx={{ fontWeight: 600 }}
            />
            <Typography variant="h5" sx={{ fontWeight: 600 }}>
              {primaryEntityType.charAt(0).toUpperCase() + primaryEntityType.slice(1)}
            </Typography>
            <Box sx={{ ml: 'auto', p: 1, border: '1px solid', borderColor: 'divider', borderRadius: 1 }}>
              <Typography variant="caption" color="text.secondary">
                {migration.migrationId}
              </Typography>
            </Box>
          </Box>

          {/* Migration Info */}
          <Box sx={{ display: 'flex', gap: 4, mb: 3, flexWrap: 'wrap' }}>
            <Box>
              <Typography variant="body2" color="text.secondary">
                <strong>Source:</strong> {migration.sourceStore}
              </Typography>
            </Box>
            <Box>
              <Typography variant="body2" color="text.secondary">
                <strong>Destination:</strong> {migration.destinationStore}
              </Typography>
            </Box>
            <Box>
              <Typography variant="body2" color="text.secondary">
                <strong>Created:</strong> {formatDate(migration.startedAt)}
              </Typography>
            </Box>
            <Box>
              <Typography variant="body2" color="text.secondary">
                <strong>Updated:</strong> {formatDate(migration.completedAt)}
              </Typography>
            </Box>
          </Box>

          {/* Migration Summary */}
          <Box sx={{ 
            backgroundColor: 'primary.50', 
            p: 2, 
            borderRadius: 1, 
            mb: 3,
            border: '1px solid',
            borderColor: 'primary.light',
          }}>
            <Typography variant="body2" color="primary.main" sx={{ mb: 2, fontWeight: 600 }}>
              Migration {migration.status}: {migration.successfulEntities} successful, {migration.failedEntities} failed, {migration.totalEntities - migration.processedEntities} skipped
            </Typography>
          </Box>

          {/* Statistics */}
          <Stack direction="row" spacing={4} justifyContent="center" sx={{ py: 3 }}>
            <Box sx={{ textAlign: 'center' }}>
              <Typography variant="h2" color="success.main" sx={{ fontWeight: 700, fontSize: '3rem' }}>
                {migration.successfulEntities}
              </Typography>
              <Typography variant="body2" color="text.secondary">
                Success
              </Typography>
            </Box>
            <Box sx={{ textAlign: 'center' }}>
              <Typography variant="h2" color="error.main" sx={{ fontWeight: 700, fontSize: '3rem' }}>
                {migration.failedEntities}
              </Typography>
              <Typography variant="body2" color="text.secondary">
                Failed
              </Typography>
            </Box>
            <Box sx={{ textAlign: 'center' }}>
              <Typography variant="h2" color="primary.main" sx={{ fontWeight: 700, fontSize: '3rem' }}>
                {migration.totalEntities}
              </Typography>
              <Typography variant="body2" color="text.secondary">
                Total
              </Typography>
            </Box>
            <Box sx={{ textAlign: 'center' }}>
              <Typography variant="h2" color="text.primary" sx={{ fontWeight: 700, fontSize: '3rem' }}>
                {successRate}%
              </Typography>
              <Typography variant="body2" color="text.secondary">
                Success Rate
              </Typography>
            </Box>
          </Stack>
        </CardContent>
      </Card>

      {/* Failure Details - Show if there are errors or failed entities */}
      {errors.length > 0 && (
        <Card>
          <CardContent sx={{ p: 0 }}>
            <Box sx={{ p: 3, display: 'flex', alignItems: 'center', gap: 1, borderBottom: '1px solid', borderColor: 'divider' }}>
              <ErrorIcon sx={{ color: 'error.main' }} />
              <Typography variant="h6" sx={{ fontWeight: 600, color: 'error.main' }}>
                Failure Details ({errors.length} failed entities)
              </Typography>
            </Box>

            {errors.length > 0 ? (
              <Table>
                <TableHead>
                  <TableRow sx={{ backgroundColor: 'background.default' }}>
                    <TableCell sx={{ fontWeight: 600, py: 2 }}>Name</TableCell>
                    <TableCell sx={{ fontWeight: 600, py: 2 }}>EntityType</TableCell>
                    <TableCell sx={{ fontWeight: 600, py: 2 }}>Error</TableCell>
                    <TableCell sx={{ fontWeight: 600, py: 2 }}>ProcessedAt</TableCell>
                    <TableCell sx={{ fontWeight: 600, py: 2 }}>Request</TableCell>
                    <TableCell sx={{ fontWeight: 600, py: 2 }}>Response</TableCell>
                  </TableRow>
                </TableHead>
                <TableBody>
                  {errors.map((failure, failureIndex) => (
                    <TableRow key={failureIndex}>
                      <TableCell sx={{ fontSize: '0.875rem' }}>
                        {(() => {
                          // Debug: Log the failure data to see what we have
                          console.log(`🔍 Entity ${failure.entityId} data:`, {
                            entityName: failure.entityName,
                            entityId: failure.entityId,
                            error: failure.error
                          });
                          
                          // Use entityName from API response, fallback to extraction from error message
                          let entityName = failure.entityName;
                          
                          // If no entityName from API, try to extract from error message
                          if (!entityName) {
                            const nameMatch = failure.error.match(/name: '([^']+)'/);
                            entityName = nameMatch ? nameMatch[1] : undefined;
                          }
                          
                          const displayName = entityName ? `${entityName} (${failure.entityId})` : failure.entityId;
                          console.log(`🔍 Entity ${failure.entityId} display name:`, displayName);
                          
                          return displayName;
                        })()}
                      </TableCell>
                      <TableCell sx={{ fontSize: '0.875rem' }}>{failure.entityType}</TableCell>
                      <TableCell sx={{ fontSize: '0.875rem', maxWidth: '400px' }}>
                        <Typography variant="body2" sx={{ wordBreak: 'break-word' }}>
                          {failure.error}
                        </Typography>
                      </TableCell>
                      <TableCell sx={{ fontSize: '0.875rem' }}>{formatDateTime(failure.processedAt)}</TableCell>
                                             <TableCell sx={{ fontSize: '0.875rem' }}>
                         {(() => {
                           return (
                             <IconButton
                               size="small"
                               onClick={() => {
                                 if (failure.requestPayloadBlobUrl) {
                                   window.open(failure.requestPayloadBlobUrl, '_blank');
                                 } else {
                                   alert('Request payload not available for this error.');
                                 }
                               }}
                               disabled={!failure.requestPayloadBlobUrl}
                               sx={{ 
                                 color: failure.requestPayloadBlobUrl ? 'primary.main' : 'text.disabled',
                                 '&:hover': {
                                   color: failure.requestPayloadBlobUrl ? 'primary.dark' : 'text.disabled'
                                 }
                               }}
                             >
                               <DownloadIcon />
                             </IconButton>
                           );
                         })()}
                       </TableCell>
                       <TableCell sx={{ fontSize: '0.875rem' }}>
                         {(() => {
                           return (
                             <IconButton
                               size="small"
                               onClick={() => {
                                 if (failure.responsePayloadBlobUrl) {
                                   window.open(failure.responsePayloadBlobUrl, '_blank');
                                 } else {
                                   alert('Response payload not available for this error.');
                                 }
                               }}
                               disabled={!failure.responsePayloadBlobUrl}
                               sx={{ 
                                 color: failure.responsePayloadBlobUrl ? 'primary.main' : 'text.disabled',
                                 '&:hover': {
                                   color: failure.responsePayloadBlobUrl ? 'primary.dark' : 'text.disabled'
                                 }
                               }}
                             >
                               <DownloadIcon />
                             </IconButton>
                           );
                         })()}
                       </TableCell>
                    </TableRow>
                  ))}
                </TableBody>
              </Table>
            ) : (
              <Box sx={{ p: 3, textAlign: 'center' }}>
                <Typography variant="body1" color="text.secondary" sx={{ mb: 2 }}>
                  {migration.failedEntities} entities failed during migration, but detailed error information is not available.
                </Typography>
                <Typography variant="body2" color="text.secondary">
                  This could be due to:
                </Typography>
                <Box component="ul" sx={{ textAlign: 'left', mt: 1, color: 'text.secondary' }}>
                  <li>Errors not being logged to the database</li>
                  <li>Error logging being disabled during migration</li>
                  <li>Errors being cleared after migration completion</li>
                </Box>
              </Box>
            )}
          </CardContent>
        </Card>
      )}

      {/* No Errors Message */}
      {migration.failedEntities === 0 && errors.length === 0 && (
        <Card>
          <CardContent sx={{ p: 3, textAlign: 'center' }}>
            <CheckCircleIcon sx={{ fontSize: 48, color: 'success.main', mb: 2 }} />
            <Typography variant="h6" color="success.main" sx={{ mb: 1 }}>
              No Failures
            </Typography>
            <Typography variant="body2" color="text.secondary">
              This migration completed successfully with no errors.
            </Typography>
          </CardContent>
        </Card>
      )}
    </Box>
  );
}; 