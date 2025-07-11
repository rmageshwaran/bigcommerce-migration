import React, { useState, useMemo } from 'react';
import {
  Box,
  Card,
  CardContent,
  Typography,
  LinearProgress,
  Chip,
  Table,
  TableBody,
  TableCell,
  TableContainer,
  TableHead,
  TableRow,
  IconButton,
  Alert,
  useTheme,
  alpha,
} from '@mui/material';
import {
  Queue as QueueIcon,
  Error as ErrorIcon,
  Warning as WarningIcon,
  CheckCircle as CheckCircleIcon,
  Speed as SpeedIcon,
  Refresh as RefreshIcon,
  TrendingUp as TrendingUpIcon,
  TrendingDown as TrendingDownIcon,
} from '@mui/icons-material';
import { Line, Bar } from 'react-chartjs-2';
import type { ChartOptions } from 'chart.js';
import { useDashboard } from '../../context/DashboardContext';

interface QueueVisualizationProps {
  height?: number;
  showDetails?: boolean;
}

interface QueueStatus {
  name: string;
  activeMessages: number;
  deadLetterMessages: number;
  processingRate: number;
  avgProcessingTime: number;
  status: 'healthy' | 'warning' | 'error';
  throughput: number[];
  timestamps: string[];
}

const QueueStatusCard: React.FC<{ queue: QueueStatus }> = ({ queue }) => {
  const theme = useTheme();
  
  const getStatusColor = () => {
    switch (queue.status) {
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

  const getStatusIcon = () => {
    switch (queue.status) {
      case 'healthy':
        return <CheckCircleIcon sx={{ color: 'success.main' }} />;
      case 'warning':
        return <WarningIcon sx={{ color: 'warning.main' }} />;
      case 'error':
        return <ErrorIcon sx={{ color: 'error.main' }} />;
      default:
        return <QueueIcon />;
    }
  };

  const getTrend = () => {
    if (queue.throughput.length < 2) return null;
    const current = queue.throughput[queue.throughput.length - 1];
    const previous = queue.throughput[queue.throughput.length - 2];
    return current > previous ? 'up' : current < previous ? 'down' : 'stable';
  };

  return (
    <Card sx={{ mb: 2 }}>
      <CardContent>
        <Box sx={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', mb: 2 }}>
          <Box sx={{ display: 'flex', alignItems: 'center' }}>
            {getStatusIcon()}
            <Typography variant="h6" sx={{ ml: 1 }}>
              {queue.name}
            </Typography>
          </Box>
          <Chip
            size="small"
            label={queue.status}
            sx={{
              bgcolor: alpha(getStatusColor(), 0.1),
              color: getStatusColor(),
              border: `1px solid ${alpha(getStatusColor(), 0.3)}`,
            }}
          />
        </Box>

        <Box sx={{ display: 'flex', gap: 3, mb: 2 }}>
          <Box>
            <Typography variant="body2" color="text.secondary">
              Active Messages
            </Typography>
            <Typography variant="h6">
              {queue.activeMessages.toLocaleString()}
            </Typography>
          </Box>
          <Box>
            <Typography variant="body2" color="text.secondary">
              Dead Letter
            </Typography>
            <Typography variant="h6" color={queue.deadLetterMessages > 0 ? 'error.main' : 'inherit'}>
              {queue.deadLetterMessages.toLocaleString()}
            </Typography>
          </Box>
          <Box>
            <Typography variant="body2" color="text.secondary">
              Processing Rate
            </Typography>
            <Box sx={{ display: 'flex', alignItems: 'center' }}>
              <Typography variant="h6">
                {queue.processingRate.toFixed(1)}/s
              </Typography>
              {getTrend() && (
                <Box sx={{ ml: 1, color: getTrend() === 'up' ? 'success.main' : 'error.main' }}>
                  {getTrend() === 'up' ? <TrendingUpIcon fontSize="small" /> : <TrendingDownIcon fontSize="small" />}
                </Box>
              )}
            </Box>
          </Box>
        </Box>

        <Box sx={{ mb: 2 }}>
          <Typography variant="body2" color="text.secondary" gutterBottom>
            Queue Health
          </Typography>
          <LinearProgress
            variant="determinate"
            value={queue.status === 'healthy' ? 100 : queue.status === 'warning' ? 70 : 30}
            color={queue.status === 'healthy' ? 'success' : queue.status === 'warning' ? 'warning' : 'error'}
            sx={{ height: 8, borderRadius: 4 }}
          />
        </Box>

        {queue.deadLetterMessages > 0 && (
          <Alert severity="warning" sx={{ mt: 2 }}>
            {queue.deadLetterMessages} messages in dead letter queue require attention
          </Alert>
        )}
      </CardContent>
    </Card>
  );
};

export const QueueVisualization: React.FC<QueueVisualizationProps> = ({
  height = 500,
  showDetails = true,
}) => {
  const theme = useTheme();
  const { state, refreshData } = useDashboard();
  const [selectedQueue, setSelectedQueue] = useState<string | null>(null);

  // Mock queue data (in real app, this would come from API)
  const queues: QueueStatus[] = [
    {
      name: 'Migration Queue',
      activeMessages: 1250,
      deadLetterMessages: 3,
      processingRate: 45.2,
      avgProcessingTime: 2.8,
      status: 'healthy',
      throughput: [40, 42, 45, 48, 45, 47, 45],
      timestamps: ['10:00', '10:05', '10:10', '10:15', '10:20', '10:25', '10:30'],
    },
    {
      name: 'Entity Processing',
      activeMessages: 890,
      deadLetterMessages: 12,
      processingRate: 32.1,
      avgProcessingTime: 3.2,
      status: 'warning',
      throughput: [35, 38, 32, 30, 32, 34, 32],
      timestamps: ['10:00', '10:05', '10:10', '10:15', '10:20', '10:25', '10:30'],
    },
    {
      name: 'Error Recovery',
      activeMessages: 45,
      deadLetterMessages: 0,
      processingRate: 8.5,
      avgProcessingTime: 1.5,
      status: 'healthy',
      throughput: [8, 9, 8, 7, 8, 9, 8],
      timestamps: ['10:00', '10:05', '10:10', '10:15', '10:20', '10:25', '10:30'],
    },
    {
      name: 'Notification Queue',
      activeMessages: 234,
      deadLetterMessages: 0,
      processingRate: 125.7,
      avgProcessingTime: 0.3,
      status: 'healthy',
      throughput: [120, 125, 130, 128, 125, 127, 125],
      timestamps: ['10:00', '10:05', '10:10', '10:15', '10:20', '10:25', '10:30'],
    },
  ];

  const totalActiveMessages = queues.reduce((sum, q) => sum + q.activeMessages, 0);
  const totalDeadLetterMessages = queues.reduce((sum, q) => sum + q.deadLetterMessages, 0);
  const totalProcessingRate = queues.reduce((sum, q) => sum + q.processingRate, 0);
  const healthyQueues = queues.filter(q => q.status === 'healthy').length;

  // Queue throughput comparison chart
  const throughputChartData = {
    labels: queues[0].timestamps,
    datasets: queues.map((queue, index) => ({
      label: queue.name,
      data: queue.throughput,
      borderColor: [
        theme.palette.primary.main,
        theme.palette.secondary.main,
        theme.palette.success.main,
        theme.palette.warning.main,
      ][index % 4],
      backgroundColor: alpha([
        theme.palette.primary.main,
        theme.palette.secondary.main,
        theme.palette.success.main,
        theme.palette.warning.main,
      ][index % 4], 0.1),
      borderWidth: 2,
      tension: 0.4,
    })),
  };

  // Queue size comparison chart
  const queueSizeData = {
    labels: queues.map(q => q.name),
    datasets: [
      {
        label: 'Active Messages',
        data: queues.map(q => q.activeMessages),
        backgroundColor: theme.palette.primary.main,
        borderColor: theme.palette.primary.main,
        borderWidth: 1,
      },
      {
        label: 'Dead Letter Messages',
        data: queues.map(q => q.deadLetterMessages),
        backgroundColor: theme.palette.error.main,
        borderColor: theme.palette.error.main,
        borderWidth: 1,
      },
    ],
  };

  const lineChartOptions: ChartOptions<'line'> = {
    responsive: true,
    maintainAspectRatio: false,
    plugins: {
      legend: {
        position: 'top' as const,
      },
      title: {
        display: true,
        text: 'Queue Processing Rates Over Time',
      },
    },
    scales: {
      y: {
        beginAtZero: true,
        ticks: {
          callback: (value) => `${value}/s`,
        },
      },
    },
  };

  const barChartOptions: ChartOptions<'bar'> = {
    responsive: true,
    maintainAspectRatio: false,
    plugins: {
      legend: {
        position: 'top' as const,
      },
      title: {
        display: true,
        text: 'Queue Message Counts',
      },
    },
    scales: {
      y: {
        beginAtZero: true,
        ticks: {
          callback: (value) => value.toLocaleString(),
        },
      },
    },
  };

  return (
    <Card>
      <CardContent>
        <Box sx={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', mb: 3 }}>
          <Typography variant="h6" component="div">
            Queue Monitoring
          </Typography>
          
          <Box sx={{ display: 'flex', alignItems: 'center', gap: 2 }}>
            <Typography variant="body2" color="text.secondary">
              {healthyQueues}/{queues.length} Healthy
            </Typography>
            <IconButton size="small" onClick={refreshData}>
              <RefreshIcon />
            </IconButton>
          </Box>
        </Box>

        {/* Queue Summary */}
        <Box sx={{ display: 'flex', gap: 4, mb: 3 }}>
          <Box>
            <Typography variant="body2" color="text.secondary">
              Total Active Messages
            </Typography>
            <Typography variant="h5">
              {totalActiveMessages.toLocaleString()}
            </Typography>
          </Box>
          <Box>
            <Typography variant="body2" color="text.secondary">
              Dead Letter Messages
            </Typography>
            <Typography variant="h5" color={totalDeadLetterMessages > 0 ? 'error.main' : 'inherit'}>
              {totalDeadLetterMessages.toLocaleString()}
            </Typography>
          </Box>
          <Box>
            <Typography variant="body2" color="text.secondary">
              Total Processing Rate
            </Typography>
            <Typography variant="h5">
              {totalProcessingRate.toFixed(1)}/s
            </Typography>
          </Box>
        </Box>

        {/* Alerts */}
        {totalDeadLetterMessages > 0 && (
          <Alert severity="error" sx={{ mb: 3 }}>
            {totalDeadLetterMessages} messages in dead letter queues require immediate attention
          </Alert>
        )}

        {/* Charts */}
        <Box sx={{ display: 'flex', gap: 2, mb: 3 }}>
          <Box sx={{ flex: 1 }}>
            <Card>
              <CardContent>
                <Box sx={{ height: 200 }}>
                  <Line data={throughputChartData} options={lineChartOptions} />
                </Box>
              </CardContent>
            </Card>
          </Box>
          
          <Box sx={{ flex: 1 }}>
            <Card>
              <CardContent>
                <Box sx={{ height: 200 }}>
                  <Bar data={queueSizeData} options={barChartOptions} />
                </Box>
              </CardContent>
            </Card>
          </Box>
        </Box>

        {/* Queue Details */}
        {showDetails && (
          <Box>
            <Typography variant="h6" gutterBottom>
              Queue Details
            </Typography>
            
            <Box sx={{ display: 'flex', flexDirection: 'column', gap: 1 }}>
              {queues.map((queue, index) => (
                <QueueStatusCard key={index} queue={queue} />
              ))}
            </Box>
          </Box>
        )}
      </CardContent>
    </Card>
  );
}; 