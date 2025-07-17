import React, { useState } from 'react';
import {
  Box,
  Card,
  CardContent,
  Typography,
  Stepper,
  Step,
  StepLabel,
  StepContent,
  Button,
  TextField,
  FormControl,
  InputLabel,
  Select,
  MenuItem,
  FormGroup,
  FormControlLabel,
  Checkbox,
  Switch,
  Slider,
  Alert,
  Accordion,
  AccordionSummary,
  AccordionDetails,
  Chip,
  Divider,
  Grid,
  IconButton,
  Tooltip,
  Dialog,
  DialogTitle,
  DialogContent,
  DialogActions,
  LinearProgress,
  Table,
  TableBody,
  TableCell,
  TableContainer,
  TableHead,
  TableRow,
  useTheme,
} from '@mui/material';
import {
  ExpandMore as ExpandMoreIcon,
  Store as StoreIcon,
  Category as CategoryIcon,
  Inventory as InventoryIcon,
  Settings as SettingsIcon,
  PlayArrow as PlayArrowIcon,
  Info as InfoIcon,
  Warning as WarningIcon,
  Check as CheckIcon,
  Close as CloseIcon,
  Refresh as RefreshIcon,
  Help as HelpIcon,
} from '@mui/icons-material';

interface MigrationConfig {
  sourceStore: {
    hash: string;
    apiKey: string;
    apiSecret: string;
    storeUrl: string;
    name: string;
  };
  destinationStore: {
    hash: string;
    apiKey: string;
    apiSecret: string;
    storeUrl: string;
    name: string;
  };
  entities: {
    categories: boolean;
    products: boolean;
    brands: boolean;
    variants: boolean;
    images: boolean;
    modifiers: boolean;
  };
  batchSizes: {
    categories: number;
    products: number;
    brands: number;
    variants: number;
    images: number;
    modifiers: number;
  };
  options: {
    continueOnError: boolean;
    skipExisting: boolean;
    validateData: boolean;
    enableLogging: boolean;
    overwriteExisting: boolean;
    preserveIds: boolean;
    rateLimitCompliance: boolean;
    maxConcurrency: number;
    retryAttempts: number;
    notifyOnCompletion: boolean;
  };
  scheduling: {
    startImmediately: boolean;
    scheduledStartTime: string;
    priority: 'low' | 'normal' | 'high';
  };
}

interface MigrationConfigurationPanelProps {
  open: boolean;
  onClose: () => void;
  onSubmit: (config: MigrationConfig) => void;
  initialConfig?: Partial<MigrationConfig>;
}

const initialConfig: MigrationConfig = {
  sourceStore: {
    hash: '',
    apiKey: '',
    apiSecret: '',
    storeUrl: '',
    name: '',
  },
  destinationStore: {
    hash: '',
    apiKey: '',
    apiSecret: '',
    storeUrl: '',
    name: '',
  },
  entities: {
    categories: true,
    products: true,
    brands: true,
    variants: true,
    images: true,
    modifiers: false,
  },
  batchSizes: {
    categories: 50,
    products: 25,
    brands: 100,
    variants: 50,
    images: 10,
    modifiers: 25,
  },
  options: {
    continueOnError: true,
    skipExisting: false,
    validateData: true,
    enableLogging: true,
    overwriteExisting: false,
    preserveIds: false,
    rateLimitCompliance: true,
    maxConcurrency: 3,
    retryAttempts: 3,
    notifyOnCompletion: true,
  },
  scheduling: {
    startImmediately: true,
    scheduledStartTime: '',
    priority: 'normal',
  },
};

