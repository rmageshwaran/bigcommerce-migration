import React, { useState, useEffect } from 'react';
import {
  Badge,
  Box,
  Card,
  CardContent,
  Chip,
  Divider,
  IconButton,
  List,
  ListItem,
  ListItemText,
  Menu,
  MenuItem,
  Typography,
  Button,
  Tooltip,
  Avatar,
  Stack,
  TextField,
  InputAdornment,
  Fade,
  Collapse
} from '@mui/material';
import {
  Notifications as NotificationsIcon,
  Clear as ClearIcon,
  Search as SearchIcon,
  CheckCircle as CheckCircleIcon,
  Error as ErrorIcon,
  Warning as WarningIcon,
  Info as InfoIcon,
  PlayArrow as PlayArrowIcon,
  Delete as DeleteIcon,
  DoneAll as DoneAllIcon,
  FilterList as FilterListIcon,
  Close as CloseIcon,
  VolumeUp as VolumeUpIcon,
  VolumeOff as VolumeOffIcon,
  Refresh as RefreshIcon
} from '@mui/icons-material';
import { notificationService, NotificationType, NotificationPriority } from '../../services/notificationService';
import type { Notification } from '../../services/notificationService';

interface NotificationCenterProps {
  open: boolean;
  onClose: () => void;
  anchorEl: HTMLElement | null;
}

