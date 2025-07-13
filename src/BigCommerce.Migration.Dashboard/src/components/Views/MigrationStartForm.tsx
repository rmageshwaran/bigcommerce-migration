import React, { useState, useEffect } from 'react';
import {
  Box,
  Typography,
  Button,
  Card,
  CardContent,
  Grid,
  TextField,
  Checkbox,
  FormControlLabel,
  IconButton,
  Stepper,
  Step,
  StepLabel,
  StepContent,
  LinearProgress,
  Alert,
  Switch,
  Slider,
  Chip,
  InputAdornment,
  CircularProgress,
  Paper,
  List,
  ListItem,
  ListItemText,
  ListItemIcon,
  Accordion,
  AccordionSummary,
  AccordionDetails,
} from '@mui/material';
import {
  Visibility,
  VisibilityOff,
  CheckCircle,
  Cancel,
  ArrowBack,
  ArrowForward,
  PlayArrow,
  ExpandMore,
  Category,
  Inventory,
  Store as BrandIcon,
  Tune,
  Image,
  Settings,
} from '@mui/icons-material';
import { useDashboard } from '../../context/DashboardContext';
import type { EntityConfiguration, MigrationOptions } from '../../types';
import { notificationService } from '../../services/notificationService';
import { getApiService } from '../../services/apiService';

// Define additional types needed for the form
interface StoreConfiguration {
  storeUrl: string;
  storeHash: string;
  clientId: string;
  accessToken: string;
  apiPath: string;
  name: string;
}

interface MigrationConfig {
  name: string;
  description: string;
  sourceStore: StoreConfiguration;
  destinationStore: StoreConfiguration;
  selectedEntities: Record<string, boolean>;
  batchSizes: Record<string, number>;
  options: MigrationOptions & {
    enableDetailedLogging: boolean;
    enableRealTimeUpdates: boolean;
    maxRetries: number;
    rateLimit: number;
  };
}

type EntityType = 'categories' | 'products' | 'brands' | 'variants' | 'images' | 'modifiers';

