import React, { useState } from 'react';
import {
  Box,
  Card,
  CardContent,
  Typography,
  LinearProgress,
  Chip,
  Avatar,
  List,
  ListItem,
  ListItemAvatar,
  ListItemText,
  Alert,
  IconButton,
  Collapse,
  useTheme,
  alpha,
} from '@mui/material';
import {
  CheckCircle as CheckCircleIcon,
  Error as ErrorIcon,
  Warning as WarningIcon,
  Storage as StorageIcon,
  Speed as SpeedIcon,
  Memory as MemoryIcon,
  Cloud as CloudIcon,
  Router as RouterIcon,
  Security as SecurityIcon,
  ExpandMore as ExpandMoreIcon,
  ExpandLess as ExpandLessIcon,
  Refresh as RefreshIcon,
} from '@mui/icons-material';
import { useDashboard } from '../../context/DashboardContext';
import type { SystemStatus, ServiceHealth } from '../../types';

interface SystemHealthPanelProps {
  showDetails?: boolean;
  autoRefresh?: boolean;
}

interface ServiceStatusItem {
  name: string;
  status: SystemStatus;
  responseTime?: string;
  uptime?: string;
  error?: string;
  icon: React.ReactNode;
  critical: boolean;
}

const StatusChip: React.FC<{ status: SystemStatus }> = ({ status }) => {
  const getStatusProps = () => {
    switch (status) {
      case 'healthy':
        return { color: 'success' as const, label: 'Healthy' };
      case 'warning':
        return { color: 'warning' as const, label: 'Warning' };
      case 'error':
        return { color: 'error' as const, label: 'Error' };
      default:
        return { color: 'default' as const, label: 'Unknown' };
    }
  };

  const { color, label } = getStatusProps();
  return <Chip size="small" color={color} label={label} />;
};