const StoreConfigurationStep: React.FC<{
  config: MigrationConfig;
  onUpdate: (config: Partial<MigrationConfig>) => void;
}> = ({ config, onUpdate }) => {
  const [testingConnection, setTestingConnection] = useState<'source' | 'destination' | null>(null);
  const [connectionStatus, setConnectionStatus] = useState<{
    source: 'success' | 'error' | 'pending' | null;
    destination: 'success' | 'error' | 'pending' | null;
  }>({ source: null, destination: null });

  const handleStoreUpdate = (type: 'sourceStore' | 'destinationStore', field: string, value: string) => {
    onUpdate({
      [type]: {
        ...config[type],
        [field]: value,
      },
    });
  };

  const testConnection = async (type: 'source' | 'destination') => {
    setTestingConnection(type);
    setConnectionStatus(prev => ({
      ...prev,
      [type]: 'pending',
    }));

    // Simulate API test
    setTimeout(() => {
      const success = Math.random() > 0.3; // 70% success rate
      setConnectionStatus(prev => ({
        ...prev,
        [type]: success ? 'success' : 'error',
      }));
      setTestingConnection(null);
    }, 2000);
  };

  const renderStoreConfig = (
    title: string,
    type: 'sourceStore' | 'destinationStore',
    store: MigrationConfig['sourceStore']
  ) => (
    <Card sx={{ mb: 2 }}>
      <CardContent>
        <Typography variant="h6" gutterBottom sx={{ display: 'flex', alignItems: 'center' }}>
          <StoreIcon sx={{ mr: 1 }} />
          {title}
        </Typography>

        <Box sx={{ display: 'flex', flexDirection: 'column', gap: 2 }}>
          <Box sx={{ display: 'flex', gap: 2, flexWrap: 'wrap' }}>
            <Box sx={{ flex: '1 1 300px' }}>
            <TextField
              fullWidth
              label="Store Hash"
              value={store.hash}
              onChange={(e) => handleStoreUpdate(type, 'hash', e.target.value)}
              placeholder="abc123def456"
              required
            />
            </Box>
            <Box sx={{ flex: '1 1 300px' }}>
              <TextField
                fullWidth
                label="Store Name"
              value={store.name}
              onChange={(e) => handleStoreUpdate(type, 'name', e.target.value)}
              placeholder="My Store"
            />
            </Box>
          </Box>
          <Box>
            <TextField
              fullWidth
              label="Store URL"
              value={store.storeUrl}
              onChange={(e) => handleStoreUpdate(type, 'storeUrl', e.target.value)}
              placeholder="https://store-abc123def456.mybigcommerce.com"
              required
            />
          </Box>
          <Box sx={{ display: 'flex', gap: 2, flexWrap: 'wrap' }}>
            <Box sx={{ flex: '1 1 300px' }}>
              <TextField
                fullWidth
                type="password"
                label="API Key"
                value={store.apiKey}
                onChange={(e) => handleStoreUpdate(type, 'apiKey', e.target.value)}
                required
              />
            </Box>
            <Box sx={{ flex: '1 1 300px' }}>
              <TextField
                fullWidth
                type="password"
                label="API Secret"
                value={store.apiSecret}
                onChange={(e) => handleStoreUpdate(type, 'apiSecret', e.target.value)}
                required
              />
            </Box>
          </Box>
        </Box>

        <Box sx={{ mt: 2, display: 'flex', alignItems: 'center', gap: 2 }}>
          <Button
            variant="outlined"
            onClick={() => testConnection(type === 'sourceStore' ? 'source' : 'destination')}
            disabled={!store.hash || !store.apiKey || !store.apiSecret || testingConnection !== null}
            startIcon={testingConnection === (type === 'sourceStore' ? 'source' : 'destination') ? undefined : <RefreshIcon />}
          >
            {testingConnection === (type === 'sourceStore' ? 'source' : 'destination') ? 'Testing...' : 'Test Connection'}
          </Button>

          {testingConnection === (type === 'sourceStore' ? 'source' : 'destination') && (
            <LinearProgress sx={{ width: 100 }} />
          )}

          {connectionStatus[type === 'sourceStore' ? 'source' : 'destination'] === 'success' && (
            <Chip icon={<CheckIcon />} label="Connection Successful" color="success" size="small" />
          )}

          {connectionStatus[type === 'sourceStore' ? 'source' : 'destination'] === 'error' && (
            <Chip icon={<CloseIcon />} label="Connection Failed" color="error" size="small" />
          )}
        </Box>
      </CardContent>
    </Card>
  );

  return (
    <Box>
      {renderStoreConfig('Source Store', 'sourceStore', config.sourceStore)}
      {renderStoreConfig('Destination Store', 'destinationStore', config.destinationStore)}
      
      <Alert severity="info" sx={{ mt: 2 }}>
        <Typography variant="body2">
          Both stores must have valid API credentials with the following permissions:
          <strong> Products (Read/Write), Categories (Read/Write), Brands (Read/Write)</strong>
        </Typography>
      </Alert>
    </Box>
  );
};

