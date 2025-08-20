import React, { useState } from 'react';
import {
  Box,
  Card,
  CardContent,
  Typography,
  Button,
  Stack,
  TextField,
  Alert,
  Chip
} from '@mui/material';
import { 
  PlayArrow as StartIcon,
  Stop as StopIcon,
  Send as SendIcon
} from '@mui/icons-material';

interface EventSimulatorProps {
  onEventGenerated?: (eventType: string, eventData: any) => void;
}

export const EventSimulator: React.FC<EventSimulatorProps> = ({ onEventGenerated }) => {
  const [isSimulating, setIsSimulating] = useState(false);
  const [migrationId, setMigrationId] = useState('test-migration-001');
  const [simulationLog, setSimulationLog] = useState<string[]>([]);

  // Simulate SignalR events by triggering them manually
  const simulateEvent = (eventType: string, eventData: any) => {
    const timestamp = new Date().toLocaleTimeString();
    const logMessage = `${timestamp}: Simulated ${eventType}`;
    setSimulationLog(prev => [logMessage, ...prev.slice(0, 9)]);
    
    if (onEventGenerated) {
      onEventGenerated(eventType, eventData);
    }

    // Trigger the event through SignalR service if available
    const signalRService = (window as any).signalRService;
    if (signalRService && signalRService.simulateEvent) {
      signalRService.simulateEvent(eventType, eventData);
    }
  };

  const simulateMigrationStarted = () => {
    const event = {
      migrationId,
      sourceStore: 'test-source-store',
      destinationStore: 'test-destination-store',
      startDateTime: new Date().toISOString(),
      entities: [
        { entityType: 'products', totalCount: 100 },
        { entityType: 'categories', totalCount: 50 }
      ]
    };
    simulateEvent('migration-started', event);
  };

  const simulateChunkProgress = () => {
    const event = {
      migrationId,
      entityType: 'products',
      chunkNumber: 1,
      totalChunks: 10,
      chunkSize: 10,
      processedInChunk: 10,
      failedInChunk: 0,
      cumulativeProcessed: 10,
      cumulativeFailed: 0,
      totalEntitiesForType: 100,
      progressPercentage: 10,
      status: 'processing',
      message: 'Processing products chunk 1 of 10',
      processingTimeMs: 2500
    };
    simulateEvent('chunk-progress', event);
  };

  const simulateMigrationCompleted = () => {
    const event = {
      migrationId,
      status: 'Completed',
      message: 'Migration completed successfully',
      totalProcessedEntities: 150,
      totalFailedEntities: 0,
      durationMs: 30000,
      endDateTime: new Date().toISOString()
    };
    simulateEvent('migration-completed', event);
  };

  const simulateError = () => {
    const event = {
      migrationId,
      errorType: 'ValidationError',
      errorMessage: 'Product data validation failed',
      entityType: 'products',
      entityId: 'product-123'
    };
    simulateEvent('error', event);
  };

  const simulateFullMigrationFlow = async () => {
    setIsSimulating(true);
    
    try {
      // Start migration
      simulateMigrationStarted();
      
      // Wait and send progress updates
      for (let i = 1; i <= 5; i++) {
        await new Promise(resolve => setTimeout(resolve, 2000)); // 2 second intervals
        
        const progressEvent = {
          migrationId,
          entityType: 'products',
          chunkNumber: i,
          totalChunks: 5,
          chunkSize: 20,
          processedInChunk: 20,
          failedInChunk: 0,
          cumulativeProcessed: i * 20,
          cumulativeFailed: 0,
          totalEntitiesForType: 100,
          progressPercentage: (i * 20),
          status: 'processing',
          message: `Processing products chunk ${i} of 5`,
          processingTimeMs: 2000
        };
        simulateEvent('chunk-progress', progressEvent);
      }
      
      // Complete migration
      await new Promise(resolve => setTimeout(resolve, 2000));
      simulateMigrationCompleted();
      
    } finally {
      setIsSimulating(false);
    }
  };

  const stopSimulation = () => {
    setIsSimulating(false);
  };

  const simulateConcurrentMigrations = async () => {
    setIsSimulating(true);
    
    try {
      const migrations = [
        { id: `${migrationId}-concurrent-1`, delay: 0 },
        { id: `${migrationId}-concurrent-2`, delay: 1000 },
        { id: `${migrationId}-concurrent-3`, delay: 2000 }
      ];

      // Start multiple migrations concurrently
      const migrationPromises = migrations.map(async (migration) => {
        await new Promise(resolve => setTimeout(resolve, migration.delay));
        
        // Start each migration
        simulateEvent('migration-started', {
          migrationId: migration.id,
          sourceStore: `source-store-${migration.id}`,
          destinationStore: `dest-store-${migration.id}`,
          startDateTime: new Date().toISOString(),
          entities: [{ entityType: 'products', totalCount: 50 }]
        });

        // Send a few progress events for each
        for (let i = 1; i <= 3; i++) {
          await new Promise(resolve => setTimeout(resolve, 2000));
          simulateEvent('chunk-progress', {
            migrationId: migration.id,
            entityType: 'products',
            chunkNumber: i,
            totalChunks: 3,
            chunkSize: 10,
            processedInChunk: 10,
            cumulativeProcessed: i * 10,
            cumulativeFailed: 0,
            totalEntitiesForType: 30,
            progressPercentage: (i * 33.33),
            status: 'processing',
            message: `Processing ${migration.id} chunk ${i} of 3`
          });
        }

        // Complete migration
        await new Promise(resolve => setTimeout(resolve, 2000));
        simulateEvent('migration-completed', {
          migrationId: migration.id,
          status: 'Completed',
          message: `Migration ${migration.id} completed successfully`,
          totalProcessedEntities: 30,
          totalFailedEntities: 0,
          durationMs: 15000,
          endDateTime: new Date().toISOString()
        });
      });

      await Promise.all(migrationPromises);
    } finally {
      setIsSimulating(false);
    }
  };

  const simulateErrorScenarios = async () => {
    setIsSimulating(true);
    
    try {
      const errorMigrationId = `${migrationId}-error-test`;
      
      // Start migration
      simulateEvent('migration-started', {
        migrationId: errorMigrationId,
        sourceStore: 'error-test-source',
        destinationStore: 'error-test-dest',
        startDateTime: new Date().toISOString(),
        entities: [{ entityType: 'products', totalCount: 100 }]
      });

      await new Promise(resolve => setTimeout(resolve, 2000));

      // Send some progress
      simulateEvent('chunk-progress', {
        migrationId: errorMigrationId,
        entityType: 'products',
        chunkNumber: 1,
        totalChunks: 5,
        chunkSize: 20,
        processedInChunk: 15,
        failedInChunk: 5,
        cumulativeProcessed: 15,
        cumulativeFailed: 5,
        totalEntitiesForType: 100,
        progressPercentage: 15,
        status: 'processing',
        message: 'Processing with some failures'
      });

      await new Promise(resolve => setTimeout(resolve, 2000));

      // Send error event
      simulateEvent('error', {
        migrationId: errorMigrationId,
        errorType: 'ValidationError',
        errorMessage: 'Product validation failed due to missing required fields',
        entityType: 'products',
        entityId: 'product-error-123'
      });

      await new Promise(resolve => setTimeout(resolve, 2000));

      // Send cancellation event
      simulateEvent('migration-completed', {
        migrationId: errorMigrationId,
        status: 'Cancelled',
        message: 'Migration cancelled due to validation errors',
        totalProcessedEntities: 15,
        totalFailedEntities: 5,
        durationMs: 8000,
        endDateTime: new Date().toISOString()
      });

      // Test another error scenario - API timeout
      await new Promise(resolve => setTimeout(resolve, 3000));
      
      const timeoutMigrationId = `${migrationId}-timeout-test`;
      
      simulateEvent('migration-started', {
        migrationId: timeoutMigrationId,
        sourceStore: 'timeout-test-source',
        destinationStore: 'timeout-test-dest',
        startDateTime: new Date().toISOString(),
        entities: [{ entityType: 'categories', totalCount: 50 }]
      });

      await new Promise(resolve => setTimeout(resolve, 2000));

      simulateEvent('error', {
        migrationId: timeoutMigrationId,
        errorType: 'TimeoutError',
        errorMessage: 'API request timed out after 30 seconds',
        entityType: 'categories',
        entityId: 'category-timeout-456'
      });

      await new Promise(resolve => setTimeout(resolve, 2000));

      simulateEvent('migration-completed', {
        migrationId: timeoutMigrationId,
        status: 'Failed',
        message: 'Migration failed due to API timeouts',
        totalProcessedEntities: 0,
        totalFailedEntities: 50,
        durationMs: 6000,
        endDateTime: new Date().toISOString()
      });
      
    } finally {
      setIsSimulating(false);
    }
  };

  return (
    <Card sx={{ mb: 3 }}>
      <CardContent>
        <Typography variant="h6" gutterBottom>
          🎭 Event Simulator
        </Typography>
        
        <Typography variant="body2" color="text.secondary" sx={{ mb: 2 }}>
          Generate test SignalR events to validate the simplified event system
        </Typography>

        <Stack spacing={2}>
          <TextField
            label="Migration ID"
            value={migrationId}
            onChange={(e) => setMigrationId(e.target.value)}
            size="small"
            disabled={isSimulating}
          />

          <Stack direction="row" spacing={1} flexWrap="wrap">
            <Button
              variant="contained"
              startIcon={<StartIcon />}
              onClick={simulateFullMigrationFlow}
              disabled={isSimulating}
              color="primary"
            >
              Simulate Full Migration
            </Button>

            <Button
              variant="outlined"
              startIcon={<StopIcon />}
              onClick={stopSimulation}
              disabled={!isSimulating}
              color="error"
            >
              Stop Simulation
            </Button>

            <Button
              variant="contained"
              onClick={simulateConcurrentMigrations}
              disabled={isSimulating}
              color="secondary"
            >
              Test Concurrent Migrations
            </Button>

            <Button
              variant="contained"
              onClick={simulateErrorScenarios}
              disabled={isSimulating}
              color="warning"
            >
              Test Error Scenarios
            </Button>
          </Stack>

          <Stack direction="row" spacing={1} flexWrap="wrap">
            <Button
              variant="outlined"
              startIcon={<SendIcon />}
              onClick={simulateMigrationStarted}
              disabled={isSimulating}
              size="small"
            >
              Migration Started
            </Button>

            <Button
              variant="outlined"
              startIcon={<SendIcon />}
              onClick={simulateChunkProgress}
              disabled={isSimulating}
              size="small"
            >
              Chunk Progress
            </Button>

            <Button
              variant="outlined"
              startIcon={<SendIcon />}
              onClick={simulateMigrationCompleted}
              disabled={isSimulating}
              size="small"
            >
              Migration Completed
            </Button>

            <Button
              variant="outlined"
              startIcon={<SendIcon />}
              onClick={simulateError}
              disabled={isSimulating}
              size="small"
              color="error"
            >
              Error Event
            </Button>
          </Stack>

          {isSimulating && (
            <Alert severity="info">
              <Typography variant="body2">
                🔄 Simulating migration flow... Events will be sent every 2 seconds to test rate limiting.
              </Typography>
            </Alert>
          )}

          {simulationLog.length > 0 && (
            <Box>
              <Typography variant="subtitle2" gutterBottom>
                Simulation Log:
              </Typography>
              <Stack spacing={0.5}>
                {simulationLog.map((log, index) => (
                  <Chip
                    key={index}
                    label={log}
                    size="small"
                    variant="outlined"
                  />
                ))}
              </Stack>
            </Box>
          )}
        </Stack>
      </CardContent>
    </Card>
  );
};

export default EventSimulator;