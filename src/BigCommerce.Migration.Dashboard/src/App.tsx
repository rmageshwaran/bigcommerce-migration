import React, { useEffect, useState } from 'react';
import { BrowserRouter as Router, Routes, Route, useNavigate, useSearchParams, Navigate } from 'react-router-dom';
import { 
  CssBaseline, 
  Box, 
  Typography, 
  Button, 
  Card, 
  CardContent, 
  TextField, 
  Stack,
} from '@mui/material';
import { DashboardLayout } from './components/Layout/DashboardLayout';
import { DashboardProvider } from './context/DashboardContext';
import { MigrationOverview } from './components/Dashboard/MigrationOverview';
import { MigrationStartForm } from './components/Views/MigrationStartForm';
import { HistoryView } from './components/Views/HistoryView';
import { MigrationDetailView } from './components/Views/MigrationDetailView';
import NotificationToastContainer from './components/Notifications/NotificationToastContainer';
import { ThemeContextProvider } from './contexts/ThemeContext';
import { SignalRIntegrationTest } from './components/Tests/SignalRIntegrationTest';
import { Phase5ComprehensiveTest } from './components/Tests/Phase5ComprehensiveTest';

// New page components for the redesigned interface
const HomePage = () => (
  <MigrationOverview />
);

const BulkMigrationPage = () => (
  <MigrationStartForm />
);

const SelectiveMigrationPage = () => (
  <Box p={3}>
    <Typography variant="h4" gutterBottom>
      Selective Content Migration
    </Typography>
    <Typography variant="body1">
      Select specific entities to migrate with real-time progress tracking.
    </Typography>
  </Box>
);

const HistoryPage = () => (
  <HistoryView />
);

const MigrationDetailPage = () => (
  <MigrationDetailView />
);

const SettingsPage = () => (
  <Box p={3}>
    <Typography variant="h4" gutterBottom>
      Settings
    </Typography>
    <Typography variant="body1">
      Configure migration settings and preferences.
    </Typography>
  </Box>
);

// Removed: Enhanced Migration Page - real-time progress is now only shown on main overview page

function App() {
  useEffect(() => {
    // Global error handler for browser extension errors
    const handleGlobalError = (event: ErrorEvent) => {
      // Suppress common browser extension errors that don't affect our app
      if (event.message && (
        event.message.includes('message channel closed') ||
        event.message.includes('Extension context invalidated') ||
        event.message.includes('Could not establish connection')
      )) {
        event.preventDefault();
        console.warn('Browser extension error suppressed:', event.message);
        return;
      }
      
      // Log other errors for debugging
      console.error('Global error:', event.error);
    };

    // Add global error listener
    window.addEventListener('error', handleGlobalError);

    // Cleanup
    return () => {
      window.removeEventListener('error', handleGlobalError);
    };
  }, []);

  return (
    <ThemeContextProvider>
      <CssBaseline />
      <DashboardProvider
        config={{
          signalRUrl: 'http://localhost:7071/api',
          apiBaseUrl: '/api',
          autoConnect: true
        }}
      >
        <Router>
          <DashboardLayout>
            <Routes>
              <Route path="/" element={<HomePage />} />
              <Route path="/start" element={<BulkMigrationPage />} />
              <Route path="/selective" element={<SelectiveMigrationPage />} />
              <Route path="/history" element={<HistoryPage />} />
              <Route path="/history/:requestId" element={<MigrationDetailPage />} />
              <Route path="/settings" element={<SettingsPage />} />
              <Route path="/test/signalr" element={<SignalRIntegrationTest />} />
              <Route path="/test/phase5" element={<Phase5ComprehensiveTest />} />
              {/* Removed: /enhanced and /realtime routes - real-time progress is now only on main page */}
            </Routes>
          </DashboardLayout>
        </Router>
      </DashboardProvider>
      <NotificationToastContainer />
    </ThemeContextProvider>
  );
}

export default App;
