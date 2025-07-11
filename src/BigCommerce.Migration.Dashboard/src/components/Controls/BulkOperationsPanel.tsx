import React, { useState, useMemo } from 'react';
import {
  Box,
  Card,
  CardContent,
  Typography,
  Button,
  Checkbox,
  Table,
  TableBody,
  TableCell,
  TableContainer,
  TableHead,
  TableRow,
  Chip,
  Dialog,
  DialogTitle,
  DialogContent,
  DialogActions,
  FormControl,
  InputLabel,
  Select,
  MenuItem,
  Alert,
  LinearProgress,
  IconButton,
  Tooltip,
  Menu,
  MenuList,
  MenuItem as MenuItemComponent,
  ListItemIcon,
  ListItemText,
  Divider,
  useTheme,
  alpha,
} from '@mui/material';
import {
  PlayArrow as PlayArrowIcon,
  Stop as StopIcon,
  Pause as PauseIcon,
  Cancel as CancelIcon,
  Delete as DeleteIcon,
  MoreVert as MoreVertIcon,
  CheckCircle as CheckCircleIcon,
  Error as ErrorIcon,
  Warning as WarningIcon,
  Settings as SettingsIcon,
  Download as DownloadIcon,
  Refresh as RefreshIcon,
} from '@mui/icons-material';
import { useDashboard } from '../../context/DashboardContext';
import type { MigrationProgress, MigrationStatus } from '../../types';

interface BulkOperationsPanelProps {
  onBulkAction?: (action: string, migrationIds: string[], options?: any) => void;
  onSingleAction?: (action: string, migrationId: string) => void;
  showAdvancedOptions?: boolean;
}

interface BulkOperationConfirmation {
  open: boolean;
  action: string | null;
  selectedCount: number;
  title: string;
  message: string;
  severity: 'warning' | 'error' | 'info';
  confirmText: string;
}

interface BulkOperationOptions {
  force: boolean;
  waitForCompletion: boolean;
  notifyOnComplete: boolean;
  retryCount: number;
}

