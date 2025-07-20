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
  Tabs, 
  Tab,
  Table,
  TableHead,
  TableBody,
  TableRow,
  TableCell,
} from '@mui/material';
import { DashboardLayout } from './components/Layout/DashboardLayout';
import { DashboardProvider } from './context/DashboardContext';
import { MigrationOverview } from './components/Dashboard/MigrationOverview';
import { MigrationStartForm } from './components/Views/MigrationStartForm';
import { HistoryView } from './components/Views/HistoryView';
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

const MigrationDetailPage = () => {
  const [activeTab, setActiveTab] = useState('data-migrations-v2');

  const tabs = [
    { value: 'data-migrations', label: 'Data Migrations' },
    { value: 'data-migrations-v2', label: 'Data Migrations V2' },
    { value: 'code-deployment', label: 'Code Deployment' },
    { value: 'scheduled-migration', label: 'Scheduled Migration' },
  ];

  const failureDetails = [
    {
      entityId: '1',
      entityType: 'Customer',
      error: 'Error creating customers: email ravi@247commerce.co.uk already in use',
      processedAt: 'Thu, 10-Jul-2025',
    },
  ];

  return (
    <Box sx={{ p: 3 }}>
      {/* Tab Navigation */}
      <Tabs 
        value={activeTab} 
        onChange={(_, value) => setActiveTab(value)}
        sx={{
          mb: 3,
          '& .MuiTab-root': {
            textTransform: 'none',
            fontWeight: 500,
            fontSize: '0.875rem',
            color: 'text.secondary',
            '&.Mui-selected': {
              color: 'primary.main',
              fontWeight: 600,
            },
          },
          '& .MuiTabs-indicator': {
            height: '3px',
            backgroundColor: 'primary.main',
          },
        }}
      >
        {tabs.map(tab => (
          <Tab key={tab.value} value={tab.value} label={tab.label} />
        ))}
      </Tabs>

      {/* Back Link */}
      <Button
        variant="outlined"
        startIcon={<Typography>←</Typography>}
        onClick={() => window.history.back()}
        sx={{
          mb: 3,
          textTransform: 'none',
          fontWeight: 500,
          borderColor: 'divider',
        }}
      >
        Back to Migration History
      </Button>

      {/* Migration Detail Card */}
      <Card sx={{ mb: 3 }}>
        <CardContent sx={{ p: 3 }}>
          {/* Status and Entity */}
          <Box sx={{ display: 'flex', alignItems: 'center', gap: 2, mb: 3 }}>
            <Box
              sx={{
                backgroundColor: 'success.light',
                color: 'success.contrastText',
                px: 2,
                py: 0.5,
                borderRadius: 1,
                fontSize: '0.75rem',
                fontWeight: 600,
              }}
            >
              Completed
            </Box>
            <Typography variant="h5" sx={{ fontWeight: 600 }}>
              Customer
            </Typography>
            <Box sx={{ ml: 'auto', p: 1, border: '1px solid', borderColor: 'divider', borderRadius: 1 }}>
              <Typography variant="caption" color="text.secondary">
                3e4af3bd7f484b4b9ec7921a8ce08f05
              </Typography>
            </Box>
          </Box>

          {/* Migration Info */}
          <Box sx={{ display: 'flex', gap: 4, mb: 3, flexWrap: 'wrap' }}>
            <Box>
              <Typography variant="body2" color="text.secondary">
                <strong>Source:</strong> hhq4ls8eea
              </Typography>
            </Box>
            <Box>
              <Typography variant="body2" color="text.secondary">
                <strong>Destination:</strong> rvirff4ku
              </Typography>
            </Box>
            <Box>
              <Typography variant="body2" color="text.secondary">
                <strong>Created:</strong> Thu, 10-Jul-2025
              </Typography>
            </Box>
            <Box>
              <Typography variant="body2" color="text.secondary">
                <strong>Updated:</strong> Thu, 10-Jul-2025
              </Typography>
            </Box>
          </Box>

          {/* Migration Summary */}
          <Box sx={{ 
            backgroundColor: 'primary.50', 
            p: 2, 
            borderRadius: 1, 
            mb: 3,
            border: '1px solid',
            borderColor: 'primary.light',
          }}>
            <Typography variant="body2" color="primary.main" sx={{ mb: 2, fontWeight: 600 }}>
              Migration completed: 0 successful, 1 failed, 0 skipped
            </Typography>
          </Box>

          {/* Statistics */}
          <Box sx={{ display: 'flex', gap: 4, justifyContent: 'center', py: 3 }}>
            <Box sx={{ textAlign: 'center' }}>
              <Typography variant="h2" color="success.main" sx={{ fontWeight: 700, fontSize: '3rem' }}>
                0
              </Typography>
              <Typography variant="body2" color="text.secondary">
                Success
              </Typography>
            </Box>
            <Box sx={{ textAlign: 'center' }}>
              <Typography variant="h2" color="error.main" sx={{ fontWeight: 700, fontSize: '3rem' }}>
                0
              </Typography>
              <Typography variant="body2" color="text.secondary">
                Failed
              </Typography>
            </Box>
            <Box sx={{ textAlign: 'center' }}>
              <Typography variant="h2" color="primary.main" sx={{ fontWeight: 700, fontSize: '3rem' }}>
                0
              </Typography>
              <Typography variant="body2" color="text.secondary">
                Total
              </Typography>
            </Box>
            <Box sx={{ textAlign: 'center' }}>
              <Typography variant="h2" color="text.primary" sx={{ fontWeight: 700, fontSize: '3rem' }}>
                0%
              </Typography>
              <Typography variant="body2" color="text.secondary">
                Success Rate
              </Typography>
            </Box>
          </Box>
        </CardContent>
      </Card>

      {/* Failure Details */}
      <Card>
        <CardContent sx={{ p: 0 }}>
          <Box sx={{ p: 3, display: 'flex', alignItems: 'center', gap: 1, borderBottom: '1px solid', borderColor: 'divider' }}>
            <Box
              sx={{
                width: 20,
                height: 20,
                borderRadius: '50%',
                backgroundColor: 'error.main',
                display: 'flex',
                alignItems: 'center',
                justifyContent: 'center',
                color: 'white',
                fontSize: '0.75rem',
              }}
            >
              ⨯
            </Box>
            <Typography variant="h6" sx={{ fontWeight: 600, color: 'error.main' }}>
              Failure Details
            </Typography>
          </Box>

          <Table>
            <TableHead>
              <TableRow sx={{ backgroundColor: 'background.default' }}>
                <TableCell sx={{ fontWeight: 600, py: 2 }}>EntityId</TableCell>
                <TableCell sx={{ fontWeight: 600, py: 2 }}>EntityType</TableCell>
                <TableCell sx={{ fontWeight: 600, py: 2 }}>Error</TableCell>
                <TableCell sx={{ fontWeight: 600, py: 2 }}>ProcessedAt</TableCell>
              </TableRow>
            </TableHead>
            <TableBody>
              {failureDetails.map((failure, failureIndex) => (
                <TableRow key={failureIndex}>
                  <TableCell sx={{ fontSize: '0.875rem' }}>{failure.entityId}</TableCell>
                  <TableCell sx={{ fontSize: '0.875rem' }}>{failure.entityType}</TableCell>
                  <TableCell sx={{ fontSize: '0.875rem', maxWidth: '400px' }}>
                    <Typography variant="body2" sx={{ wordBreak: 'break-word' }}>
                      {failure.error}
                    </Typography>
                  </TableCell>
                  <TableCell sx={{ fontSize: '0.875rem' }}>{failure.processedAt}</TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        </CardContent>
      </Card>
    </Box>
  );
};

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
