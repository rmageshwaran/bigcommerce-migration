import React, { useState } from 'react';
import {
  Box,
  CssBaseline,
  AppBar,
  Toolbar,
  Typography,
  IconButton,
  Tabs,
  Tab,
  Switch,
  FormControlLabel,
  Button,
  Menu,
  MenuItem,
  ListItemIcon,
  ListItemText,
} from '@mui/material';
import {
  Help as HelpIcon,
  BugReport as DebugIcon,
} from '@mui/icons-material';
import { Outlet, useNavigate, useLocation } from 'react-router-dom';
import ThemeToggle from './ThemeToggle';

interface DashboardLayoutProps {
  children?: React.ReactNode;
}

interface NavigationTab {
  label: string;
  value: string;
  path: string;
}

const navigationTabs: NavigationTab[] = [
  { label: 'Home', value: 'home', path: '/' },
  { label: 'Bulk Content Migration', value: 'bulk', path: '/start' },
  { label: 'Selective Content Migration', value: 'selective', path: '/selective' },
  { label: 'History & Rollback', value: 'history', path: '/history' },
  { label: 'Settings', value: 'settings', path: '/settings' },
];

export const DashboardLayout: React.FC<DashboardLayoutProps> = ({ children }) => {
  const navigate = useNavigate();
  const location = useLocation();
  const [helpMenuAnchor, setHelpMenuAnchor] = useState<null | HTMLElement>(null);

  // Determine current tab based on location
  const getCurrentTab = () => {
    const currentTab = navigationTabs.find(tab => {
      if (tab.path === '/') {
        return location.pathname === '/';
      }
      return location.pathname.startsWith(tab.path);
    });
    return currentTab?.value || 'home';
  };

  const handleTabChange = (event: React.SyntheticEvent, newValue: string) => {
    const tab = navigationTabs.find(t => t.value === newValue);
    if (tab) {
      navigate(tab.path);
    }
  };

  const handleHelpMenuOpen = (event: React.MouseEvent<HTMLElement>) => {
    setHelpMenuAnchor(event.currentTarget);
  };

  const handleHelpMenuClose = () => {
    setHelpMenuAnchor(null);
  };

  const handleToggleDebug = () => {
    // Dispatch a custom event that the MigrationOverview can listen to
    window.dispatchEvent(new CustomEvent('toggleDebugInfo'));
    handleHelpMenuClose();
  };

  const handleSignalRTest = () => {
    navigate('/test/signalr');
    handleHelpMenuClose();
  };

  return (
    <Box sx={{ display: 'flex', flexDirection: 'column', minHeight: '100vh' }}>
      <CssBaseline />
      
      {/* Top Navigation */}
      <AppBar position="static" elevation={0}>
        <Toolbar sx={{ justifyContent: 'space-between', px: 3 }}>
          {/* Left side - Navigation Tabs */}
          <Box sx={{ display: 'flex', alignItems: 'center' }}>
            <Tabs
              value={getCurrentTab()}
              onChange={handleTabChange}
              textColor="inherit"
              sx={{
                '& .MuiTab-root': {
                  color: 'text.secondary',
                  '&.Mui-selected': {
                    color: 'primary.main',
                  },
                },
              }}
            >
              {navigationTabs.map((tab) => (
                <Tab
                  key={tab.value}
                  label={tab.label}
                  value={tab.value}
                  sx={{
                    textTransform: 'none',
                    fontWeight: 500,
                    fontSize: '0.875rem',
                    minWidth: 'auto',
                    px: 2,
                  }}
                />
              ))}
            </Tabs>
          </Box>

          {/* Right side - Controls */}
          <Box sx={{ display: 'flex', alignItems: 'center', gap: 2 }}>
            {/* Theme Toggle */}
            <ThemeToggle />

            {/* Help Icon */}
            <IconButton
              onClick={handleHelpMenuOpen}
              color="inherit"
              sx={{ 
                '&:hover': {
                  backgroundColor: 'action.hover'
                }
              }}
            >
              <HelpIcon />
            </IconButton>
            
            {/* Help Menu */}
            <Menu
              anchorEl={helpMenuAnchor}
              open={Boolean(helpMenuAnchor)}
              onClose={handleHelpMenuClose}
              anchorOrigin={{
                vertical: 'bottom',
                horizontal: 'right',
              }}
              transformOrigin={{
                vertical: 'top',
                horizontal: 'right',
              }}
            >
              <MenuItem onClick={handleToggleDebug}>
                <ListItemIcon>
                  <DebugIcon />
                </ListItemIcon>
                <ListItemText>
                  Toggle Debug Info
                </ListItemText>
              </MenuItem>
              <MenuItem onClick={handleSignalRTest}>
                <ListItemIcon>
                  <DebugIcon />
                </ListItemIcon>
                <ListItemText>
                  SignalR Test Page
                </ListItemText>
              </MenuItem>
            </Menu>
          </Box>
        </Toolbar>
      </AppBar>

      {/* Main Content */}
      <Box
        component="main"
        sx={{
          flexGrow: 1,
          bgcolor: 'background.default',
          pt: 3,
          width: '100%',
          height: '100%',
        }}
      >
        <Box sx={{ px: 3, width: '100%', height: '100%' }}>
          {children || <Outlet />}
        </Box>
      </Box>
    </Box>
  );
}; 