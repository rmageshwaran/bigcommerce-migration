import React, { useState } from 'react';
import {
  Box,
  Typography,
  Button,
  Card,
  CardContent,
  FormControl,
  Select,
  MenuItem,
  FormControlLabel,
  Checkbox,
  FormGroup,
  Link,
} from '@mui/material';
import {
  ArrowForward as ArrowForwardIcon,
} from '@mui/icons-material';
import { useDashboard } from '../../context/DashboardContext';
import { notificationService } from '../../services/notificationService';
import { getApiService } from '../../services/apiService';

interface MigrationFormData {
  sourceStore: string;
  sourceStorefront: string;
  destinationStore: string;
  destinationStorefront: string;
  selectedEntities: {
    // Design and Content Migration
    blogPosts: boolean;
    couponCodes: boolean;
    
    // Product Data Migration
    brands: boolean;
    categories: boolean;
    priceLists: boolean;
    promotions: boolean;
    
    // Order Data Migration
    orders: boolean;
    giftCertificates: boolean;
    customers: boolean;
    
    // Other
    permanentRedirects: boolean;
    currencies: boolean;
  };
}

// Hardcoded store configurations for development
const storeConfigurations = {
  production: {
    storeId: "tmdsef6c6o",
    accessToken: "ar247sdrwg6b5oo4c8h2n2nu2yat0w7",
    channelId: "1"
  },
  staging: {
    storeId: "in2msaitrc", 
    accessToken: "bntqbbjvnnap8agkdbo5bekqehb473z",
    channelId: "1"
  },
  development: {
    storeId: "dev12345xyz",
    accessToken: "dev_token_abc123def456ghi789",
    channelId: "1"
  }
};

const EntitySelectionCard: React.FC<{
  title: string;
  color: string;
  entities: { key: keyof MigrationFormData['selectedEntities']; label: string }[];
  selectedEntities: MigrationFormData['selectedEntities'];
  onEntityChange: (key: keyof MigrationFormData['selectedEntities'], checked: boolean) => void;
}> = ({ title, color, entities, selectedEntities, onEntityChange }) => (
  <Card sx={{ 
    height: '100%',
    borderLeft: `4px solid ${color}`,
    '&:hover': {
      boxShadow: 2,
    },
  }}>
    <CardContent>
      <Typography variant="h6" gutterBottom sx={{ fontWeight: 600, color: 'text.primary' }}>
        {title}
      </Typography>
      <FormGroup>
        {entities.map(entity => (
          <FormControlLabel
            key={entity.key}
            control={
              <Checkbox
                checked={selectedEntities[entity.key]}
                onChange={(e) => onEntityChange(entity.key, e.target.checked)}
                size="small"
                sx={{
                  '&.Mui-checked': {
                    color: color,
                  },
                }}
              />
            }
            label={
              <Typography variant="body2" sx={{ fontSize: '0.875rem' }}>
                {entity.label}
              </Typography>
            }
            sx={{ 
              marginBottom: 0.5,
              '& .MuiFormControlLabel-label': {
                fontSize: '0.875rem',
              },
            }}
          />
        ))}
      </FormGroup>
    </CardContent>
  </Card>
);

