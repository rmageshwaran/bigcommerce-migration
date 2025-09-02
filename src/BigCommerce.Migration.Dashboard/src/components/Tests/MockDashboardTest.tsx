import React, { useState, useEffect } from 'react';
import {
  Box,
  Card,
  CardContent,
  Typography,
  Button,
  Alert,
  Switch,
  FormControlLabel,
  Stack,
  Divider
} from '@mui/material';
import { EnhancedMigrationOverview } from '../Dashboard/EnhancedMigrationOverview';

// Mock data that matches the structure expected by EnhancedMigrationOverview
interface MockMigrationData {
  migrationId: string;
  sourceStore: string;
  destinationStore: string;
  status: string;
  startDateTime: string;
  lastUpdated: string;
  estimatedEndTime?: string;
  overallProgress: number;
  totalProcessed: number;
  totalSuccess: number;
  totalFailed: number;
  totalSkipped: number;
  entities: Array<{
    entityType: string;
    status: string;
    progressPercentage: number;
    processedCount: number;
    totalCount: number;
    successCount: number;
    failedCount: number;
    skippedCount: number;
    currentChunk?: number;
    totalChunks?: number;
    processingSpeed?: number;
    showTotalCount?: boolean;
  }>;
}

// Create a mock hook that mimics useEnhancedMigrationProgress
const useMockEnhancedMigrationProgress = (migrationId: string, mockData: MockMigrationData | null) => {
  const [isConnected, setIsConnected] = useState(true);
  const [lastUpdated, setLastUpdated] = useState(new Date());

  // Update lastUpdated every few seconds to simulate real-time updates
  useEffect(() => {
    const interval = setInterval(() => {
      setLastUpdated(new Date());
    }, 3000);
    return () => clearInterval(interval);
  }, []);

  return {
    migrationData: mockData,
    isConnected,
    isLoading: false,
    error: null,
    lastUpdated,
    refreshData: () => {
      setLastUpdated(new Date());
    }
  };
};