const EntitySelectionStep: React.FC<{
  config: MigrationConfig;
  onUpdate: (config: Partial<MigrationConfig>) => void;
}> = ({ config, onUpdate }) => {
  const entityInfo = {
    categories: { icon: <CategoryIcon />, description: 'Product categories and category tree structure' },
    products: { icon: <InventoryIcon />, description: 'Products with basic information, pricing, and descriptions' },
    brands: { icon: <StoreIcon />, description: 'Brand information and logos' },
    variants: { icon: <InventoryIcon />, description: 'Product variants (SKUs, options, inventory)' },
    images: { icon: <InventoryIcon />, description: 'Product and category images' },
    modifiers: { icon: <SettingsIcon />, description: 'Product option sets and modifiers' },
  };

  const handleEntityToggle = (entity: keyof MigrationConfig['entities']) => {
    onUpdate({
      entities: {
        ...config.entities,
        [entity]: !config.entities[entity],
      },
    });
  };

  const handleBatchSizeChange = (entity: keyof MigrationConfig['batchSizes'], value: number) => {
    onUpdate({
      batchSizes: {
        ...config.batchSizes,
        [entity]: value,
      },
    });
  };

  const selectedEntities = Object.entries(config.entities).filter(([_, enabled]) => enabled);
  const estimatedDuration = selectedEntities.length * 15; // Mock calculation

  return (
    <Box>
      <Typography variant="h6" gutterBottom>
        Select Entities to Migrate
      </Typography>

      <FormGroup>
        {Object.entries(entityInfo).map(([entity, info]) => (
          <Accordion key={entity} sx={{ mb: 1 }}>
            <AccordionSummary expandIcon={<ExpandMoreIcon />}>
              <FormControlLabel
                control={
                  <Checkbox
                    checked={config.entities[entity as keyof MigrationConfig['entities']]}
                    onChange={() => handleEntityToggle(entity as keyof MigrationConfig['entities'])}
                    onClick={(e) => e.stopPropagation()}
                  />
                }
                label={
                  <Box sx={{ display: 'flex', alignItems: 'center' }}>
                    {info.icon}
                    <Typography sx={{ ml: 1, textTransform: 'capitalize' }}>
                      {entity}
                    </Typography>
                  </Box>
                }
              />
            </AccordionSummary>
            <AccordionDetails>
              <Typography variant="body2" color="text.secondary" sx={{ mb: 2 }}>
                {info.description}
              </Typography>
              
              {config.entities[entity as keyof MigrationConfig['entities']] && (
                <Box>
                  <Typography variant="body2" gutterBottom>
                    Batch Size: {config.batchSizes[entity as keyof MigrationConfig['batchSizes']]}
                  </Typography>
                  <Slider
                    value={config.batchSizes[entity as keyof MigrationConfig['batchSizes']]}
                    onChange={(_, value) => handleBatchSizeChange(entity as keyof MigrationConfig['batchSizes'], value as number)}
                    min={entity === 'images' ? 5 : 10}
                    max={entity === 'products' ? 50 : 200}
                    step={entity === 'images' ? 5 : 10}
                    marks={[
                      { value: entity === 'images' ? 5 : 10, label: 'Small' },
                      { value: entity === 'images' ? 20 : 50, label: 'Medium' },
                      { value: entity === 'images' ? 30 : 100, label: 'Large' },
                    ]}
                    valueLabelDisplay="auto"
                  />
                </Box>
              )}
            </AccordionDetails>
          </Accordion>
        ))}
      </FormGroup>

      <Card sx={{ mt: 3, bgcolor: 'background.default' }}>
        <CardContent>
          <Typography variant="h6" gutterBottom>
            Migration Summary
          </Typography>
          <Box sx={{ display: 'flex', gap: 2, flexWrap: 'wrap' }}>
            <Box>
              <Typography variant="body2" color="text.secondary">Selected Entities</Typography>
              <Typography variant="h6">{selectedEntities.length}</Typography>
            </Box>
            <Box>
              <Typography variant="body2" color="text.secondary">Estimated Duration</Typography>
              <Typography variant="h6">{estimatedDuration} minutes</Typography>
            </Box>
            <Box>
              <Typography variant="body2" color="text.secondary">Processing Order</Typography>
              <Typography variant="body2">
                Categories → Brands → Products → Variants → Images → Modifiers
              </Typography>
            </Box>
          </Box>
        </CardContent>
      </Card>
    </Box>
  );
};

