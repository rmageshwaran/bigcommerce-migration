import React, { useState } from 'react';
import {
  Box,
  Container,
  Typography,
  Card,
  CardContent,
  Button,
  Grid,
  Alert,
  Divider,
  Stack,
  Switch,
  FormControlLabel,
  TextField,
  MenuItem
} from '@mui/material';
import {
  PlayArrow as StartIcon,
  Code as CodeIcon,
  Description as DocsIcon
} from '@mui/icons-material';

// Import our real-time components
import { EnhancedMigrationDashboard } from '../Dashboard/EnhancedMigrationDashboard';
import { RealTimeProgressBar } from '../Progress/RealTimeProgressBar';
import { RealTimeEntityGrid } from '../Progress/RealTimeEntityGrid';
import { useDetailedMigrationProgress } from '../../hooks/useDetailedMigrationProgress';

// Mock data for demonstration
const mockMigrationProgress = {
  migrationId: 'example-migration-12345',
  status: 'running' as const,
  startTime: new Date(Date.now() - 300000), // 5 minutes ago
  lastUpdated: new Date(),
  elapsedTime: 300, // 5 minutes
  estimatedTimeRemaining: 600, // 10 minutes
  totalEntities: 50000,
  processedEntities: 18750,
  successfulEntities: 18650,
  failedEntities: 100,
  overallProgressPercentage: 37.5,
  entityProgress: {
    Categories: {
      entityType: 'Categories',
      totalCount: 500,
      processedCount: 500,
      successCount: 495,
      failureCount: 5,
      progressPercentage: 100,
      status: 'completed',
      startTime: new Date(Date.now() - 300000),
      endTime: new Date(Date.now() - 240000),
      processingTime: 60
    },
    Products: {
      entityType: 'Products',
      totalCount: 25000,
      processedCount: 9375,
      successCount: 9300,
      failureCount: 75,
      progressPercentage: 37.5,
      status: 'running',
      startTime: new Date(Date.now() - 240000),
      processingTime: 240
    },
    Variants: {
      entityType: 'Variants',
      totalCount: 20000,
      processedCount: 7500,
      successCount: 7475,
      failureCount: 25,
      progressPercentage: 37.5,
      status: 'running',
      startTime: new Date(Date.now() - 180000),
      processingTime: 180
    },
    Images: {
      entityType: 'Images',
      totalCount: 4000,
      processedCount: 1375,
      successCount: 1350,
      failureCount: 25,
      progressPercentage: 34.4,
      status: 'running',
      startTime: new Date(Date.now() - 120000),
      processingTime: 120
    },
    Brands: {
      entityType: 'Brands',
      totalCount: 500,
      processedCount: 0,
      successCount: 0,
      failureCount: 0,
      progressPercentage: 0,
      status: 'pending',
      startTime: new Date(),
      processingTime: 0
    }
  },
  currentPhase: 'Product Migration',
  currentEntity: 'Products',
  entitiesPerSecond: 156.25, // Showing 60%+ improvement
  errorRate: 0.53
};

