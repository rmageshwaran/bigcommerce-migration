import React from 'react';
import {
  Box,
  List,
  ListItem,
  ListItemButton,
  ListItemIcon,
  ListItemText,
  Toolbar,
  Typography,
  Divider,
} from '@mui/material';
import {
  Dashboard as DashboardIcon,
  Assessment as AssessmentIcon,
  Queue as QueueIcon,
  Settings as SettingsIcon,
  History as HistoryIcon,
  MonitorHeart as MonitorIcon,
  PlayArrow as PlayArrowIcon,
} from '@mui/icons-material';
import { useNavigate, useLocation } from 'react-router-dom';

interface DashboardSidebarProps {
  onItemClick?: () => void;
}

interface NavigationItem {
  text: string;
  icon: React.ReactNode;
  path: string;
}

const navigationItems: NavigationItem[] = [
  { text: 'Overview', icon: <DashboardIcon />, path: '/' },
  { text: 'Start Migration', icon: <PlayArrowIcon />, path: '/start' },
  { text: 'Active Migrations', icon: <AssessmentIcon />, path: '/migrations' },
  { text: 'System Health', icon: <MonitorIcon />, path: '/health' },
  { text: 'Queue Status', icon: <QueueIcon />, path: '/queues' },
  { text: 'Migration History', icon: <HistoryIcon />, path: '/history' },
  { text: 'Settings', icon: <SettingsIcon />, path: '/settings' },
];

export const DashboardSidebar: React.FC<DashboardSidebarProps> = ({ onItemClick }) => {
  const navigate = useNavigate();
  const location = useLocation();

  const handleItemClick = (path: string) => {
    navigate(path);
    if (onItemClick) {
      onItemClick();
    }
  };

  return (
    <Box sx={{ height: '100%', bgcolor: 'background.paper' }}>
      <Toolbar>
        <Typography variant="h6" noWrap component="div">
          BigCommerce
        </Typography>
      </Toolbar>
      <Divider />
      
      <List>
        {navigationItems.map((item) => (
          <ListItem key={item.text} disablePadding>
            <ListItemButton
              selected={location.pathname === item.path}
              onClick={() => handleItemClick(item.path)}
              sx={{
                '&.Mui-selected': {
                  bgcolor: 'primary.light',
                  color: 'primary.contrastText',
                  '&:hover': {
                    bgcolor: 'primary.main',
                  },
                },
              }}
            >
              <ListItemIcon
                sx={{
                  color: location.pathname === item.path ? 'primary.contrastText' : 'inherit',
                }}
              >
                {item.icon}
              </ListItemIcon>
              <ListItemText primary={item.text} />
            </ListItemButton>
          </ListItem>
        ))}
      </List>
    </Box>
  );
}; 