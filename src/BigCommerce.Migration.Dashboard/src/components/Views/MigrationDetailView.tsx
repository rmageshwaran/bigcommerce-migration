import React, { useState } from 'react';
import {
  Box,
  Card,
  CardContent,
  Typography,
  Tabs,
  Tab,
  LinearProgress,
  Table,
  TableBody,
  TableCell,
  TableContainer,
  TableHead,
  TableRow,
  Chip,
  Alert,
  Button,
  IconButton,
  Dialog,
  DialogTitle,
  DialogContent,
  DialogActions,
  Timeline,
  TimelineItem,
  TimelineSeparator,
  TimelineConnector,
  TimelineContent,
  TimelineDot,
  TimelineOppositeContent,
  Accordion,
  AccordionSummary,
  AccordionDetails,
  List,
  ListItem,
  ListItemText,
  ListItemIcon,
  Divider,
  useTheme,
  alpha,
} from '@mui/material';
import {
  ExpandMore as ExpandMoreIcon,
  Error as ErrorIcon,
  Warning as WarningIcon,
  CheckCircle as CheckCircleIcon,
  Info as InfoIcon,
  PlayArrow as PlayArrowIcon,
  Pause as PauseIcon,
  Stop as StopIcon,
  Refresh as RefreshIcon,
  Download as DownloadIcon,
  Visibility as VisibilityIcon,
  Close as CloseIcon,
} from '@mui/icons-material';
import { useDashboard } from '../../context/DashboardContext';
import type { MigrationProgress, EntityProgress } from '../../types';

interface MigrationDetailViewProps {
  migrationId: string;
  onClose?: () => void;
  onAction?: (action: string, migrationId: string) => void;
}

interface TabPanelProps {
  children?: React.ReactNode;
  index: number;
  value: number;
}

const TabPanel: React.FC<TabPanelProps> = ({ children, value, index, ...other }) => (
  <div
    role="tabpanel"
    hidden={value !== index}
    id={`migration-tabpanel-${index}`}
    aria-labelledby={`migration-tab-${index}`}
    {...other}
  >
    {value === index && <Box sx={{ p: 3 }}>{children}</Box>}
  </div>
);

