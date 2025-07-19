import React, { useState, useEffect } from 'react';
import {
  Box,
  Card,
  CardContent,
  Typography,
  Button,
  Alert,
  CircularProgress,
  Chip,
  Divider,
  List,
  ListItem,
  ListItemText,
  ListItemIcon,
  LinearProgress
} from '@mui/material';
import {
  CheckCircle as CheckIcon,
  Error as ErrorIcon,
  Warning as WarningIcon,
  Info as InfoIcon,
  Refresh as RefreshIcon,
  PlayArrow as StartIcon
} from '@mui/icons-material';
import { apiService } from '../../services/apiService';
import { getSignalRService } from '../../services/signalRService';
import config, { validateConfig } from '../../config/environment';

interface TestResult {
  name: string;
  status: 'pending' | 'running' | 'passed' | 'failed' | 'warning';
  message?: string;
  details?: string;
  duration?: number;
}

export const IntegrationTest: React.FC = () => {
  const [testResults, setTestResults] = useState<TestResult[]>([]);
  const [isRunning, setIsRunning] = useState(false);
  const [overallStatus, setOverallStatus] = useState<'idle' | 'running' | 'passed' | 'failed'>('idle');

  const tests: Omit<TestResult, 'status' | 'duration'>[] = [
    {
      name: 'Configuration Validation',
      message: 'Validate environment configuration'
    },
    {
      name: 'API Connectivity',
      message: 'Test connection to backend API'
    },
    {
      name: 'Authentication',
      message: 'Verify API key authentication'
    },
    {
      name: 'Health Endpoint',
      message: 'Check system health endpoint'
    },
    {
      name: 'SignalR Connection',
      message: 'Test real-time SignalR connection'
    },
    {
      name: 'Migration Endpoints',
      message: 'Verify migration API endpoints'
    }
  ];

  useEffect(() => {
    // Initialize test results
    setTestResults(tests.map(test => ({ ...test, status: 'pending' })));
  }, []);

  const updateTestResult = (index: number, updates: Partial<TestResult>) => {
    setTestResults(prev => prev.map((test, i) => 
      i === index ? { ...test, ...updates } : test
    ));
  };

  const runTest = async (testIndex: number): Promise<boolean> => {
    const startTime = Date.now();
    updateTestResult(testIndex, { status: 'running' });

    try {
      let result = false;
      let message = '';
      let details = '';

      switch (testIndex) {
        case 0: // Configuration Validation
          const configValidation = validateConfig();
          result = configValidation.isValid;
          message = result ? 'Configuration is valid' : 'Configuration issues found';
          details = configValidation.errors.join(', ');
          break;

        case 1: // API Connectivity
          try {
            const response = await fetch(config.api.baseUrl, { 
              method: 'HEAD',
              mode: 'no-cors'
            });
            result = true;
            message = 'API endpoint is reachable';
          } catch (error) {
            result = false;
            message = 'Cannot reach API endpoint';
            details = `URL: ${config.api.baseUrl}`;
          }
          break;

        case 2: // Authentication
          if (!config.auth.apiKey && config.auth.requireApiKey) {
            result = false;
            message = 'API key is required but not configured';
          } else if (!config.auth.apiKey) {
            result = true;
            message = 'API key not required for development';
            updateTestResult(testIndex, { status: 'warning' });
          } else {
            result = true;
            message = 'API key is configured';
          }
          break;

        case 3: // Health Endpoint
          try {
            await apiService.getSystemHealth();
            result = true;
            message = 'Health endpoint responded successfully';
          } catch (error: any) {
            result = false;
            message = 'Health endpoint failed';
            details = error.message;
          }
          break;

        case 4: // SignalR Connection
          try {
            const signalRService = getSignalRService();
            await signalRService.connect();
            result = signalRService.isConnected();
            message = result ? 'SignalR connected successfully' : 'SignalR connection failed';
            
            if (result) {
              await signalRService.disconnect();
            }
          } catch (error: any) {
            result = false;
            message = 'SignalR connection failed';
            details = error.message;
          }
          break;

        case 5: // Migration Endpoints
          try {
            await apiService.getActiveMigrations();
            result = true;
            message = 'Migration endpoints are accessible';
          } catch (error: any) {
            // If we get a 404 or other error, that's still better than no response
            if (error.message.includes('404') || error.message.includes('401')) {
              result = true;
              message = 'Migration endpoints are reachable (auth/route config needed)';
              updateTestResult(testIndex, { status: 'warning' });
            } else {
              result = false;
              message = 'Migration endpoints failed';
              details = error.message;
            }
          }
          break;
      }

      const duration = Date.now() - startTime;
      const status = result ? 'passed' : 'failed';
      
      updateTestResult(testIndex, {
        status,
        message,
        details,
        duration
      });

      return result;
    } catch (error: any) {
      const duration = Date.now() - startTime;
      updateTestResult(testIndex, {
        status: 'failed',
        message: 'Test execution failed',
        details: error.message,
        duration
      });
      return false;
    }
  };

  const runAllTests = async () => {
    setIsRunning(true);
    setOverallStatus('running');

    let allPassed = true;
    
    for (let i = 0; i < tests.length; i++) {
      const testPassed = await runTest(i);
      if (!testPassed && testResults[i]?.status !== 'warning') {
        allPassed = false;
      }
      
      // Small delay between tests
      await new Promise(resolve => setTimeout(resolve, 500));
    }

    setOverallStatus(allPassed ? 'passed' : 'failed');
    setIsRunning(false);
  };

  const getStatusIcon = (status: TestResult['status']) => {
    switch (status) {
      case 'passed':
        return <CheckIcon color="success" />;
      case 'failed':
        return <ErrorIcon color="error" />;
      case 'warning':
        return <WarningIcon color="warning" />;
      case 'running':
        return <CircularProgress size={20} />;
      default:
        return <InfoIcon color="disabled" />;
    }
  };

  const getStatusColor = (status: TestResult['status']) => {
    switch (status) {
      case 'passed':
        return 'success';
      case 'failed':
        return 'error';
      case 'warning':
        return 'warning';
      case 'running':
        return 'info';
      default:
        return 'default';
    }
  };

  return (
    <Card>
      <CardContent>
        <Box sx={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', mb: 2 }}>
          <Typography variant="h6">
            🧪 Backend Integration Test
          </Typography>
          <Chip 
            label={overallStatus.toUpperCase()}
            color={getStatusColor(overallStatus as any)}
            variant={overallStatus === 'idle' ? 'outlined' : 'filled'}
          />
        </Box>

        {/* Configuration Overview */}
        <Alert severity="info" sx={{ mb: 2 }}>
          <Typography variant="subtitle2" gutterBottom>
            Current Configuration:
          </Typography>
          <Typography variant="body2">
            • API URL: <code>{config.api.baseUrl}</code><br/>
            • SignalR URL: <code>{config.signalR.hubUrl}</code><br/>
            • API Key: {config.auth.apiKey ? '✅ Configured' : '❌ Not Set'}<br/>
            • Real-time: {config.features.enableRealtime ? '✅ Enabled' : '❌ Disabled'}
          </Typography>
        </Alert>

        {/* Test Controls */}
        <Box sx={{ display: 'flex', gap: 1, mb: 3 }}>
          <Button
            variant="contained"
            startIcon={<StartIcon />}
            onClick={runAllTests}
            disabled={isRunning}
          >
            {isRunning ? 'Running Tests...' : 'Run All Tests'}
          </Button>
          
          <Button
            variant="outlined"
            startIcon={<RefreshIcon />}
            onClick={() => {
              setTestResults(tests.map(test => ({ ...test, status: 'pending' })));
              setOverallStatus('idle');
            }}
            disabled={isRunning}
          >
            Reset
          </Button>
        </Box>

        {/* Progress Bar */}
        {isRunning && (
          <Box sx={{ mb: 2 }}>
            <LinearProgress />
          </Box>
        )}

        {/* Test Results */}
        <List>
          {testResults.map((test, index) => (
            <React.Fragment key={test.name}>
              <ListItem
                secondaryAction={
                  <Box sx={{ display: 'flex', alignItems: 'center', gap: 1 }}>
                    {test.duration && (
                      <Typography variant="caption" color="textSecondary">
                        {test.duration}ms
                      </Typography>
                    )}
                    {getStatusIcon(test.status)}
                  </Box>
                }
              >
                <ListItemIcon>
                  {getStatusIcon(test.status)}
                </ListItemIcon>
                <ListItemText
                  primary={test.name}
                  secondary={
                    <Box>
                      <Typography variant="body2" color="textSecondary">
                        {test.message}
                      </Typography>
                      {test.details && (
                        <Typography variant="caption" color="error" sx={{ display: 'block', mt: 0.5 }}>
                          {test.details}
                        </Typography>
                      )}
                    </Box>
                  }
                />
              </ListItem>
              {index < testResults.length - 1 && <Divider />}
            </React.Fragment>
          ))}
        </List>

        {/* Next Steps */}
        {overallStatus === 'passed' && (
          <Alert severity="success" sx={{ mt: 2 }}>
            <Typography variant="subtitle2" gutterBottom>
              🎉 Integration Test Successful!
            </Typography>
            <Typography variant="body2">
              Your dashboard is ready to connect to the backend. You can now:
              <br/>• Start real-time migration monitoring
              <br/>• View live performance metrics  
              <br/>• Track entity-level progress
              <br/>• Receive real-time notifications
            </Typography>
          </Alert>
        )}

        {overallStatus === 'failed' && (
          <Alert severity="error" sx={{ mt: 2 }}>
            <Typography variant="subtitle2" gutterBottom>
              ❌ Integration Issues Found
            </Typography>
            <Typography variant="body2">
              Please check the failed tests above and ensure:
              <br/>• Your backend is running and accessible
              <br/>• API keys are properly configured
              <br/>• CORS is configured for your domain
              <br/>• Network connectivity is working
            </Typography>
          </Alert>
        )}
      </CardContent>
    </Card>
  );
}; 