export const MigrationStartForm: React.FC = () => {
  const [loading, setLoading] = useState(false);
  const [formData, setFormData] = useState<MigrationFormData>({
    sourceStore: 'production',
    sourceStorefront: 'Main Storefront',
    destinationStore: 'staging',
    destinationStorefront: 'Main Storefront',
    selectedEntities: {
      blogPosts: false,
      couponCodes: false,
      brands: false,
      categories: false,
      priceLists: false,
      promotions: false,
      orders: false,
      giftCertificates: false,
      customers: false,
      permanentRedirects: false,
      currencies: false,
    },
  });

  const handleEntityChange = (key: keyof MigrationFormData['selectedEntities'], checked: boolean) => {
    setFormData(prev => ({
      ...prev,
      selectedEntities: {
        ...prev.selectedEntities,
        [key]: checked,
      },
    }));
  };

  const handleStartMigration = async () => {
    setLoading(true);
    try {
      // Get selected entities
      const selectedEntities = Object.entries(formData.selectedEntities)
        .filter(([_, selected]) => selected)
        .map(([key, _]) => key);

      if (selectedEntities.length === 0) {
        notificationService.error('No Entities Selected', 'Please select at least one entity type to migrate');
        return;
      }

      // Map entity names to backend format
      const entityMapping: Record<string, string> = {
        blogPosts: 'blog_posts',
        couponCodes: 'coupon_codes',
        brands: 'brands',
        categories: 'categories',
        priceLists: 'price_lists',
        promotions: 'promotions',
        orders: 'orders',
        giftCertificates: 'gift_certificates',
        customers: 'customers',
        permanentRedirects: 'permanent_redirects',
        currencies: 'currencies',
      };

      // Get store configurations
      const sourceStoreConfig = storeConfigurations[formData.sourceStore as keyof typeof storeConfigurations];
      const destinationStoreConfig = storeConfigurations[formData.destinationStore as keyof typeof storeConfigurations];

      if (!sourceStoreConfig || !destinationStoreConfig) {
        notificationService.error('Invalid Store Selection', 'Please select valid source and destination stores');
        return;
      }

      const migrationRequest = {
        entities: selectedEntities.map(entity => entityMapping[entity] || entity),
        sourceStore: {
          storeId: sourceStoreConfig.storeId,
          accessToken: sourceStoreConfig.accessToken,
          channelId: sourceStoreConfig.channelId
        },
        destinationStore: {
          storeId: destinationStoreConfig.storeId,
          accessToken: destinationStoreConfig.accessToken,
          channelId: destinationStoreConfig.channelId
        }
      };

      console.log('🚀 Starting migration with payload:', migrationRequest);
      
      const apiService = getApiService();
      const response = await apiService.startMigration(migrationRequest);
      
      notificationService.success('Migration Started', 'Migration has been started successfully!');
      
      // Reset selected entities
      setFormData(prev => ({
        ...prev,
        selectedEntities: {
          blogPosts: false,
          couponCodes: false,
          brands: false,
          categories: false,
          priceLists: false,
          promotions: false,
          orders: false,
          giftCertificates: false,
          customers: false,
          permanentRedirects: false,
          currencies: false,
        },
      }));
      
    } catch (error) {
      console.error('Failed to start migration:', error);
      notificationService.error('Migration Failed', 'Failed to start migration. Please try again.');
    } finally {
      setLoading(false);
    }
  };

  const hasSelectedEntities = Object.values(formData.selectedEntities).some(Boolean);

  const entityGroups = [
    {
      title: 'Design and Content Migration',
      color: '#4285f4',
      entities: [
        { key: 'blogPosts' as const, label: 'Blog Posts' },
        { key: 'couponCodes' as const, label: 'Coupon Codes' },
      ],
    },
    {
      title: 'Product Data Migration',
      color: '#34a853',
      entities: [
        { key: 'brands' as const, label: 'Brands' },
        { key: 'categories' as const, label: 'Categories' },
        { key: 'priceLists' as const, label: 'Price Lists' },
        { key: 'promotions' as const, label: 'Promotions' },
      ],
    },
    {
      title: 'Order Data Migration',
      color: '#fbbc04',
      entities: [
        { key: 'orders' as const, label: 'Orders' },
        { key: 'giftCertificates' as const, label: 'Gift Certificates' },
        { key: 'customers' as const, label: 'Customers' },
      ],
    },
    {
      title: 'Other',
      color: '#ea4335',
      entities: [
        { key: 'permanentRedirects' as const, label: 'Permanent Redirects' },
        { key: 'currencies' as const, label: 'Currencies' },
      ],
    },
  ];

  return (
    <Box sx={{ p: { xs: 2, md: 4 }, maxWidth: '1200px', mx: 'auto' }}>
      {/* Header */}
      <Box sx={{ 
        display: 'flex', 
        justifyContent: 'space-between', 
        alignItems: 'flex-start',
        mb: 4,
        flexDirection: { xs: 'column', md: 'row' },
        gap: { xs: 2, md: 0 },
      }}>
        <Typography 
          variant="h5" 
          component="h1" 
          sx={{ 
            fontWeight: 600,
            color: 'text.primary',
            fontSize: '1.5rem',
          }}
        >
          Content and Design Migration Settings
        </Typography>
        
        <Box sx={{ textAlign: { xs: 'left', md: 'right' } }}>
          <Typography variant="body2" color="text.secondary" gutterBottom>
            Production Website URL:
          </Typography>
          <Link 
            href="https://stagingpro-stagingapp-jai-sandbox-01.mybigcommerce.com" 
            target="_blank"
            sx={{ 
              color: 'primary.main',
              textDecoration: 'none',
              fontSize: '0.875rem',
              '&:hover': {
                textDecoration: 'underline',
              },
            }}
          >
            https://stagingpro-stagingapp-jai-sandbox-01.mybigcommerce.com
          </Link>
        </Box>
      </Box>

      {/* Store Configuration */}
      <Card sx={{ mb: 4 }}>
        <CardContent sx={{ p: 3 }}>
          <Box sx={{ display: 'flex', flexDirection: { xs: 'column', md: 'row' }, gap: 3, alignItems: { md: 'flex-end' } }}>
            <Box sx={{ flex: '0 0 auto', minWidth: { md: '150px' } }}>
              <Typography variant="subtitle1" sx={{ fontWeight: 600 }}>
                Source
              </Typography>
              <FormControl fullWidth size="small">
                <Select
                  value={formData.sourceStore}
                  onChange={(e) => setFormData(prev => ({ ...prev, sourceStore: e.target.value }))}
                  sx={{ 
                    '& .MuiOutlinedInput-notchedOutline': {
                      borderColor: 'divider',
                    },
                  }}
                >
                  <MenuItem value="production">Production</MenuItem>
                  <MenuItem value="staging">Staging</MenuItem>
                  <MenuItem value="development">Development</MenuItem>
                </Select>
              </FormControl>
            </Box>

            <Box sx={{ flex: '1 1 auto', minWidth: { md: '200px' } }}>
              <Typography variant="subtitle1" sx={{ fontWeight: 600 }}>
                Source Storefront
              </Typography>
              <FormControl fullWidth size="small">
                <Select
                  value={formData.sourceStorefront}
                  onChange={(e) => setFormData(prev => ({ ...prev, sourceStorefront: e.target.value }))}
                  sx={{ 
                    '& .MuiOutlinedInput-notchedOutline': {
                      borderColor: 'divider',
                    },
                  }}
                >
                  <MenuItem value="Main Storefront">Main Storefront</MenuItem>
                  <MenuItem value="Secondary Storefront">Secondary Storefront</MenuItem>
                </Select>
              </FormControl>
            </Box>

            <Box sx={{ flex: '0 0 auto', textAlign: 'center', display: { xs: 'none', md: 'block' } }}>
              <ArrowForwardIcon sx={{ color: 'text.secondary', fontSize: '2rem' }} />
            </Box>

            <Box sx={{ flex: '0 0 auto', minWidth: { md: '150px' } }}>
              <Typography variant="subtitle1" sx={{ fontWeight: 600 }}>
                Destination
              </Typography>
              <FormControl fullWidth size="small">
                <Select
                  value={formData.destinationStore}
                  onChange={(e) => setFormData(prev => ({ ...prev, destinationStore: e.target.value }))}
                  sx={{ 
                    '& .MuiOutlinedInput-notchedOutline': {
                      borderColor: 'divider',
                    },
                  }}
                >
                  <MenuItem value="production">Production</MenuItem>
                  <MenuItem value="staging">Staging</MenuItem>
                  <MenuItem value="development">Development</MenuItem>
                </Select>
              </FormControl>
            </Box>

            <Box sx={{ flex: '1 1 auto', minWidth: { md: '200px' } }}>
              <Typography variant="subtitle1" sx={{ fontWeight: 600 }}>
                Destination Storefront
              </Typography>
              <FormControl fullWidth size="small">
                <Select
                  value={formData.destinationStorefront}
                  onChange={(e) => setFormData(prev => ({ ...prev, destinationStorefront: e.target.value }))}
                  sx={{ 
                    '& .MuiOutlinedInput-notchedOutline': {
                      borderColor: 'divider',
                    },
                  }}
                >
                  <MenuItem value="Main Storefront">Main Storefront</MenuItem>
                  <MenuItem value="Secondary Storefront">Secondary Storefront</MenuItem>
                </Select>
              </FormControl>
            </Box>

            <Box sx={{ flex: '0 0 auto', textAlign: { xs: 'center', md: 'right' }, display: 'flex', alignItems: 'flex-end' }}>
              <Button
                variant="contained"
                startIcon={<ArrowForwardIcon />}
                onClick={handleStartMigration}
                disabled={!hasSelectedEntities || loading}
                sx={{
                  textTransform: 'none',
                  fontWeight: 600,
                  px: 3,
                  py: 1.5,
                  borderRadius: '6px',
                  height: 40, // Match the height of small input fields
                  whiteSpace: 'nowrap',
                  fontSize: '0.875rem',
                }}
              >
                {loading ? 'Starting...' : 'Start the Migration'}
              </Button>
            </Box>
          </Box>
        </CardContent>
      </Card>

      {/* Entity Selection */}
      <Box sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr', md: 'repeat(2, 1fr)', lg: 'repeat(4, 1fr)' }, gap: 3 }}>
        {entityGroups.map((group, index) => (
          <Box key={index}>
            <EntitySelectionCard
              title={group.title}
              color={group.color}
              entities={group.entities}
              selectedEntities={formData.selectedEntities}
              onEntityChange={handleEntityChange}
                          />
            </Box>
          ))}
        </Box>
    </Box>
  );
}; 