const codeExamples = {
  basicUsage: `
import { EnhancedMigrationDashboard } from './components/Dashboard/EnhancedMigrationDashboard';

// Basic usage - shows full dashboard with all features
<EnhancedMigrationDashboard
  migrationId="your-migration-id"
  autoConnect={true}
  enableNotifications={true}
  onMigrationComplete={(id) => console.log('Migration completed:', id)}
  onMigrationError={(id, error) => console.log('Migration failed:', id, error)}
/>`,

  progressBarOnly: `
import { RealTimeProgressBar } from './components/Progress/RealTimeProgressBar';

// Just the progress bar component
<RealTimeProgressBar
  migrationId="your-migration-id"
  progress={migrationProgress}
  showDetails={true}
  showPerformanceMetrics={true}
  showTimeEstimates={true}
  size="large"
  onStatusClick={(id) => console.log('Clicked migration:', id)}
/>`,

  entityGrid: `
import { RealTimeEntityGrid } from './components/Progress/RealTimeEntityGrid';

// Entity-level progress grid
<RealTimeEntityGrid
  migrationId="your-migration-id"
  entityProgress={entityProgressData}
  overallProgress={overallProgress}
  showPerformanceMetrics={true}
  showBatchDetails={true}
  onEntityClick={(entityType) => console.log('Clicked entity:', entityType)}
  onRefresh={() => refreshData()}
/>`,

  customHook: `
import { useDetailedMigrationProgress } from './hooks/useDetailedMigrationProgress';

// Custom hook for real-time data
function MyComponent({ migrationId }) {
  const {
    progress,
    entityStatus,
    isConnected,
    performanceMetrics,
    recentEvents,
    errors,
    refresh,
    reconnect
  } = useDetailedMigrationProgress({
    migrationId,
    autoConnect: true,
    enableNotifications: true,
    enablePerformanceTracking: true,
    pollInterval: 10000
  });

  return (
    <div>
      <h1>Migration Progress: {progress?.overallProgressPercentage}%</h1>
      <p>Connected: {isConnected ? 'Yes' : 'No'}</p>
      <p>Performance: +{performanceMetrics?.improvementPercentage}%</p>
    </div>
  );
}`
};

