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

const HistoryPage = () => {
  const [activeTab, setActiveTab] = useState('data-migrations-v2');
  const [fromDate, setFromDate] = useState('Thu, 10-Jul-2025');
  const [toDate, setToDate] = useState('Thu, 17-Jul-2025');
  const [requestId, setRequestId] = useState('');
  const navigate = useNavigate();

  const mockMigrations = [
    {
      requestId: 'b09ec106e141445eb10ade399a246d8e',
      sourceEnvironment: 'Production',
      destinationEnvironment: 'Staging',
      entityType: 'Customers',
      success: 0,
      fail: 1,
      skipped: 0,
      total: 1,
      successRate: '0%',
    },
    {
      requestId: '427d5298d6cb4626af6126712174d814',
      sourceEnvironment: 'Staging',
      destinationEnvironment: 'Production',
      entityType: 'Customers',
      success: 1,
      fail: 0,
      skipped: 0,
      total: 1,
      successRate: '100%',
    },
    {
      requestId: '194333f0866d4fe8bf72ddff6c9fa2bd',
      sourceEnvironment: 'Production',
      destinationEnvironment: 'Staging',
      entityType: 'Customers',
      success: 0,
      fail: 0,
      skipped: 0,
      total: 0,
      successRate: '0%',
    },
    {
      requestId: '0b112c17f4ad4872b2fe1c345d726101',
      sourceEnvironment: 'Production',
      destinationEnvironment: 'Staging',
      entityType: 'Customers',
      success: 0,
      fail: 0,
      skipped: 0,
      total: 0,
      successRate: '0%',
    },
    {
      requestId: '3e4af3bd7f484b4b9ec7921a8ce08f05',
      sourceEnvironment: 'Staging',
      destinationEnvironment: 'Production',
      entityType: 'Customer',
      success: 0,
      fail: 1,
      skipped: 0,
      total: 1,
      successRate: '0%',
    },
    {
      requestId: '0e15477dcf034412bc46298789d97bfe',
      sourceEnvironment: 'Staging',
      destinationEnvironment: 'Production',
      entityType: 'CustomerGroup',
      success: 1,
      fail: 1,
      skipped: 0,
      total: 2,
      successRate: '50%',
    },
    {
      requestId: '316bc5d3f8f740caa50b7c996df85db6',
      sourceEnvironment: 'Staging',
      destinationEnvironment: 'Production',
      entityType: 'Customer',
      success: 0,
      fail: 1,
      skipped: 0,
      total: 1,
      successRate: '0%',
    },
    {
      requestId: 'c861b1576b6f4f538bb326e26dfae11d',
      sourceEnvironment: 'Staging',
      destinationEnvironment: 'Production',
      entityType: 'Customer',
      success: 1,
      fail: 0,
      skipped: 0,
      total: 1,
      successRate: '100%',
    },
  ];

  const tabs = [
    { value: 'data-migrations', label: 'Data Migrations' },
    { value: 'data-migrations-v2', label: 'Data Migrations V2' },
    { value: 'code-deployment', label: 'Code Deployment' },
    { value: 'scheduled-migration', label: 'Scheduled Migration' },
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

      {/* Filters */}
      <Card sx={{ mb: 3 }}>
        <CardContent>
          <Box sx={{ display: 'flex', flexDirection: { xs: 'column', md: 'row' }, gap: 2 }}>
            <Box sx={{ flex: 1 }}>
              <Typography variant="subtitle2" gutterBottom sx={{ fontWeight: 600 }}>
                From
              </Typography>
              <TextField
                fullWidth
                size="small"
                value={fromDate}
                onChange={(e) => setFromDate(e.target.value)}
                placeholder="Thu, 10-Jul-2025"
              />
            </Box>
            <Box sx={{ flex: 1 }}>
              <Typography variant="subtitle2" gutterBottom sx={{ fontWeight: 600 }}>
                To
              </Typography>
              <TextField
                fullWidth
                size="small"
                value={toDate}
                onChange={(e) => setToDate(e.target.value)}
                placeholder="Thu, 17-Jul-2025"
              />
            </Box>
            <Box sx={{ flex: 1 }}>
              <Typography variant="subtitle2" gutterBottom sx={{ fontWeight: 600 }}>
                Request ID
              </Typography>
              <TextField
                fullWidth
                size="small"
                value={requestId}
                onChange={(e) => setRequestId(e.target.value)}
                placeholder="Enter Request ID"
              />
            </Box>
            <Box sx={{ flex: 1 }}>
              <Button
                variant="contained"
                fullWidth
                sx={{
                  mt: { xs: 0, md: 3 },
                  textTransform: 'none',
                  fontWeight: 600,
                }}
              >
                Apply Filter
              </Button>
            </Box>
            <Box sx={{ flex: 1 }}>
              <Button
                variant="outlined"
                fullWidth
                sx={{
                  mt: { xs: 0, md: 3 },
                  textTransform: 'none',
                  fontWeight: 500,
                }}
              >
                Reset
              </Button>
            </Box>
          </Box>
        </CardContent>
      </Card>

      {/* Data Table */}
      <Card>
        <CardContent sx={{ p: 0 }}>
          <Table>
            <TableHead>
              <TableRow sx={{ backgroundColor: 'background.default' }}>
                <TableCell sx={{ fontWeight: 600, py: 2 }}>Request ID</TableCell>
                <TableCell sx={{ fontWeight: 600, py: 2 }}>Source Environment</TableCell>
                <TableCell sx={{ fontWeight: 600, py: 2 }}>Destination Environment</TableCell>
                <TableCell sx={{ fontWeight: 600, py: 2 }}>Entity Type</TableCell>
                <TableCell sx={{ fontWeight: 600, py: 2 }}>Success</TableCell>
                <TableCell sx={{ fontWeight: 600, py: 2 }}>Fail</TableCell>
                <TableCell sx={{ fontWeight: 600, py: 2 }}>Skipped</TableCell>
                <TableCell sx={{ fontWeight: 600, py: 2 }}>Total</TableCell>
                <TableCell sx={{ fontWeight: 600, py: 2 }}>Success Rate</TableCell>
              </TableRow>
            </TableHead>
            <TableBody>
              {mockMigrations.map((migration) => (
                <TableRow 
                  key={migration.requestId}
                  sx={{ 
                    '&:hover': { 
                      backgroundColor: 'action.hover',
                    },
                    cursor: 'pointer',
                  }}
                  onClick={() => navigate(`/history/${migration.requestId}`)}
                >
                  <TableCell>
                    <Typography
                      component="span"
                      sx={{
                        color: 'primary.main',
                        textDecoration: 'underline',
                        fontSize: '0.875rem',
                        cursor: 'pointer',
                      }}
                    >
                      {migration.requestId}
                    </Typography>
                  </TableCell>
                  <TableCell sx={{ fontSize: '0.875rem' }}>{migration.sourceEnvironment}</TableCell>
                  <TableCell sx={{ fontSize: '0.875rem' }}>{migration.destinationEnvironment}</TableCell>
                  <TableCell sx={{ fontSize: '0.875rem' }}>{migration.entityType}</TableCell>
                  <TableCell sx={{ fontSize: '0.875rem', color: migration.success > 0 ? 'success.main' : 'text.primary' }}>
                    {migration.success}
                  </TableCell>
                  <TableCell sx={{ fontSize: '0.875rem', color: migration.fail > 0 ? 'error.main' : 'text.primary' }}>
                    {migration.fail}
                  </TableCell>
                  <TableCell sx={{ fontSize: '0.875rem', color: migration.skipped > 0 ? 'warning.main' : 'text.primary' }}>
                    {migration.skipped}
                  </TableCell>
                  <TableCell sx={{ fontSize: '0.875rem' }}>{migration.total}</TableCell>
                  <TableCell sx={{ 
                    fontSize: '0.875rem',
                    color: migration.successRate === '100%' ? 'success.main' : 
                           migration.successRate === '0%' ? 'error.main' : 'warning.main',
                    fontWeight: 500,
                  }}>
                    {migration.successRate}
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>

          {/* Pagination */}
          <Box sx={{ 
            display: 'flex', 
            justifyContent: 'space-between', 
            alignItems: 'center',
            p: 2,
            borderTop: '1px solid',
            borderColor: 'divider',
          }}>
            <Typography variant="body2" color="text.secondary">
              Showing 1 to 8 of 8 entries
            </Typography>
            <Box sx={{ display: 'flex', alignItems: 'center', gap: 1 }}>
              <Button variant="outlined" size="small" disabled>
                Previous
              </Button>
              <Button variant="contained" size="small" sx={{ minWidth: '32px' }}>
                1
              </Button>
              <Button variant="outlined" size="small" disabled>
                Next
              </Button>
            </Box>
          </Box>
        </CardContent>
      </Card>
    </Box>
  );
};

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
