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
  Container,
} from '@mui/material';
import {
  Help as HelpIcon,
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
  const [dataMigrationV2, setDataMigrationV2] = useState(true);

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

  const handleHelpClick = () => {
    console.log('Help clicked');
    // TODO: Implement help functionality
  };

  const handleDataMigrationToggle = (event: React.ChangeEvent<HTMLInputElement>) => {
    setDataMigrationV2(event.target.checked);
    // TODO: Implement version toggle functionality
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
            {/* Data Migration V2 Toggle */}
            <FormControlLabel
              control={
                <Switch
                  checked={dataMigrationV2}
                  onChange={handleDataMigrationToggle}
                  size="small"
                  color="primary"
                />
              }
              label={
                <Typography variant="body2" sx={{ fontWeight: 500 }}>
                  Data Migration V2
                </Typography>
              }
              labelPlacement="start"
              sx={{
                margin: 0,
                '& .MuiFormControlLabel-label': {
                  color: 'text.secondary',
                },
              }}
            />

            {/* Theme Toggle */}
            <ThemeToggle />

            {/* Help Button */}
            <Button
              variant="outlined"
              startIcon={<HelpIcon />}
              onClick={handleHelpClick}
              size="small"
              sx={{
                textTransform: 'none',
                fontWeight: 500,
                borderRadius: '6px',
                px: 2,
              }}
            >
              Help
            </Button>
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
        }}
      >
        <Container maxWidth="xl" sx={{ px: 3 }}>
          {children || <Outlet />}
        </Container>
      </Box>
    </Box>
  );
}; 