const AdvancedOptionsStep: React.FC<{
  config: MigrationConfig;
  onUpdate: (config: Partial<MigrationConfig>) => void;
}> = ({ config, onUpdate }) => {
  const handleOptionToggle = (option: keyof MigrationConfig['options']) => {
    onUpdate({
      options: {
        ...config.options,
        [option]: !config.options[option],
      },
    });
  };

  const handleOptionChange = (option: keyof MigrationConfig['options'], value: number | string) => {
    onUpdate({
      options: {
        ...config.options,
        [option]: value,
      },
    });
  };

  const handleSchedulingChange = (field: keyof MigrationConfig['scheduling'], value: any) => {
    onUpdate({
      scheduling: {
        ...config.scheduling,
        [field]: value,
      },
    });
  };

  return (
    <Box>
      <Accordion defaultExpanded>
        <AccordionSummary expandIcon={<ExpandMoreIcon />}>
          <Typography variant="h6">Processing Options</Typography>
        </AccordionSummary>
        <AccordionDetails>
          <FormGroup>
            <FormControlLabel
              control={
                <Switch
                  checked={config.options.continueOnError}
                  onChange={() => handleOptionToggle('continueOnError')}
                />
              }
              label="Continue on Error"
            />
            <Typography variant="caption" color="text.secondary" sx={{ ml: 4, mb: 2 }}>
              Continue processing even if individual items fail
            </Typography>

            <FormControlLabel
              control={
                <Switch
                  checked={config.options.skipExisting}
                  onChange={() => handleOptionToggle('skipExisting')}
                />
              }
              label="Skip Existing Items"
            />
            <Typography variant="caption" color="text.secondary" sx={{ ml: 4, mb: 2 }}>
              Skip items that already exist in the destination store
            </Typography>

            <FormControlLabel
              control={
                <Switch
                  checked={config.options.validateData}
                  onChange={() => handleOptionToggle('validateData')}
                />
              }
              label="Validate Data"
            />
            <Typography variant="caption" color="text.secondary" sx={{ ml: 4, mb: 2 }}>
              Validate all data before importing
            </Typography>

            <FormControlLabel
              control={
                <Switch
                  checked={config.options.rateLimitCompliance}
                  onChange={() => handleOptionToggle('rateLimitCompliance')}
                />
              }
              label="Rate Limit Compliance"
            />
            <Typography variant="caption" color="text.secondary" sx={{ ml: 4, mb: 2 }}>
              Automatically throttle requests to comply with API rate limits
            </Typography>
          </FormGroup>

          <Box sx={{ mt: 3 }}>
            <Typography variant="body2" gutterBottom>
              Max Concurrency: {config.options.maxConcurrency}
            </Typography>
            <Slider
              value={config.options.maxConcurrency}
              onChange={(_, value) => handleOptionChange('maxConcurrency', value as number)}
              min={1}
              max={10}
              step={1}
              marks={[
                { value: 1, label: '1' },
                { value: 5, label: '5' },
                { value: 10, label: '10' },
              ]}
              valueLabelDisplay="auto"
            />
          </Box>

          <Box sx={{ mt: 3 }}>
            <FormControl fullWidth>
              <InputLabel>Retry Attempts</InputLabel>
              <Select
                value={config.options.retryAttempts}
                onChange={(e) => handleOptionChange('retryAttempts', e.target.value as number)}
              >
                <MenuItem value={0}>No Retries</MenuItem>
                <MenuItem value={1}>1 Retry</MenuItem>
                <MenuItem value={3}>3 Retries</MenuItem>
                <MenuItem value={5}>5 Retries</MenuItem>
              </Select>
            </FormControl>
          </Box>
        </AccordionDetails>
      </Accordion>

      <Accordion sx={{ mt: 2 }}>
        <AccordionSummary expandIcon={<ExpandMoreIcon />}>
          <Typography variant="h6">Scheduling & Priority</Typography>
        </AccordionSummary>
        <AccordionDetails>
          <FormControlLabel
            control={
              <Switch
                checked={config.scheduling.startImmediately}
                onChange={(e) => handleSchedulingChange('startImmediately', e.target.checked)}
              />
            }
            label="Start Immediately"
          />

          {!config.scheduling.startImmediately && (
            <TextField
              fullWidth
              type="datetime-local"
              label="Scheduled Start Time"
              value={config.scheduling.scheduledStartTime}
              onChange={(e) => handleSchedulingChange('scheduledStartTime', e.target.value)}
              sx={{ mt: 2 }}
              InputLabelProps={{ shrink: true }}
            />
          )}

          <FormControl fullWidth sx={{ mt: 2 }}>
            <InputLabel>Priority</InputLabel>
            <Select
              value={config.scheduling.priority}
              onChange={(e) => handleSchedulingChange('priority', e.target.value)}
            >
              <MenuItem value="low">Low</MenuItem>
              <MenuItem value="normal">Normal</MenuItem>
              <MenuItem value="high">High</MenuItem>
            </Select>
          </FormControl>

          <FormControlLabel
            control={
              <Switch
                checked={config.options.notifyOnCompletion}
                onChange={() => handleOptionToggle('notifyOnCompletion')}
              />
            }
            label="Notify on Completion"
            sx={{ mt: 2 }}
          />
        </AccordionDetails>
      </Accordion>
    </Box>
  );
};

