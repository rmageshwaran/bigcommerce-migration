import React, { useEffect, useState } from 'react';
import {
  Box,
  Card,
  CardContent,
  Typography,
  ToggleButton,
  ToggleButtonGroup,
  useTheme,
} from '@mui/material';
import {
  Timeline as TimelineIcon,
  PieChart as PieChartIcon,
  BarChart as BarChartIcon,
} from '@mui/icons-material';
import { Line } from 'react-chartjs-2';
import type { ChartOptions } from 'chart.js';
import {
  Chart as ChartJS,
  CategoryScale,
  LinearScale,
  PointElement,
  LineElement,
  Title,
  Tooltip,
  Legend,
  TimeScale,
} from 'chart.js';
import 'chartjs-adapter-date-fns';
import { useDashboard } from '../../context/DashboardContext';

// Register Chart.js components
ChartJS.register(
  CategoryScale,
  LinearScale,
  PointElement,
  LineElement,
  Title,
  Tooltip,
  Legend,
  TimeScale,
);

type ChartType = 'progress' | 'entities' | 'speed';

interface ProgressChartProps {
  height?: number;
  showControls?: boolean;
}

export const ProgressChart: React.FC<ProgressChartProps> = ({ 
  height = 400, 
  showControls = true 
}) => {
  const theme = useTheme();
  const { state } = useDashboard();
  const { activeMigrations } = state;
  const [chartType, setChartType] = useState<ChartType>('progress');
  const [progressHistory, setProgressHistory] = useState<{
    [migrationId: string]: {
      timestamps: string[];
      progressValues: number[];
      speedValues: number[];
    }
  }>({});

  const migrationList = Array.from(activeMigrations.values());

  // Update progress history when migrations change
  useEffect(() => {
    const now = new Date().toLocaleTimeString();
    
    setProgressHistory(prev => {
      const updated = { ...prev };
      
      migrationList.forEach(migration => {
        if (!updated[migration.migrationId]) {
          updated[migration.migrationId] = {
            timestamps: [],
            progressValues: [],
            speedValues: []
          };
        }
        
        const history = updated[migration.migrationId];
        
        // Add new data point
        history.timestamps.push(now);
        history.progressValues.push(migration.overallProgressPercentage);
        history.speedValues.push(migration.entitiesPerSecond);
        
        // Keep only last 20 data points
        if (history.timestamps.length > 20) {
          history.timestamps.shift();
          history.progressValues.shift();
          history.speedValues.shift();
        }
      });
      
      return updated;
    });
  }, [migrationList]);

  // Progress over time chart
  const getProgressChartData = () => {
    const datasets = migrationList.map((migration, index) => {
      const colors = [
        theme.palette.primary.main,
        theme.palette.secondary.main,
        theme.palette.success.main,
        theme.palette.warning.main,
        theme.palette.error.main,
      ];
      
      const color = colors[index % colors.length];
      const history = progressHistory[migration.migrationId] || { timestamps: [], progressValues: [] };
      
      return {
        label: `Migration ${migration.migrationId.slice(-8)}`,
        data: history.progressValues,
        borderColor: color,
        backgroundColor: `${color}20`,
        borderWidth: 2,
        fill: true,
        tension: 0.4,
      };
    });

    const labels = migrationList.length > 0 
      ? progressHistory[migrationList[0].migrationId]?.timestamps || []
      : [];

    return { labels, datasets };
  };

  // Entity breakdown pie chart
  const getEntityChartData = () => {
    const entityTotals = migrationList.reduce((acc, migration) => {
      Object.entries(migration.entityProgress).forEach(([entityType, progress]) => {
        if (!acc[entityType]) {
          acc[entityType] = { total: 0, processed: 0 };
        }
        acc[entityType].total += progress.totalCount;
        acc[entityType].processed += progress.processedCount;
      });
      return acc;
    }, {} as Record<string, { total: number; processed: number }>);

    const labels = Object.keys(entityTotals);
    const data = labels.map(label => entityTotals[label].processed);
    
    const colors = [
      theme.palette.primary.main,
      theme.palette.secondary.main,
      theme.palette.success.main,
      theme.palette.warning.main,
      theme.palette.error.main,
      theme.palette.info.main,
    ];

    return {
      labels: labels.map(label => label.charAt(0).toUpperCase() + label.slice(1)),
      datasets: [
        {
          data,
          backgroundColor: colors.slice(0, labels.length),
          borderColor: colors.slice(0, labels.length),
          borderWidth: 2,
        },
      ],
    };
  };

  // Processing speed chart
  const getSpeedChartData = () => {
    const datasets = migrationList.map((migration, index) => {
      const colors = [
        theme.palette.primary.main,
        theme.palette.secondary.main,
        theme.palette.success.main,
        theme.palette.warning.main,
        theme.palette.error.main,
      ];
      
      const color = colors[index % colors.length];
      const history = progressHistory[migration.migrationId] || { timestamps: [], speedValues: [] };
      
      return {
        label: `Migration ${migration.migrationId.slice(-8)}`,
        data: history.speedValues,
        backgroundColor: color,
        borderColor: color,
        borderWidth: 1,
      };
    });

    const labels = migrationList.length > 0 
      ? progressHistory[migrationList[0].migrationId]?.timestamps || []
      : [];

    return { labels, datasets };
  };

  // Chart options
  const lineChartOptions: ChartOptions<'line'> = {
    responsive: true,
    maintainAspectRatio: false,
    plugins: {
      legend: {
        position: 'top' as const,
      },
      title: {
        display: true,
        text: 'Migration Progress Over Time (%)',
      },
      tooltip: {
        callbacks: {
          label: (context) => {
            return `${context.dataset.label}: ${context.parsed.y.toFixed(1)}%`;
          },
        },
      },
    },
    scales: {
      y: {
        beginAtZero: true,
        max: 100,
        ticks: {
          callback: (value) => `${value}%`,
        },
      },
    },
    animation: {
      duration: 750,
      easing: 'easeInOutQuart',
    },
  };



  const handleChartTypeChange = (
    _: React.MouseEvent<HTMLElement>,
    newType: ChartType | null,
  ) => {
    if (newType !== null) {
      setChartType(newType);
    }
  };

  const renderChart = () => {
    if (migrationList.length === 0) {
      return (
        <Box 
          sx={{ 
            height: height, 
            display: 'flex', 
            alignItems: 'center', 
            justifyContent: 'center',
            color: 'text.secondary'
          }}
        >
          <Typography variant="h6">No active migrations to display</Typography>
        </Box>
      );
    }

    switch (chartType) {
      case 'progress':
        return <Line data={getProgressChartData()} options={lineChartOptions} />;
      case 'entities':
        return <Line data={getEntityChartData()} options={lineChartOptions} />;
      case 'speed':
        return <Line data={getSpeedChartData()} options={lineChartOptions} />;
      default:
        return null;
    }
  };

  return (
    <Card>
      <CardContent>
        <Box sx={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', mb: 2 }}>
          <Typography variant="h6" component="div">
            Migration Analytics
          </Typography>
          
          {showControls && (
            <ToggleButtonGroup
              value={chartType}
              exclusive
              onChange={handleChartTypeChange}
              size="small"
            >
              <ToggleButton value="progress">
                <TimelineIcon sx={{ mr: 1 }} />
                Progress
              </ToggleButton>
              <ToggleButton value="entities">
                <PieChartIcon sx={{ mr: 1 }} />
                Entities
              </ToggleButton>
              <ToggleButton value="speed">
                <BarChartIcon sx={{ mr: 1 }} />
                Speed
              </ToggleButton>
            </ToggleButtonGroup>
          )}
        </Box>
        
        <Box sx={{ height: height }}>
          {renderChart()}
        </Box>
        
        {migrationList.length > 0 && (
          <Box sx={{ mt: 2, display: 'flex', flexWrap: 'wrap', gap: 2 }}>
            <Box sx={{ flex: '1 1 200px' }}>
              <Typography variant="body2" color="text.secondary">
                Active Migrations: {migrationList.filter(m => m.status === 'running').length}
              </Typography>
            </Box>
            <Box sx={{ flex: '1 1 200px' }}>
              <Typography variant="body2" color="text.secondary">
                Average Progress: {(migrationList.reduce((sum, m) => sum + m.overallProgressPercentage, 0) / migrationList.length).toFixed(1)}%
              </Typography>
            </Box>
            <Box sx={{ flex: '1 1 200px' }}>
              <Typography variant="body2" color="text.secondary">
                Total Speed: {migrationList.reduce((sum, m) => sum + m.entitiesPerSecond, 0).toFixed(1)} entities/sec
              </Typography>
            </Box>
          </Box>
        )}
      </CardContent>
    </Card>
  );
}; 