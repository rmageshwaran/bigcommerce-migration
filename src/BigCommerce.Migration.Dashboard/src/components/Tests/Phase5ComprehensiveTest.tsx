import React, { useState, useEffect } from 'react';
import {
  Box,
  Card,
  CardContent,
  Typography,
  Button,
  Alert,
  Stack,
  Chip,
  CircularProgress,
  List,
  ListItem,
  ListItemText,
  Divider,
  LinearProgress,
  Paper,
  Grid,
  Accordion,
  AccordionSummary,
  AccordionDetails,
  IconButton,
  Table,
  TableBody,
  TableCell,
  TableContainer,
  TableHead,
  TableRow
} from '@mui/material';
import { 
  CheckCircle as SuccessIcon,
  Error as ErrorIcon,
  Warning as WarningIcon,
  PlayArrow as StartIcon,
  Stop as StopIcon,
  Refresh as RefreshIcon,
  ExpandMore as ExpandMoreIcon,
  Assessment as MetricsIcon
} from '@mui/icons-material';
import { getSignalRService } from '../../services/signalRService';
import { config } from '../../config/environment';
import EventSimulator from './EventSimulator';

interface TestResult {
  id: string;
  name: string;
  status: 'pending' | 'running' | 'passed' | 'failed' | 'warning';
  message: string;
  details?: string;
  duration?: number;
  timestamp?: Date;
  events?: any[];
  metrics?: any;
}

interface EventMetrics {
  totalEvents: number;
  eventsByType: Record<string, number>;
  averageEventInterval: number;
  rateLimitingDetected: boolean;
  lastEventTimestamp?: Date;
}

