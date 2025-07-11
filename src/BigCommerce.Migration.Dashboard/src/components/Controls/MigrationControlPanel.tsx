import React, { useState } from 'react';
import {
  Box,
  Card,
  CardContent,
  Typography,
  Button,
  IconButton,
  Dialog,
  DialogTitle,
  DialogContent,
  DialogActions,
  TextField,
  FormControl,
  InputLabel,
  Select,
  MenuItem,
  Chip,
  Divider,
  Alert,
  LinearProgress,
  Tooltip,
  useTheme,
  alpha,
} from '@mui/material';
import {
  PlayArrow as PlayArrowIcon,
  Stop as StopIcon,
  Pause as PauseIcon,
  Refresh as RefreshIcon,
  Cancel as CancelIcon,
  Settings as SettingsIcon,
  Add as AddIcon,
  Delete as DeleteIcon,
  Edit as EditIcon,
  Warning as WarningIcon,
} from '@mui/icons-material';
import { useDashboard } from '../../context/DashboardContext';
import type { MigrationProgress, MigrationStatus } from '../../types';

interface MigrationControlPanelProps {
  migrationId?: string;
  onMigrationAction?: (action: string, migrationId: string) => void;
}

interface MigrationActionConfirmation {
  open: boolean;
  action: 'start' | 'stop' | 'pause' | 'cancel' | 'delete' | null;
  migrationId: string | null;
  title: string;
  message: string;
  severity: 'warning' | 'error' | 'info';
}

const MigrationActionButton: React.FC<{
  action: string;
  icon: React.ReactNode;
  color: 'primary' | 'secondary' | 'error' | 'warning' | 'success';
  disabled?: boolean;
  onClick: () => void;
  tooltip: string;
}> = ({ action, icon, color, disabled, onClick, tooltip }) => (
  <Tooltip title={disabled ? `Cannot ${action.toLowerCase()}` : tooltip}>
    <span>
      <IconButton
        color={color}
        onClick={onClick}
        disabled={disabled}
        size="small"
        sx={{ mx: 0.5 }}
      >
        {icon}
      </IconButton>
    </span>
  </Tooltip>
);

