import { useEffect, useState } from 'react';
import { BrowserRouter as Router, Routes, Route, useNavigate } from 'react-router-dom';
import { 
  CssBaseline, 
  Box, 
  Typography, 
  Button, 
  Card, 
  CardContent, 
  TextField, 
} from '@mui/material';
import { DashboardLayout } from './components/Layout/DashboardLayout';
import { DashboardProvider } from './context/DashboardContext';
import { MigrationOverview } from './components/Dashboard/MigrationOverview';
import { MigrationStartForm } from './components/Views/MigrationStartForm';
import { HistoryView } from './components/Views/HistoryView';
import { MigrationDetailView } from './components/Views/MigrationDetailView';
import NotificationToastContainer from './components/Notifications/NotificationToastContainer';
import { ThemeContextProvider } from './contexts/ThemeContext';

// New page components for the redesigned interface
const HomePage = () => (
  <MigrationOverview />
);

const SelectiveMigrationPage = () => (
  <div style={{ padding: '20px' }}>
    <h2>Selective Content Migration</h2>
    <p>This page will allow users to select specific items for migration.</p>
    <p>Coming soon...</p>
  </div>
);

const HistoryPage = () => <HistoryView />;

const MigrationDetailPage = () => <MigrationDetailView />;

const SettingsPage = () => (
  <div style={{ padding: '20px' }}>
    <h2>Settings</h2>
    <p>System configuration and settings.</p>
    <p>Coming soon...</p>
  </div>
);

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
              <Route path="/start" element={<MigrationStartForm />} />
              <Route path="/selective" element={<SelectiveMigrationPage />} />
              <Route path="/history" element={<HistoryPage />} />
              <Route path="/history/:requestId" element={<MigrationDetailPage />} />
              <Route path="/settings" element={<SettingsPage />} />
            </Routes>
          </DashboardLayout>
        </Router>
      </DashboardProvider>
      <NotificationToastContainer />
    </ThemeContextProvider>
  );
}

export default App;
