import React, { useState } from 'react';
import {
  Dialog,
  DialogTitle,
  DialogContent,
  DialogActions,
  Button,

  TextField,
  Typography,
  Box,
  Chip,
  Alert,
  CircularProgress
} from '@mui/material';
import type { LiveCancellationRequest } from '../../types';
import { CancellationScope } from '../../types';

interface LiveCancellationDialogProps {
  open: boolean;
  migrationId: string;
  onClose: () => void;
  onConfirm: (request: LiveCancellationRequest) => Promise<void>;
  isCancelling?: boolean;
}

/**
 * Simplified Live Cancellation Dialog (Task 7.6)
 * 
 * Provides Migration-level cancellation only for simplicity.
 * Future versions can expand to support EntityType, Batch, and Store scopes.
 */
export const LiveCancellationDialog: React.FC<LiveCancellationDialogProps> = ({
  open,
  migrationId,
  onClose,
  onConfirm,
  isCancelling = false
}) => {
  // Simplified: Only Migration scope supported for now
  const [reason, setReason] = useState<string>('User requested migration cancellation');
  const [error, setError] = useState<string>('');

  const handleConfirm = async () => {
    setError('');
    
    // Simple validation for migration-only cancellation
    if (!reason.trim()) {
      setError('Please provide a reason for cancellation');
      return;
    }

    const request: LiveCancellationRequest = {
      migrationId,
      scope: CancellationScope.Migration,
      reason: reason.trim()
    };

    try {
      await onConfirm(request);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to cancel migration');
    }
  };

  // Simplified: Only migration scope, so fixed values
  const getScopeDescription = (): string => {
    return 'Cancels the entire migration including all entities, batches, and stores';
  };

  const getAffectedComponents = (): string[] => {
    return ['All entities', 'All batches', 'All stores', 'All orchestrators'];
  };

  const handleClose = () => {
    if (!isCancelling) {
      setReason('User requested migration cancellation');
      setError('');
      onClose();
    }
  };

  return (
    <Dialog 
      open={open} 
      onClose={handleClose}
      maxWidth="md" 
      fullWidth
      disableEscapeKeyDown={isCancelling}
    >
      <DialogTitle sx={{ 
        backgroundColor: 'error.main', 
        color: 'white',
        display: 'flex',
        alignItems: 'center',
        gap: 1
      }}>
        🛑 Cancel Migration
      </DialogTitle>
      
      <DialogContent sx={{ pt: 3 }}>
        <Typography variant="body2" color="text.secondary" gutterBottom>
          <strong>Migration ID:</strong> {migrationId}
        </Typography>

        {error && (
          <Alert severity="error" sx={{ mb: 2 }}>
            {error}
          </Alert>
        )}

        <Box sx={{ display: 'flex', flexDirection: 'column', gap: 3 }}>
          {/* Scope Description - Simplified for Migration only */}
          <Alert severity="warning" sx={{ backgroundColor: 'warning.lighter' }}>
            <Typography variant="body2">
              <strong>This will cancel the entire migration</strong> including all entities, batches, and stores.
              This action cannot be undone.
            </Typography>
          </Alert>

          {/* Cancellation Reason */}
          <TextField
            fullWidth
            label="Cancellation Reason"
            value={reason}
            onChange={(e) => setReason(e.target.value)}
            disabled={isCancelling}
            multiline
            rows={2}
            placeholder="Provide a reason for cancellation (for audit trail)"
            helperText="This reason will be logged for tracking and debugging purposes"
          />

          {/* Affected Components - Simplified */}
          <Box>
            <Typography variant="subtitle2" gutterBottom>
              This will stop:
            </Typography>
            <Box sx={{ display: 'flex', flexWrap: 'wrap', gap: 1 }}>
              {getAffectedComponents().map((component) => (
                <Chip 
                  key={component} 
                  label={component} 
                  size="small" 
                  variant="outlined"
                  color="warning"
                />
              ))}
            </Box>
          </Box>
        </Box>
      </DialogContent>
      
      <DialogActions sx={{ p: 3, pt: 2 }}>
        <Button 
          onClick={handleClose} 
          disabled={isCancelling}
          variant="outlined"
        >
          Cancel
        </Button>
        <Button 
          onClick={handleConfirm} 
          color="error" 
          variant="contained"
          disabled={isCancelling}
          startIcon={isCancelling ? <CircularProgress size={16} /> : null}
        >
          {isCancelling ? 'Cancelling...' : 'Cancel Migration'}
        </Button>
      </DialogActions>
    </Dialog>
  );
};