const StoreConfigurationStep: React.FC<{ 
  formData: MigrationConfig;
  onUpdate: (data: Partial<MigrationConfig>) => void;
}> = ({ formData, onUpdate }) => {
  const [showSourcePassword, setShowSourcePassword] = useState(false);
  const [showDestPassword, setShowDestPassword] = useState(false);
  const [sourceStatus, setSourceStatus] = useState<'idle' | 'testing' | 'success' | 'error'>('idle');
  const [destStatus, setDestStatus] = useState<'idle' | 'testing' | 'success' | 'error'>('idle');

  const testConnection = async (type: 'source' | 'destination') => {
    const config = type === 'source' ? formData.sourceStore : formData.destinationStore;
    const setStatus = type === 'source' ? setSourceStatus : setDestStatus;
    
    setStatus('testing');
    try {
      const apiService = getApiService();
      const isValid = await apiService.testConnection();
      setStatus(isValid ? 'success' : 'error');
    } catch (error) {
      setStatus('error');
    }
  };

  const renderStoreConfig = (
    title: string,
    config: StoreConfiguration,
    onConfigUpdate: (update: Partial<StoreConfiguration>) => void,
    showPassword: boolean,
    setShowPassword: (show: boolean) => void,
    status: typeof sourceStatus,
    onTest: () => void
  ) => (
    <Card>
      <CardContent>
        <Typography variant="h6" gutterBottom>
          {title}
        </Typography>
        <Box sx={{ display: 'flex', flexDirection: 'column', gap: 2 }}>
          <Box sx={{ display: 'flex', gap: 2, flexWrap: 'wrap' }}>
            <Box sx={{ flex: '1 1 300px' }}>
              <TextField
                fullWidth
                label="Store URL"
                value={config.storeUrl}
                onChange={(e) => onConfigUpdate({ storeUrl: e.target.value })}
                placeholder="https://your-store.mybigcommerce.com"
              />
            </Box>
            <Box sx={{ flex: '1 1 300px' }}>
              <TextField
                fullWidth
                label="Store Hash"
                value={config.storeHash}
                onChange={(e) => onConfigUpdate({ storeHash: e.target.value })}
                placeholder="abc123def"
              />
            </Box>
          </Box>
          
          <TextField
            fullWidth
            label="Client ID"
            value={config.clientId}
            onChange={(e) => onConfigUpdate({ clientId: e.target.value })}
            placeholder="your-client-id"
          />
          
          <TextField
            fullWidth
            label="Access Token"
            type={showPassword ? 'text' : 'password'}
            value={config.accessToken}
            onChange={(e) => onConfigUpdate({ accessToken: e.target.value })}
            placeholder="your-access-token"
            InputProps={{
              endAdornment: (
                <InputAdornment position="end">
                  <IconButton onClick={() => setShowPassword(!showPassword)} edge="end">
                    {showPassword ? <VisibilityOff /> : <Visibility />}
                  </IconButton>
                </InputAdornment>
              ),
            }}
          />
          
          <Box sx={{ display: 'flex', gap: 2, flexWrap: 'wrap' }}>
            <Box sx={{ flex: '1 1 200px' }}>
              <TextField
                fullWidth
                label="API Path"
                value={config.apiPath}
                onChange={(e) => onConfigUpdate({ apiPath: e.target.value })}
                placeholder="/stores/{store_hash}/v3/"
              />
            </Box>
            <Box sx={{ flex: '1 1 200px' }}>
              <TextField
                fullWidth
                label="Store Name"
                value={config.name}
                onChange={(e) => onConfigUpdate({ name: e.target.value })}
                placeholder="My Store"
              />
            </Box>
          </Box>
          
          <Box sx={{ display: 'flex', gap: 1, alignItems: 'center' }}>
            <Button
              variant="outlined"
              onClick={onTest}
              disabled={status === 'testing'}
              startIcon={status === 'testing' ? <CircularProgress size={16} /> : undefined}
            >
              Test Connection
            </Button>
            {status === 'success' && <CheckCircle color="success" />}
            {status === 'error' && <Cancel color="error" />}
          </Box>
        </Box>
      </CardContent>
    </Card>
  );

  return (
    <Box sx={{ display: 'flex', flexDirection: 'column', gap: 3 }}>
      <Box sx={{ display: 'flex', gap: 3, flexWrap: 'wrap' }}>
        <Box sx={{ flex: '1 1 400px' }}>
          {renderStoreConfig(
            'Source Store',
            formData.sourceStore,
            (update) => onUpdate({ sourceStore: { ...formData.sourceStore, ...update } }),
            showSourcePassword,
            setShowSourcePassword,
            sourceStatus,
            () => testConnection('source')
          )}
        </Box>
        <Box sx={{ flex: '1 1 400px' }}>
          {renderStoreConfig(
            'Destination Store',
            formData.destinationStore,
            (update) => onUpdate({ destinationStore: { ...formData.destinationStore, ...update } }),
            showDestPassword,
            setShowDestPassword,
            destStatus,
            () => testConnection('destination')
          )}
        </Box>
      </Box>
    </Box>
  );
};

