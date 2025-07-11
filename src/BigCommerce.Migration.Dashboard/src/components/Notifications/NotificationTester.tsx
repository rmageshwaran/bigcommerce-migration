import React from 'react';
import {
  Card,
  CardContent,
  Typography,
  Button,
  Stack,
  Box,
  Chip,
  Divider
} from '@mui/material';
import {
  Notifications as NotificationsIcon,
  PlayArrow as PlayIcon
} from '@mui/icons-material';
import { notificationService } from '../../services/notificationService';
import { useThemeMode } from '../../contexts/ThemeContext';

const NotificationTester: React.FC = () => {
  const { mode, actualMode, isSystemMode } = useThemeMode();
  
  const testNotifications = {
    success: () => {
      notificationService.success('Migration Started', 'Products migration has started successfully');
    },
    error: () => {
      notificationService.error('Migration Failed', 'Connection to BigCommerce API failed');
    },
    warning: () => {
      notificationService.warning('Rate Limit Warning', 'Approaching API rate limits');
    },
    info: () => {
      notificationService.info('System Update', 'Dashboard updated to version 2.1.0');
    },
    migrationComplete: () => {
      notificationService.migrationCompleted('mig_12345', 'Products Migration', '2h 15m');
    },
    migrationFailed: () => {
      notificationService.migrationFailed('mig_12346', 'Categories Migration', 'Invalid API credentials');
    },
    systemAlert: () => {
      notificationService.systemAlert('System Maintenance', 'Scheduled maintenance in 15 minutes');
    },
    batchComplete: () => {
      notificationService.batchCompleted('mig_12347', 'Products', 150, 500);
    }
  };

  const handleToggleSound = () => {
    const currentState = notificationService.isSoundEnabled();
    notificationService.setSoundEnabled(!currentState);
  };

  const handleClearAll = () => {
    notificationService.clearAllNotifications();
  };

  const handleMarkAllRead = () => {
    notificationService.markAllAsRead();
  };

  return (
    <Card sx={{ maxWidth: 600, margin: 'auto', mt: 2 }}>
      <CardContent>
        <Box sx={{ display: 'flex', alignItems: 'center', mb: 2 }}>
          <NotificationsIcon sx={{ mr: 1 }} />
          <Typography variant="h6">
            Notification System Tester
          </Typography>
          <Chip 
            label="Development Tool" 
            color="warning" 
            size="small" 
            sx={{ ml: 2 }}
          />
        </Box>
        
        <Typography variant="body2" color="text.secondary" sx={{ mb: 3 }}>
          Test different notification types to see how they appear in the notification center and as toast messages.
        </Typography>

        <Stack spacing={2}>
          <Box>
            <Typography variant="subtitle2" sx={{ mb: 1 }}>
              Basic Notifications
            </Typography>
            <Stack direction="row" spacing={1} flexWrap="wrap">
              <Button
                variant="outlined"
                color="success"
                onClick={testNotifications.success}
                startIcon={<PlayIcon />}
                size="small"
              >
                Success
              </Button>
              <Button
                variant="outlined"
                color="error"
                onClick={testNotifications.error}
                startIcon={<PlayIcon />}
                size="small"
              >
                Error
              </Button>
              <Button
                variant="outlined"
                color="warning"
                onClick={testNotifications.warning}
                startIcon={<PlayIcon />}
                size="small"
              >
                Warning
              </Button>
              <Button
                variant="outlined"
                color="info"
                onClick={testNotifications.info}
                startIcon={<PlayIcon />}
                size="small"
              >
                Info
              </Button>
            </Stack>
          </Box>

          <Divider />

          <Box>
            <Typography variant="subtitle2" sx={{ mb: 1 }}>
              Migration-Specific Notifications
            </Typography>
            <Stack direction="row" spacing={1} flexWrap="wrap">
              <Button
                variant="outlined"
                color="success"
                onClick={testNotifications.migrationComplete}
                startIcon={<PlayIcon />}
                size="small"
              >
                Migration Complete
              </Button>
              <Button
                variant="outlined"
                color="error"
                onClick={testNotifications.migrationFailed}
                startIcon={<PlayIcon />}
                size="small"
              >
                Migration Failed
              </Button>
              <Button
                variant="outlined"
                color="primary"
                onClick={testNotifications.batchComplete}
                startIcon={<PlayIcon />}
                size="small"
              >
                Batch Complete
              </Button>
            </Stack>
          </Box>

          <Divider />

          <Box>
            <Typography variant="subtitle2" sx={{ mb: 1 }}>
              System Notifications
            </Typography>
            <Stack direction="row" spacing={1} flexWrap="wrap">
              <Button
                variant="outlined"
                color="warning"
                onClick={testNotifications.systemAlert}
                startIcon={<PlayIcon />}
                size="small"
              >
                System Alert
              </Button>
            </Stack>
          </Box>

          <Divider />

          <Box>
            <Typography variant="subtitle2" sx={{ mb: 1 }}>
              Notification Management
            </Typography>
            <Stack direction="row" spacing={1} flexWrap="wrap">
              <Button
                variant="contained"
                color="primary"
                onClick={handleToggleSound}
                size="small"
              >
                Toggle Sound
              </Button>
              <Button
                variant="contained"
                color="secondary"
                onClick={handleMarkAllRead}
                size="small"
              >
                Mark All Read
              </Button>
              <Button
                variant="contained"
                color="error"
                onClick={handleClearAll}
                size="small"
              >
                Clear All
              </Button>
            </Stack>
          </Box>
        </Stack>

        <Box sx={{ mt: 3, p: 2, bgcolor: 'background.default', borderRadius: 1 }}>
          <Typography variant="body2" color="text.secondary">
            <strong>How to test:</strong>
            <br />
            1. Click any notification button to trigger a notification
            <br />
            2. Check the notification bell icon in the header for the unread count
            <br />
            3. Click the bell icon to open the notification center
            <br />
            4. Toast notifications will appear in the top-right corner
            <br />
            5. Sound notifications will play if enabled
            <br />
            6. Use the theme toggle in the header to test dark/light mode
          </Typography>
        </Box>

        <Box sx={{ mt: 2, p: 2, bgcolor: 'action.hover', borderRadius: 1 }}>
          <Typography variant="body2" color="text.secondary">
            <strong>Current Theme:</strong> {mode} mode
            {isSystemMode && ` (system detected: ${actualMode})`}
            <br />
            <strong>Active Mode:</strong> {actualMode}
          </Typography>
        </Box>
      </CardContent>
    </Card>
  );
};

export default NotificationTester; 