export const MockDashboardTest: React.FC = () => {
  const [isSimulating, setIsSimulating] = useState(true);
  const [mockData, setMockData] = useState<MockMigrationData>({
    migrationId: '0e0d261c-337f-4724-a718-d8343b358d04',
    sourceStore: 'Source Store',
    destinationStore: 'Destination Store',
    status: 'running',
    startDateTime: new Date(Date.now() - 2 * 60 * 60 * 1000).toISOString(), // 2 hours ago
    lastUpdated: new Date().toISOString(),
    estimatedEndTime: new Date(Date.now() + 1 * 60 * 60 * 1000).toISOString(), // 1 hour from now
    overallProgress: 44.8,
    totalProcessed: 58188,
    totalSuccess: 57191,
    totalFailed: 67,
    totalSkipped: 930,
    entities: [
      {
        entityType: 'products',
        status: 'completed',
        progressPercentage: 100.0,
        processedCount: 3909,
        totalCount: 3909,
        successCount: 3909,
        failedCount: 0,
        skippedCount: 0
      },
              {
          entityType: 'options',
          status: 'completed',
          progressPercentage: 100.0,
          processedCount: 2847,
          totalCount: 2847,
          successCount: 2847,
          failedCount: 0,
          skippedCount: 0,
          showTotalCount: true // Component discovery phase
        },
        {
          entityType: 'modifiers',
          status: 'completed',
          progressPercentage: 100.0,
          processedCount: 2100,
          totalCount: 2100,
          successCount: 2100,
          failedCount: 0,
          skippedCount: 0,
          showTotalCount: true // Component discovery phase
        },
        {
          entityType: 'reviews',
          status: 'completed',
          progressPercentage: 100.0,
          processedCount: 2500,
          totalCount: 2500,
          successCount: 2500,
          failedCount: 0,
          skippedCount: 0,
          showTotalCount: true // Component discovery phase
        },
        {
          entityType: 'product-related',
          status: 'completed',
          progressPercentage: 99.9,
          processedCount: 3677,
          totalCount: 3677,
          successCount: 3674,
          failedCount: 0,
          skippedCount: 3,
          showTotalCount: true // Static discovery phase
        },
        {
          entityType: 'product-images',
          status: 'completed',
          progressPercentage: 100.0,
          processedCount: 17388,
          totalCount: 17388,
          successCount: 16555,
          failedCount: 0,
          skippedCount: 833,
          showTotalCount: false // Dynamic discovery phase
        },
        {
          entityType: 'product-channel-assign',
          status: 'completed',
          progressPercentage: 100.0,
          processedCount: 3907,
          totalCount: 3907,
          successCount: 3903,
          failedCount: 0,
          skippedCount: 4,
          showTotalCount: false // Dynamic discovery phase
        },
        {
          entityType: 'product-metafields',
          status: 'completed',
          progressPercentage: 100.0,
          processedCount: 5085,
          totalCount: 5085,
          successCount: 5085,
          failedCount: 0,
          skippedCount: 0,
          showTotalCount: true // Static discovery phase
        },
        {
          entityType: 'variants',
          status: 'processing',
          progressPercentage: 21.7,
          processedCount: 216,
          totalCount: 996,
          successCount: 149,
          failedCount: 67,
          skippedCount: 0,
          showTotalCount: true // Static discovery phase
        }
    ]
  });

  // Simulate real-time progress updates
  useEffect(() => {
    if (!isSimulating) return;

    const interval = setInterval(() => {
      setMockData(prev => {
        const updated = { ...prev };
        
        // Update variants processing (the active entity)
        const variantsEntity = updated.entities.find(e => e.entityType?.toLowerCase() === 'variants');
        if (variantsEntity && variantsEntity.status?.toLowerCase() === 'processing') {
          const increment = Math.floor(Math.random() * 15) + 5; // 5-20 items
          const newProcessed = Math.min(variantsEntity.processedCount + increment, variantsEntity.totalCount);
          const newSuccess = Math.min(variantsEntity.successCount + increment - Math.floor(Math.random() * 3), newProcessed);
          const newFailed = variantsEntity.failedCount + Math.floor(Math.random() * 2);
          
          variantsEntity.processedCount = newProcessed;
          variantsEntity.successCount = newSuccess;
          variantsEntity.failedCount = Math.min(newFailed, newProcessed - newSuccess);
          variantsEntity.progressPercentage = (newProcessed / variantsEntity.totalCount) * 100;
          
          if (newProcessed >= variantsEntity.totalCount) {
            variantsEntity.status = 'completed';
            updated.status = 'completed';
          }
        }

        // Update overall progress
        const totalProcessed = updated.entities.reduce((sum, e) => sum + e.processedCount, 0);
        const totalSuccess = updated.entities.reduce((sum, e) => sum + e.successCount, 0);
        const totalFailed = updated.entities.reduce((sum, e) => sum + e.failedCount, 0);
        const totalSkipped = updated.entities.reduce((sum, e) => sum + e.skippedCount, 0);
        const totalEntities = updated.entities.reduce((sum, e) => sum + e.totalCount, 0);
        
        updated.totalProcessed = totalProcessed;
        updated.totalSuccess = totalSuccess;
        updated.totalFailed = totalFailed;
        updated.totalSkipped = totalSkipped;
        updated.overallProgress = totalEntities > 0 ? (totalProcessed / totalEntities) * 100 : 0;
        updated.lastUpdated = new Date().toISOString();

        return updated;
      });
    }, 2000); // Update every 2 seconds

    return () => clearInterval(interval);
  }, [isSimulating]);

  const resetMockData = () => {
    setMockData({
      migrationId: '0e0d261c-337f-4724-a718-d8343b358d04',
      sourceStore: 'Source Store',
      destinationStore: 'Destination Store',
      status: 'running',
      startDateTime: new Date(Date.now() - 2 * 60 * 60 * 1000).toISOString(),
      lastUpdated: new Date().toISOString(),
      estimatedEndTime: new Date(Date.now() + 1 * 60 * 60 * 1000).toISOString(),
      overallProgress: 44.8,
      totalProcessed: 58188,
      totalSuccess: 57191,
      totalFailed: 67,
      totalSkipped: 930,
      entities: [
        {
          entityType: 'products',
          status: 'completed',
          progressPercentage: 100.0,
          processedCount: 3909,
          totalCount: 3909,
          successCount: 3909,
          failedCount: 0,
          skippedCount: 0,
          showTotalCount: true // Static discovery phase
        },
        {
          entityType: 'options',
          status: 'completed',
          progressPercentage: 100.0,
          processedCount: 2847,
          totalCount: 2847,
          successCount: 2847,
          failedCount: 0,
          skippedCount: 0,
          showTotalCount: true // Component discovery phase
        },
        {
          entityType: 'modifiers',
          status: 'completed',
          progressPercentage: 100.0,
          processedCount: 2100,
          totalCount: 2100,
          successCount: 2100,
          failedCount: 0,
          skippedCount: 0,
          showTotalCount: true // Component discovery phase
        },
        {
          entityType: 'reviews',
          status: 'completed',
          progressPercentage: 100.0,
          processedCount: 2500,
          totalCount: 2500,
          successCount: 2500,
          failedCount: 0,
          skippedCount: 0,
          showTotalCount: true // Component discovery phase
        },
        {
          entityType: 'product-related',
          status: 'completed',
          progressPercentage: 99.9,
          processedCount: 3677,
          totalCount: 3677,
          successCount: 3674,
          failedCount: 0,
          skippedCount: 3,
          showTotalCount: true // Static discovery phase
        },
        {
          entityType: 'product-images',
          status: 'completed',
          progressPercentage: 100.0,
          processedCount: 17388,
          totalCount: 17388,
          successCount: 16555,
          failedCount: 0,
          skippedCount: 833,
          showTotalCount: false // Dynamic discovery phase
        },
        {
          entityType: 'product-channel-assign',
          status: 'completed',
          progressPercentage: 100.0,
          processedCount: 3907,
          totalCount: 3907,
          successCount: 3903,
          failedCount: 0,
          skippedCount: 4,
          showTotalCount: false // Dynamic discovery phase
        },
        {
          entityType: 'product-metafields',
          status: 'completed',
          progressPercentage: 100.0,
          processedCount: 5085,
          totalCount: 5085,
          successCount: 5085,
          failedCount: 0,
          skippedCount: 0,
          showTotalCount: true // Static discovery phase
        },
        {
          entityType: 'variants',
          status: 'processing',
          progressPercentage: 21.7,
          processedCount: 216,
          totalCount: 996,
          successCount: 149,
          failedCount: 67,
          skippedCount: 0,
          showTotalCount: true // Static discovery phase
        }
      ]
    });
  };

  // Create a mock version of the EnhancedMigrationOverview that uses our mock data
  const MockEnhancedMigrationOverview = ({ migrationId }: { migrationId: string }) => {
    // Override the useEnhancedMigrationProgress hook with our mock version
    const originalHook = require('../../hooks/useEnhancedMigrationProgress').useEnhancedMigrationProgress;
    
    // Temporarily replace the hook with our mock
    React.useEffect(() => {
      const mockHook = () => useMockEnhancedMigrationProgress(migrationId, mockData);
      (window as any).__mockHook = mockHook;
    }, [migrationId]);

    return <EnhancedMigrationOverview migrationId={migrationId} />;
  };

  return (
    <Box sx={{ p: 3, maxWidth: 1200, mx: 'auto' }}>
      <Typography variant="h4" gutterBottom>
        🎨 Dashboard UI Testing with Mock Data
      </Typography>

      {/* Controls */}
      <Card sx={{ mb: 3 }}>
        <CardContent>
          <Stack direction="row" justifyContent="space-between" alignItems="center">
            <Typography variant="h6">
              Mock Data Controls
            </Typography>
            <Stack direction="row" spacing={2} alignItems="center">
              <FormControlLabel
                control={
                  <Switch
                    checked={isSimulating}
                    onChange={(e) => setIsSimulating(e.target.checked)}
                  />
                }
                label={isSimulating ? "Live Simulation" : "Static Data"}
              />
              <Button variant="outlined" size="small" onClick={resetMockData}>
                Reset Data
              </Button>
            </Stack>
          </Stack>

          <Alert severity="info" sx={{ mt: 2 }}>
            <Typography variant="subtitle2">Mock Dashboard Mode</Typography>
            <Typography variant="body2">
              This shows your new UI with realistic mock data. 
              {isSimulating ? " Data updates every 2 seconds to simulate real progress." : " Data is static for UI testing."}
            </Typography>
          </Alert>
        </CardContent>
      </Card>

      <Divider sx={{ mb: 3 }} />

      {/* Mock Dashboard Display */}
                <Box>
            <Typography variant="h5" gutterBottom fontWeight="bold">
              Live Dashboard Preview
            </Typography>
        
        <Box sx={{ border: '2px dashed', borderColor: 'primary.main', borderRadius: 2, p: 2 }}>
          {/* Direct mock data display using the same component structure */}
          <Card>
            <CardContent>
              <Stack direction="row" justifyContent="space-between" alignItems="center" sx={{ mb: 3 }}>
                <Typography variant="h5" fontWeight="bold" gutterBottom>
                  Migration Overview
                </Typography>
                <Stack direction="row" spacing={1} alignItems="center">
                  <Box sx={{ 
                    display: 'inline-flex', 
                    alignItems: 'center', 
                    px: 1, 
                    py: 0.5, 
                    borderRadius: 1, 
                    border: '1px solid', 
                    borderColor: 'success.main',
                    color: 'success.main',
                    fontSize: '0.75rem'
                  }}>
                    ✅ Live Updates
                  </Box>
                </Stack>
              </Stack>

              {/* Migration Details */}
              <Box sx={{ mb: 3 }}>
                <Typography variant="h6" fontWeight="medium" sx={{ mb: 1 }}>
                  <strong>Migration ID:</strong> {mockData.migrationId}
                </Typography>
                <Typography variant="h6" fontWeight="medium" sx={{ mb: 1 }}>
                  <strong>Source Store:</strong> {mockData.sourceStore}
                </Typography>
                <Typography variant="h6" fontWeight="medium" sx={{ mb: 1 }}>
                  <strong>Destination Store:</strong> {mockData.destinationStore}
                </Typography>
                <Typography variant="h6" fontWeight="medium" sx={{ mb: 1 }}>
                  <strong>Migration Status:</strong> 
                  <Box component="span" sx={{ 
                    ml: 1, 
                    px: 1, 
                    py: 0.5, 
                    borderRadius: 1, 
                    backgroundColor: mockData.status?.toLowerCase() === 'completed' ? 'success.main' : 'primary.main',
                    color: 'white',
                    fontSize: '0.875rem',
                    fontWeight: 'bold',
                    textTransform: 'uppercase'
                  }}>
                    {mockData.status?.toLowerCase() === 'completed' ? 'MIGRATION COMPLETED' : 'MIGRATION IN PROGRESS'}
                  </Box>
                </Typography>
              </Box>

              <Typography variant="h6" sx={{ mb: 3, color: 'text.secondary', fontWeight: 'medium' }}>
                Processed: {mockData.totalProcessed.toLocaleString()}  |  
                Success: {mockData.totalSuccess.toLocaleString()}  |  
                Failed: {mockData.totalFailed.toLocaleString()}  |  
                Skipped: {mockData.totalSkipped.toLocaleString()}
              </Typography>

              <Divider sx={{ mb: 3 }} />

              <Typography variant="h5" gutterBottom fontWeight="bold">
                Entities
              </Typography>

              {mockData.entities.map((entity) => (
                <Stack 
                  key={entity.entityType} 
                  direction="row" 
                  justifyContent="space-between" 
                  alignItems="center" 
                  sx={{ mb: 2, p: 2, border: '1px solid', borderColor: 'divider', borderRadius: 1 }}
                >
                  <Stack direction="row" alignItems="center" spacing={2}>
                    <Typography variant="h6" fontWeight="medium" sx={{ textTransform: 'capitalize', minWidth: '200px' }}>
                      {entity.entityType}
                    </Typography>
                    <Box sx={{ fontSize: '1.5rem' }}>
                      {entity.status?.toLowerCase() === 'completed' ? '✅' : 
                       ['processing', 'inprogress', 'in_progress', 'in-progress'].includes(entity.status?.toLowerCase() || '') ? '▶️' : '📅'}
                    </Box>
                  </Stack>
                  
                  <Typography variant="h6" fontWeight="medium" color="text.primary">
                    {entity.showTotalCount !== false 
                      ? `${entity.processedCount.toLocaleString()}/${entity.totalCount.toLocaleString()}  |  ✅ ${entity.successCount.toLocaleString()}  ❌ ${entity.failedCount.toLocaleString()}  ⏭️ ${entity.skippedCount.toLocaleString()}`
                      : `✅ ${entity.successCount.toLocaleString()}  ❌ ${entity.failedCount.toLocaleString()}  ⏭️ ${entity.skippedCount.toLocaleString()}`
                    }
                  </Typography>
                </Stack>
              ))}
            </CardContent>
          </Card>
        </Box>
      </Box>
    </Box>
  );
};
