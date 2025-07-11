import React, { useState, useMemo } from 'react';
import {
  Box,
  Card,
  CardContent,
  Typography,
  Grid,
  Select,
  MenuItem,
  FormControl,
  InputLabel,
  Chip,
  LinearProgress,
  useTheme,
  alpha,
} from '@mui/material';
import {
  TrendingUp as TrendingUpIcon,
  TrendingDown as TrendingDownIcon,
  Speed as SpeedIcon,
  Error as ErrorIcon,
  CheckCircle as CheckCircleIcon,
  Timeline as TimelineIcon,
  Assessment as AssessmentIcon,
} from '@mui/icons-material';
import { Line, Bar, Doughnut } from 'react-chartjs-2';
import type { ChartOptions } from 'chart.js';
import { useDashboard } from '../../context/DashboardContext';

type MetricsPeriod = '1h' | '6h' | '24h' | '7d';

interface PerformanceMetricsProps {
  height?: number;
  period?: MetricsPeriod;
}

interface MetricCardData {
  title: string;
  value: string | number;
  change: number;
  trend: 'up' | 'down' | 'stable';
  unit?: string;
  color: 'primary' | 'success' | 'warning' | 'error';
  icon: React.ReactNode;
}

const MetricCard: React.FC<{ data: MetricCardData }> = ({ data }) => {
  const theme = useTheme();
  
  const getTrendIcon = () => {
    switch (data.trend) {
      case 'up':
        return <TrendingUpIcon fontSize="small" />;
      case 'down':
        return <TrendingDownIcon fontSize="small" />;
      default:
        return null;
    }
  };

  const getTrendColor = () => {
    switch (data.trend) {
      case 'up':
        return data.color === 'error' ? theme.palette.error.main : theme.palette.success.main;
      case 'down':
        return data.color === 'error' ? theme.palette.success.main : theme.palette.error.main;
      default:
        return theme.palette.text.secondary;
    }
  };

  return (
    <Card sx={{ height: '100%' }}>
      <CardContent>
        <Box sx={{ display: 'flex', alignItems: 'center', mb: 2 }}>
          <Box sx={{ color: `${data.color}.main`, mr: 1 }}>
            {data.icon}
          </Box>
          <Typography variant="h6" component="div" sx={{ fontSize: '0.9rem' }}>
            {data.title}
          </Typography>
        </Box>
        
        <Typography variant="h4" component="div" sx={{ mb: 1 }}>
          {data.value}
          {data.unit && (
            <Typography component="span" variant="body2" sx={{ ml: 0.5 }}>
              {data.unit}
            </Typography>
          )}
        </Typography>
        
        {data.trend !== 'stable' && (
          <Box sx={{ display: 'flex', alignItems: 'center' }}>
            <Box sx={{ color: getTrendColor(), mr: 0.5 }}>
              {getTrendIcon()}
            </Box>
            <Typography 
              variant="body2" 
              sx={{ color: getTrendColor() }}
            >
              {Math.abs(data.change).toFixed(1)}%
            </Typography>
          </Box>
        )}
      </CardContent>
    </Card>
  );
};

