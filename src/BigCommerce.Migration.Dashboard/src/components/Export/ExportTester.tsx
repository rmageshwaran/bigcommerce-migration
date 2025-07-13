import React, { useRef } from 'react';
import {
  Card,
  CardContent,
  Typography,
  Box,
  Grid,
  Stack,
  Chip,
  Divider,
  Table,
  TableBody,
  TableCell,
  TableContainer,
  TableHead,
  TableRow,
  Paper,
  Alert,
  LinearProgress,
} from '@mui/material';
import {
  Download as DownloadIcon,
  Assessment as ChartIcon,
  TableChart as TableIcon,
} from '@mui/icons-material';
import { LineChart, Line, XAxis, YAxis, CartesianGrid, Tooltip, ResponsiveContainer, BarChart, Bar } from 'recharts';
import ExportButton from './ExportButton';
import { exportService } from '../../services/exportService';
import type { 
  MigrationExportData, 
  EntityExportData, 
  ErrorExportData, 
  PerformanceExportData 
} from '../../services/exportService';

const ExportTester: React.FC = () => {
  const chartRef = useRef<HTMLDivElement>(null);

  // Sample data for testing
  const sampleMigrations: MigrationExportData[] = [
    {
      migrationId: 'mig_001',
      name: 'Products Migration - Store A to B',
      status: 'completed',
      sourceStore: 'store-a.mybigcommerce.com',
      destinationStore: 'store-b.mybigcommerce.com',
      startTime: '2025-01-10 10:00:00',
      endTime: '2025-01-10 12:30:00',
      duration: '2h 30m',
      totalEntities: 1500,
      processedEntities: 1500,
      successfulEntities: 1485,
      failedEntities: 15,
      successRate: '99.0%',
      entitiesPerSecond: 1.67,
      entityTypes: 'Products, Variants, Images',
      currentPhase: 'completed',
      errorCount: 15,
    },
    {
      migrationId: 'mig_002',
      name: 'Categories Migration - Store C to D',
      status: 'running',
      sourceStore: 'store-c.mybigcommerce.com',
      destinationStore: 'store-d.mybigcommerce.com',
      startTime: '2025-01-10 14:00:00',
      duration: '45m',
      totalEntities: 250,
      processedEntities: 180,
      successfulEntities: 175,
      failedEntities: 5,
      successRate: '97.2%',
      entitiesPerSecond: 2.1,
      entityTypes: 'Categories',
      currentPhase: 'processing',
      errorCount: 5,
    },
    {
      migrationId: 'mig_003',
      name: 'Brands Migration - Store E to F',
      status: 'failed',
      sourceStore: 'store-e.mybigcommerce.com',
      destinationStore: 'store-f.mybigcommerce.com',
      startTime: '2025-01-10 16:00:00',
      endTime: '2025-01-10 16:15:00',
      duration: '15m',
      totalEntities: 50,
      processedEntities: 20,
      successfulEntities: 15,
      failedEntities: 5,
      successRate: '75.0%',
      entitiesPerSecond: 1.3,
      entityTypes: 'Brands',
      currentPhase: 'failed',
      errorCount: 10,
    },
  ];

  const sampleEntities: EntityExportData[] = [
    {
      migrationId: 'mig_001',
      entityType: 'Products',
      totalCount: 1200,
      processedCount: 1200,
      successCount: 1185,
      failureCount: 15,
      progressPercentage: '100%',
      processingSpeed: 1.8,
      currentBatch: 12,
      totalBatches: 12,
      status: 'completed',
      lastUpdated: '2025-01-10 12:30:00',
    },
    {
      migrationId: 'mig_001',
      entityType: 'Variants',
      totalCount: 3500,
      processedCount: 3500,
      successCount: 3485,
      failureCount: 15,
      progressPercentage: '100%',
      processingSpeed: 2.1,
      currentBatch: 35,
      totalBatches: 35,
      status: 'completed',
      lastUpdated: '2025-01-10 12:25:00',
    },
    {
      migrationId: 'mig_002',
      entityType: 'Categories',
      totalCount: 250,
      processedCount: 180,
      successCount: 175,
      failureCount: 5,
      progressPercentage: '72%',
      processingSpeed: 2.2,
      estimatedTimeRemaining: '32m',
      currentBatch: 18,
      totalBatches: 25,
      status: 'running',
      lastUpdated: '2025-01-10 14:45:00',
    },
  ];

  const sampleErrors: ErrorExportData[] = [
    {
      migrationId: 'mig_001',
      entityType: 'Products',
      entityId: 'prod_12345',
      errorCode: 'VALIDATION_ERROR',
      errorMessage: 'Product name exceeds maximum length of 255 characters',
      timestamp: '2025-01-10 11:30:00',
      severity: 'warning',
      retryCount: 2,
      resolved: true,
      resolvedBy: 'Auto-truncation',
      resolvedAt: '2025-01-10 11:32:00',
    },
    {
      migrationId: 'mig_001',
      entityType: 'Variants',
      entityId: 'var_67890',
      errorCode: 'API_ERROR',
      errorMessage: 'SKU already exists in destination store',
      timestamp: '2025-01-10 12:15:00',
      severity: 'error',
      retryCount: 3,
      resolved: false,
    },
    {
      migrationId: 'mig_002',
      entityType: 'Categories',
      entityId: 'cat_54321',
      errorCode: 'RATE_LIMIT',
      errorMessage: 'API rate limit exceeded, retrying after delay',
      timestamp: '2025-01-10 14:20:00',
      severity: 'info',
      retryCount: 1,
      resolved: true,
      resolvedBy: 'Automatic retry',
      resolvedAt: '2025-01-10 14:22:00',
    },
  ];

  const samplePerformance: PerformanceExportData[] = [
    {
      migrationId: 'mig_001',
      timestamp: '2025-01-10 10:00:00',
      entitiesPerSecond: 1.2,
      apiRequestsPerSecond: 8.5,
      memoryUsage: '156 MB',
      cpuUsage: '25%',
      queueLength: 0,
      activeConnections: 2,
      responseTime: 250,
      errorRate: '0.1%',
    },
    {
      migrationId: 'mig_001',
      timestamp: '2025-01-10 11:00:00',
      entitiesPerSecond: 1.8,
      apiRequestsPerSecond: 11.2,
      memoryUsage: '178 MB',
      cpuUsage: '45%',
      queueLength: 5,
      activeConnections: 3,
      responseTime: 180,
      errorRate: '0.8%',
    },
    {
      migrationId: 'mig_001',
      timestamp: '2025-01-10 12:00:00',
      entitiesPerSecond: 2.1,
      apiRequestsPerSecond: 12.0,
      memoryUsage: '192 MB',
      cpuUsage: '52%',
      queueLength: 3,
      activeConnections: 4,
      responseTime: 160,
      errorRate: '1.2%',
    },
  ];

  // Chart data for testing
  const chartData = [
    { time: '10:00', entities: 120, success: 118, errors: 2 },
    { time: '11:00', entities: 180, success: 175, errors: 5 },
    { time: '12:00', entities: 210, success: 205, errors: 5 },
    { time: '13:00', entities: 150, success: 148, errors: 2 },
    { time: '14:00', entities: 190, success: 185, errors: 5 },
  ];

  return (
    <Card sx={{ maxWidth: 1200, margin: 'auto', mt: 2 }}>
      <CardContent>
        <Box sx={{ display: 'flex', alignItems: 'center', mb: 3 }}>
          <DownloadIcon sx={{ mr: 1 }} />
          <Typography variant="h6">
            Export System Tester
          </Typography>
          <Chip 
            label="Development Tool" 
            color="success" 
            size="small" 
            sx={{ ml: 2 }}
          />
        </Box>
        
        <Alert severity="info" sx={{ mb: 3 }}>
          <Typography variant="body2">
            Test the export functionality with sample data. Choose different export formats (CSV, Excel, PDF) 
            and see how the system handles various data types. All exports will download to your default download folder.
          </Typography>
        </Alert>

        <Box sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr', md: 'repeat(2, 1fr)' }, gap: 3 }}>
          {/* Migration Data Export */}
          <Box>
            <Card variant="outlined">
              <CardContent>
                <Box sx={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', mb: 2 }}>
                  <Typography variant="h6" fontWeight={600}>
                    Migration Reports
                  </Typography>
                  <ExportButton
                    dataType="migrations"
                    data={sampleMigrations}
                    title="Migration Reports"
                    variant="button"
                    size="small"
                  />
                </Box>
                
                <Typography variant="body2" color="text.secondary" sx={{ mb: 2 }}>
                  Export comprehensive migration reports with status, timing, and success metrics.
                </Typography>

                <Box sx={{ display: 'flex', gap: 1, mb: 2 }}>
                  <Chip label={`${sampleMigrations.length} migrations`} size="small" />
                  <Chip label="All statuses" size="small" color="primary" />
                  <Chip label="With metrics" size="small" color="success" />
                </Box>

                <TableContainer component={Paper} variant="outlined" sx={{ maxHeight: 200 }}>
                  <Table size="small">
                    <TableHead>
                      <TableRow>
                        <TableCell>Migration</TableCell>
                        <TableCell>Status</TableCell>
                        <TableCell align="right">Success Rate</TableCell>
                      </TableRow>
                    </TableHead>
                    <TableBody>
                      {sampleMigrations.map((migration) => (
                        <TableRow key={migration.migrationId}>
                          <TableCell>{migration.name}</TableCell>
                          <TableCell>
                            <Chip 
                              label={migration.status} 
                              size="small" 
                              color={
                                migration.status === 'completed' ? 'success' : 
                                migration.status === 'running' ? 'primary' : 'error'
                              }
                            />
                          </TableCell>
                          <TableCell align="right">{migration.successRate}</TableCell>
                        </TableRow>
                      ))}
                    </TableBody>
                  </Table>
                </TableContainer>
              </CardContent>
            </Card>
          </Box>

          {/* Entity Progress Export */}
          <Box>
            <Card variant="outlined">
              <CardContent>
                <Box sx={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', mb: 2 }}>
                  <Typography variant="h6" fontWeight={600}>
                    Entity Progress
                  </Typography>
                  <ExportButton
                    dataType="entities"
                    data={sampleEntities}
                    title="Entity Progress Report"
                    variant="button"
                    size="small"
                  />
                </Box>
                
                <Typography variant="body2" color="text.secondary" sx={{ mb: 2 }}>
                  Export detailed entity-level progress with processing speeds and batch information.
                </Typography>

                <Box sx={{ display: 'flex', gap: 1, mb: 2 }}>
                  <Chip label={`${sampleEntities.length} entity types`} size="small" />
                  <Chip label="With progress" size="small" color="primary" />
                  <Chip label="Real-time data" size="small" color="success" />
                </Box>

                <Stack spacing={1}>
                  {sampleEntities.map((entity, index) => (
                    <Box key={index}>
                      <Box sx={{ display: 'flex', justifyContent: 'space-between', mb: 0.5 }}>
                        <Typography variant="body2">{entity.entityType}</Typography>
                        <Typography variant="body2">{entity.progressPercentage}</Typography>
                      </Box>
                      <LinearProgress 
                        variant="determinate" 
                        value={parseInt(entity.progressPercentage)} 
                        sx={{ height: 6, borderRadius: 3 }}
                      />
                    </Box>
                  ))}
                </Stack>
              </CardContent>
            </Card>
          </Box>

          {/* Error Reports Export */}
          <Box>
            <Card variant="outlined">
              <CardContent>
                <Box sx={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', mb: 2 }}>
                  <Typography variant="h6" fontWeight={600}>
                    Error Reports
                  </Typography>
                  <ExportButton
                    dataType="errors"
                    data={sampleErrors}
                    title="Error Analysis Report"
                    variant="button"
                    size="small"
                  />
                </Box>
                
                <Typography variant="body2" color="text.secondary" sx={{ mb: 2 }}>
                  Export detailed error logs with resolution status and troubleshooting information.
                </Typography>

                <Box sx={{ display: 'flex', gap: 1, mb: 2 }}>
                  <Chip label={`${sampleErrors.length} errors`} size="small" />
                  <Chip label="With resolution" size="small" color="warning" />
                  <Chip label="Detailed logs" size="small" color="error" />
                </Box>

                <Stack spacing={1}>
                  {sampleErrors.map((error, index) => (
                    <Box key={index} sx={{ 
                      p: 1, 
                      border: 1, 
                      borderColor: 'divider', 
                      borderRadius: 1,
                      backgroundColor: error.resolved ? 'success.light' : 'error.light',
                      opacity: 0.1
                    }}>
                      <Typography variant="body2" fontWeight={500}>
                        {error.errorCode}
                      </Typography>
                      <Typography variant="caption" color="text.secondary">
                        {error.errorMessage}
                      </Typography>
                    </Box>
                  ))}
                </Stack>
              </CardContent>
            </Card>
          </Box>

          {/* Performance Data Export */}
          <Box>
            <Card variant="outlined">
              <CardContent>
                <Box sx={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', mb: 2 }}>
                  <Typography variant="h6" fontWeight={600}>
                    Performance Metrics
                  </Typography>
                  <ExportButton
                    dataType="performance"
                    data={samplePerformance}
                    title="Performance Analysis"
                    variant="button"
                    size="small"
                  />
                </Box>
                
                <Typography variant="body2" color="text.secondary" sx={{ mb: 2 }}>
                  Export system performance data including throughput, memory usage, and response times.
                </Typography>

                <Box sx={{ display: 'flex', gap: 1, mb: 2 }}>
                  <Chip label={`${samplePerformance.length} data points`} size="small" />
                  <Chip label="Time series" size="small" color="primary" />
                  <Chip label="System metrics" size="small" color="info" />
                </Box>

                <Box sx={{ display: 'grid', gridTemplateColumns: 'repeat(4, 1fr)', gap: 2 }}>
                  <Box>
                    <Typography variant="body2" color="text.secondary">Avg Speed</Typography>
                    <Typography variant="h6">1.7 entities/sec</Typography>
                  </Box>
                  <Box sx={{ gridColumn: 'span 4' }}>
                    <Typography variant="body2" color="text.secondary">Avg Response</Typography>
                    <Typography variant="h6">197ms</Typography>
                  </Box>
                  <Box>
                    <Typography variant="body2" color="text.secondary">Memory Usage</Typography>
                    <Typography variant="h6">175 MB</Typography>
                  </Box>
                  <Box sx={{ gridColumn: 'span 4' }}>
                    <Typography variant="body2" color="text.secondary">Error Rate</Typography>
                    <Typography variant="h6">0.7%</Typography>
                  </Box>
                </Box>
              </CardContent>
            </Card>
          </Box>

          {/* Chart Export */}
          <Box>
            <Card variant="outlined">
              <CardContent>
                <Box sx={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', mb: 2 }}>
                  <Typography variant="h6" fontWeight={600}>
                    Chart Export
                  </Typography>
                  <ExportButton
                    dataType="charts"
                    chartElement={chartRef.current || undefined}
                    title="Migration Progress Chart"
                    variant="button"
                    size="small"
                  />
                </Box>
                
                <Typography variant="body2" color="text.secondary" sx={{ mb: 2 }}>
                  Export charts as PNG images or PDF documents. Perfect for reports and presentations.
                </Typography>

                <Box ref={chartRef} sx={{ width: '100%', height: 300 }}>
                  <ResponsiveContainer>
                    <LineChart data={chartData}>
                      <CartesianGrid strokeDasharray="3 3" />
                      <XAxis dataKey="time" />
                      <YAxis />
                      <Tooltip />
                      <Line 
                        type="monotone" 
                        dataKey="entities" 
                        stroke="#1976d2" 
                        strokeWidth={2}
                        name="Total Entities"
                      />
                      <Line 
                        type="monotone" 
                        dataKey="success" 
                        stroke="#2e7d32" 
                        strokeWidth={2}
                        name="Successful"
                      />
                      <Line 
                        type="monotone" 
                        dataKey="errors" 
                        stroke="#d32f2f" 
                        strokeWidth={2}
                        name="Errors"
                      />
                    </LineChart>
                  </ResponsiveContainer>
                </Box>
              </CardContent>
            </Card>
          </Box>
        </Box>

        <Divider sx={{ my: 3 }} />

        <Box sx={{ p: 2, bgcolor: 'background.default', borderRadius: 1 }}>
          <Typography variant="body2" color="text.secondary">
            <strong>Export Features:</strong>
            <br />
            • <strong>Multiple Formats:</strong> CSV (data analysis), Excel (formatted reports), PDF (sharing)
            <br />
            • <strong>Custom Filenames:</strong> Set your own filename or use auto-generated timestamps
            <br />
            • <strong>Date Filtering:</strong> Export data within specific date ranges
            <br />
            • <strong>Chart Export:</strong> High-resolution PNG or PDF exports of charts
            <br />
            • <strong>Real-time Data:</strong> Export current migration status and progress
            <br />
            • <strong>Sample Data:</strong> Try exports even without real migration data
          </Typography>
        </Box>
      </CardContent>
    </Card>
  );
};

export default ExportTester; 