const MigrationCard: React.FC<{
  migration: MigrationProgress;
  onAction: (action: string, migrationId: string) => void;
}> = ({ migration, onAction }) => {
  const theme = useTheme();

  const getStatusColor = (status: MigrationStatus) => {
    switch (status) {
      case 'running': return theme.palette.primary.main;
      case 'completed': return theme.palette.success.main;
      case 'failed': return theme.palette.error.main;
      case 'cancelled': return theme.palette.warning.main;
      case 'pending': return theme.palette.info.main;
      default: return theme.palette.text.secondary;
    }
  };

  const canStart = migration.status === 'pending' || migration.status === 'cancelled';
  const canStop = migration.status === 'running';
  const canPause = migration.status === 'running';
  const canCancel = migration.status === 'running' || migration.status === 'pending';
  const canDelete = migration.status === 'completed' || migration.status === 'failed' || migration.status === 'cancelled';

  return (
    <Card sx={{ mb: 2, border: `2px solid ${alpha(getStatusColor(migration.status), 0.3)}` }}>
      <CardContent>
        <Box sx={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', mb: 2 }}>
          <Box>
            <Typography variant="h6" gutterBottom>
              Migration {migration.migrationId.slice(-8)}
            </Typography>
            <Chip
              label={migration.status.toUpperCase()}
              size="small"
              sx={{
                bgcolor: alpha(getStatusColor(migration.status), 0.1),
                color: getStatusColor(migration.status),
                border: `1px solid ${alpha(getStatusColor(migration.status), 0.3)}`,
              }}
            />
          </Box>
          
          <Box sx={{ display: 'flex', alignItems: 'center' }}>
            <MigrationActionButton
              action="Start"
              icon={<PlayArrowIcon />}
              color="success"
              disabled={!canStart}
              onClick={() => onAction('start', migration.migrationId)}
              tooltip="Start migration"
            />
            
            <MigrationActionButton
              action="Pause"
              icon={<PauseIcon />}
              color="warning"
              disabled={!canPause}
              onClick={() => onAction('pause', migration.migrationId)}
              tooltip="Pause migration"
            />
            
            <MigrationActionButton
              action="Stop"
              icon={<StopIcon />}
              color="error"
              disabled={!canStop}
              onClick={() => onAction('stop', migration.migrationId)}
              tooltip="Stop migration"
            />
            
            <MigrationActionButton
              action="Cancel"
              icon={<CancelIcon />}
              color="error"
              disabled={!canCancel}
              onClick={() => onAction('cancel', migration.migrationId)}
              tooltip="Cancel migration"
            />
            
            <Divider orientation="vertical" flexItem sx={{ mx: 1 }} />
            
            <MigrationActionButton
              action="Settings"
              icon={<SettingsIcon />}
              color="primary"
              disabled={migration.status === 'running'}
              onClick={() => onAction('configure', migration.migrationId)}
              tooltip="Configure migration"
            />
            
            <MigrationActionButton
              action="Delete"
              icon={<DeleteIcon />}
              color="error"
              disabled={!canDelete}
              onClick={() => onAction('delete', migration.migrationId)}
              tooltip="Delete migration"
            />
          </Box>
        </Box>

        <Box sx={{ mb: 2 }}>
          <Box sx={{ display: 'flex', justifyContent: 'space-between', mb: 1 }}>
            <Typography variant="body2">Progress</Typography>
            <Typography variant="body2">{migration.overallProgressPercentage.toFixed(1)}%</Typography>
          </Box>
          <LinearProgress
            variant="determinate"
            value={migration.overallProgressPercentage}
            sx={{ height: 8, borderRadius: 4 }}
            color={migration.status === 'running' ? 'primary' : migration.status === 'completed' ? 'success' : 'inherit'}
          />
        </Box>

        <Box sx={{ display: 'flex', justifyContent: 'space-between', flexWrap: 'wrap', gap: 2 }}>
          <Box>
            <Typography variant="caption" color="text.secondary">Entities</Typography>
            <Typography variant="body2">
              {migration.processedEntities.toLocaleString()} / {migration.totalEntities.toLocaleString()}
            </Typography>
          </Box>
          <Box>
            <Typography variant="caption" color="text.secondary">Speed</Typography>
            <Typography variant="body2">{migration.entitiesPerSecond.toFixed(1)}/s</Typography>
          </Box>
          <Box>
            <Typography variant="caption" color="text.secondary">Success Rate</Typography>
            <Typography variant="body2">
              {migration.processedEntities > 0 
                ? ((migration.successfulEntities / migration.processedEntities) * 100).toFixed(1) 
                : 0}%
            </Typography>
          </Box>
          <Box>
            <Typography variant="caption" color="text.secondary">Current Phase</Typography>
            <Typography variant="body2">{migration.currentPhase}</Typography>
          </Box>
        </Box>
      </CardContent>
    </Card>
  );
};

export const MigrationControlPanel: React.FC<MigrationControlPanelProps> = ({
  migrationId,
  onMigrationAction,
}) => {
  const { state } = useDashboard();
  const { activeMigrations } = state;
  const [confirmation, setConfirmation] = useState<MigrationActionConfirmation>({
    open: false,
    action: null,
    migrationId: null,
    title: '',
    message: '',
    severity: 'info',
  });
  const [newMigrationDialog, setNewMigrationDialog] = useState(false);

  const migrationList = migrationId 
    ? Array.from(activeMigrations.values()).filter(m => m.migrationId === migrationId)
    : Array.from(activeMigrations.values());

  const handleAction = (action: string, targetMigrationId: string) => {
    const migration = activeMigrations.get(targetMigrationId);
    if (!migration) return;

    let title = '';
    let message = '';
    let severity: 'warning' | 'error' | 'info' = 'info';

    switch (action) {
      case 'start':
        title = 'Start Migration';
        message = `Are you sure you want to start migration ${targetMigrationId.slice(-8)}?`;
        severity = 'info';
        break;
      case 'pause':
        title = 'Pause Migration';
        message = `Migration ${targetMigrationId.slice(-8)} will be paused. You can resume it later.`;
        severity = 'warning';
        break;
      case 'stop':
        title = 'Stop Migration';
        message = `Migration ${targetMigrationId.slice(-8)} will be stopped. This action cannot be undone.`;
        severity = 'warning';
        break;
      case 'cancel':
        title = 'Cancel Migration';
        message = `Migration ${targetMigrationId.slice(-8)} will be cancelled. All progress will be lost.`;
        severity = 'error';
        break;
      case 'delete':
        title = 'Delete Migration';
        message = `Migration ${targetMigrationId.slice(-8)} and all its data will be permanently deleted.`;
        severity = 'error';
        break;
      case 'configure':
        // Handle configuration without confirmation
        onMigrationAction?.(action, targetMigrationId);
        return;
      default:
        return;
    }

    setConfirmation({
      open: true,
      action: action as any,
      migrationId: targetMigrationId,
      title,
      message,
      severity,
    });
  };

  const handleConfirmAction = () => {
    if (confirmation.action && confirmation.migrationId) {
      onMigrationAction?.(confirmation.action, confirmation.migrationId);
      
      // Simulate API call result
      console.log(`${confirmation.action} action executed for migration ${confirmation.migrationId}`);
    }
    
    setConfirmation({
      open: false,
      action: null,
      migrationId: null,
      title: '',
      message: '',
      severity: 'info',
    });
  };

  const handleCancelAction = () => {
    setConfirmation({
      open: false,
      action: null,
      migrationId: null,
      title: '',
      message: '',
      severity: 'info',
    });
  };

  const runningMigrations = migrationList.filter(m => m.status === 'running').length;
  const pendingMigrations = migrationList.filter(m => m.status === 'pending').length;
  const completedMigrations = migrationList.filter(m => m.status === 'completed').length;

  return (
    <Box>
      {/* Control Panel Header */}
      <Card sx={{ mb: 3 }}>
        <CardContent>
          <Box sx={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', mb: 2 }}>
            <Typography variant="h5" component="h2">
              Migration Control Panel
            </Typography>
            
            <Box sx={{ display: 'flex', gap: 1 }}>
              <Button
                variant="contained"
                startIcon={<AddIcon />}
                onClick={() => setNewMigrationDialog(true)}
                color="primary"
              >
                New Migration
              </Button>
              
              <Button
                variant="outlined"
                startIcon={<RefreshIcon />}
                onClick={() => window.location.reload()}
                color="primary"
              >
                Refresh
              </Button>
            </Box>
          </Box>

          {/* Summary Stats */}
          <Box sx={{ display: 'flex', gap: 4 }}>
            <Box>
              <Typography variant="h6" color="primary">{runningMigrations}</Typography>
              <Typography variant="body2" color="text.secondary">Running</Typography>
            </Box>
            <Box>
              <Typography variant="h6" color="warning.main">{pendingMigrations}</Typography>
              <Typography variant="body2" color="text.secondary">Pending</Typography>
            </Box>
            <Box>
              <Typography variant="h6" color="success.main">{completedMigrations}</Typography>
              <Typography variant="body2" color="text.secondary">Completed</Typography>
            </Box>
            <Box>
              <Typography variant="h6">{migrationList.length}</Typography>
              <Typography variant="body2" color="text.secondary">Total</Typography>
            </Box>
          </Box>

          {runningMigrations > 3 && (
            <Alert severity="warning" sx={{ mt: 2 }}>
              <Typography variant="body2">
                Multiple migrations running simultaneously may impact performance.
              </Typography>
            </Alert>
          )}
        </CardContent>
      </Card>

      {/* Migration Cards */}
      <Box>
        {migrationList.length === 0 ? (
          <Card>
            <CardContent sx={{ textAlign: 'center', py: 6 }}>
              <Typography variant="h6" color="text.secondary" gutterBottom>
                No migrations found
              </Typography>
              <Typography variant="body2" color="text.secondary" sx={{ mb: 3 }}>
                Start by creating a new migration to see controls here
              </Typography>
              <Button
                variant="contained"
                startIcon={<AddIcon />}
                onClick={() => setNewMigrationDialog(true)}
              >
                Create Migration
              </Button>
            </CardContent>
          </Card>
        ) : (
          migrationList.map((migration) => (
            <MigrationCard
              key={migration.migrationId}
              migration={migration}
              onAction={handleAction}
            />
          ))
        )}
      </Box>

      {/* Action Confirmation Dialog */}
      <Dialog
        open={confirmation.open}
        onClose={handleCancelAction}
        maxWidth="sm"
        fullWidth
      >
        <DialogTitle sx={{ display: 'flex', alignItems: 'center' }}>
          <WarningIcon sx={{ mr: 1, color: `${confirmation.severity}.main` }} />
          {confirmation.title}
        </DialogTitle>
        <DialogContent>
          <Alert severity={confirmation.severity} sx={{ mb: 2 }}>
            {confirmation.message}
          </Alert>
          {confirmation.action === 'delete' && (
            <Typography variant="body2" color="text.secondary">
              This action is permanent and cannot be undone.
            </Typography>
          )}
        </DialogContent>
        <DialogActions>
          <Button onClick={handleCancelAction} color="inherit">
            Cancel
          </Button>
          <Button
            onClick={handleConfirmAction}
            color={confirmation.severity === 'error' ? 'error' : 'primary'}
            variant="contained"
          >
            {confirmation.action === 'delete' ? 'Delete' : 'Confirm'}
          </Button>
        </DialogActions>
      </Dialog>

      {/* New Migration Dialog Placeholder */}
      <Dialog
        open={newMigrationDialog}
        onClose={() => setNewMigrationDialog(false)}
        maxWidth="md"
        fullWidth
      >
        <DialogTitle>Create New Migration</DialogTitle>
        <DialogContent>
          <Alert severity="info" sx={{ mb: 2 }}>
            Migration configuration will be implemented in the next component.
          </Alert>
          <Typography variant="body2" color="text.secondary">
            This dialog will contain the migration setup form with store configuration, 
            entity selection, and migration options.
          </Typography>
        </DialogContent>
        <DialogActions>
          <Button onClick={() => setNewMigrationDialog(false)}>Close</Button>
        </DialogActions>
      </Dialog>
    </Box>
  );
}; 