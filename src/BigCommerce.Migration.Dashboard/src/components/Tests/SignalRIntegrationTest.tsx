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
  Divider
} from '@mui/material';
import { 
  CheckCircle as ConnectedIcon,
  Error as DisconnectedIcon,
  Refresh as RefreshIcon 
} from '@mui/icons-material';
import { getSignalRService } from '../../services/signalRService';
import { config } from '../../config/environment';

interface SignalREvent {
  id: string;
  type: string;
  data: any;
  timestamp: Date;
}

export const SignalRIntegrationTest: React.FC = () => {
  const [isConnected, setIsConnected] = useState(false);
  const [connectionState, setConnectionState] = useState('Disconnected');
  const [connectionId, setConnectionId] = useState('');
  const [events, setEvents] = useState<SignalREvent[]>([]);
  const [isConnecting, setIsConnecting] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const signalRService = getSignalRService();

  // Add event to list
  const addEvent = (type: string, data: any) => {
    const event: SignalREvent = {
      id: `${Date.now()}_${Math.random()}`,
      type,
      data,
      timestamp: new Date()
    };
    setEvents(prev => [event, ...prev.slice(0, 19)]); // Keep last 20 events
  };

  // Test SignalR connection
  const testConnection = async () => {
    setIsConnecting(true);
    setError(null);
    
    try {
      if (!signalRService.isConnected()) {
        await signalRService.connect();
      }
      addEvent('Connection', 'Successfully connected to SignalR');
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to connect');
      addEvent('Error', err);
    } finally {
      setIsConnecting(false);
    }
  };

  // Disconnect from SignalR
  const disconnect = async () => {
    try {
      await signalRService.disconnect();
      addEvent('Disconnection', 'Disconnected from SignalR');
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to disconnect');
    }
  };

  // Test simplified events (Phase 3)
  const testSimplifiedEvents = async () => {
    try {
      // Test the new simplified event system
      addEvent('Test Simplified Events', 'Testing Phase 3 simplified event reception...');
      
      // In a real scenario, these events would come from our backend
      // via the centralized broadcasting service → SignalR Functions → frontend pipeline
      console.log('🎯 Testing Phase 3 simplified event system:');
      console.log('- Expecting: migration-started, chunk-progress, migration-completed, error');
      console.log('- Backend sends via CentralizedProgressBroadcastService');
      console.log('- Events are rate-limited and simplified for better performance');
      console.log('- Frontend receives clean, consistent event structure');
      
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to test simplified events');
    }
  };

  // Clear events
  const clearEvents = () => {
    setEvents([]);
  };

  // Set up event listeners
  useEffect(() => {
    // Connection state listener
    const unsubscribeConnection = signalRService.on('connectionStateChanged', (connectionData) => {
      setConnectionState(connectionData.state || 'Unknown');
      setIsConnected(connectionData.state === 'Connected');
      setConnectionId(connectionData.connectionId || '');
      addEvent('ConnectionStateChanged', connectionData);
    });

    // Phase 3: Simplified SignalR Event Listeners
    const unsubscribeMigrationStarted = signalRService.on('migration-started', (event) => {
      addEvent('🚀 Migration Started (Phase 3)', event);
    });

    const unsubscribeChunkProgress = signalRService.on('chunk-progress', (event) => {
      addEvent('📊 Chunk Progress (Phase 3)', event);
    });

    const unsubscribeMigrationCompleted = signalRService.on('migration-completed', (event) => {
      addEvent('✅ Migration Completed (Phase 3)', event);
    });

    const unsubscribeSimplifiedError = signalRService.on('error', (event) => {
      addEvent('❌ Error Event (Phase 3)', event);
    });

    // Legacy event listeners (for backward compatibility testing)
    const unsubscribeProgress = signalRService.on('migrationProgress', (progress) => {
      addEvent('MigrationProgress (Legacy)', progress);
    });

    const unsubscribeStatus = signalRService.on('MigrationStatus', (status) => {
      addEvent('MigrationStatus (Legacy)', status);
    });

    const unsubscribeDetailedProgress = signalRService.on('DetailedProgress', (progress) => {
      addEvent('EntityProgressUpdated→DetailedProgress (Legacy)', progress);
    });

    const unsubscribeBatchStarted = signalRService.on('BatchStarted', (batch) => {
      addEvent('BatchProgressUpdated→BatchStarted (Legacy)', batch);
    });

    const unsubscribeBatchCompleted = signalRService.on('BatchCompleted', (batch) => {
      addEvent('BatchProgressUpdated→BatchCompleted (Legacy)', batch);
    });

    const unsubscribeEntityUpdate = signalRService.on('entityUpdate', (entityProgress) => {
      addEvent('EntityProgressUpdated→entityUpdate (Legacy)', entityProgress);
    });

    const unsubscribePerformanceMetrics = signalRService.on('PerformanceMetrics', (metrics) => {
      addEvent('PerformanceMetrics (legacy)', metrics);
    });

    // System health listener
    const unsubscribeHealth = signalRService.on('systemHealth', (health) => {
      addEvent('SystemHealth', health);
    });

    // Legacy error listener (for testing backward compatibility)
    const unsubscribeLegacyError = signalRService.on('ErrorOccurred', (errorData) => {
      addEvent('ErrorOccurred (Legacy)', errorData);
      setError(errorData.message || 'SignalR error occurred');
    });

    // Get initial connection state
    const connectionState = signalRService.getConnectionState();
    setIsConnected(connectionState.isConnected);
    setConnectionState(connectionState.connectionState || 'Disconnected');

    // Cleanup
    return () => {
      // Simplified event unsubscriptions
      unsubscribeMigrationStarted();
      unsubscribeChunkProgress();
      unsubscribeMigrationCompleted();
      unsubscribeSimplifiedError();
      
      // Legacy event unsubscriptions
      unsubscribeConnection();
      unsubscribeProgress();
      unsubscribeStatus();
      unsubscribeDetailedProgress();
      unsubscribeBatchStarted();
      unsubscribeBatchCompleted();
      unsubscribeEntityUpdate();
      unsubscribePerformanceMetrics();
      unsubscribeHealth();
      unsubscribeLegacyError();
    };
  }, [signalRService]);

  return (
    <Box p={3}>
      <Typography variant="h4" gutterBottom>
        📡 SignalR Integration Test
      </Typography>
      
      <Stack spacing={3}>
        {/* Connection Status */}
        <Card>
          <CardContent>
            <Typography variant="h6" gutterBottom>
              Connection Status
            </Typography>
            
            <Stack direction="row" spacing={2} alignItems="center" mb={2}>
              {isConnected ? (
                <>
                  <ConnectedIcon color="success" />
                  <Chip label={connectionState} color="success" />
                </>
              ) : (
                <>
                  <DisconnectedIcon color="error" />
                  <Chip label={connectionState} color="error" />
                </>
              )}
              
              {connectionId && (
                <Typography variant="body2" color="text.secondary">
                  ID: {connectionId}
                </Typography>
              )}
            </Stack>

            <Stack direction="row" spacing={2}>
              <Button
                variant="contained"
                onClick={testConnection}
                disabled={isConnecting || isConnected}
                startIcon={isConnecting ? <CircularProgress size={20} /> : <RefreshIcon />}
              >
                {isConnecting ? 'Connecting...' : 'Connect'}
              </Button>
              
              <Button
                variant="outlined"
                onClick={disconnect}
                disabled={!isConnected}
              >
                Disconnect
              </Button>
              
              <Button
                variant="outlined"
                onClick={testSimplifiedEvents}
                disabled={!isConnected}
                              >
                  Test Simplified Events
                </Button>
            </Stack>

            {error && (
              <Alert severity="error" sx={{ mt: 2 }}>
                {error}
              </Alert>
            )}
          </CardContent>
        </Card>

        {/* Configuration Info */}
        <Card>
          <CardContent>
            <Typography variant="h6" gutterBottom>
              Configuration
            </Typography>
            
            <List dense>
              <ListItem>
                <ListItemText 
                  primary="Hub URL" 
                  secondary={config.signalR.hubUrl} 
                />
              </ListItem>
              <ListItem>
                <ListItemText 
                  primary="API Base URL" 
                  secondary={config.api.baseUrl} 
                />
              </ListItem>
              <ListItem>
                <ListItemText 
                  primary="Has API Key" 
                  secondary={config.auth.apiKey ? 'Yes' : 'No'} 
                />
              </ListItem>
              <ListItem>
                <ListItemText 
                  primary="Debug Logging" 
                  secondary={config.features.enableDebugLogging ? 'Enabled' : 'Disabled'} 
                />
              </ListItem>
            </List>
          </CardContent>
        </Card>

        {/* Event Log */}
        <Card>
          <CardContent>
            <Stack direction="row" justifyContent="space-between" alignItems="center" mb={2}>
              <Typography variant="h6">
                Event Log ({events.length})
              </Typography>
              <Button onClick={clearEvents} variant="outlined" size="small">
                Clear
              </Button>
            </Stack>
            
            {events.length === 0 ? (
              <Typography color="text.secondary">
                No events received. Connect to SignalR to see real-time events.
              </Typography>
            ) : (
              <List sx={{ maxHeight: 400, overflow: 'auto' }}>
                {events.map((event, index) => (
                  <React.Fragment key={event.id}>
                    <ListItem>
                      <ListItemText
                        primary={
                          <Stack direction="row" spacing={1} alignItems="center">
                            <Chip label={event.type} size="small" variant="outlined" />
                            <Typography variant="body2" color="text.secondary">
                              {event.timestamp.toLocaleTimeString()}
                            </Typography>
                          </Stack>
                        }
                        secondary={
                          <Typography 
                            variant="body2" 
                            component="pre" 
                            sx={{ 
                              fontSize: '0.75rem', 
                              fontFamily: 'monospace',
                              maxHeight: 100,
                              overflow: 'auto',
                              backgroundColor: 'rgba(0, 0, 0, 0.04)',
                              padding: 1,
                              borderRadius: 1,
                              mt: 1
                            }}
                          >
                            {JSON.stringify(event.data, null, 2)}
                          </Typography>
                        }
                      />
                    </ListItem>
                    {index < events.length - 1 && <Divider />}
                  </React.Fragment>
                ))}
              </List>
            )}
          </CardContent>
        </Card>
      </Stack>
    </Box>
  );
}; 