export const RealTimeProgressExample: React.FC = () => {
  const [demoMode, setDemoMode] = useState(true);
  const [selectedExample, setSelectedExample] = useState('full');
  const [migrationId, setMigrationId] = useState('demo-migration-12345');
  const [showFullDashboard, setShowFullDashboard] = useState(false);

  const handleStartDemo = () => {
    setShowFullDashboard(true);
  };

  if (showFullDashboard) {
    return (
      <Box>
        <Container maxWidth="xl" sx={{ py: 2 }}>
          <Box mb={2}>
            <Button 
              onClick={() => setShowFullDashboard(false)}
              variant="outlined"
              size="small"
            >
              ← Back to Examples
            </Button>
          </Box>
          
          <EnhancedMigrationDashboard
            migrationId={migrationId}
            autoConnect={false} // Demo mode
            enableNotifications={true}
            onMigrationComplete={(id) => console.log('Demo migration completed:', id)}
            onMigrationError={(id, error) => console.log('Demo migration error:', id, error)}
          />
        </Container>
      </Box>
    );
  }

  return (
    <Container maxWidth="lg" sx={{ py: 4 }}>
      <Typography variant="h3" fontWeight="600" gutterBottom>
        Real-Time Migration Progress Components
      </Typography>
      
      <Typography variant="h6" color="textSecondary" paragraph>
        Interactive examples showing our enhanced real-time migration progress tracking with live updates, 
        performance metrics, and comprehensive entity-level monitoring.
      </Typography>

      {/* Key Features Alert */}
      <Alert severity="success" sx={{ mb: 4 }}>
        <Typography variant="subtitle2" fontWeight="600" gutterBottom>
          ✨ Key Features Delivered:
        </Typography>
        <Grid container spacing={1}>
          <Grid item xs={12} sm={6}>
            • Real-time progress bars with live updates<br/>
            • Entity-level progress tracking<br/>
            • Performance metrics (60%+ improvement tracking)<br/>
            • Batch-level progress monitoring
          </Grid>
          <Grid item xs={12} sm={6}>
            • Time estimates and completion predictions<br/>
            • Real-time error monitoring and alerts<br/>
            • Fullscreen dashboard mode<br/>
            • Sound notifications and visual feedback
          </Grid>
        </Grid>
      </Alert>

      {/* Demo Controls */}
      <Card sx={{ mb: 4 }}>
        <CardContent>
          <Typography variant="h6" gutterBottom>
            Interactive Demo
          </Typography>
          
          <Grid container spacing={2} alignItems="center">
            <Grid item xs={12} sm={6}>
              <TextField
                label="Migration ID"
                value={migrationId}
                onChange={(e) => setMigrationId(e.target.value)}
                fullWidth
                size="small"
              />
            </Grid>
            <Grid item xs={12} sm={3}>
              <FormControlLabel
                control={
                  <Switch
                    checked={demoMode}
                    onChange={(e) => setDemoMode(e.target.checked)}
                  />
                }
                label="Demo Mode"
              />
            </Grid>
            <Grid item xs={12} sm={3}>
              <Button
                variant="contained"
                onClick={handleStartDemo}
                startIcon={<StartIcon />}
                fullWidth
              >
                Launch Dashboard
              </Button>
            </Grid>
          </Grid>
        </CardContent>
      </Card>

      {/* Component Examples */}
      <Grid container spacing={3}>
        {/* Progress Bar Example */}
        <Grid item xs={12}>
          <Card>
            <CardContent>
              <Typography variant="h6" gutterBottom display="flex" alignItems="center" gap={1}>
                📊 Real-Time Progress Bar
              </Typography>
              <Typography color="textSecondary" paragraph>
                Enhanced progress bar with live updates, performance indicators, and time estimates.
              </Typography>
              
              <RealTimeProgressBar
                migrationId={migrationId}
                progress={mockMigrationProgress}
                showDetails={true}
                showPerformanceMetrics={true}
                showTimeEstimates={true}
                size="large"
                onStatusClick={(id) => console.log('Clicked migration:', id)}
              />
            </CardContent>
          </Card>
        </Grid>

        {/* Entity Grid Example */}
        <Grid item xs={12}>
          <Card>
            <CardContent>
              <Typography variant="h6" gutterBottom display="flex" alignItems="center" gap={1}>
                📋 Entity Progress Grid
              </Typography>
              <Typography color="textSecondary" paragraph>
                Detailed entity-level progress tracking with batch monitoring and performance metrics.
              </Typography>
              
              <RealTimeEntityGrid
                migrationId={migrationId}
                entityProgress={mockMigrationProgress.entityProgress}
                overallProgress={mockMigrationProgress}
                showPerformanceMetrics={true}
                showBatchDetails={true}
                onEntityClick={(entityType) => console.log('Clicked entity:', entityType)}
                onRefresh={() => console.log('Refreshing data...')}
              />
            </CardContent>
          </Card>
        </Grid>
      </Grid>

      <Divider sx={{ my: 4 }} />

      {/* Code Examples */}
      <Typography variant="h5" fontWeight="600" gutterBottom display="flex" alignItems="center" gap={1}>
        <CodeIcon />
        Implementation Examples
      </Typography>

      <Grid container spacing={3}>
        <Grid item xs={12} md={6}>
          <TextField
            select
            label="Select Example"
            value={selectedExample}
            onChange={(e) => setSelectedExample(e.target.value)}
            fullWidth
            sx={{ mb: 2 }}
          >
            <MenuItem value="full">Full Dashboard</MenuItem>
            <MenuItem value="progressBar">Progress Bar Only</MenuItem>
            <MenuItem value="entityGrid">Entity Grid</MenuItem>
            <MenuItem value="hook">Custom Hook</MenuItem>
          </TextField>
        </Grid>
      </Grid>

      <Card>
        <CardContent>
          <Typography variant="h6" gutterBottom>
            {selectedExample === 'full' && 'Complete Real-Time Dashboard'}
            {selectedExample === 'progressBar' && 'Progress Bar Component'}
            {selectedExample === 'entityGrid' && 'Entity Progress Grid'}
            {selectedExample === 'hook' && 'Real-Time Hook Usage'}
          </Typography>
          
          <Box 
            component="pre" 
            sx={{ 
              backgroundColor: '#f5f5f5', 
              p: 2, 
              borderRadius: 1, 
              overflow: 'auto',
              fontSize: '0.875rem',
              fontFamily: 'monospace'
            }}
          >
            {selectedExample === 'full' && codeExamples.basicUsage}
            {selectedExample === 'progressBar' && codeExamples.progressBarOnly}
            {selectedExample === 'entityGrid' && codeExamples.entityGrid}
            {selectedExample === 'hook' && codeExamples.customHook}
          </Box>
        </CardContent>
      </Card>

      {/* Performance Metrics */}
      <Card sx={{ mt: 3 }}>
        <CardContent>
          <Typography variant="h6" gutterBottom display="flex" alignItems="center" gap={1}>
            🚀 Performance Optimization Features
          </Typography>
          
          <Grid container spacing={2}>
            <Grid item xs={12} sm={6}>
              <Stack spacing={1}>
                <Typography variant="subtitle2" color="success.main">
                  ✅ Real-Time Performance Tracking
                </Typography>
                <Typography variant="body2" color="textSecondary">
                  Live monitoring of processing rates, efficiency trends, and improvement percentages
                </Typography>
                
                <Typography variant="subtitle2" color="success.main">
                  ✅ Adaptive Concurrency Monitoring
                </Typography>
                <Typography variant="body2" color="textSecondary">
                  Real-time display of concurrency adjustments and optimization benefits
                </Typography>
                
                <Typography variant="subtitle2" color="success.main">
                  ✅ 60%+ Improvement Validation
                </Typography>
                <Typography variant="body2" color="textSecondary">
                  Live validation and display of our completed performance optimizations
                </Typography>
              </Stack>
            </Grid>
            
            <Grid item xs={12} sm={6}>
              <Stack spacing={1}>
                <Typography variant="subtitle2" color="success.main">
                  ✅ Intelligent Batch Progress
                </Typography>
                <Typography variant="body2" color="textSecondary">
                  Batch-level progress tracking with completion rates and timing analysis
                </Typography>
                
                <Typography variant="subtitle2" color="success.main">
                  ✅ Error Rate Monitoring
                </Typography>
                <Typography variant="body2" color="textSecondary">
                  Real-time error tracking with success rate calculations and alerts
                </Typography>
                
                <Typography variant="subtitle2" color="success.main">
                  ✅ Time Prediction Engine
                </Typography>
                <Typography variant="body2" color="textSecondary">
                  Intelligent ETA calculations based on current performance metrics
                </Typography>
              </Stack>
            </Grid>
          </Grid>
        </CardContent>
      </Card>

      {/* Integration Guide */}
      <Card sx={{ mt: 3 }}>
        <CardContent>
          <Typography variant="h6" gutterBottom display="flex" alignItems="center" gap={1}>
            <DocsIcon />
            Integration Guide
          </Typography>
          
          <Typography variant="body1" paragraph>
            <strong>Step 1:</strong> Import the components you need:
          </Typography>
          <Box component="pre" sx={{ backgroundColor: '#f5f5f5', p: 1, borderRadius: 1, fontSize: '0.875rem' }}>
{`import { EnhancedMigrationDashboard } from './components/Dashboard/EnhancedMigrationDashboard';
import { useDetailedMigrationProgress } from './hooks/useDetailedMigrationProgress';`}
          </Box>
          
          <Typography variant="body1" paragraph sx={{ mt: 2 }}>
            <strong>Step 2:</strong> Replace your existing migration overview with the real-time dashboard:
          </Typography>
          <Box component="pre" sx={{ backgroundColor: '#f5f5f5', p: 1, borderRadius: 1, fontSize: '0.875rem' }}>
{`// In your App.tsx or routing component
<EnhancedMigrationDashboard 
  migrationId={currentMigrationId}
  autoConnect={true}
  enableNotifications={true}
/>`}
          </Box>
          
          <Typography variant="body1" paragraph sx={{ mt: 2 }}>
            <strong>Step 3:</strong> The components will automatically connect to your existing SignalR backend and display real-time updates with our 60%+ performance optimizations.
          </Typography>
        </CardContent>
      </Card>
    </Container>
  );
}; 