const ServiceStatusCard: React.FC<{ service: ServiceStatusItem }> = ({ service }) => {
  const theme = useTheme();
  const [expanded, setExpanded] = useState(false);

  const getStatusColor = () => {
    switch (service.status) {
      case 'healthy':
        return theme.palette.success.main;
      case 'warning':
        return theme.palette.warning.main;
      case 'error':
        return theme.palette.error.main;
      default:
        return theme.palette.text.secondary;
    }
  };

  return (
    <Card sx={{ mb: 1 }}>
      <CardContent sx={{ py: 1.5 }}>
        <Box sx={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between' }}>
          <Box sx={{ display: 'flex', alignItems: 'center' }}>
            <Avatar 
              sx={{ 
                width: 32, 
                height: 32, 
                mr: 2,
                bgcolor: alpha(getStatusColor(), 0.1),
                color: getStatusColor()
              }}
            >
              {service.icon}
            </Avatar>
            <Box>
              <Typography variant="body1" sx={{ fontWeight: 'medium' }}>
                {service.name}
              </Typography>
              <Box sx={{ display: 'flex', alignItems: 'center', gap: 1 }}>
                <StatusChip status={service.status} />
                {service.responseTime && (
                  <Typography variant="caption" color="text.secondary">
                    {service.responseTime}
                  </Typography>
                )}
              </Box>
            </Box>
          </Box>
          
          <IconButton
            size="small"
            onClick={() => setExpanded(!expanded)}
          >
            {expanded ? <ExpandLessIcon /> : <ExpandMoreIcon />}
          </IconButton>
        </Box>

        <Collapse in={expanded}>
          <Box sx={{ mt: 2, pl: 6 }}>
            <Typography variant="body2" color="text.secondary">
              Uptime: {service.uptime || 'N/A'}
            </Typography>
            {service.error && (
              <Alert severity="error" sx={{ mt: 1 }}>
                {service.error}
              </Alert>
            )}
          </Box>
        </Collapse>
      </CardContent>
    </Card>
  );
};

export const SystemHealthPanel: React.FC<SystemHealthPanelProps> = ({
  showDetails = true,
  autoRefresh = true,
}) => {
  const { state, refreshData } = useDashboard();
  const { systemHealth, signalRConnection, apiConnected } = state;

  // Mock service data (in real app, this would come from systemHealth)
  const services: ServiceStatusItem[] = [
    {
      name: 'API Gateway',
      status: apiConnected ? 'healthy' : 'error',
      responseTime: '120ms',
      uptime: '99.9%',
      icon: <CloudIcon />,
      critical: true,
    },
    {
      name: 'BigCommerce API',
      status: 'healthy',
      responseTime: '95ms',
      uptime: '99.5%',
      icon: <RouterIcon />,
      critical: true,
    },
    {
      name: 'Database',
      status: 'healthy',
      responseTime: '25ms',
      uptime: '100%',
      icon: <StorageIcon />,
      critical: true,
    },
    {
      name: 'SignalR Hub',
      status: signalRConnection.isConnected ? 'healthy' : 'warning',
      responseTime: '50ms',
      uptime: '98.7%',
      icon: <SecurityIcon />,
      critical: false,
    },
    {
      name: 'Rate Limiter',
      status: 'warning',
      responseTime: '15ms',
      uptime: '99.2%',
      error: 'Approaching rate limit threshold',
      icon: <SpeedIcon />,
      critical: false,
    },
  ];

  const overallStatus: SystemStatus = services.some(s => s.status === 'error' && s.critical)
    ? 'error'
    : services.some(s => s.status === 'warning')
    ? 'warning'
    : 'healthy';

  const criticalServices = services.filter(s => s.critical);
  const healthyServices = services.filter(s => s.status === 'healthy').length;
  const warningServices = services.filter(s => s.status === 'warning').length;
  const errorServices = services.filter(s => s.status === 'error').length;

  // System metrics (mock data)
  const systemMetrics = {
    cpuUsage: 65,
    memoryUsage: 78,
    diskUsage: 45,
    networkLatency: 120,
    activeConnections: 45,
    requestsPerSecond: 125,
  };

  return (
    <Card>
      <CardContent>
        <Box sx={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', mb: 2 }}>
          <Typography variant="h6" component="div">
            System Health
          </Typography>
          
          <Box sx={{ display: 'flex', alignItems: 'center', gap: 1 }}>
            <StatusChip status={overallStatus} />
            <IconButton size="small" onClick={refreshData}>
              <RefreshIcon />
            </IconButton>
          </Box>
        </Box>

        {/* Health Summary */}
        <Box sx={{ mb: 3 }}>
          <Box sx={{ display: 'flex', gap: 2, mb: 2 }}>
            <Box sx={{ flex: 1 }}>
              <Typography variant="body2" color="text.secondary">
                Services Status
              </Typography>
              <Typography variant="h6">
                {healthyServices}/{services.length} Healthy
              </Typography>
            </Box>
            <Box sx={{ flex: 1 }}>
              <Typography variant="body2" color="text.secondary">
                Uptime
              </Typography>
              <Typography variant="h6">
                99.2%
              </Typography>
            </Box>
          </Box>

          {(warningServices > 0 || errorServices > 0) && (
            <Alert 
              severity={errorServices > 0 ? 'error' : 'warning'} 
              sx={{ mb: 2 }}
            >
              {errorServices > 0 && `${errorServices} service(s) down. `}
              {warningServices > 0 && `${warningServices} service(s) have warnings.`}
            </Alert>
          )}
        </Box>

        {/* System Metrics */}
        <Box sx={{ mb: 3 }}>
          <Typography variant="subtitle1" gutterBottom>
            System Metrics
          </Typography>
          
          <Box sx={{ display: 'flex', flexDirection: 'column', gap: 2 }}>
            <Box>
              <Box sx={{ display: 'flex', justifyContent: 'space-between', mb: 1 }}>
                <Typography variant="body2">CPU Usage</Typography>
                <Typography variant="body2">{systemMetrics.cpuUsage}%</Typography>
              </Box>
              <LinearProgress 
                variant="determinate" 
                value={systemMetrics.cpuUsage} 
                color={systemMetrics.cpuUsage > 80 ? 'error' : systemMetrics.cpuUsage > 60 ? 'warning' : 'success'}
              />
            </Box>

            <Box>
              <Box sx={{ display: 'flex', justifyContent: 'space-between', mb: 1 }}>
                <Typography variant="body2">Memory Usage</Typography>
                <Typography variant="body2">{systemMetrics.memoryUsage}%</Typography>
              </Box>
              <LinearProgress 
                variant="determinate" 
                value={systemMetrics.memoryUsage} 
                color={systemMetrics.memoryUsage > 80 ? 'error' : systemMetrics.memoryUsage > 60 ? 'warning' : 'success'}
              />
            </Box>

            <Box>
              <Box sx={{ display: 'flex', justifyContent: 'space-between', mb: 1 }}>
                <Typography variant="body2">Disk Usage</Typography>
                <Typography variant="body2">{systemMetrics.diskUsage}%</Typography>
              </Box>
              <LinearProgress 
                variant="determinate" 
                value={systemMetrics.diskUsage} 
                color={systemMetrics.diskUsage > 80 ? 'error' : systemMetrics.diskUsage > 60 ? 'warning' : 'success'}
              />
            </Box>
          </Box>
        </Box>

        {/* Performance Indicators */}
        <Box sx={{ mb: 3 }}>
          <Typography variant="subtitle1" gutterBottom>
            Performance
          </Typography>
          
          <Box sx={{ display: 'flex', flexWrap: 'wrap', gap: 2 }}>
            <Box sx={{ flex: '1 1 150px' }}>
              <Typography variant="body2" color="text.secondary">
                Avg Response Time
              </Typography>
              <Typography variant="h6">
                {systemMetrics.networkLatency}ms
              </Typography>
            </Box>
            <Box sx={{ flex: '1 1 150px' }}>
              <Typography variant="body2" color="text.secondary">
                Active Connections
              </Typography>
              <Typography variant="h6">
                {systemMetrics.activeConnections}
              </Typography>
            </Box>
            <Box sx={{ flex: '1 1 150px' }}>
              <Typography variant="body2" color="text.secondary">
                Requests/sec
              </Typography>
              <Typography variant="h6">
                {systemMetrics.requestsPerSecond}
              </Typography>
            </Box>
          </Box>
        </Box>

        {/* Service Details */}
        {showDetails && (
          <Box>
            <Typography variant="subtitle1" gutterBottom>
              Service Status
            </Typography>
            
            <Box sx={{ maxHeight: 300, overflow: 'auto' }}>
              {services.map((service, index) => (
                <ServiceStatusCard key={index} service={service} />
              ))}
            </Box>
          </Box>
        )}
      </CardContent>
    </Card>
  );
}; 