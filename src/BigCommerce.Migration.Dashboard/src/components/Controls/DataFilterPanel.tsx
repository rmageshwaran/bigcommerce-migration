import React, { useState, useEffect } from 'react';
import {
  Box,
  Card,
  CardContent,
  Typography,
  TextField,
  FormControl,
  InputLabel,
  Select,
  MenuItem,
  Chip,
  Button,
  IconButton,
  Accordion,
  AccordionSummary,
  AccordionDetails,
  Slider,
  Switch,
  FormControlLabel,
  Autocomplete,
  Divider,
  Badge,
  useTheme,
} from '@mui/material';
import { DatePicker } from '@mui/x-date-pickers/DatePicker';
import { LocalizationProvider } from '@mui/x-date-pickers/LocalizationProvider';
import {
  ExpandMore as ExpandMoreIcon,
  FilterList as FilterListIcon,
  Clear as ClearIcon,
  Search as SearchIcon,
  SavedSearch as SavedSearchIcon,
} from '@mui/icons-material';
import { AdapterDateFns } from '@mui/x-date-pickers/AdapterDateFns';
import { useDashboard } from '../../context/DashboardContext';
import type { MigrationStatus } from '../../types';

interface FilterState {
  search: string;
  status: MigrationStatus[];
  entityTypes: string[];
  dateRange: {
    start: Date | null;
    end: Date | null;
  };
  progressRange: [number, number];
  speedRange: [number, number];
  errorRateRange: [number, number];
  storeHashes: string[];
  showCompleted: boolean;
  showFailed: boolean;
  sortBy: 'name' | 'status' | 'progress' | 'speed' | 'created' | 'updated';
  sortOrder: 'asc' | 'desc';
}

interface DataFilterPanelProps {
  onFiltersChange?: (filters: FilterState) => void;
  onSearchChange?: (searchTerm: string) => void;
  compactMode?: boolean;
}

const initialFilters: FilterState = {
  search: '',
  status: [],
  entityTypes: [],
  dateRange: {
    start: null,
    end: null,
  },
  progressRange: [0, 100],
  speedRange: [0, 1000],
  errorRateRange: [0, 100],
  storeHashes: [],
  showCompleted: true,
  showFailed: true,
  sortBy: 'updated',
  sortOrder: 'desc',
};

const statusOptions: MigrationStatus[] = ['pending', 'running', 'completed', 'failed', 'cancelled'];
const entityTypeOptions = ['products', 'categories', 'brands', 'variants', 'images', 'modifiers'];
const sortByOptions = [
  { value: 'name', label: 'Name' },
  { value: 'status', label: 'Status' },
  { value: 'progress', label: 'Progress' },
  { value: 'speed', label: 'Speed' },
  { value: 'created', label: 'Created Date' },
  { value: 'updated', label: 'Last Updated' },
];