const EntitySelectionStep: React.FC<{
  formData: MigrationConfig;
  onUpdate: (data: Partial<MigrationConfig>) => void;
}> = ({ formData, onUpdate }) => {
  const entityTypes = [
    { key: 'categories' as EntityType, name: 'Categories', icon: Category, count: 150, description: 'Product categories and subcategories' },
    { key: 'products' as EntityType, name: 'Products', icon: Inventory, count: 2500, description: 'Product catalog with basic information' },
    { key: 'brands' as EntityType, name: 'Brands', icon: BrandIcon, count: 45, description: 'Brand information and metadata' },
    { key: 'variants' as EntityType, name: 'Product Variants', icon: Tune, count: 8200, description: 'Product options and variants' },
    { key: 'images' as EntityType, name: 'Images', icon: Image, count: 12500, description: 'Product and category images' },
    { key: 'modifiers' as EntityType, name: 'Modifiers', icon: Settings, count: 320, description: 'Product modifiers and options' },
  ];

  const handleEntityToggle = (entityType: EntityType) => {
    const newSelected = {
      ...formData.selectedEntities,
      [entityType]: !formData.selectedEntities[entityType],
    };
    onUpdate({ selectedEntities: newSelected });
  };

  const handleBatchSizeChange = (entityType: EntityType, value: number) => {
    const newBatchSizes = {
      ...formData.batchSizes,
      [entityType]: value,
    };
    onUpdate({ batchSizes: newBatchSizes });
  };

  return (
    <Box sx={{ display: 'flex', flexDirection: 'column', gap: 3 }}>
      <Typography variant="h6">Select entities to migrate</Typography>
      
      <Box sx={{ display: 'flex', gap: 2, flexWrap: 'wrap' }}>
        {entityTypes.map((entity) => {
          const IconComponent = entity.icon;
          const isSelected = formData.selectedEntities[entity.key];
          const batchSize = formData.batchSizes[entity.key] || 50;
          
          return (
            <Box key={entity.key} sx={{ flex: '1 1 400px', minWidth: '300px' }}>
              <Card 
                variant={isSelected ? 'elevation' : 'outlined'}
                sx={{ 
                  borderColor: isSelected ? 'primary.main' : 'divider',
                  backgroundColor: isSelected ? 'primary.50' : 'background.paper'
                }}
              >
                <CardContent>
                  <Box sx={{ display: 'flex', alignItems: 'center', gap: 2, mb: 2 }}>
                    <FormControlLabel
                      control={
                        <Checkbox
                          checked={isSelected}
                          onChange={() => handleEntityToggle(entity.key)}
                          color="primary"
                        />
                      }
                      label={
                        <Box sx={{ display: 'flex', alignItems: 'center', gap: 1 }}>
                          <IconComponent color={isSelected ? 'primary' : 'action'} />
                          <Box>
                            <Typography variant="h6">{entity.name}</Typography>
                            <Typography variant="body2" color="text.secondary">
                              {entity.description}
                            </Typography>
                          </Box>
                        </Box>
                      }
                    />
                  </Box>
                  
                  <Box sx={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', mb: 2 }}>
                    <Typography variant="body2" color="text.secondary">
                      Available: {entity.count.toLocaleString()} items
                    </Typography>
                    <Chip 
                      label={`Batch: ${batchSize}`}
                      size="small"
                      color={isSelected ? 'primary' : 'default'}
                    />
                  </Box>
                  
                  {isSelected && (
                    <Box sx={{ mt: 2 }}>
                      <Typography variant="body2" gutterBottom>
                        Batch Size: {batchSize}
                      </Typography>
                      <Slider
                        value={batchSize}
                        onChange={(_, value) => handleBatchSizeChange(entity.key, value as number)}
                        min={5}
                        max={200}
                        step={5}
                        marks={[
                          { value: 5, label: '5' },
                          { value: 50, label: '50' },
                          { value: 100, label: '100' },
                          { value: 200, label: '200' }
                        ]}
                        sx={{ mt: 1 }}
                      />
                      <Typography variant="caption" color="text.secondary">
                        Smaller batches = more reliable, larger batches = faster
                      </Typography>
                    </Box>
                  )}
                </CardContent>
              </Card>
            </Box>
          );
        })}
      </Box>
    </Box>
  );
};

const MigrationOptionsStep: React.FC<{
  formData: MigrationConfig;
  onUpdate: (data: Partial<MigrationConfig>) => void;
}> = ({ formData, onUpdate }) => {
  return (
    <Box sx={{ display: 'flex', flexDirection: 'column', gap: 3 }}>
      <Typography variant="h6">Migration Options</Typography>
      
      <Box sx={{ display: 'flex', gap: 3, flexWrap: 'wrap' }}>
        <Box sx={{ flex: '1 1 400px' }}>
          <Card>
            <CardContent>
              <Typography variant="h6" gutterBottom>
                Basic Options
              </Typography>
              
              <Box sx={{ display: 'flex', flexDirection: 'column', gap: 2 }}>
                <TextField
                  fullWidth
                  label="Migration Name"
                  value={formData.name}
                  onChange={(e) => onUpdate({ name: e.target.value })}
                  placeholder="My Migration"
                />
                
                <TextField
                  fullWidth
                  label="Description"
                  value={formData.description}
                  onChange={(e) => onUpdate({ description: e.target.value })}
                  multiline
                  rows={3}
                  placeholder="Describe this migration..."
                />
                
                <FormControlLabel
                  control={
                    <Switch
                      checked={formData.options.continueOnError}
                      onChange={(e) => onUpdate({ 
                        options: { ...formData.options, continueOnError: e.target.checked }
                      })}
                    />
                  }
                  label="Continue on Error"
                />
                
                <FormControlLabel
                  control={
                    <Switch
                      checked={formData.options.skipExisting}
                      onChange={(e) => onUpdate({ 
                        options: { ...formData.options, skipExisting: e.target.checked }
                      })}
                    />
                  }
                  label="Skip Existing Items"
                />
                
                <FormControlLabel
                  control={
                    <Switch
                      checked={formData.options.validateData}
                      onChange={(e) => onUpdate({ 
                        options: { ...formData.options, validateData: e.target.checked }
                      })}
                    />
                  }
                  label="Validate Data"
                />
              </Box>
            </CardContent>
          </Card>
        </Box>
        
        <Box sx={{ flex: '1 1 400px' }}>
          <Card>
            <CardContent>
              <Typography variant="h6" gutterBottom>
                Advanced Options
              </Typography>
              
              <Box sx={{ display: 'flex', flexDirection: 'column', gap: 3 }}>
                <FormControlLabel
                  control={
                    <Switch
                      checked={formData.options.enableDetailedLogging}
                      onChange={(e) => onUpdate({ 
                        options: { ...formData.options, enableDetailedLogging: e.target.checked }
                      })}
                    />
                  }
                  label="Enable Detailed Logging"
                />
                
                <FormControlLabel
                  control={
                    <Switch
                      checked={formData.options.enableRealTimeUpdates}
                      onChange={(e) => onUpdate({ 
                        options: { ...formData.options, enableRealTimeUpdates: e.target.checked }
                      })}
                    />
                  }
                  label="Enable Real-time Updates"
                />
                
                <Box>
                  <Typography variant="body2" gutterBottom>
                    Max Retries: {formData.options.maxRetries}
                  </Typography>
                  <Slider
                    value={formData.options.maxRetries}
                    onChange={(_, value) => onUpdate({ 
                      options: { ...formData.options, maxRetries: value as number }
                    })}
                    min={0}
                    max={10}
                    step={1}
                    marks={[
                      { value: 0, label: '0' },
                      { value: 3, label: '3' },
                      { value: 5, label: '5' },
                      { value: 10, label: '10' }
                    ]}
                  />
                </Box>
                
                <Box>
                  <Typography variant="body2" gutterBottom>
                    Rate Limit: {formData.options.rateLimit} req/sec
                  </Typography>
                  <Slider
                    value={formData.options.rateLimit}
                    onChange={(_, value) => onUpdate({ 
                      options: { ...formData.options, rateLimit: value as number }
                    })}
                    min={1}
                    max={30}
                    step={1}
                    marks={[
                      { value: 1, label: '1' },
                      { value: 5, label: '5' },
                      { value: 10, label: '10' },
                      { value: 30, label: '30' }
                    ]}
                  />
                </Box>
              </Box>
            </CardContent>
          </Card>
        </Box>
      </Box>
    </Box>
  );
};

const ReviewStep: React.FC<{
  formData: MigrationConfig;
}> = ({ formData }) => {
  const selectedEntities = Object.entries(formData.selectedEntities)
    .filter(([_, selected]) => selected)
    .map(([key, _]) => key);

  const totalEntities = selectedEntities.length;
  const avgBatchSize = selectedEntities.reduce((sum, entity) => 
    sum + (formData.batchSizes[entity as EntityType] || 50), 0) / totalEntities;
  
  const estimatedDuration = Math.ceil(totalEntities * avgBatchSize / formData.options.rateLimit / 60);

  return (
    <Box sx={{ display: 'flex', flexDirection: 'column', gap: 3 }}>
      <Typography variant="h6">Review Migration Configuration</Typography>
      
      <Box sx={{ display: 'flex', gap: 3, flexWrap: 'wrap' }}>
        <Box sx={{ flex: '1 1 400px' }}>
          <Card>
            <CardContent>
              <Typography variant="h6" gutterBottom>
                Migration Summary
              </Typography>
              
              <List>
                <ListItem>
                  <ListItemText 
                    primary="Migration Name" 
                    secondary={formData.name || 'Untitled Migration'} 
                  />
                </ListItem>
                
                <ListItem>
                  <ListItemText 
                    primary="Description" 
                    secondary={formData.description || 'No description'} 
                  />
                </ListItem>
                
                <ListItem>
                  <ListItemText 
                    primary="Selected Entities" 
                    secondary={`${selectedEntities.length} entity type(s)`} 
                  />
                </ListItem>
                
                <ListItem>
                  <ListItemText 
                    primary="Estimated Duration" 
                    secondary={`~${estimatedDuration} minutes`} 
                  />
                </ListItem>
                
                <ListItem>
                  <ListItemText 
                    primary="Rate Limit" 
                    secondary={`${formData.options.rateLimit} requests/second`} 
                  />
                </ListItem>
              </List>
            </CardContent>
          </Card>
        </Box>
        
        <Box sx={{ flex: '1 1 400px' }}>
          <Card>
            <CardContent>
              <Typography variant="h6" gutterBottom>
                Selected Entities
              </Typography>
              
              <List>
                {selectedEntities.map((entityType) => (
                  <ListItem key={entityType}>
                    <ListItemText 
                      primary={entityType.charAt(0).toUpperCase() + entityType.slice(1)} 
                      secondary={`Batch size: ${formData.batchSizes[entityType as EntityType] || 50}`} 
                    />
                  </ListItem>
                ))}
              </List>
              
              {selectedEntities.length === 0 && (
                <Alert severity="warning">
                  No entities selected for migration
                </Alert>
              )}
            </CardContent>
          </Card>
        </Box>
      </Box>
      
      <Accordion>
        <AccordionSummary expandIcon={<ExpandMore />}>
          <Typography variant="h6">Advanced Configuration</Typography>
        </AccordionSummary>
        <AccordionDetails>
          <Box sx={{ display: 'flex', gap: 2, flexWrap: 'wrap' }}>
            <Box sx={{ flex: '1 1 400px' }}>
              <Typography variant="subtitle2">Options</Typography>
              <List dense>
                <ListItem>
                  <ListItemText 
                    primary="Continue on Error" 
                    secondary={formData.options.continueOnError ? 'Yes' : 'No'} 
                  />
                </ListItem>
                <ListItem>
                  <ListItemText 
                    primary="Skip Existing" 
                    secondary={formData.options.skipExisting ? 'Yes' : 'No'} 
                  />
                </ListItem>
                <ListItem>
                  <ListItemText 
                    primary="Validate Data" 
                    secondary={formData.options.validateData ? 'Yes' : 'No'} 
                  />
                </ListItem>
                <ListItem>
                  <ListItemText 
                    primary="Max Retries" 
                    secondary={formData.options.maxRetries} 
                  />
                </ListItem>
              </List>
            </Box>
            
            <Box sx={{ flex: '1 1 400px' }}>
              <Typography variant="subtitle2">Store Configuration</Typography>
              <List dense>
                <ListItem>
                  <ListItemText 
                    primary="Source Store" 
                    secondary={formData.sourceStore.name || formData.sourceStore.storeUrl} 
                  />
                </ListItem>
                <ListItem>
                  <ListItemText 
                    primary="Destination Store" 
                    secondary={formData.destinationStore.name || formData.destinationStore.storeUrl} 
                  />
                </ListItem>
              </List>
            </Box>
          </Box>
        </AccordionDetails>
      </Accordion>
    </Box>
  );
};

export const MigrationStartForm: React.FC = () => {
  const [activeStep, setActiveStep] = useState(0);
  const [loading, setLoading] = useState(false);
  const [formData, setFormData] = useState<MigrationConfig>({
    name: '',
    description: '',
    sourceStore: {
      storeUrl: '',
      storeHash: '',
      clientId: '',
      accessToken: '',
      apiPath: '/stores/{store_hash}/v3/',
      name: '',
    },
    destinationStore: {
      storeUrl: '',
      storeHash: '',
      clientId: '',
      accessToken: '',
      apiPath: '/stores/{store_hash}/v3/',
      name: '',
    },
    selectedEntities: {
      categories: false,
      products: false,
      brands: false,
      variants: false,
      images: false,
      modifiers: false,
    },
    batchSizes: {
      categories: 50,
      products: 50,
      brands: 50,
      variants: 50,
      images: 50,
      modifiers: 50,
    },
    options: {
      continueOnError: true,
      skipExisting: true,
      validateData: true,
      enableLogging: true,
      enableDetailedLogging: false,
      enableRealTimeUpdates: true,
      maxRetries: 3,
      rateLimit: 10,
    },
  });

  const steps = [
    { label: 'Store Configuration', content: StoreConfigurationStep },
    { label: 'Entity Selection', content: EntitySelectionStep },
    { label: 'Migration Options', content: MigrationOptionsStep },
    { label: 'Review & Start', content: ReviewStep },
  ];

  const handleNext = () => {
    if (activeStep < steps.length - 1) {
      setActiveStep(activeStep + 1);
    }
  };

  const handleBack = () => {
    if (activeStep > 0) {
      setActiveStep(activeStep - 1);
    }
  };

  const handleUpdateFormData = (update: Partial<MigrationConfig>) => {
    setFormData(prev => ({ ...prev, ...update }));
  };

  const handleStartMigration = async () => {
    setLoading(true);
    try {
      // Validate form data
      const selectedEntities = Object.entries(formData.selectedEntities)
        .filter(([_, selected]) => selected);
      
      if (selectedEntities.length === 0) {
        throw new Error('Please select at least one entity type to migrate');
      }

      // Convert form data to API format (matching backend MigrationRequest model)
      const selectedEntityTypes = selectedEntities
        .filter(([_, isSelected]) => isSelected)
        .map(([entityType, _]) => entityType.charAt(0).toUpperCase() + entityType.slice(1));

      const migrationRequest = {
        sourceStore: {
          storeId: formData.sourceStore.storeHash,
          accessToken: formData.sourceStore.accessToken,
          channelId: "1" // Default channel for now
        },
        destinationStore: {
          storeId: formData.destinationStore.storeHash,
          accessToken: formData.destinationStore.accessToken,
          channelId: "1" // Default channel for now
        },
        entities: selectedEntityTypes,
        settings: {
          maxApiCallsPerSecond: formData.options.rateLimit || 12,
          enableAdaptiveBatching: true,
          logLevel: formData.options.enableLogging ? "DEBUG" : "INFO",
          requestTimeoutSeconds: 30,
          maxRetries: formData.options.maxRetries || 3
        }
      };

      const apiService = getApiService();
      const response = await apiService.startMigration(migrationRequest);
      
      notificationService.success('Migration Started', 'Migration started successfully!');
      
      // Reset form or redirect
      setFormData({
        ...formData,
        name: '',
        description: '',
        selectedEntities: {
          categories: false,
          products: false,
          brands: false,
          variants: false,
          images: false,
          modifiers: false,
        },
      });
      
      setActiveStep(0);
    } catch (error) {
      console.error('Failed to start migration:', error);
      notificationService.error('Migration Failed', 'Failed to start migration');
    } finally {
      setLoading(false);
    }
  };

  const canProceed = () => {
    switch (activeStep) {
      case 0:
        return formData.sourceStore.storeUrl && formData.sourceStore.accessToken &&
               formData.destinationStore.storeUrl && formData.destinationStore.accessToken;
      case 1:
        return Object.values(formData.selectedEntities).some(Boolean);
      case 2:
        return formData.name.trim() !== '';
      case 3:
        return Object.values(formData.selectedEntities).some(Boolean);
      default:
        return false;
    }
  };

  const StepComponent = steps[activeStep].content;

  return (
    <Box sx={{ p: 3 }}>
      <Typography variant="h4" gutterBottom>
        Start New Migration
      </Typography>
      
      <Paper sx={{ p: 3, mt: 2 }}>
        <Stepper activeStep={activeStep} orientation="vertical">
          {steps.map((step, index) => (
            <Step key={step.label}>
              <StepLabel>{step.label}</StepLabel>
              <StepContent>
                {index === activeStep && (
                  <Box sx={{ mt: 2 }}>
                    <StepComponent 
                      formData={formData} 
                      onUpdate={handleUpdateFormData}
                    />
                    
                    <Box sx={{ display: 'flex', justifyContent: 'space-between', mt: 3 }}>
                      <Button
                        disabled={activeStep === 0}
                        onClick={handleBack}
                        startIcon={<ArrowBack />}
                      >
                        Back
                      </Button>
                      
                      {activeStep === steps.length - 1 ? (
                        <Button
                          variant="contained"
                          onClick={handleStartMigration}
                          disabled={!canProceed() || loading}
                          startIcon={loading ? <CircularProgress size={16} /> : <PlayArrow />}
                        >
                          {loading ? 'Starting...' : 'Start Migration'}
                        </Button>
                      ) : (
                        <Button
                          variant="contained"
                          onClick={handleNext}
                          disabled={!canProceed()}
                          endIcon={<ArrowForward />}
                        >
                          Next
                        </Button>
                      )}
                    </Box>
                  </Box>
                )}
              </StepContent>
            </Step>
          ))}
        </Stepper>
      </Paper>
      
      {loading && (
        <Box sx={{ mt: 2 }}>
          <LinearProgress />
          <Typography variant="body2" sx={{ mt: 1 }}>
            Starting migration...
          </Typography>
        </Box>
      )}
    </Box>
  );
}; 