export const Phase5ComprehensiveTest: React.FC = () => {
  const [testResults, setTestResults] = useState<TestResult[]>([]);
  const [isRunning, setIsRunning] = useState(false);
  const [currentTestIndex, setCurrentTestIndex] = useState(-1);
  const [eventLog, setEventLog] = useState<any[]>([]);
  const [eventMetrics, setEventMetrics] = useState<EventMetrics>({
    totalEvents: 0,
    eventsByType: {},
    averageEventInterval: 0,
    rateLimitingDetected: false
  });
  const [connectionState, setConnectionState] = useState('Disconnected');
  const [error, setError] = useState<string | null>(null);

  const signalRService = getSignalRService();

  // Test definitions for Phase 5
  const testSuite: Array<{id: string, name: string, description: string}> = [
    {
      id: 'signalr-connection',
      name: 'SignalR Connection',
      description: 'Test basic SignalR connection and state management'
    },
    {
      id: 'event-structure-validation',
      name: 'Event Structure Validation',
      description: 'Validate the 4 simplified event types are received correctly'
    },
    {
      id: 'rate-limiting-verification',
      name: 'Rate Limiting Verification',
      description: 'Verify centralized broadcasting respects 2-second intervals'
    },
    {
      id: 'migration-lifecycle-flow',
      name: 'Migration Lifecycle Flow',
      description: 'Test complete migration start → progress → completion flow'
    },
    {
      id: 'error-handling-scenarios',
      name: 'Error Handling Scenarios',
      description: 'Test error and cancellation event handling'
    },
    {
      id: 'concurrent-migration-support',
      name: 'Concurrent Migration Support',
      description: 'Test multiple migrations running simultaneously'
    },
    {
      id: 'dashboard-integration',
      name: 'Dashboard Integration',
      description: 'Test real-time dashboard updates with simplified events'
    },
    {
      id: 'performance-metrics',
      name: 'Performance Metrics',
      description: 'Measure performance improvements over legacy system'
    }
  ];

  // Initialize test results
  useEffect(() => {
    const initialResults = testSuite.map(test => ({
      id: test.id,
      name: test.name,
      status: 'pending' as const,
      message: test.description,
      events: [],
      metrics: {}
    }));
    setTestResults(initialResults);
  }, []);

  // Event logging for metrics
  const logEvent = (eventType: string, eventData: any) => {
    const timestamp = new Date();
    const event = {
      id: `${timestamp.getTime()}_${Math.random()}`,
      type: eventType,
      data: eventData,
      timestamp
    };

    setEventLog(prev => [event, ...prev.slice(0, 99)]); // Keep last 100 events

    // Update metrics
    setEventMetrics(prev => {
      const newMetrics = { ...prev };
      newMetrics.totalEvents++;
      newMetrics.eventsByType[eventType] = (newMetrics.eventsByType[eventType] || 0) + 1;
      newMetrics.lastEventTimestamp = timestamp;

      // Calculate rate limiting detection
      if (prev.lastEventTimestamp) {
        const interval = timestamp.getTime() - prev.lastEventTimestamp.getTime();
        if (interval >= 1800 && interval <= 2200) { // 2 seconds ± 200ms tolerance
          newMetrics.rateLimitingDetected = true;
        }
      }

      return newMetrics;
    });
  };

  // Set up SignalR event listeners
  useEffect(() => {
    // Connection state listener
    const unsubscribeConnection = signalRService.on('connectionStateChanged', (connectionData) => {
      setConnectionState(connectionData.state || 'Unknown');
      logEvent('connection-state', connectionData);
    });

    // Phase 4/5: Simplified SignalR Event Listeners
    const unsubscribeMigrationStarted = signalRService.on('migration-started', (event) => {
      logEvent('migration-started', event);
    });

    const unsubscribeChunkProgress = signalRService.on('chunk-progress', (event) => {
      logEvent('chunk-progress', event);
    });

    const unsubscribeMigrationCompleted = signalRService.on('migration-completed', (event) => {
      logEvent('migration-completed', event);
    });

    const unsubscribeError = signalRService.on('error', (event) => {
      logEvent('error', event);
    });

    // Legacy event listeners for backward compatibility testing
    const unsubscribeProgress = signalRService.on('migrationProgress', (progress) => {
      logEvent('migrationProgress-legacy', progress);
    });

    const unsubscribeStatus = signalRService.on('MigrationStatus', (status) => {
      logEvent('MigrationStatus-legacy', status);
    });

    return () => {
      unsubscribeConnection();
      unsubscribeMigrationStarted();
      unsubscribeChunkProgress();
      unsubscribeMigrationCompleted();
      unsubscribeError();
      unsubscribeProgress();
      unsubscribeStatus();
    };
  }, []);

  // Update test result
  const updateTestResult = (testId: string, updates: Partial<TestResult>) => {
    setTestResults(prev => prev.map(test => 
      test.id === testId ? { ...test, ...updates, timestamp: new Date() } : test
    ));
  };

  // Run individual test
  const runTest = async (testId: string): Promise<boolean> => {
    const startTime = Date.now();
    updateTestResult(testId, { status: 'running', message: 'Test in progress...' });

    try {
      let result = false;
      let message = '';
      let details = '';
      let metrics = {};

      switch (testId) {
        case 'signalr-connection':
          try {
            if (!signalRService.isConnected()) {
              await signalRService.connect();
            }
            result = signalRService.isConnected();
            message = result ? 'SignalR connection successful' : 'SignalR connection failed';
            metrics = { connectionState, connectionTime: Date.now() - startTime };
          } catch (error: any) {
            result = false;
            message = 'SignalR connection failed';
            details = error.message;
          }
          break;

        case 'event-structure-validation':
          // Check all events for simplified event types
          const receivedEventTypes = [...new Set(eventLog.map(e => e.type))];
          const expectedEventTypes = ['migration-started', 'chunk-progress', 'migration-completed', 'error'];
          
          // Check if we have received any of the expected simplified event types
          const hasSimplifiedEvents = receivedEventTypes.some(type => expectedEventTypes.includes(type));
          const receivedSimplifiedTypes = receivedEventTypes.filter(type => expectedEventTypes.includes(type));
          
          result = hasSimplifiedEvents;
          message = result 
            ? `Event structure validation passed. Received: ${receivedSimplifiedTypes.join(', ')}`
            : 'No simplified events received yet';
          metrics = { 
            receivedEventTypes: receivedSimplifiedTypes, 
            expectedEventTypes,
            allEventTypes: receivedEventTypes
          };
          break;

        case 'rate-limiting-verification':
          result = eventMetrics.rateLimitingDetected;
          message = result 
            ? 'Rate limiting detected (2-second intervals)' 
            : 'Rate limiting not yet confirmed';
          metrics = { ...eventMetrics };
          break;

        case 'migration-lifecycle-flow':
          const lifecycleEvents = eventLog.filter(e => 
            ['migration-started', 'chunk-progress', 'migration-completed'].includes(e.type)
          );
          result = lifecycleEvents.length >= 3;
          message = result 
            ? `Complete lifecycle detected (${lifecycleEvents.length} events)`
            : 'Partial or no lifecycle events detected';
          metrics = { lifecycleEvents: lifecycleEvents.length };
          break;

        case 'error-handling-scenarios':
          const errorEvents = eventLog.filter(e => e.type === 'error');
          result = errorEvents.length > 0;
          message = result 
            ? `Error handling verified (${errorEvents.length} error events)`
            : 'No error events received for testing';
          metrics = { errorEvents: errorEvents.length };
          break;

        case 'concurrent-migration-support':
          // Check for events from multiple migration IDs
          const migrationIds = [...new Set(eventLog
            .filter(e => e.data?.migrationId)
            .map(e => e.data.migrationId)
          )];
          result = migrationIds.length > 1;
          message = result 
            ? `Concurrent migrations detected (${migrationIds.length} different IDs)`
            : 'Single or no migration detected';
          metrics = { migrationIds, concurrentCount: migrationIds.length };
          break;

        case 'dashboard-integration':
          // Test if dashboard components can handle simplified events
          const simplifiedEvents = eventLog.filter(e => 
            ['migration-started', 'chunk-progress', 'migration-completed', 'error'].includes(e.type)
          );
          result = simplifiedEvents.length > 0;
          message = result 
            ? `Dashboard integration ready (${simplifiedEvents.length} simplified events)`
            : 'No simplified events for dashboard integration';
          metrics = { simplifiedEvents: simplifiedEvents.length };
          break;

        case 'performance-metrics':
          const legacyEvents = eventLog.filter(e => e.type.includes('legacy'));
          const simplifiedEventsCount = eventLog.filter(e => 
            ['migration-started', 'chunk-progress', 'migration-completed', 'error'].includes(e.type)
          ).length;
          
          const reductionRatio = legacyEvents.length > 0 
            ? (legacyEvents.length - simplifiedEventsCount) / legacyEvents.length 
            : 0;
          
          result = reductionRatio > 0 || simplifiedEventsCount > 0;
          message = result 
            ? `Event reduction: ${Math.round(reductionRatio * 100)}% (${simplifiedEventsCount} simplified vs ${legacyEvents.length} legacy)`
            : 'Performance metrics inconclusive';
          metrics = { 
            legacyEvents: legacyEvents.length, 
            simplifiedEvents: simplifiedEventsCount, 
            reductionRatio 
          };
          break;

        default:
          result = false;
          message = 'Unknown test';
      }

      const duration = Date.now() - startTime;
      const status = result ? 'passed' : 'failed';
      
      updateTestResult(testId, {
        status,
        message,
        details,
        duration,
        metrics
      });

      return result;
    } catch (error: any) {
      const duration = Date.now() - startTime;
      updateTestResult(testId, {
        status: 'failed',
        message: 'Test execution failed',
        details: error.message,
        duration
      });
      return false;
    }
  };

  // Run all tests
  const runAllTests = async () => {
    setIsRunning(true);
    setError(null);
    
    try {
      // Ensure SignalR connection
      if (!signalRService.isConnected()) {
        await signalRService.connect();
      }

      for (let i = 0; i < testSuite.length; i++) {
        setCurrentTestIndex(i);
        await runTest(testSuite[i].id);
        
        // Brief pause between tests
        await new Promise(resolve => setTimeout(resolve, 1000));
      }
    } catch (error: any) {
      setError(`Test suite failed: ${error.message}`);
    } finally {
      setIsRunning(false);
      setCurrentTestIndex(-1);
    }
  };

  // Clear all results
  const clearResults = () => {
    setTestResults(prev => prev.map(test => ({
      ...test,
      status: 'pending' as const,
      message: test.name,
      details: undefined,
      duration: undefined,
      timestamp: undefined,
      events: [],
      metrics: {}
    })));
    setEventLog([]);
    setEventMetrics({
      totalEvents: 0,
      eventsByType: {},
      averageEventInterval: 0,
      rateLimitingDetected: false
    });
    setError(null);
  };

  // Connect to SignalR
  const connectSignalR = async () => {
    try {
      await signalRService.connect();
    } catch (error: any) {
      setError(`Connection failed: ${error.message}`);
    }
  };

  // Disconnect from SignalR
  const disconnectSignalR = async () => {
    try {
      await signalRService.disconnect();
    } catch (error: any) {
      setError(`Disconnection failed: ${error.message}`);
    }
  };

  const getStatusIcon = (status: string) => {
    switch (status) {
      case 'passed': return <SuccessIcon color="success" />;
      case 'failed': return <ErrorIcon color="error" />;
      case 'warning': return <WarningIcon color="warning" />;
      case 'running': return <CircularProgress size={20} />;
      default: return null;
    }
  };

  const getStatusColor = (status: string) => {
    switch (status) {
      case 'passed': return 'success';
      case 'failed': return 'error';
      case 'warning': return 'warning';
      case 'running': return 'info';
      default: return 'default';
    }
  };

  return (
    <Box sx={{ p: 3 }}>
      <Typography variant="h4" gutterBottom>
        🧪 Phase 5: Comprehensive End-to-End Validation
      </Typography>
      
      <Typography variant="body1" color="text.secondary" sx={{ mb: 3 }}>
        Comprehensive testing suite for the simplified SignalR event system. 
        This validates all aspects of the Phase 4 implementation including lifecycle events, 
        rate limiting, error handling, and performance improvements.
      </Typography>

      {error && (
        <Alert severity="error" sx={{ mb: 3 }}>
          {error}
        </Alert>
      )}

      {/* Event Simulator */}
      <EventSimulator onEventGenerated={logEvent} />

      {/* Control Panel */}
      <Card sx={{ mb: 3 }}>
        <CardContent>
          <Stack direction="row" spacing={2} alignItems="center">
            <Chip 
              icon={connectionState === 'Connected' ? <SuccessIcon /> : <ErrorIcon />}
              label={`SignalR: ${connectionState}`}
              color={connectionState === 'Connected' ? 'success' : 'error'}
            />
            
            <Button
              variant="contained"
              startIcon={<StartIcon />}
              onClick={runAllTests}
              disabled={isRunning}
            >
              Run All Tests
            </Button>

            <Button
              variant="outlined"
              startIcon={<RefreshIcon />}
              onClick={clearResults}
              disabled={isRunning}
            >
              Clear Results
            </Button>

            <Button
              variant="outlined"
              onClick={connectSignalR}
              disabled={connectionState === 'Connected' || isRunning}
            >
              Connect
            </Button>

            <Button
              variant="outlined"
              onClick={disconnectSignalR}
              disabled={connectionState === 'Disconnected' || isRunning}
            >
              Disconnect
            </Button>

            {isRunning && (
              <Typography variant="body2" color="text.secondary">
                Running test {currentTestIndex + 1} of {testSuite.length}
              </Typography>
            )}
          </Stack>
        </CardContent>
      </Card>

      {/* Progress Bar */}
      {isRunning && (
        <LinearProgress 
          variant="determinate" 
          value={(currentTestIndex + 1) / testSuite.length * 100} 
          sx={{ mb: 3 }}
        />
      )}

      {/* Test Results Grid */}
      <Grid container spacing={3}>
        {/* Test Results */}
        <Grid item xs={12} lg={8}>
          <Card>
            <CardContent>
              <Typography variant="h6" gutterBottom>
                Test Results
              </Typography>
              
              <List>
                {testResults.map((test, index) => (
                  <React.Fragment key={test.id}>
                    <ListItem>
                      <Box sx={{ display: 'flex', alignItems: 'center', width: '100%' }}>
                        <Box sx={{ mr: 2 }}>
                          {getStatusIcon(test.status)}
                        </Box>
                        
                        <Box sx={{ flexGrow: 1 }}>
                          <Typography variant="subtitle1">
                            {test.name}
                          </Typography>
                          <Typography variant="body2" color="text.secondary">
                            {test.message}
                          </Typography>
                          
                          {test.details && (
                            <Typography variant="caption" color="error">
                              {test.details}
                            </Typography>
                          )}
                          
                          {test.duration && (
                            <Typography variant="caption" color="text.secondary">
                              {' '}({test.duration}ms)
                            </Typography>
                          )}
                        </Box>
                        
                        <Box>
                          <Chip 
                            label={test.status}
                            color={getStatusColor(test.status) as any}
                            size="small"
                          />
                        </Box>
                      </Box>
                    </ListItem>
                    
                    {index < testResults.length - 1 && <Divider />}
                  </React.Fragment>
                ))}
              </List>
            </CardContent>
          </Card>
        </Grid>

        {/* Metrics and Event Log */}
        <Grid item xs={12} lg={4}>
          <Stack spacing={3}>
            {/* Real-time Metrics */}
            <Card>
              <CardContent>
                <Typography variant="h6" gutterBottom>
                  <MetricsIcon sx={{ mr: 1, verticalAlign: 'middle' }} />
                  Real-time Metrics
                </Typography>
                
                <Stack spacing={1}>
                  <Box>
                    <Typography variant="body2">Total Events</Typography>
                    <Typography variant="h6">{eventMetrics.totalEvents}</Typography>
                  </Box>
                  
                  <Box>
                    <Typography variant="body2">Rate Limiting</Typography>
                    <Chip 
                      label={eventMetrics.rateLimitingDetected ? 'Detected' : 'Not Detected'}
                      color={eventMetrics.rateLimitingDetected ? 'success' : 'default'}
                      size="small"
                    />
                  </Box>
                  
                  <Box>
                    <Typography variant="body2">Event Types</Typography>
                    {Object.entries(eventMetrics.eventsByType).map(([type, count]) => (
                      <Chip key={type} label={`${type}: ${count}`} size="small" sx={{ mr: 0.5, mb: 0.5 }} />
                    ))}
                  </Box>
                </Stack>
              </CardContent>
            </Card>

            {/* Recent Events */}
            <Card>
              <CardContent>
                <Typography variant="h6" gutterBottom>
                  Recent Events ({eventLog.length})
                </Typography>
                
                <List dense sx={{ maxHeight: 400, overflow: 'auto' }}>
                  {eventLog.slice(0, 10).map((event) => (
                    <ListItem key={event.id} divider>
                      <ListItemText
                        primary={
                          <Typography variant="caption" component="div">
                            <Chip 
                              label={event.type} 
                              size="small" 
                              color={event.type.includes('error') ? 'error' : 'default'}
                            />
                          </Typography>
                        }
                        secondary={
                          <Typography variant="caption" color="text.secondary">
                            {event.timestamp.toLocaleTimeString()}
                          </Typography>
                        }
                      />
                    </ListItem>
                  ))}
                </List>
              </CardContent>
            </Card>
          </Stack>
        </Grid>
      </Grid>

      {/* Detailed Results Accordion */}
      <Card sx={{ mt: 3 }}>
        <CardContent>
          <Typography variant="h6" gutterBottom>
            Detailed Test Results
          </Typography>
          
          {testResults.map((test) => (
            <Accordion key={test.id}>
              <AccordionSummary expandIcon={<ExpandMoreIcon />}>
                <Box sx={{ display: 'flex', alignItems: 'center', width: '100%' }}>
                  <Box sx={{ mr: 2 }}>
                    {getStatusIcon(test.status)}
                  </Box>
                  <Typography variant="subtitle1" sx={{ flexGrow: 1 }}>
                    {test.name}
                  </Typography>
                  <Chip 
                    label={test.status}
                    color={getStatusColor(test.status) as any}
                    size="small"
                  />
                </Box>
              </AccordionSummary>
              
              <AccordionDetails>
                <Stack spacing={2}>
                  <Typography variant="body2">
                    <strong>Message:</strong> {test.message}
                  </Typography>
                  
                  {test.details && (
                    <Typography variant="body2">
                      <strong>Details:</strong> {test.details}
                    </Typography>
                  )}
                  
                  {test.duration && (
                    <Typography variant="body2">
                      <strong>Duration:</strong> {test.duration}ms
                    </Typography>
                  )}
                  
                  {test.metrics && Object.keys(test.metrics).length > 0 && (
                    <Box>
                      <Typography variant="body2"><strong>Metrics:</strong></Typography>
                      <Paper sx={{ p: 1, backgroundColor: 'grey.50' }}>
                        <pre style={{ fontSize: '12px', margin: 0 }}>
                          {JSON.stringify(test.metrics, null, 2)}
                        </pre>
                      </Paper>
                    </Box>
                  )}
                </Stack>
              </AccordionDetails>
            </Accordion>
          ))}
        </CardContent>
      </Card>
    </Box>
  );
};

export default Phase5ComprehensiveTest;