export const DataFilterPanel: React.FC<DataFilterPanelProps> = ({
  onFiltersChange,
  onSearchChange,
  compactMode = false,
}) => {
  const theme = useTheme();
  const { state } = useDashboard();
  const { activeMigrations } = state;
  const [filters, setFilters] = useState<FilterState>(initialFilters);
  const [expanded, setExpanded] = useState<string | false>('search');
  const [savedFilters, setSavedFilters] = useState<{ name: string; filters: FilterState }[]>([]);

  const migrationList = Array.from(activeMigrations.values());
  
  // Extract unique store hashes from migrations
  const availableStoreHashes = [...new Set(migrationList.map(m => m.migrationId.split('-')[0]))];

  const getActiveFilterCount = () => {
    let count = 0;
    if (filters.search) count++;
    if (filters.status.length > 0) count++;
    if (filters.entityTypes.length > 0) count++;
    if (filters.dateRange.start || filters.dateRange.end) count++;
    if (filters.progressRange[0] !== 0 || filters.progressRange[1] !== 100) count++;
    if (filters.speedRange[0] !== 0 || filters.speedRange[1] !== 1000) count++;
    if (filters.errorRateRange[0] !== 0 || filters.errorRateRange[1] !== 100) count++;
    if (filters.storeHashes.length > 0) count++;
    if (!filters.showCompleted || !filters.showFailed) count++;
    return count;
  };

  const handleFilterChange = (newFilters: Partial<FilterState>) => {
    const updatedFilters = { ...filters, ...newFilters };
    setFilters(updatedFilters);
    onFiltersChange?.(updatedFilters);
    
    if (newFilters.search !== undefined) {
      onSearchChange?.(newFilters.search);
    }
  };

  const handleClearFilters = () => {
    setFilters(initialFilters);
    onFiltersChange?.(initialFilters);
    onSearchChange?.('');
  };

  const handleSaveFilters = () => {
    const name = `Filter Set ${savedFilters.length + 1}`;
    setSavedFilters(prev => [...prev, { name, filters }]);
  };

  const handleLoadSavedFilter = (savedFilter: { name: string; filters: FilterState }) => {
    setFilters(savedFilter.filters);
    onFiltersChange?.(savedFilter.filters);
    onSearchChange?.(savedFilter.filters.search);
  };

  const handleAccordionChange = (panel: string) => (event: React.SyntheticEvent, isExpanded: boolean) => {
    setExpanded(isExpanded ? panel : false);
  };

  const activeFilterCount = getActiveFilterCount();

  if (compactMode) {
    return (
      <Card sx={{ mb: 2 }}>
        <CardContent sx={{ py: 2 }}>
          <Box sx={{ display: 'flex', gap: 2, alignItems: 'center' }}>
            <TextField
              placeholder="Search migrations..."
              value={filters.search}
              onChange={(e) => handleFilterChange({ search: e.target.value })}
              size="small"
              sx={{ flex: 1 }}
              InputProps={{
                startAdornment: <SearchIcon sx={{ mr: 1, color: 'text.secondary' }} />,
              }}
            />
            
            <FormControl size="small" sx={{ minWidth: 120 }}>
              <InputLabel>Status</InputLabel>
              <Select
                multiple
                value={filters.status}
                onChange={(e) => handleFilterChange({ status: e.target.value as MigrationStatus[] })}
                renderValue={(selected) => selected.length + ' selected'}
              >
                {statusOptions.map((status) => (
                  <MenuItem key={status} value={status}>
                    {status.charAt(0).toUpperCase() + status.slice(1)}
                  </MenuItem>
                ))}
              </Select>
            </FormControl>

            <Badge badgeContent={activeFilterCount} color="primary">
              <IconButton size="small">
                <FilterListIcon />
              </IconButton>
            </Badge>

            {activeFilterCount > 0 && (
              <IconButton size="small" onClick={handleClearFilters}>
                <ClearIcon />
              </IconButton>
            )}
          </Box>
        </CardContent>
      </Card>
    );
  }

  return (
    <Card sx={{ mb: 3 }}>
      <CardContent>
        <Box sx={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', mb: 2 }}>
          <Typography variant="h6" sx={{ display: 'flex', alignItems: 'center' }}>
            <FilterListIcon sx={{ mr: 1 }} />
            Filters & Search
          </Typography>
          
          <Box sx={{ display: 'flex', gap: 1 }}>
            <Badge badgeContent={activeFilterCount} color="primary">
              <Chip 
                label={`${activeFilterCount} active`} 
                size="small" 
                color={activeFilterCount > 0 ? 'primary' : 'default'}
              />
            </Badge>
            
            {activeFilterCount > 0 && (
              <Button
                size="small"
                onClick={handleClearFilters}
                startIcon={<ClearIcon />}
              >
                Clear All
              </Button>
            )}
            
            <Button
              size="small"
              onClick={handleSaveFilters}
              startIcon={<SavedSearchIcon />}
              disabled={activeFilterCount === 0}
            >
              Save
            </Button>
          </Box>
        </Box>

        {/* Search */}
        <Accordion expanded={expanded === 'search'} onChange={handleAccordionChange('search')}>
          <AccordionSummary expandIcon={<ExpandMoreIcon />}>
            <Typography variant="subtitle1">Search & Sort</Typography>
          </AccordionSummary>
          <AccordionDetails>
            <Box sx={{ display: 'flex', flexDirection: 'column', gap: 2 }}>
              <TextField
                label="Search migrations"
                placeholder="Search by ID, store hash, entity type..."
                value={filters.search}
                onChange={(e) => handleFilterChange({ search: e.target.value })}
                fullWidth
                InputProps={{
                  startAdornment: <SearchIcon sx={{ mr: 1, color: 'text.secondary' }} />,
                }}
              />
              
              <Box sx={{ display: 'flex', gap: 2 }}>
                <FormControl sx={{ flex: 1 }}>
                  <InputLabel>Sort By</InputLabel>
                  <Select
                    value={filters.sortBy}
                    onChange={(e) => handleFilterChange({ sortBy: e.target.value as any })}
                  >
                    {sortByOptions.map((option) => (
                      <MenuItem key={option.value} value={option.value}>
                        {option.label}
                      </MenuItem>
                    ))}
                  </Select>
                </FormControl>
                
                <FormControl sx={{ flex: 1 }}>
                  <InputLabel>Order</InputLabel>
                  <Select
                    value={filters.sortOrder}
                    onChange={(e) => handleFilterChange({ sortOrder: e.target.value as 'asc' | 'desc' })}
                  >
                    <MenuItem value="asc">Ascending</MenuItem>
                    <MenuItem value="desc">Descending</MenuItem>
                  </Select>
                </FormControl>
              </Box>
            </Box>
          </AccordionDetails>
        </Accordion>

        {/* Status & Entity Types */}
        <Accordion expanded={expanded === 'status'} onChange={handleAccordionChange('status')}>
          <AccordionSummary expandIcon={<ExpandMoreIcon />}>
            <Typography variant="subtitle1">Status & Entity Types</Typography>
          </AccordionSummary>
          <AccordionDetails>
            <Box sx={{ display: 'flex', flexDirection: 'column', gap: 3 }}>
              <Box>
                <Typography variant="body2" gutterBottom>Migration Status</Typography>
                <Box sx={{ display: 'flex', flexWrap: 'wrap', gap: 1 }}>
                  {statusOptions.map((status) => (
                    <Chip
                      key={status}
                      label={status.charAt(0).toUpperCase() + status.slice(1)}
                      variant={filters.status.includes(status) ? 'filled' : 'outlined'}
                      onClick={() => {
                        const newStatus = filters.status.includes(status)
                          ? filters.status.filter(s => s !== status)
                          : [...filters.status, status];
                        handleFilterChange({ status: newStatus });
                      }}
                      color={filters.status.includes(status) ? 'primary' : 'default'}
                    />
                  ))}
                </Box>
              </Box>
              
              <Box>
                <Typography variant="body2" gutterBottom>Entity Types</Typography>
                <Box sx={{ display: 'flex', flexWrap: 'wrap', gap: 1 }}>
                  {entityTypeOptions.map((entityType) => (
                    <Chip
                      key={entityType}
                      label={entityType.charAt(0).toUpperCase() + entityType.slice(1)}
                      variant={filters.entityTypes.includes(entityType) ? 'filled' : 'outlined'}
                      onClick={() => {
                        const newEntityTypes = filters.entityTypes.includes(entityType)
                          ? filters.entityTypes.filter(e => e !== entityType)
                          : [...filters.entityTypes, entityType];
                        handleFilterChange({ entityTypes: newEntityTypes });
                      }}
                      color={filters.entityTypes.includes(entityType) ? 'secondary' : 'default'}
                    />
                  ))}
                </Box>
              </Box>
            </Box>
          </AccordionDetails>
        </Accordion>

        {/* Progress & Performance */}
        <Accordion expanded={expanded === 'metrics'} onChange={handleAccordionChange('metrics')}>
          <AccordionSummary expandIcon={<ExpandMoreIcon />}>
            <Typography variant="subtitle1">Progress & Performance</Typography>
          </AccordionSummary>
          <AccordionDetails>
            <Box sx={{ display: 'flex', flexDirection: 'column', gap: 3 }}>
              <Box>
                <Typography variant="body2" gutterBottom>
                  Progress Range: {filters.progressRange[0]}% - {filters.progressRange[1]}%
                </Typography>
                <Slider
                  value={filters.progressRange}
                  onChange={(_, newValue) => handleFilterChange({ progressRange: newValue as [number, number] })}
                  valueLabelDisplay="auto"
                  min={0}
                  max={100}
                  marks={[
                    { value: 0, label: '0%' },
                    { value: 50, label: '50%' },
                    { value: 100, label: '100%' },
                  ]}
                />
              </Box>
              
              <Box>
                <Typography variant="body2" gutterBottom>
                  Processing Speed: {filters.speedRange[0]} - {filters.speedRange[1]} entities/sec
                </Typography>
                <Slider
                  value={filters.speedRange}
                  onChange={(_, newValue) => handleFilterChange({ speedRange: newValue as [number, number] })}
                  valueLabelDisplay="auto"
                  min={0}
                  max={1000}
                  marks={[
                    { value: 0, label: '0' },
                    { value: 500, label: '500' },
                    { value: 1000, label: '1000' },
                  ]}
                />
              </Box>
              
              <Box>
                <Typography variant="body2" gutterBottom>
                  Error Rate: {filters.errorRateRange[0]}% - {filters.errorRateRange[1]}%
                </Typography>
                <Slider
                  value={filters.errorRateRange}
                  onChange={(_, newValue) => handleFilterChange({ errorRateRange: newValue as [number, number] })}
                  valueLabelDisplay="auto"
                  min={0}
                  max={100}
                  marks={[
                    { value: 0, label: '0%' },
                    { value: 50, label: '50%' },
                    { value: 100, label: '100%' },
                  ]}
                />
              </Box>
            </Box>
          </AccordionDetails>
        </Accordion>

        {/* Date Range & Stores */}
        <Accordion expanded={expanded === 'advanced'} onChange={handleAccordionChange('advanced')}>
          <AccordionSummary expandIcon={<ExpandMoreIcon />}>
            <Typography variant="subtitle1">Date Range & Stores</Typography>
          </AccordionSummary>
          <AccordionDetails>
            <LocalizationProvider dateAdapter={AdapterDateFns}>
              <Box sx={{ display: 'flex', flexDirection: 'column', gap: 3 }}>
                <Box sx={{ display: 'flex', gap: 2 }}>
                  <DatePicker
                    label="Start Date"
                    value={filters.dateRange.start}
                    onChange={(date) => handleFilterChange({ 
                      dateRange: { ...filters.dateRange, start: date } 
                    })}
                    slotProps={{ textField: { size: 'small', sx: { flex: 1 } } }}
                  />
                  <DatePicker
                    label="End Date"
                    value={filters.dateRange.end}
                    onChange={(date) => handleFilterChange({ 
                      dateRange: { ...filters.dateRange, end: date } 
                    })}
                    slotProps={{ textField: { size: 'small', sx: { flex: 1 } } }}
                  />
                </Box>
                
                <Autocomplete
                  multiple
                  options={availableStoreHashes}
                  value={filters.storeHashes}
                  onChange={(_, newValue) => handleFilterChange({ storeHashes: newValue })}
                  renderInput={(params) => (
                    <TextField {...params} label="Store Hashes" placeholder="Select stores..." />
                  )}
                  renderTags={(value, getTagProps) =>
                    value.map((option, index) => (
                      <Chip variant="outlined" label={option} {...getTagProps({ index })} />
                    ))
                  }
                />
                
                <Box>
                  <FormControlLabel
                    control={
                      <Switch
                        checked={filters.showCompleted}
                        onChange={(e) => handleFilterChange({ showCompleted: e.target.checked })}
                      />
                    }
                    label="Show Completed Migrations"
                  />
                  <FormControlLabel
                    control={
                      <Switch
                        checked={filters.showFailed}
                        onChange={(e) => handleFilterChange({ showFailed: e.target.checked })}
                      />
                    }
                    label="Show Failed Migrations"
                  />
                </Box>
              </Box>
            </LocalizationProvider>
          </AccordionDetails>
        </Accordion>

        {/* Saved Filters */}
        {savedFilters.length > 0 && (
          <Box sx={{ mt: 2 }}>
            <Divider sx={{ mb: 2 }} />
            <Typography variant="subtitle2" gutterBottom>Saved Filter Sets</Typography>
            <Box sx={{ display: 'flex', flexWrap: 'wrap', gap: 1 }}>
              {savedFilters.map((savedFilter, index) => (
                <Chip
                  key={index}
                  label={savedFilter.name}
                  onClick={() => handleLoadSavedFilter(savedFilter)}
                  onDelete={() => setSavedFilters(prev => prev.filter((_, i) => i !== index))}
                  variant="outlined"
                />
              ))}
            </Box>
          </Box>
        )}
      </CardContent>
    </Card>
  );
}; 