export const PerformanceMetrics: React.FC<PerformanceMetricsProps> = ({
  height = 600,
  period = '1h',
}) => {
  const theme = useTheme();
  const { state } = useDashboard();
  const { activeMigrations, systemHealth } = state;
  const [selectedPeriod, setSelectedPeriod] = useState<MetricsPeriod>(period);

  const migrationList = Array.from(activeMigrations.values());

  // Calculate performance metrics
  const performanceMetrics = useMemo(() => {
    const totalProcessed = migrationList.reduce((sum, m) => sum + m.processedEntities, 0);
    const totalSuccess = migrationList.reduce((sum, m) => sum + m.successfulEntities, 0);
    const totalFailed = migrationList.reduce((sum, m) => sum + m.failedEntities, 0);
    const totalSpeed = migrationList.reduce((sum, m) => sum + m.entitiesPerSecond, 0);
    const avgSpeed = totalSpeed / Math.max(migrationList.length, 1);
    const successRate = totalProcessed > 0 ? (totalSuccess / totalProcessed) * 100 : 0;
    const errorRate = totalProcessed > 0 ? (totalFailed / totalProcessed) * 100 : 0;
    const avgProgress = migrationList.reduce((sum, m) => sum + m.overallProgressPercentage, 0) / Math.max(migrationList.length, 1);

    return {
      totalProcessed,
      totalSuccess,
      totalFailed,
      totalSpeed,
      avgSpeed,
      successRate,
      errorRate,
      avgProgress,
      activeMigrations: migrationList.filter(m => m.status === 'running').length,
      completedMigrations: migrationList.filter(m => m.status === 'completed').length,
    };
  }, [migrationList]);

  // Generate metric cards data
  const metricCards: MetricCardData[] = [
    {
      title: 'Throughput',
      value: performanceMetrics.totalSpeed.toFixed(1),
      change: 12.5,
      trend: 'up',
      unit: '/sec',
      color: 'primary',
      icon: <SpeedIcon />
    },
    {
      title: 'Success Rate',
      value: performanceMetrics.successRate.toFixed(1),
      change: 2.3,
      trend: 'up',
      unit: '%',
      color: 'success',
      icon: <CheckCircleIcon />
    },
    {
      title: 'Error Rate',
      value: performanceMetrics.errorRate.toFixed(1),
      change: 1.8,
      trend: 'down',
      unit: '%',
      color: 'error',
      icon: <ErrorIcon />
    },
    {
      title: 'Avg Progress',
      value: performanceMetrics.avgProgress.toFixed(1),
      change: 15.2,
      trend: 'up',
      unit: '%',
      color: 'primary',
      icon: <AssessmentIcon />
    },
  ];

  // Throughput trend chart data
  const throughputChartData = {
    labels: ['1h ago', '45m ago', '30m ago', '15m ago', 'Now'],
    datasets: [
      {
        label: 'Entities/sec',
        data: [45, 52, 48, 58, performanceMetrics.totalSpeed],
        borderColor: theme.palette.primary.main,
        backgroundColor: alpha(theme.palette.primary.main, 0.1),
        borderWidth: 2,
        fill: true,
        tension: 0.4,
      },
    ],
  };

  // Entity type performance comparison
  const entityPerformanceData = {
    labels: ['Products', 'Categories', 'Brands', 'Variants', 'Images'],
    datasets: [
      {
        label: 'Processing Speed',
        data: [25, 45, 30, 20, 15],
        backgroundColor: [
          theme.palette.primary.main,
          theme.palette.secondary.main,
          theme.palette.success.main,
          theme.palette.warning.main,
          theme.palette.error.main,
        ],
        borderWidth: 2,
        borderColor: theme.palette.background.paper,
      },
    ],
  };

  // Error distribution chart
  const errorDistributionData = {
    labels: ['API Errors', 'Validation Errors', 'Network Errors', 'Rate Limits'],
    datasets: [
      {
        data: [12, 8, 5, 3],
        backgroundColor: [
          theme.palette.error.main,
          theme.palette.warning.main,
          theme.palette.info.main,
          theme.palette.secondary.main,
        ],
        borderWidth: 2,
        borderColor: theme.palette.background.paper,
      },
    ],
  };

  // Chart options
  const lineChartOptions: ChartOptions<'line'> = {
    responsive: true,
    maintainAspectRatio: false,
    plugins: {
      legend: {
        display: false,
      },
      title: {
        display: true,
        text: 'Processing Throughput Trend',
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
    elements: {
      point: {
        radius: 4,
        hoverRadius: 6,
      },
    },
  };

  const barChartOptions: ChartOptions<'bar'> = {
    responsive: true,
    maintainAspectRatio: false,
    plugins: {
      legend: {
        display: false,
      },
      title: {
        display: true,
        text: 'Entity Type Performance',
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

  const doughnutChartOptions: ChartOptions<'doughnut'> = {
    responsive: true,
    maintainAspectRatio: false,
    plugins: {
      legend: {
        position: 'bottom',
      },
      title: {
        display: true,
        text: 'Error Distribution',
      },
    },
  };

  return (
    <Card>
      <CardContent>
        <Box sx={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', mb: 3 }}>
          <Typography variant="h6" component="div">
            Performance Analytics
          </Typography>
          
          <FormControl size="small" sx={{ minWidth: 120 }}>
            <InputLabel>Period</InputLabel>
            <Select
              value={selectedPeriod}
              label="Period"
              onChange={(e) => setSelectedPeriod(e.target.value as MetricsPeriod)}
            >
              <MenuItem value="1h">Last Hour</MenuItem>
              <MenuItem value="6h">Last 6 Hours</MenuItem>
              <MenuItem value="24h">Last 24 Hours</MenuItem>
              <MenuItem value="7d">Last 7 Days</MenuItem>
            </Select>
          </FormControl>
        </Box>

        {/* Metric Cards */}
        <Grid container spacing={2} sx={{ mb: 3 }}>
          {metricCards.map((metric, index) => (
            <Grid item xs={12} sm={6} md={3} key={index}>
              <MetricCard data={metric} />
            </Grid>
          ))}
        </Grid>

        {/* Performance Charts */}
        <Grid container spacing={3}>
          {/* Throughput Trend */}
          <Grid item xs={12} md={6}>
            <Card>
              <CardContent>
                <Box sx={{ height: 200 }}>
                  <Line data={throughputChartData} options={lineChartOptions} />
                </Box>
              </CardContent>
            </Card>
          </Grid>

          {/* Entity Performance */}
          <Grid item xs={12} md={6}>
            <Card>
              <CardContent>
                <Box sx={{ height: 200 }}>
                  <Bar data={entityPerformanceData} options={barChartOptions} />
                </Box>
              </CardContent>
            </Card>
          </Grid>

          {/* Error Distribution */}
          <Grid item xs={12} md={6}>
            <Card>
              <CardContent>
                <Box sx={{ height: 200 }}>
                  <Doughnut data={errorDistributionData} options={doughnutChartOptions} />
                </Box>
              </CardContent>
            </Card>
          </Grid>

          {/* Performance Summary */}
          <Grid item xs={12} md={6}>
            <Card>
              <CardContent>
                <Typography variant="h6" gutterBottom>
                  Performance Summary
                </Typography>
                
                <Box sx={{ mb: 2 }}>
                  <Box sx={{ display: 'flex', justifyContent: 'space-between', mb: 1 }}>
                    <Typography variant="body2">System Efficiency</Typography>
                    <Typography variant="body2">85%</Typography>
                  </Box>
                  <LinearProgress variant="determinate" value={85} color="success" />
                </Box>

                <Box sx={{ mb: 2 }}>
                  <Box sx={{ display: 'flex', justifyContent: 'space-between', mb: 1 }}>
                    <Typography variant="body2">Resource Utilization</Typography>
                    <Typography variant="body2">72%</Typography>
                  </Box>
                  <LinearProgress variant="determinate" value={72} color="primary" />
                </Box>

                <Box sx={{ mb: 2 }}>
                  <Box sx={{ display: 'flex', justifyContent: 'space-between', mb: 1 }}>
                    <Typography variant="body2">Error Recovery Rate</Typography>
                    <Typography variant="body2">94%</Typography>
                  </Box>
                  <LinearProgress variant="determinate" value={94} color="info" />
                </Box>

                <Box sx={{ mt: 3 }}>
                  <Typography variant="body2" color="text.secondary">
                    <strong>Recommendations:</strong>
                  </Typography>
                  <Box sx={{ mt: 1 }}>
                    <Chip 
                      label="Optimize batch size" 
                      size="small" 
                      color="primary" 
                      sx={{ mr: 1, mb: 1 }}
                    />
                    <Chip 
                      label="Increase concurrency" 
                      size="small" 
                      color="secondary" 
                      sx={{ mr: 1, mb: 1 }}
                    />
                    <Chip 
                      label="Monitor rate limits" 
                      size="small" 
                      color="warning" 
                      sx={{ mr: 1, mb: 1 }}
                    />
                  </Box>
                </Box>
              </CardContent>
            </Card>
          </Grid>
        </Grid>
      </CardContent>
    </Card>
  );
}; 