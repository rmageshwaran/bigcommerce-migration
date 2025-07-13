import React, { useEffect } from 'react';
import { BrowserRouter as Router, Routes, Route } from 'react-router-dom';
import { CssBaseline } from '@mui/material';
import { DashboardLayout } from './components/Layout/DashboardLayout';
import { DashboardProvider } from './context/DashboardContext';
import { MigrationOverview } from './components/Dashboard/MigrationOverview';
import { MigrationStartForm } from './components/Views/MigrationStartForm';
import { PerformanceMetrics } from './components/Charts/PerformanceMetrics';
import { SystemHealthPanel } from './components/Charts/SystemHealthPanel';
import { QueueVisualization } from './components/Charts/QueueVisualization';
import { MigrationControlPanel } from './components/Controls/MigrationControlPanel';
import { DataFilterPanel } from './components/Controls/DataFilterPanel';
import NotificationToastContainer from './components/Notifications/NotificationToastContainer';
import NotificationTester from './components/Notifications/NotificationTester';
import { ThemeContextProvider } from './contexts/ThemeContext';
import ExportTester from './components/Export/ExportTester';

// Theme is now managed by ThemeContextProvider

// Temporary page components
const MigrationsPage = () => {
  const handleMigrationAction = (action: string, migrationId: string) => {
    console.log(`Migration action: ${action} for ${migrationId}`);
    // In real app, this would call the API
  };

  const handleFiltersChange = (filters: any) => {
    console.log('Filters changed:', filters);
    // In real app, this would filter the migration list
  };

  return (
    <div style={{ padding: '20px' }}>
      <DataFilterPanel 
        onFiltersChange={handleFiltersChange}
        compactMode={false}
      />
      <MigrationControlPanel 
        onMigrationAction={handleMigrationAction}
      />
    </div>
  );
};

const HealthPage = () => (
  <div style={{ padding: '20px' }}>
    <SystemHealthPanel showDetails={true} autoRefresh={true} />
  </div>
);

const QueuesPage = () => (
  <div style={{ padding: '20px' }}>
    <QueueVisualization height={600} showDetails={true} />
  </div>
);

const HistoryPage = () => (
  <div style={{ padding: '20px' }}>
    <h2>Migration History</h2>
    <p>This page will show completed migrations, historical data, and analytics.</p>
    <p>Coming soon in Phase 5...</p>
  </div>
);

const SettingsPage = () => (
  <div style={{ padding: '20px' }}>
    <h2>Settings & Testing</h2>
    <p>Test dashboard features and configure system settings.</p>
    <NotificationTester />
    <ExportTester />
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
          autoConnect: true // Enable auto-connect now that SignalR is properly implemented
        }}
      >
        <Router>
          <DashboardLayout>
            <Routes>
              <Route path="/" element={<MigrationOverview />} />
              <Route path="/start" element={<MigrationStartForm />} />
              <Route path="/migrations" element={<MigrationsPage />} />
              <Route path="/health" element={<HealthPage />} />
              <Route path="/queues" element={<QueuesPage />} />
              <Route path="/history" element={<HistoryPage />} />
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