const NotificationCenter: React.FC<NotificationCenterProps> = ({
  open,
  onClose,
  anchorEl
}) => {
  const [notifications, setNotifications] = useState<Notification[]>([]);
  const [filteredNotifications, setFilteredNotifications] = useState<Notification[]>([]);
  const [searchTerm, setSearchTerm] = useState('');
  const [filterType, setFilterType] = useState<'all' | 'unread' | 'migration' | 'system'>('all');
  const [filterPriority, setFilterPriority] = useState<'all' | NotificationPriority>('all');
  const [soundEnabled, setSoundEnabled] = useState(true);
  const [expandedNotifications, setExpandedNotifications] = useState<Set<string>>(new Set());

  useEffect(() => {
    const updateNotifications = (newNotifications: Notification[]) => {
      setNotifications(newNotifications);
    };

    notificationService.addListener(updateNotifications);
    setNotifications(notificationService.getNotifications());
    setSoundEnabled(notificationService.isSoundEnabled());

    return () => {
      notificationService.removeListener(updateNotifications);
    };
  }, []);

  useEffect(() => {
    let filtered = notifications;

    // Apply search filter
    if (searchTerm) {
      filtered = filtered.filter(notification =>
        notification.title.toLowerCase().includes(searchTerm.toLowerCase()) ||
        notification.message.toLowerCase().includes(searchTerm.toLowerCase())
      );
    }

    // Apply type filter
    if (filterType !== 'all') {
      switch (filterType) {
        case 'unread':
          filtered = filtered.filter(n => !n.read);
          break;
        case 'migration':
          filtered = filtered.filter(n => n.migrationId);
          break;
        case 'system':
          filtered = filtered.filter(n => n.type === NotificationType.SYSTEM_ALERT);
          break;
      }
    }

    // Apply priority filter
    if (filterPriority !== 'all') {
      filtered = filtered.filter(n => n.priority === filterPriority);
    }

    setFilteredNotifications(filtered);
  }, [notifications, searchTerm, filterType, filterPriority]);

  const handleMarkAsRead = (notificationId: string) => {
    notificationService.markAsRead(notificationId);
  };

  const handleMarkAllAsRead = () => {
    notificationService.markAllAsRead();
  };

  const handleDeleteNotification = (notificationId: string) => {
    notificationService.removeNotification(notificationId);
  };

  const handleClearAll = () => {
    notificationService.clearAllNotifications();
  };

  const handleToggleSound = () => {
    const newSoundEnabled = !soundEnabled;
    setSoundEnabled(newSoundEnabled);
    notificationService.setSoundEnabled(newSoundEnabled);
  };

  const handleToggleExpanded = (notificationId: string) => {
    const newExpanded = new Set(expandedNotifications);
    if (newExpanded.has(notificationId)) {
      newExpanded.delete(notificationId);
    } else {
      newExpanded.add(notificationId);
    }
    setExpandedNotifications(newExpanded);
  };

  const getNotificationIcon = (type: NotificationType) => {
    switch (type) {
      case NotificationType.SUCCESS:
      case NotificationType.MIGRATION_COMPLETE:
      case NotificationType.BATCH_COMPLETE:
        return <CheckCircleIcon color="success" />;
      case NotificationType.ERROR:
      case NotificationType.MIGRATION_FAILED:
        return <ErrorIcon color="error" />;
      case NotificationType.WARNING:
      case NotificationType.SYSTEM_ALERT:
        return <WarningIcon color="warning" />;
      default:
        return <InfoIcon color="info" />;
    }
  };

  const getNotificationColor = (type: NotificationType) => {
    switch (type) {
      case NotificationType.SUCCESS:
      case NotificationType.MIGRATION_COMPLETE:
      case NotificationType.BATCH_COMPLETE:
        return 'success';
      case NotificationType.ERROR:
      case NotificationType.MIGRATION_FAILED:
        return 'error';
      case NotificationType.WARNING:
      case NotificationType.SYSTEM_ALERT:
        return 'warning';
      default:
        return 'info';
    }
  };

  const getPriorityColor = (priority: NotificationPriority) => {
    switch (priority) {
      case NotificationPriority.CRITICAL:
        return 'error';
      case NotificationPriority.HIGH:
        return 'warning';
      case NotificationPriority.MEDIUM:
        return 'info';
      case NotificationPriority.LOW:
        return 'default';
      default:
        return 'default';
    }
  };

  const formatTimeAgo = (timestamp: Date) => {
    const now = new Date();
    const diff = now.getTime() - timestamp.getTime();
    const minutes = Math.floor(diff / 60000);
    const hours = Math.floor(minutes / 60);
    const days = Math.floor(hours / 24);

    if (days > 0) return `${days}d ago`;
    if (hours > 0) return `${hours}h ago`;
    if (minutes > 0) return `${minutes}m ago`;
    return 'Just now';
  };

  const unreadCount = notifications.filter(n => !n.read).length;

  return (
    <Menu
      anchorEl={anchorEl}
      open={open}
      onClose={onClose}
      PaperProps={{
        sx: {
          width: 480,
          maxWidth: '90vw',
          maxHeight: '70vh',
          overflow: 'hidden'
        }
      }}
      anchorOrigin={{
        vertical: 'bottom',
        horizontal: 'right'
      }}
      transformOrigin={{
        vertical: 'top',
        horizontal: 'right'
      }}
    >
      <Box sx={{ p: 2 }}>
        {/* Header */}
        <Box sx={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', mb: 2 }}>
          <Typography variant="h6" sx={{ display: 'flex', alignItems: 'center', gap: 1 }}>
            <NotificationsIcon />
            Notifications
            {unreadCount > 0 && (
              <Badge badgeContent={unreadCount} color="error" sx={{ ml: 1 }} />
            )}
          </Typography>
          <Box sx={{ display: 'flex', gap: 1 }}>
            <Tooltip title={soundEnabled ? 'Disable Sounds' : 'Enable Sounds'}>
              <IconButton size="small" onClick={handleToggleSound}>
                {soundEnabled ? <VolumeUpIcon /> : <VolumeOffIcon />}
              </IconButton>
            </Tooltip>
            <Tooltip title="Close">
              <IconButton size="small" onClick={onClose}>
                <CloseIcon />
              </IconButton>
            </Tooltip>
          </Box>
        </Box>

        {/* Search and Filters */}
        <Stack spacing={2} sx={{ mb: 2 }}>
          <TextField
            size="small"
            placeholder="Search notifications..."
            value={searchTerm}
            onChange={(e) => setSearchTerm(e.target.value)}
            InputProps={{
              startAdornment: (
                <InputAdornment position="start">
                  <SearchIcon />
                </InputAdornment>
              )
            }}
          />
          
          <Stack direction="row" spacing={1} sx={{ alignItems: 'center' }}>
            <Chip
              label="All"
              variant={filterType === 'all' ? 'filled' : 'outlined'}
              onClick={() => setFilterType('all')}
              size="small"
            />
            <Chip
              label="Unread"
              variant={filterType === 'unread' ? 'filled' : 'outlined'}
              onClick={() => setFilterType('unread')}
              size="small"
            />
            <Chip
              label="Migration"
              variant={filterType === 'migration' ? 'filled' : 'outlined'}
              onClick={() => setFilterType('migration')}
              size="small"
            />
            <Chip
              label="System"
              variant={filterType === 'system' ? 'filled' : 'outlined'}
              onClick={() => setFilterType('system')}
              size="small"
            />
          </Stack>
        </Stack>

        {/* Action Buttons */}
        <Stack direction="row" spacing={1} sx={{ mb: 2 }}>
          <Button
            size="small"
            startIcon={<DoneAllIcon />}
            onClick={handleMarkAllAsRead}
            disabled={unreadCount === 0}
          >
            Mark All Read
          </Button>
          <Button
            size="small"
            startIcon={<DeleteIcon />}
            onClick={handleClearAll}
            disabled={notifications.length === 0}
          >
            Clear All
          </Button>
        </Stack>

        <Divider />
      </Box>

      {/* Notifications List */}
      <Box sx={{ maxHeight: '40vh', overflow: 'auto' }}>
        {filteredNotifications.length === 0 ? (
          <Box sx={{ p: 3, textAlign: 'center' }}>
            <Typography variant="body2" color="text.secondary">
              {notifications.length === 0 ? 'No notifications yet' : 'No notifications match your filters'}
            </Typography>
          </Box>
        ) : (
          <List sx={{ p: 0 }}>
            {filteredNotifications.map((notification, index) => (
              <React.Fragment key={notification.id}>
                <ListItem
                  sx={{
                    flexDirection: 'column',
                    alignItems: 'stretch',
                    py: 1.5,
                    px: 2,
                    backgroundColor: notification.read ? 'transparent' : 'action.hover',
                    '&:hover': {
                      backgroundColor: 'action.selected'
                    }
                  }}
                >
                  <Box sx={{ display: 'flex', alignItems: 'flex-start', width: '100%' }}>
                    <Avatar
                      sx={{
                        width: 32,
                        height: 32,
                        mr: 1.5,
                        backgroundColor: `${getNotificationColor(notification.type)}.light`
                      }}
                    >
                      {getNotificationIcon(notification.type)}
                    </Avatar>
                    
                    <Box sx={{ flexGrow: 1, minWidth: 0 }}>
                      <Box sx={{ display: 'flex', alignItems: 'flex-start', justifyContent: 'space-between' }}>
                        <Box sx={{ flexGrow: 1 }}>
                          <Typography
                            variant="subtitle2"
                            sx={{
                              fontWeight: notification.read ? 'normal' : 'bold',
                              lineHeight: 1.3
                            }}
                          >
                            {notification.title}
                          </Typography>
                          <Typography
                            variant="body2"
                            color="text.secondary"
                            sx={{ 
                              mt: 0.5,
                              display: '-webkit-box',
                              WebkitLineClamp: expandedNotifications.has(notification.id) ? 'none' : 2,
                              WebkitBoxOrient: 'vertical',
                              overflow: 'hidden'
                            }}
                          >
                            {notification.message}
                          </Typography>
                        </Box>
                        
                        <Box sx={{ display: 'flex', flexDirection: 'column', alignItems: 'flex-end', ml: 1 }}>
                          <Typography variant="caption" color="text.secondary">
                            {formatTimeAgo(notification.timestamp)}
                          </Typography>
                          <Chip
                            label={notification.priority}
                            size="small"
                            color={getPriorityColor(notification.priority)}
                            sx={{ mt: 0.5 }}
                          />
                        </Box>
                      </Box>

                      {/* Actions */}
                      <Box sx={{ display: 'flex', alignItems: 'center', gap: 1, mt: 1 }}>
                        {!notification.read && (
                          <Button
                            size="small"
                            onClick={() => handleMarkAsRead(notification.id)}
                            sx={{ minWidth: 'auto', p: 0.5 }}
                          >
                            Mark Read
                          </Button>
                        )}
                        
                        {notification.message.length > 100 && (
                          <Button
                            size="small"
                            onClick={() => handleToggleExpanded(notification.id)}
                            sx={{ minWidth: 'auto', p: 0.5 }}
                          >
                            {expandedNotifications.has(notification.id) ? 'Show Less' : 'Show More'}
                          </Button>
                        )}
                        
                        <Button
                          size="small"
                          onClick={() => handleDeleteNotification(notification.id)}
                          sx={{ minWidth: 'auto', p: 0.5 }}
                        >
                          Delete
                        </Button>
                      </Box>

                      {/* Notification Actions */}
                      {notification.actions && notification.actions.length > 0 && (
                        <Box sx={{ mt: 1 }}>
                          <Stack direction="row" spacing={1}>
                            {notification.actions.map((action, actionIndex) => (
                              <Button
                                key={actionIndex}
                                size="small"
                                variant={action.style === 'primary' ? 'contained' : 'outlined'}
                                color={action.style === 'danger' ? 'error' : 'primary'}
                                onClick={action.action}
                              >
                                {action.label}
                              </Button>
                            ))}
                          </Stack>
                        </Box>
                      )}
                    </Box>
                  </Box>
                </ListItem>
                {index < filteredNotifications.length - 1 && <Divider />}
              </React.Fragment>
            ))}
          </List>
        )}
      </Box>
    </Menu>
  );
};

export default NotificationCenter; 