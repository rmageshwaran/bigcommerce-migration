import React, { useState, useEffect, useCallback } from 'react';
import {
  Box,
  Card,
  CardContent,
  Typography,
  TextField,
  Button,
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableRow,
  Chip,
  CircularProgress,
  Alert,
  IconButton,
  Tooltip,
  FormControl,
  InputLabel,
  Select,
  MenuItem,
  Pagination,
  Stack,
} from '@mui/material';
import {
  Search as SearchIcon,
  Refresh as RefreshIcon,
  FilterList as FilterListIcon,
  Clear as ClearIcon,
  Visibility as ViewIcon,
  Download as DownloadIcon,
} from '@mui/icons-material';
import { DatePicker } from '@mui/x-date-pickers/DatePicker';
import { LocalizationProvider } from '@mui/x-date-pickers/LocalizationProvider';
import { AdapterDateFns } from '@mui/x-date-pickers/AdapterDateFns';
import { format, subDays, startOfDay, endOfDay } from 'date-fns';
import { useNavigate } from 'react-router-dom';
import { apiService } from '../../services/apiService';
import type { MigrationStatus } from '../../types';

interface MigrationHistoryItem {
  migrationId: string;
  sourceStore: string;
  destinationStore: string;
  startedAt: string;
  completedAt: string;
  status: MigrationStatus;
  totalEntities: number;
  processedEntities: number;
  successfulEntities: number;
  failedEntities: number;
  percentageCompleted: number;
  entities: string[];
}

interface MigrationHistoryResponse {
  migrations: MigrationHistoryItem[];
  totalCount: number;
  pageSize: number;
  currentPage: number;
  totalPages: number;
  hasMorePages: boolean;
  message: string;
}

interface FilterState {
  startDate: Date | null;
  endDate: Date | null;
  requestId: string;
  status: MigrationStatus | '';
  sourceStore: string;
  destinationStore: string;
  entityType: string;
}

const initialFilters: FilterState = {
  startDate: subDays(new Date(), 7),
  endDate: new Date(),
  requestId: '',
  status: '',
  sourceStore: '',
  destinationStore: '',
  entityType: '',
};

const statusOptions: { value: MigrationStatus; label: string; color: 'default' | 'primary' | 'secondary' | 'error' | 'info' | 'success' | 'warning' }[] = [
  { value: 'pending', label: 'Pending', color: 'default' },
  { value: 'running', label: 'Running', color: 'info' },
  { value: 'completed', label: 'Completed', color: 'success' },
  { value: 'failed', label: 'Failed', color: 'error' },
  { value: 'cancelled', label: 'Cancelled', color: 'warning' },
];

const entityTypeOptions = [
  'All',
  'brands',
  'categories',
  'products',
  'customers',
  'orders',
  'variants',
  'images',
  'modifiers',
];