export const MigrationConfigurationPanel: React.FC<MigrationConfigurationPanelProps> = ({
  open,
  onClose,
  onSubmit,
  initialConfig = {},
}) => {
  const [config, setConfig] = useState<MigrationConfig>({
    sourceStore: {
      hash: '',
      apiKey: '',
      apiSecret: '',
      storeUrl: '',
      name: '',
    },
    destinationStore: {
      hash: '',
      apiKey: '',
      apiSecret: '',
      storeUrl: '',
      name: '',
    },
    entities: {
      categories: false,
      products: false,
      brands: false,
      variants: false,
      images: false,
      modifiers: false,
    },
    batchSizes: {
      categories: 100,
      products: 50,
      brands: 100,
      variants: 100,
      images: 25,
      modifiers: 100,
    },
    options: {
      continueOnError: false,
      skipExisting: false,
      validateData: true,
      enableLogging: true,
      overwriteExisting: false,
      preserveIds: false,
      rateLimitCompliance: true,
      maxConcurrency: 5,
      retryAttempts: 3,
      notifyOnCompletion: true,
    },
    scheduling: {
      startImmediately: true,
      scheduledStartTime: '',
      priority: 'normal',
    },
    ...initialConfig,
  });
  const [activeStep, setActiveStep] = useState(0);

  const steps = [
    {
      label: 'Store Configuration',
      description: 'Configure source and destination stores',
    },
    {
      label: 'Entity Selection',
      description: 'Choose which entities to migrate',
    },
    {
      label: 'Advanced Options',
      description: 'Configure migration settings and scheduling',
    },
    {
      label: 'Review & Start',
      description: 'Review configuration and start migration',
    },
  ];

  const handleNext = () => {
    setActiveStep((prevActiveStep) => prevActiveStep + 1);
  };

  const handleBack = () => {
    setActiveStep((prevActiveStep) => prevActiveStep - 1);
  };

  const handleConfigUpdate = (updates: Partial<MigrationConfig>) => {
    setConfig(prev => ({ ...prev, ...updates }));
  };

  const handleSubmit = () => {
    onSubmit(config);
    onClose();
  };

  const isStepValid = (step: number) => {
    switch (step) {
      case 0:
        return config.sourceStore.hash && config.sourceStore.apiKey && 
               config.destinationStore.hash && config.destinationStore.apiKey;
      case 1:
        return Object.values(config.entities).some(enabled => enabled);
      case 2:
        return true; // Advanced options are optional
      default:
        return true;
    }
  };

  const renderStepContent = (step: number) => {
    switch (step) {
      case 0:
        return <StoreConfigurationStep config={config} onUpdate={handleConfigUpdate} />;
      case 1:
        return <EntitySelectionStep config={config} onUpdate={handleConfigUpdate} />;
      case 2:
        return <AdvancedOptionsStep config={config} onUpdate={handleConfigUpdate} />;
      case 3:
        return (
          <Box>
            <Typography variant="h6" gutterBottom>
              Migration Configuration Summary
            </Typography>
            
            <Alert severity="success" sx={{ mb: 2 }}>
              Configuration is valid and ready to start!
            </Alert>

            <Box sx={{ display: 'flex', flexDirection: 'column', gap: 2 }}>
              <Box sx={{ display: 'flex', gap: 2, flexWrap: 'wrap' }}>
                <Box sx={{ flex: '1 1 300px' }}>
                  <Card>
                    <CardContent>
                      <Typography variant="h6" gutterBottom>Source Store</Typography>
                      <Typography variant="body2">{config.sourceStore.name || 'Unnamed Store'}</Typography>
                      <Typography variant="caption" color="text.secondary">
                        {config.sourceStore.storeUrl}
                      </Typography>
                    </CardContent>
                  </Card>
                </Box>
                
                <Box sx={{ flex: '1 1 300px' }}>
                  <Card>
                    <CardContent>
                      <Typography variant="h6" gutterBottom>Destination Store</Typography>
                      <Typography variant="body2">{config.destinationStore.name || 'Unnamed Store'}</Typography>
                      <Typography variant="caption" color="text.secondary">
                        {config.destinationStore.storeUrl}
                      </Typography>
                    </CardContent>
                  </Card>
                </Box>
              </Box>
              
              <Card>
                <CardContent>
                  <Typography variant="h6" gutterBottom>Selected Entities</Typography>
                  <Box sx={{ display: 'flex', gap: 1, flexWrap: 'wrap' }}>
                    {Object.entries(config.entities)
                      .filter(([_, enabled]) => enabled)
                      .map(([entity]) => (
                        <Chip key={entity} label={entity} size="small" />
                      ))}
                  </Box>
                </CardContent>
              </Card>
            </Box>
          </Box>
        );
      default:
        return null;
    }
  };

  return (
    <Dialog open={open} onClose={onClose} maxWidth="lg" fullWidth>
      <DialogTitle>
        <Box sx={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
          <Typography variant="h6">Configure New Migration</Typography>
          <IconButton onClick={onClose}>
            <CloseIcon />
          </IconButton>
        </Box>
      </DialogTitle>
      
      <DialogContent>
        <Stepper activeStep={activeStep} orientation="vertical">
          {steps.map((step, index) => (
            <Step key={step.label}>
              <StepLabel>
                <Typography variant="h6">{step.label}</Typography>
                <Typography variant="body2" color="text.secondary">
                  {step.description}
                </Typography>
              </StepLabel>
              <StepContent>
                <Box sx={{ mb: 2 }}>
                  {renderStepContent(index)}
                </Box>
                <Box sx={{ mb: 1 }}>
                  <Button
                    variant="contained"
                    onClick={index === steps.length - 1 ? handleSubmit : handleNext}
                    disabled={!isStepValid(index)}
                    sx={{ mt: 1, mr: 1 }}
                    startIcon={index === steps.length - 1 ? <PlayArrowIcon /> : undefined}
                  >
                    {index === steps.length - 1 ? 'Start Migration' : 'Continue'}
                  </Button>
                  <Button
                    disabled={index === 0}
                    onClick={handleBack}
                    sx={{ mt: 1, mr: 1 }}
                  >
                    Back
                  </Button>
                </Box>
              </StepContent>
            </Step>
          ))}
        </Stepper>
      </DialogContent>
    </Dialog>
  );
}; 