const MigrationRow: React.FC<{
  migration: MigrationProgress;
  selected: boolean;
  onSelect: (migrationId: string, selected: boolean) => void;
  onAction: (action: string, migrationId: string) => void;
}> = ({ migration, selected, onSelect, onAction }) => {
  const theme = useTheme();
  const [menuAnchor, setMenuAnchor] = useState<null | HTMLElement>(null);

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

  const getStatusIcon = (status: MigrationStatus) => {
    switch (status) {
      case 'completed': return <CheckCircleIcon sx={{ color: 'success.main' }} />;
      case 'failed': return <ErrorIcon sx={{ color: 'error.main' }} />;
      case 'cancelled': return <WarningIcon sx={{ color: 'warning.main' }} />;
      case 'running': return <PlayArrowIcon sx={{ color: 'primary.main' }} />;
      default: return null;
    }
  };

  const handleMenuClick = (event: React.MouseEvent<HTMLElement>) => {
    setMenuAnchor(event.currentTarget);
  };

  const handleMenuClose = () => {
    setMenuAnchor(null);
  };

  const handleMenuAction = (action: string) => {
    onAction(action, migration.migrationId);
    handleMenuClose();
  };

  const formatTime = (seconds: number): string => {
    const hours = Math.floor(seconds / 3600);
    const minutes = Math.floor((seconds % 3600) / 60);
    if (hours > 0) return `${hours}h ${minutes}m`;
    if (minutes > 0) return `${minutes}m`;
    return `${seconds}s`;
  };

  return (
    <TableRow
      hover
      selected={selected}
      sx={{
        cursor: 'pointer',
        backgroundColor: selected ? alpha(theme.palette.primary.main, 0.08) : 'inherit',
      }}
    >
      <TableCell padding="checkbox">
        <Checkbox
          checked={selected}
          onChange={(e) => onSelect(migration.migrationId, e.target.checked)}
        />
      </TableCell>
      
      <TableCell>
        <Box sx={{ display: 'flex', alignItems: 'center' }}>
          {getStatusIcon(migration.status)}
          <Typography variant="body2" sx={{ ml: 1, fontFamily: 'monospace' }}>
            {migration.migrationId.slice(-8)}
          </Typography>
        </Box>
      </TableCell>
      
      <TableCell>
        <Chip
          size="small"
          label={migration.status.toUpperCase()}
          sx={{
            bgcolor: alpha(getStatusColor(migration.status), 0.1),
            color: getStatusColor(migration.status),
            border: `1px solid ${alpha(getStatusColor(migration.status), 0.3)}`,
          }}
        />
      </TableCell>
      
      <TableCell>
        <Box sx={{ display: 'flex', alignItems: 'center', minWidth: 120 }}>
          <Box sx={{ flex: 1, mr: 1 }}>
            <LinearProgress
              variant="determinate"
              value={migration.overallProgressPercentage}
              sx={{ height: 6, borderRadius: 3 }}
              color={migration.status === 'running' ? 'primary' : migration.status === 'completed' ? 'success' : 'inherit'}
            />
          </Box>
          <Typography variant="caption" sx={{ minWidth: 40 }}>
            {migration.overallProgressPercentage.toFixed(0)}%
          </Typography>
        </Box>
      </TableCell>
      
      <TableCell>
        <Typography variant="body2">
          {migration.processedEntities.toLocaleString()}
        </Typography>
        <Typography variant="caption" color="text.secondary">
          / {migration.totalEntities.toLocaleString()}
        </Typography>
      </TableCell>
      
      <TableCell>
        <Typography variant="body2">
          {migration.entitiesPerSecond.toFixed(1)}/s
        </Typography>
      </TableCell>
      
      <TableCell>
        <Typography variant="body2">
          {formatTime(migration.elapsedTime)}
        </Typography>
      </TableCell>
      
      <TableCell>
        <Typography variant="body2" color={migration.failedEntities > 0 ? 'error.main' : 'inherit'}>
          {migration.failedEntities}
        </Typography>
      </TableCell>
      
      <TableCell>
        <IconButton size="small" onClick={handleMenuClick}>
          <MoreVertIcon />
        </IconButton>
        
        <Menu
          anchorEl={menuAnchor}
          open={Boolean(menuAnchor)}
          onClose={handleMenuClose}
        >
          <MenuList dense>
            <MenuItemComponent onClick={() => handleMenuAction('view')}>
              <ListItemIcon><SettingsIcon fontSize="small" /></ListItemIcon>
              <ListItemText>View Details</ListItemText>
            </MenuItemComponent>
            
            <MenuItemComponent onClick={() => handleMenuAction('pause')} disabled={migration.status !== 'running'}>
              <ListItemIcon><PauseIcon fontSize="small" /></ListItemIcon>
              <ListItemText>Pause</ListItemText>
            </MenuItemComponent>
            
            <MenuItemComponent onClick={() => handleMenuAction('stop')} disabled={migration.status !== 'running'}>
              <ListItemIcon><StopIcon fontSize="small" /></ListItemIcon>
              <ListItemText>Stop</ListItemText>
            </MenuItemComponent>
            
            <Divider />
            
            <MenuItemComponent onClick={() => handleMenuAction('delete')} disabled={migration.status === 'running'}>
              <ListItemIcon><DeleteIcon fontSize="small" /></ListItemIcon>
              <ListItemText>Delete</ListItemText>
            </MenuItemComponent>
          </MenuList>
        </Menu>
      </TableCell>
    </TableRow>
  );
};