export const HistoryView: React.FC = () => {
  const navigate = useNavigate();
  const [filters, setFilters] = useState<FilterState>(initialFilters);
  const [migrations, setMigrations] = useState<MigrationHistoryItem[]>([]);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [pagination, setPagination] = useState({
    currentPage: 1,
    totalPages: 1,
    totalCount: 0,
    pageSize: 50,
  });

  const buildQueryParams = useCallback(() => {
    const params: Record<string, string> = {
      page: pagination.currentPage.toString(),
      pageSize: pagination.pageSize.toString(),
    };

    if (filters.startDate) {
      params.startDate = filters.startDate.toISOString();
    }
    if (filters.endDate) {
      params.endDate = endOfDay(filters.endDate).toISOString();
    }
    if (filters.requestId.trim()) {
      params.migrationId = filters.requestId.trim();
    }
    if (filters.status) {
      params.status = filters.status;
    }
    if (filters.sourceStore.trim()) {
      params.sourceStore = filters.sourceStore.trim();
    }
    if (filters.destinationStore.trim()) {
      params.destinationStore = filters.destinationStore.trim();
    }
    if (filters.entityType && filters.entityType !== 'All') {
      params.entityType = filters.entityType;
    }

    return params;
  }, [filters, pagination.currentPage, pagination.pageSize]);

  const fetchMigrationHistory = useCallback(async () => {
    setLoading(true);
    setError(null);

    try {
      const queryParams = buildQueryParams();
      const response = await apiService.getMigrationHistory(queryParams);
      
      setMigrations(response.data || []);
      setPagination(prev => ({
        ...prev,
        currentPage: response.page || 1,
        totalPages: response.totalPages || 1,
        totalCount: response.totalCount || 0,
      }));
    } catch (err) {
      console.error('Failed to fetch migration history:', err);
      setError(err instanceof Error ? err.message : 'Failed to fetch migration history');
      setMigrations([]);
    } finally {
      setLoading(false);
    }
  }, [buildQueryParams]);

  useEffect(() => {
    fetchMigrationHistory();
  }, [fetchMigrationHistory]);

  const handleFilterChange = (field: keyof FilterState, value: any) => {
    setFilters(prev => ({ ...prev, [field]: value }));
    setPagination(prev => ({ ...prev, currentPage: 1 })); // Reset to first page when filters change
  };

  const handleApplyFilters = () => {
    fetchMigrationHistory();
  };

  const handleResetFilters = () => {
    setFilters(initialFilters);
    setPagination(prev => ({ ...prev, currentPage: 1 }));
  };

  const handlePageChange = (event: React.ChangeEvent<unknown>, page: number) => {
    setPagination(prev => ({ ...prev, currentPage: page }));
  };

  const handleRefresh = () => {
    fetchMigrationHistory();
  };

  const handleViewDetails = (migrationId: string) => {
    navigate(`/history/${migrationId}`);
  };

  const handleExport = async (migrationId: string) => {
    try {
      // TODO: Implement export functionality
      console.log('Exporting migration:', migrationId);
    } catch (err) {
      console.error('Failed to export migration:', err);
    }
  };

  const getStatusColor = (status: MigrationStatus) => {
    const statusOption = statusOptions.find(option => option.value === status);
    return statusOption?.color || 'default';
  };

  const formatDate = (dateString: string) => {
    try {
      return format(new Date(dateString), 'MMM dd, yyyy HH:mm');
    } catch {
      return dateString;
    }
  };

  const formatSuccessRate = (successful: number, total: number) => {
    if (total === 0) return '0%';
    const rate = (successful / total) * 100;
    return `${rate.toFixed(1)}%`;
  };

  const hasActiveFilters = () => {
    return (
      filters.requestId.trim() !== '' ||
      filters.status !== '' ||
      filters.sourceStore.trim() !== '' ||
      filters.destinationStore.trim() !== '' ||
      filters.entityType !== '' ||
      filters.startDate !== initialFilters.startDate ||
      filters.endDate !== initialFilters.endDate
    );
  };

  return (
    <Box sx={{ p: 3 }}>
      {/* Error Alert */}
      {error && (
        <Alert severity="error" sx={{ mb: 3 }} onClose={() => setError(null)}>
          {error}
        </Alert>
      )}

      {/* Filters */}
      <Card sx={{ mb: 3 }}>
        <CardContent>
          <Box sx={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', mb: 2 }}>
            <Typography variant="h6" sx={{ display: 'flex', alignItems: 'center' }}>
              <FilterListIcon sx={{ mr: 1 }} />
              Filters & Search
            </Typography>
            
            <Box sx={{ display: 'flex', gap: 1 }}>
              <Tooltip title="Refresh">
                <IconButton onClick={handleRefresh} disabled={loading}>
                  <RefreshIcon />
                </IconButton>
              </Tooltip>
              
              {hasActiveFilters() && (
                <Button
                  size="small"
                  onClick={handleResetFilters}
                  startIcon={<ClearIcon />}
                >
                  Clear All
                </Button>
              )}
            </Box>
          </Box>

          <LocalizationProvider dateAdapter={AdapterDateFns}>
            <Box sx={{ display: 'flex', flexDirection: { xs: 'column', lg: 'row' }, gap: 2, mb: 2 }}>
              <DatePicker
                label="From Date"
                value={filters.startDate}
                onChange={(date) => handleFilterChange('startDate', date)}
                slotProps={{
                  textField: {
                    size: 'small',
                    fullWidth: true,
                  },
                }}
              />
              
              <DatePicker
                label="To Date"
                value={filters.endDate}
                onChange={(date) => handleFilterChange('endDate', date)}
                slotProps={{
                  textField: {
                    size: 'small',
                    fullWidth: true,
                  },
                }}
              />
              
              <TextField
                label="Request ID"
                size="small"
                value={filters.requestId}
                onChange={(e) => handleFilterChange('requestId', e.target.value)}
                placeholder="Enter Request ID"
                fullWidth
              />
              
              <FormControl size="small" fullWidth>
                <InputLabel>Status</InputLabel>
                <Select
                  value={filters.status}
                  onChange={(e) => handleFilterChange('status', e.target.value)}
                  label="Status"
                >
                  <MenuItem value="">All Statuses</MenuItem>
                  {statusOptions.map((option) => (
                    <MenuItem key={option.value} value={option.value}>
                      {option.label}
                    </MenuItem>
                  ))}
                </Select>
              </FormControl>
            </Box>

            <Box sx={{ display: 'flex', flexDirection: { xs: 'column', lg: 'row' }, gap: 2 }}>
              <TextField
                label="Source Store"
                size="small"
                value={filters.sourceStore}
                onChange={(e) => handleFilterChange('sourceStore', e.target.value)}
                placeholder="Enter source store ID"
                fullWidth
              />
              
              <TextField
                label="Destination Store"
                size="small"
                value={filters.destinationStore}
                onChange={(e) => handleFilterChange('destinationStore', e.target.value)}
                placeholder="Enter destination store ID"
                fullWidth
              />
              
              <FormControl size="small" fullWidth>
                <InputLabel>Entity Type</InputLabel>
                <Select
                  value={filters.entityType}
                  onChange={(e) => handleFilterChange('entityType', e.target.value)}
                  label="Entity Type"
                >
                  {entityTypeOptions.map((option) => (
                    <MenuItem key={option} value={option}>
                      {option}
                    </MenuItem>
                  ))}
                </Select>
              </FormControl>
              
              <Button
                variant="contained"
                onClick={handleApplyFilters}
                disabled={loading}
                startIcon={loading ? <CircularProgress size={16} /> : <SearchIcon />}
                sx={{ 
                  minWidth: 120,
                  alignSelf: 'flex-end',
                  height: 40, // Match the height of small input fields
                  whiteSpace: 'nowrap',
                  textTransform: 'none',
                  fontSize: '0.875rem',
                  fontWeight: 500,
                }}
              >
                {loading ? 'Searching...' : 'Apply Filter'}
              </Button>
            </Box>
          </LocalizationProvider>
        </CardContent>
      </Card>

      {/* Data Table */}
      <Card>
        <CardContent sx={{ p: 0 }}>
          {loading ? (
            <Box sx={{ display: 'flex', justifyContent: 'center', p: 4 }}>
              <CircularProgress />
            </Box>
          ) : migrations.length === 0 ? (
            <Box sx={{ display: 'flex', justifyContent: 'center', p: 4 }}>
              <Typography color="text.secondary">
                {error ? 'Failed to load migrations' : 'No migrations found'}
              </Typography>
            </Box>
          ) : (
            <>
              <Table>
                <TableHead>
                  <TableRow sx={{ backgroundColor: 'background.default' }}>
                    <TableCell sx={{ fontWeight: 600, py: 2 }}>Request ID</TableCell>
                    <TableCell sx={{ fontWeight: 600, py: 2 }}>Source Store</TableCell>
                    <TableCell sx={{ fontWeight: 600, py: 2 }}>Destination Store</TableCell>
                    <TableCell sx={{ fontWeight: 600, py: 2 }}>Started At</TableCell>
                    <TableCell sx={{ fontWeight: 600, py: 2 }}>Status</TableCell>
                    <TableCell sx={{ fontWeight: 600, py: 2 }}>Success</TableCell>
                    <TableCell sx={{ fontWeight: 600, py: 2 }}>Fail</TableCell>
                    <TableCell sx={{ fontWeight: 600, py: 2 }}>Total</TableCell>
                    <TableCell sx={{ fontWeight: 600, py: 2 }}>Success Rate</TableCell>
                    <TableCell sx={{ fontWeight: 600, py: 2 }}>Progress</TableCell>
                    <TableCell sx={{ fontWeight: 600, py: 2 }}>Actions</TableCell>
                  </TableRow>
                </TableHead>
                <TableBody>
                  {migrations.map((migration) => (
                    <TableRow 
                      key={migration.migrationId}
                      sx={{ 
                        '&:hover': { 
                          backgroundColor: 'action.hover',
                        },
                      }}
                    >
                      <TableCell>
                        <Typography
                          component="span"
                          sx={{
                            color: 'primary.main',
                            textDecoration: 'underline',
                            fontSize: '0.875rem',
                            cursor: 'pointer',
                            fontFamily: 'monospace',
                          }}
                          onClick={() => handleViewDetails(migration.migrationId)}
                        >
                          {migration.migrationId.substring(0, 8)}...
                        </Typography>
                      </TableCell>
                      <TableCell sx={{ fontSize: '0.875rem', fontFamily: 'monospace' }}>
                        {migration.sourceStore}
                      </TableCell>
                      <TableCell sx={{ fontSize: '0.875rem', fontFamily: 'monospace' }}>
                        {migration.destinationStore}
                      </TableCell>
                      <TableCell sx={{ fontSize: '0.875rem' }}>
                        {formatDate(migration.startedAt)}
                      </TableCell>
                      <TableCell>
                        <Chip
                          label={migration.status.charAt(0).toUpperCase() + migration.status.slice(1)}
                          color={getStatusColor(migration.status)}
                          size="small"
                          variant="outlined"
                        />
                      </TableCell>
                      <TableCell sx={{ fontSize: '0.875rem', color: 'success.main' }}>
                        {migration.successfulEntities}
                      </TableCell>
                      <TableCell sx={{ fontSize: '0.875rem', color: 'error.main' }}>
                        {migration.failedEntities}
                      </TableCell>
                      <TableCell sx={{ fontSize: '0.875rem' }}>
                        {migration.totalEntities}
                      </TableCell>
                      <TableCell sx={{ 
                        fontSize: '0.875rem',
                        color: migration.successfulEntities === migration.totalEntities ? 'success.main' : 
                               migration.failedEntities === migration.totalEntities ? 'error.main' : 'warning.main',
                        fontWeight: 500,
                      }}>
                        {formatSuccessRate(migration.successfulEntities, migration.totalEntities)}
                      </TableCell>
                      <TableCell sx={{ fontSize: '0.875rem' }}>
                        {migration.percentageCompleted.toFixed(1)}%
                      </TableCell>
                      <TableCell>
                        <Stack direction="row" spacing={1}>
                          <Tooltip title="View Details">
                            <IconButton
                              size="small"
                              onClick={() => handleViewDetails(migration.migrationId)}
                            >
                              <ViewIcon fontSize="small" />
                            </IconButton>
                          </Tooltip>
                          <Tooltip title="Export">
                            <IconButton
                              size="small"
                              onClick={() => handleExport(migration.migrationId)}
                            >
                              <DownloadIcon fontSize="small" />
                            </IconButton>
                          </Tooltip>
                        </Stack>
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
                  Showing {((pagination.currentPage - 1) * pagination.pageSize) + 1} to{' '}
                  {Math.min(pagination.currentPage * pagination.pageSize, pagination.totalCount)} of{' '}
                  {pagination.totalCount} entries
                </Typography>
                
                <Pagination
                  count={pagination.totalPages}
                  page={pagination.currentPage}
                  onChange={handlePageChange}
                  color="primary"
                  size="small"
                  showFirstButton
                  showLastButton
                />
              </Box>
            </>
          )}
        </CardContent>
      </Card>
    </Box>
  );
}; 