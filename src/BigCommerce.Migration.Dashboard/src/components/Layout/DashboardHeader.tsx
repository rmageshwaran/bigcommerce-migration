import React, { useState, useEffect } from 'react';
import {
  AppBar,
  Toolbar,
  Typography,
  IconButton,
  Box,
  Chip,
  Badge,
} from '@mui/material';
import {
  Menu as MenuIcon,
  Notifications as NotificationsIcon,
  Error as ErrorIcon,
  CheckCircle as CheckCircleIcon,
} from '@mui/icons-material';
import NotificationCenter from '../Notifications/NotificationCenter';
import { notificationService } from '../../services/notificationService';
import type { Notification } from '../../services/notificationService';
import ThemeToggle from './ThemeToggle';

interface DashboardHeaderProps {
  onMenuClick: () => void;
  drawerWidth: number;
}

export const DashboardHeader: React.FC<DashboardHeaderProps> = ({
  onMenuClick,
  drawerWidth,
}) => {
  // Mock status data - will be replaced with real data later
  const systemStatus = 'healthy' as const;
  const activeConnections = 0;
  
  // Notification center state
  const [notificationCenterOpen, setNotificationCenterOpen] = useState(false);
  const [notificationAnchorEl, setNotificationAnchorEl] = useState<HTMLElement | null>(null);
  const [unreadCount, setUnreadCount] = useState(0);
  const [notifications, setNotifications] = useState<Notification[]>([]);

  // Subscribe to notification updates
  useEffect(() => {
    const updateNotifications = (newNotifications: Notification[]) => {
      setNotifications(newNotifications);
      setUnreadCount(newNotifications.filter(n => !n.read).length);
    };

    notificationService.addListener(updateNotifications);
    setNotifications(notificationService.getNotifications());
    setUnreadCount(notificationService.getUnreadCount());

    return () => {
      notificationService.removeListener(updateNotifications);
    };
  }, []);

  const handleNotificationClick = (event: React.MouseEvent<HTMLElement>) => {
    setNotificationAnchorEl(event.currentTarget);
    setNotificationCenterOpen(true);
  };

  const handleNotificationClose = () => {
    setNotificationCenterOpen(false);
    setNotificationAnchorEl(null);
  };

  const getStatusChip = () => {
    if (systemStatus === 'healthy') {
      return (
        <Chip
          icon={<CheckCircleIcon />}
          label="System Healthy"
          color="success"
          size="small"
        />
      );
    } else if (systemStatus === 'warning') {
      return (
        <Chip
          icon={<ErrorIcon />}
          label="System Warning"
          color="warning"
          size="small"
        />
      );
    } else if (systemStatus === 'error') {
      return (
        <Chip
          icon={<ErrorIcon />}
          label="System Error"
          color="error"
          size="small"
        />
      );
    }
    return null;
  };

  return (
    <AppBar
      position="fixed"
      sx={{
        width: { md: `calc(100% - ${drawerWidth}px)` },
        ml: { md: `${drawerWidth}px` },
        bgcolor: 'primary.main',
      }}
    >
      <Toolbar>
        <IconButton
          color="inherit"
          aria-label="open drawer"
          edge="start"
          onClick={onMenuClick}
          sx={{ mr: 2, display: { md: 'none' } }}
        >
          <MenuIcon />
        </IconButton>
        
        <Typography variant="h6" component="div" sx={{ flexGrow: 1 }}>
          BigCommerce Migration Dashboard
        </Typography>
        
        <Box sx={{ display: 'flex', alignItems: 'center', gap: 2 }}>
          {getStatusChip()}
          
          <Typography variant="body2" color="inherit">
            Connections: {activeConnections}
          </Typography>
          
          <ThemeToggle />
          
          <IconButton color="inherit" onClick={handleNotificationClick}>
            <Badge badgeContent={unreadCount} color="error">
              <NotificationsIcon />
            </Badge>
          </IconButton>
          
          <NotificationCenter
            open={notificationCenterOpen}
            onClose={handleNotificationClose}
            anchorEl={notificationAnchorEl}
          />
        </Box>
      </Toolbar>
    </AppBar>
  );
}; 