export const BulkOperationsPanel: React.FC<BulkOperationsPanelProps> = ({
  onBulkAction,
  onSingleAction,
  showAdvancedOptions = true,
}) => {
  const { state } = useDashboard();
  const { activeMigrations } = state;
  const [selectedMigrations, setSelectedMigrations] = useState<Set<string>>(new Set());
  const [confirmation, setConfirmation] = useState<BulkOperationConfirmation>({
    open: false,
    action: null,
    selectedCount: 0,
    title: '',
    message: '',
    severity: 'info',
    confirmText: 'Confirm',
  });
  const [bulkOptions, setBulkOptions] = useState<BulkOperationOptions>({
    force: false,
    waitForCompletion: false,
    notifyOnComplete: true,
    retryCount: 3,
  });

  const migrationList = Array.from(activeMigrations.values());

  const selectedMigrationList = useMemo(() => {
    return migrationList.filter(m => selectedMigrations.has(m.migrationId));
  }, [migrationList, selectedMigrations]);

  const canPerformAction = (action: string) => {
    if (selectedMigrations.size === 0) return false;
    
    switch (action) {
      case 'start':
        return selectedMigrationList.some(m => m.status === 'pending' || m.status === 'cancelled');
      case 'pause':
        return selectedMigrationList.some(m => m.status === 'running');
      case 'stop':
        return selectedMigrationList.some(m => m.status === 'running');
      case 'cancel':
        return selectedMigrationList.some(m => m.status === 'running' || m.status === 'pending');
      case 'delete':
        return selectedMigrationList.some(m => m.status !== 'running');
      case 'export':
        return true;
      default:
        return false;
    }
  };

  const handleSelectAll = (checked: boolean) => {
    if (checked) {
      setSelectedMigrations(new Set(migrationList.map(m => m.migrationId)));
    } else {
      setSelectedMigrations(new Set());
    }
  };

  const handleSelectMigration = (migrationId: string, selected: boolean) => {
    const newSelected = new Set(selectedMigrations);
    if (selected) {
      newSelected.add(migrationId);
    } else {
      newSelected.delete(migrationId);
    }
    setSelectedMigrations(newSelected);
  };

  const handleBulkAction = (action: string) => {
    const selectedIds = Array.from(selectedMigrations);
    let title = '';
    let message = '';
    let severity: 'warning' | 'error' | 'info' = 'info';
    let confirmText = 'Confirm';

    switch (action) {
      case 'start':
        title = 'Start Migrations';
        message = `Start ${selectedIds.length} selected migration(s)?`;
        confirmText = 'Start';
        break;
      case 'pause':
        title = 'Pause Migrations';
        message = `Pause ${selectedIds.length} selected migration(s)? They can be resumed later.`;
        severity = 'warning';
        confirmText = 'Pause';
        break;
      case 'stop':
        title = 'Stop Migrations';
        message = `Stop ${selectedIds.length} selected migration(s)? This action cannot be undone.`;
        severity = 'warning';
        confirmText = 'Stop';
        break;
      case 'cancel':
        title = 'Cancel Migrations';
        message = `Cancel ${selectedIds.length} selected migration(s)? All progress will be lost.`;
        severity = 'error';
        confirmText = 'Cancel Migrations';
        break;
      case 'delete':
        title = 'Delete Migrations';
        message = `Permanently delete ${selectedIds.length} selected migration(s) and all their data?`;
        severity = 'error';
        confirmText = 'Delete';
        break;
      case 'export':
        // Handle export without confirmation
        onBulkAction?.(action, selectedIds, bulkOptions);
        return;
      default:
        return;
    }

    setConfirmation({
      open: true,
      action,
      selectedCount: selectedIds.length,
      title,
      message,
      severity,
      confirmText,
    });
  };

  const handleConfirmAction = () => {
    if (confirmation.action) {
      const selectedIds = Array.from(selectedMigrations);
      onBulkAction?.(confirmation.action, selectedIds, bulkOptions);
      
      // Clear selection after action
      setSelectedMigrations(new Set());
    }
    
    setConfirmation({
      open: false,
      action: null,
      selectedCount: 0,
      title: '',
      message: '',
      severity: 'info',
      confirmText: 'Confirm',
    });
  };

  const handleCancelAction = () => {
    setConfirmation({
      open: false,
      action: null,
      selectedCount: 0,
      title: '',
      message: '',
      severity: 'info',
      confirmText: 'Confirm',
    });
  };

  const handleSingleAction = (action: string, migrationId: string) => {
    onSingleAction?.(action, migrationId);
  };

  const isAllSelected = migrationList.length > 0 && selectedMigrations.size === migrationList.length;
  const isIndeterminate = selectedMigrations.size > 0 && selectedMigrations.size < migrationList.length;

  return (
    <Box>
      {/* Bulk Actions Toolbar */}
      <Card sx={{ mb: 3 }}>
        <CardContent>
          <Box sx={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', mb: 2 }}>
            <Typography variant="h6">
              Bulk Migration Operations
            </Typography>
            
            <Box sx={{ display: 'flex', alignItems: 'center', gap: 1 }}>
              <Typography variant="body2" color="text.secondary">
                {selectedMigrations.size} selected
              </Typography>
              
              <IconButton size="small" onClick={() => window.location.reload()}>
                <RefreshIcon />
              </IconButton>
            </Box>
          </Box>

          {/* Action Buttons */}
          <Box sx={{ display: 'flex', flexWrap: 'wrap', gap: 1, mb: 2 }}>
            <Button
              startIcon={<PlayArrowIcon />}
              onClick={() => handleBulkAction('start')}
              disabled={!canPerformAction('start')}
              color="success"
              variant={canPerformAction('start') ? 'contained' : 'outlined'}
            >
              Start
            </Button>
            
            <Button
              startIcon={<PauseIcon />}
              onClick={() => handleBulkAction('pause')}
              disabled={!canPerformAction('pause')}
              color="warning"
              variant={canPerformAction('pause') ? 'contained' : 'outlined'}
            >
              Pause
            </Button>
            
            <Button
              startIcon={<StopIcon />}
              onClick={() => handleBulkAction('stop')}
              disabled={!canPerformAction('stop')}
              color="error"
              variant={canPerformAction('stop') ? 'contained' : 'outlined'}
            >
              Stop
            </Button>
            
            <Button
              startIcon={<CancelIcon />}
              onClick={() => handleBulkAction('cancel')}
              disabled={!canPerformAction('cancel')}
              color="error"
              variant="outlined"
            >
              Cancel
            </Button>
            
            <Divider orientation="vertical" flexItem sx={{ mx: 1 }} />
            
            <Button
              startIcon={<DownloadIcon />}
              onClick={() => handleBulkAction('export')}
              disabled={!canPerformAction('export')}
              variant="outlined"
            >
              Export
            </Button>
            
            <Button
              startIcon={<DeleteIcon />}
              onClick={() => handleBulkAction('delete')}
              disabled={!canPerformAction('delete')}
              color="error"
              variant="outlined"
            >
              Delete
            </Button>
          </Box>

          {/* Advanced Options */}
          {showAdvancedOptions && selectedMigrations.size > 0 && (
            <Box sx={{ display: 'flex', gap: 2, flexWrap: 'wrap' }}>
              <FormControl size="small" sx={{ minWidth: 120 }}>
                <InputLabel>Retry Count</InputLabel>
                <Select
                  value={bulkOptions.retryCount}
                  onChange={(e) => setBulkOptions(prev => ({ ...prev, retryCount: e.target.value as number }))}
                >
                  <MenuItem value={0}>No Retry</MenuItem>
                  <MenuItem value={1}>1 Retry</MenuItem>
                  <MenuItem value={3}>3 Retries</MenuItem>
                  <MenuItem value={5}>5 Retries</MenuItem>
                </Select>
              </FormControl>
            </Box>
          )}
        </CardContent>
      </Card>

      {/* Migration Table */}
      <Card>
        <TableContainer>
          <Table>
            <TableHead>
              <TableRow>
                <TableCell padding="checkbox">
                  <Checkbox
                    indeterminate={isIndeterminate}
                    checked={isAllSelected}
                    onChange={(e) => handleSelectAll(e.target.checked)}
                  />
                </TableCell>
                <TableCell>Migration ID</TableCell>
                <TableCell>Status</TableCell>
                <TableCell>Progress</TableCell>
                <TableCell>Entities</TableCell>
                <TableCell>Speed</TableCell>
                <TableCell>Elapsed</TableCell>
                <TableCell>Errors</TableCell>
                <TableCell>Actions</TableCell>
              </TableRow>
            </TableHead>
            <TableBody>
              {migrationList.map((migration) => (
                <MigrationRow
                  key={migration.migrationId}
                  migration={migration}
                  selected={selectedMigrations.has(migration.migrationId)}
                  onSelect={handleSelectMigration}
                  onAction={handleSingleAction}
                />
              ))}
              
              {migrationList.length === 0 && (
                <TableRow>
                  <TableCell colSpan={9} align="center">
                    <Typography variant="body2" color="text.secondary" sx={{ py: 4 }}>
                      No migrations available
                    </Typography>
                  </TableCell>
                </TableRow>
              )}
            </TableBody>
          </Table>
        </TableContainer>
      </Card>

      {/* Confirmation Dialog */}
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
              This action is permanent and cannot be undone. All migration data will be lost.
            </Typography>
          )}
          
          {selectedMigrations.size > 0 && (
            <Box sx={{ mt: 2 }}>
              <Typography variant="body2" gutterBottom>
                Selected migrations:
              </Typography>
              <Box sx={{ display: 'flex', flexWrap: 'wrap', gap: 1 }}>
                {Array.from(selectedMigrations).slice(0, 5).map((id) => (
                  <Chip key={id} label={id.slice(-8)} size="small" />
                ))}
                {selectedMigrations.size > 5 && (
                  <Chip label={`+${selectedMigrations.size - 5} more`} size="small" variant="outlined" />
                )}
              </Box>
            </Box>
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
            {confirmation.confirmText}
          </Button>
        </DialogActions>
      </Dialog>
    </Box>
  );
}; 