const EntityDetailCard: React.FC<{ 
  entityType: string; 
  progress: EntityProgress; 
  onViewDetails: (entityType: string) => void;
}> = ({ entityType, progress, onViewDetails }) => {
  const theme = useTheme();
  
  const getProgressColor = () => {
    if (progress.progressPercentage >= 100) return 'success';
    if (progress.progressPercentage >= 75) return 'info';
    if (progress.progressPercentage >= 50) return 'warning';
    return 'error';
  };

  const getStatusIcon = () => {
    if (progress.progressPercentage >= 100) return <CheckCircleIcon color="success" />;
    if (progress.failureCount > 0) return <ErrorIcon color="error" />;
    if (progress.progressPercentage > 0) return <PlayArrowIcon color="primary" />;
    return <InfoIcon color="info" />;
  };

  return (
    <Card sx={{ mb: 2 }}>
      <CardContent>
        <Box sx={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', mb: 2 }}>
          <Box sx={{ display: 'flex', alignItems: 'center' }}>
            {getStatusIcon()}
            <Typography variant="h6" sx={{ ml: 1 }}>
              {entityType.charAt(0).toUpperCase() + entityType.slice(1)}
            </Typography>
          </Box>
          
          <Button
            size="small"
            startIcon={<VisibilityIcon />}
            onClick={() => onViewDetails(entityType)}
          >
            View Details
          </Button>
        </Box>

        <Box sx={{ mb: 2 }}>
          <Box sx={{ display: 'flex', justifyContent: 'space-between', mb: 1 }}>
            <Typography variant="body2">Progress</Typography>
            <Typography variant="body2">{progress.progressPercentage.toFixed(1)}%</Typography>
          </Box>
          <LinearProgress
            variant="determinate"
            value={progress.progressPercentage}
            color={getProgressColor()}
            sx={{ height: 8, borderRadius: 4 }}
          />
        </Box>

        <Box sx={{ display: 'flex', justifyContent: 'space-between', flexWrap: 'wrap', gap: 2 }}>
          <Box>
            <Typography variant="caption" color="text.secondary">Total</Typography>
            <Typography variant="body2">{progress.totalCount.toLocaleString()}</Typography>
          </Box>
          <Box>
            <Typography variant="caption" color="text.secondary">Processed</Typography>
            <Typography variant="body2">{progress.processedCount.toLocaleString()}</Typography>
          </Box>
          <Box>
            <Typography variant="caption" color="text.secondary">Success</Typography>
            <Typography variant="body2" color="success.main">{progress.successCount.toLocaleString()}</Typography>
          </Box>
          <Box>
            <Typography variant="caption" color="text.secondary">Errors</Typography>
            <Typography variant="body2" color={progress.failureCount > 0 ? "error.main" : "inherit"}>
              {progress.failureCount.toLocaleString()}
            </Typography>
          </Box>
          <Box>
            <Typography variant="caption" color="text.secondary">Processing Time</Typography>
            <Typography variant="body2">{progress.processingTime.toFixed(1)}s</Typography>
          </Box>
        </Box>
      </CardContent>
    </Card>
  );
};

const MigrationTimelineView: React.FC<{ migration: MigrationProgress }> = ({ migration }) => {
  const theme = useTheme();
  
  // Mock timeline events (in real app, this would come from API)
  const timelineEvents = [
    {
      time: '10:00:00',
      title: 'Migration Started',
      description: 'Migration initialization completed',
      type: 'success',
      icon: <PlayArrowIcon />,
    },
    {
      time: '10:05:30',
      title: 'Categories Processing',
      description: '1,250 categories processed successfully',
      type: 'success',
      icon: <CheckCircleIcon />,
    },
    {
      time: '10:12:15',
      title: 'Products Processing Started',
      description: 'Beginning product migration (15,000 items)',
      type: 'info',
      icon: <InfoIcon />,
    },
    {
      time: '10:18:45',
      title: 'Rate Limit Warning',
      description: 'API rate limit approaching, throttling requests',
      type: 'warning',
      icon: <WarningIcon />,
    },
    {
      time: '10:25:00',
      title: 'Processing Error',
      description: '3 product imports failed validation',
      type: 'error',
      icon: <ErrorIcon />,
    },
  ];

  const getTimelineDotColor = (type: string) => {
    switch (type) {
      case 'success': return 'success';
      case 'warning': return 'warning';
      case 'error': return 'error';
      default: return 'primary';
    }
  };

  return (
    <Timeline>
      {timelineEvents.map((event, index) => (
        <TimelineItem key={index}>
          <TimelineOppositeContent sx={{ m: 'auto 0' }}>
            <Typography variant="body2" color="text.secondary">
              {event.time}
            </Typography>
          </TimelineOppositeContent>
          <TimelineSeparator>
            <TimelineDot color={getTimelineDotColor(event.type)}>
              {event.icon}
            </TimelineDot>
            {index < timelineEvents.length - 1 && <TimelineConnector />}
          </TimelineSeparator>
          <TimelineContent sx={{ py: '12px', px: 2 }}>
            <Typography variant="h6" component="span">
              {event.title}
            </Typography>
            <Typography color="text.secondary">{event.description}</Typography>
          </TimelineContent>
        </TimelineItem>
      ))}
    </Timeline>
  );
};

const ErrorLogView: React.FC<{ migration: MigrationProgress }> = ({ migration }) => {
  // Mock error data (in real app, this would come from API)
  const errors = [
    {
      id: 1,
      timestamp: '2024-01-15 10:25:00',
      entityType: 'products',
      entityId: 'prod_12345',
      severity: 'error',
      message: 'Product validation failed: Missing required field "description"',
      details: 'Product ID prod_12345 failed validation during import process',
      resolved: false,
    },
    {
      id: 2,
      timestamp: '2024-01-15 10:23:15',
      entityType: 'products',
      entityId: 'prod_12344',
      severity: 'warning',
      message: 'Image URL not accessible',
      details: 'Product image URL returned 404, using placeholder image',
      resolved: true,
    },
    {
      id: 3,
      timestamp: '2024-01-15 10:20:30',
      entityType: 'categories',
      entityId: 'cat_567',
      severity: 'error',
      message: 'Duplicate category name detected',
      details: 'Category "Electronics" already exists in destination store',
      resolved: false,
    },
  ];

  const getSeverityColor = (severity: string) => {
    switch (severity) {
      case 'error': return 'error';
      case 'warning': return 'warning';
      default: return 'info';
    }
  };

  return (
    <Box>
      <Box sx={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', mb: 2 }}>
        <Typography variant="h6">Error Log</Typography>
        <Button startIcon={<DownloadIcon />} size="small">
          Export Log
        </Button>
      </Box>
      
      {errors.map((error) => (
        <Accordion key={error.id} sx={{ mb: 1 }}>
          <AccordionSummary expandIcon={<ExpandMoreIcon />}>
            <Box sx={{ display: 'flex', alignItems: 'center', width: '100%' }}>
              <Chip
                size="small"
                label={error.severity}
                color={getSeverityColor(error.severity)}
                sx={{ mr: 2 }}
              />
              <Typography sx={{ flex: 1 }}>{error.message}</Typography>
              <Typography variant="body2" color="text.secondary" sx={{ mr: 2 }}>
                {error.timestamp}
              </Typography>
              {error.resolved && (
                <Chip size="small" label="Resolved" color="success" />
              )}
            </Box>
          </AccordionSummary>
          <AccordionDetails>
            <Box sx={{ display: 'flex', flexDirection: 'column', gap: 1 }}>
              <Typography variant="body2">
                <strong>Entity:</strong> {error.entityType} ({error.entityId})
              </Typography>
              <Typography variant="body2">
                <strong>Details:</strong> {error.details}
              </Typography>
              <Box sx={{ mt: 2 }}>
                <Button size="small" color="primary">
                  Retry
                </Button>
                <Button size="small" color="secondary" sx={{ ml: 1 }}>
                  Mark Resolved
                </Button>
              </Box>
            </Box>
          </AccordionDetails>
        </Accordion>
      ))}
    </Box>
  );
};

export const MigrationDetailView: React.FC<MigrationDetailViewProps> = ({
  migrationId,
  onClose,
  onAction,
}) => {
  const { state } = useDashboard();
  const { activeMigrations } = state;
  const [activeTab, setActiveTab] = useState(0);
  const [entityDetailDialog, setEntityDetailDialog] = useState<string | null>(null);

  const migration = activeMigrations.get(migrationId);

  if (!migration) {
    return (
      <Card>
        <CardContent>
          <Typography variant="h6" color="error">
            Migration not found: {migrationId}
          </Typography>
        </CardContent>
      </Card>
    );
  }

  const handleTabChange = (event: React.SyntheticEvent, newValue: number) => {
    setActiveTab(newValue);
  };

  const handleViewEntityDetails = (entityType: string) => {
    setEntityDetailDialog(entityType);
  };

  const handleAction = (action: string) => {
    onAction?.(action, migrationId);
  };

  const formatTime = (seconds: number): string => {
    const hours = Math.floor(seconds / 3600);
    const minutes = Math.floor((seconds % 3600) / 60);
    const secs = Math.floor(seconds % 60);
    
    if (hours > 0) {
      return `${hours}h ${minutes}m ${secs}s`;
    } else if (minutes > 0) {
      return `${minutes}m ${secs}s`;
    } else {
      return `${secs}s`;
    }
  };

  return (
    <Box>
      {/* Header */}
      <Card sx={{ mb: 3 }}>
        <CardContent>
          <Box sx={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', mb: 2 }}>
            <Typography variant="h4">
              Migration {migration.migrationId.slice(-8)}
            </Typography>
            
            <Box sx={{ display: 'flex', gap: 1 }}>
              <Button
                startIcon={<RefreshIcon />}
                onClick={() => handleAction('refresh')}
                size="small"
              >
                Refresh
              </Button>
              {onClose && (
                <IconButton onClick={onClose}>
                  <CloseIcon />
                </IconButton>
              )}
            </Box>
          </Box>

          {/* Status and Progress */}
          <Box sx={{ display: 'flex', gap: 4, mb: 3 }}>
            <Box>
              <Typography variant="body2" color="text.secondary">Status</Typography>
              <Chip label={migration.status.toUpperCase()} color="primary" />
            </Box>
            <Box>
              <Typography variant="body2" color="text.secondary">Overall Progress</Typography>
              <Typography variant="h6">{migration.overallProgressPercentage.toFixed(1)}%</Typography>
            </Box>
            <Box>
              <Typography variant="body2" color="text.secondary">Elapsed Time</Typography>
              <Typography variant="h6">{formatTime(migration.elapsedTime)}</Typography>
            </Box>
            <Box>
              <Typography variant="body2" color="text.secondary">Processing Speed</Typography>
              <Typography variant="h6">{migration.entitiesPerSecond.toFixed(1)}/s</Typography>
            </Box>
          </Box>

          {/* Overall Progress Bar */}
          <Box sx={{ mb: 2 }}>
            <LinearProgress
              variant="determinate"
              value={migration.overallProgressPercentage}
              sx={{ height: 12, borderRadius: 6 }}
              color={migration.status === 'running' ? 'primary' : migration.status === 'completed' ? 'success' : 'inherit'}
            />
          </Box>

          {/* Current Phase */}
          <Typography variant="body2" color="text.secondary">
            Current Phase: {migration.currentPhase} - {migration.currentEntity}
          </Typography>
        </CardContent>
      </Card>

      {/* Tabs */}
      <Card>
        <Box sx={{ borderBottom: 1, borderColor: 'divider' }}>
          <Tabs value={activeTab} onChange={handleTabChange}>
            <Tab label="Entity Progress" />
            <Tab label="Timeline" />
            <Tab label="Error Log" />
            <Tab label="Configuration" />
          </Tabs>
        </Box>

        <TabPanel value={activeTab} index={0}>
          <Typography variant="h6" gutterBottom>
            Entity Migration Progress
          </Typography>
          <Box sx={{ display: 'flex', flexDirection: 'column', gap: 2 }}>
            {Object.entries(migration.entityProgress).map(([entityType, progress]) => (
              <EntityDetailCard
                key={entityType}
                entityType={entityType}
                progress={progress}
                onViewDetails={handleViewEntityDetails}
              />
            ))}
          </Box>
        </TabPanel>

        <TabPanel value={activeTab} index={1}>
          <Typography variant="h6" gutterBottom>
            Migration Timeline
          </Typography>
          <MigrationTimelineView migration={migration} />
        </TabPanel>

        <TabPanel value={activeTab} index={2}>
          <ErrorLogView migration={migration} />
        </TabPanel>

        <TabPanel value={activeTab} index={3}>
          <Typography variant="h6" gutterBottom>
            Migration Configuration
          </Typography>
          <Alert severity="info" sx={{ mb: 2 }}>
            Configuration details will be implemented in the next component.
          </Alert>
          <Typography variant="body2" color="text.secondary">
            This section will show the migration configuration including source/destination stores,
            entity selections, batch sizes, and migration options.
          </Typography>
        </TabPanel>
      </Card>

      {/* Entity Detail Dialog */}
      <Dialog
        open={!!entityDetailDialog}
        onClose={() => setEntityDetailDialog(null)}
        maxWidth="lg"
        fullWidth
      >
        <DialogTitle>
          {entityDetailDialog && `${entityDetailDialog.charAt(0).toUpperCase() + entityDetailDialog.slice(1)} Details`}
        </DialogTitle>
        <DialogContent>
          <Alert severity="info" sx={{ mb: 2 }}>
            Entity-specific detailed view will be implemented next.
          </Alert>
          <Typography variant="body2" color="text.secondary">
            This dialog will show detailed information about the specific entity type including
            individual item progress, error details, and processing logs.
          </Typography>
        </DialogContent>
        <DialogActions>
          <Button onClick={() => setEntityDetailDialog(null)}>Close</Button>
        </DialogActions>
      </Dialog>
    </Box>
  );
}; 