import React, { useState } from 'react';
import {
  Dialog,
  DialogTitle,
  DialogContent,
  DialogActions,
  Button,
  FormControl,
  FormLabel,
  RadioGroup,
  FormControlLabel,
  Radio,
  TextField,
  Switch,
  Box,
  Typography,
  Chip,
  Alert,
  LinearProgress,
  IconButton,
  Tooltip,
  Stack,
  Divider,
  Card,
  CardContent,
} from '@mui/material';
import {
  Close as CloseIcon,
  Download as DownloadIcon,
  TableChart as ExcelIcon,
  PictureAsPdf as PdfIcon,
  InsertDriveFile as CsvIcon,
  DateRange as DateRangeIcon,
  FilterList as FilterIcon,
  Assessment as ChartIcon,
  Info as InfoIcon,
} from '@mui/icons-material';
import { DatePicker } from '@mui/x-date-pickers/DatePicker';
import { AdapterDateFns } from '@mui/x-date-pickers/AdapterDateFns';
import { LocalizationProvider } from '@mui/x-date-pickers/LocalizationProvider';
import { exportService } from '../../services/exportService';
import type { ExportFormat, ExportOptions, ExportResult } from '../../services/exportService';

interface ExportDialogProps {
  open: boolean;
  onClose: () => void;
  dataType: 'migrations' | 'entities' | 'errors' | 'performance' | 'charts';
  title?: string;
  data?: unknown[];
  chartElement?: HTMLElement;
}

const ExportDialog: React.FC<ExportDialogProps> = ({
  open,
  onClose,
  dataType,
  title,
  data,
  chartElement,
}) => {
  const [format, setFormat] = useState<ExportFormat>('excel');
  const [filename, setFilename] = useState('');
  const [includeTimestamp, setIncludeTimestamp] = useState(true);
  const [includeHeaders, setIncludeHeaders] = useState(true);
  const [dateRange, setDateRange] = useState<{ start: Date | null; end: Date | null }>({
    start: null,
    end: null,
  });
  const [isExporting, setIsExporting] = useState(false);
  const [exportResult, setExportResult] = useState<ExportResult | null>(null);

  const formatIcons = {
    csv: <CsvIcon />,
    excel: <ExcelIcon />,
    pdf: <PdfIcon />,
  };

  const formatDescriptions = {
    csv: 'Comma-separated values - Great for data analysis and import into other tools',
    excel: 'Microsoft Excel format - Best for detailed analysis with formulas and formatting',
    pdf: 'Portable Document Format - Perfect for reports and sharing with stakeholders',
  };

  const getDefaultFilename = () => {
    const typeMap = {
      migrations: 'migration_report',
      entities: 'entity_progress',
      errors: 'error_report',
      performance: 'performance_metrics',
      charts: 'chart_export',
    };
    return typeMap[dataType] || 'export';
  };

  const handleExport = async () => {
    setIsExporting(true);
    setExportResult(null);

    try {
      const options: ExportOptions = {
        format,
        filename: filename || getDefaultFilename(),
        includeTimestamp,
        includeHeaders,
        dateRange: dateRange.start && dateRange.end ? {
          start: dateRange.start,
          end: dateRange.end,
        } : undefined,
      };

      let result: ExportResult;

      if (dataType === 'charts' && chartElement) {
        result = await exportService.exportChart(chartElement, { ...options, title });
      } else if (data) {
        switch (dataType) {
          case 'migrations':
            result = await exportService.exportMigrations(data as any, options);
            break;
          case 'entities':
            result = await exportService.exportEntityProgress(data as any, options);
            break;
          case 'errors':
            result = await exportService.exportErrors(data as any, options);
            break;
          case 'performance':
            result = await exportService.exportPerformance(data as any, options);
            break;
          default:
            throw new Error('Unsupported data type');
        }
      } else {
        // Use sample data for demo
        const sampleData = exportService.generateSampleMigrationData();
        result = await exportService.exportMigrations(sampleData, options);
      }

      setExportResult(result);
    } catch (error) {
      setExportResult({
        success: false,
        filename: '',
        format,
        error: error instanceof Error ? error.message : 'Export failed',
      });
    } finally {
      setIsExporting(false);
    }
  };

  const handleClose = () => {
    setExportResult(null);
    onClose();
  };

  const getFormatCount = () => {
    if (!data) return 0;
    return Array.isArray(data) ? data.length : 0;
  };

  return (
    <LocalizationProvider dateAdapter={AdapterDateFns}>
      <Dialog
        open={open}
        onClose={handleClose}
        maxWidth="md"
        fullWidth
        PaperProps={{
          sx: {
            borderRadius: 2,
            minHeight: 600,
          },
        }}
      >
        <DialogTitle sx={{ pb: 2 }}>
          <Box sx={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between' }}>
            <Box sx={{ display: 'flex', alignItems: 'center', gap: 1 }}>
              <DownloadIcon color="primary" />
              <Typography variant="h6">
                Export {title || dataType.charAt(0).toUpperCase() + dataType.slice(1)}
              </Typography>
              {getFormatCount() > 0 && (
                <Chip
                  label={`${getFormatCount()} records`}
                  size="small"
                  color="primary"
                  variant="outlined"
                />
              )}
            </Box>
            <IconButton onClick={handleClose} size="small">
              <CloseIcon />
            </IconButton>
          </Box>
        </DialogTitle>

        <DialogContent sx={{ pb: 2 }}>
          <Stack spacing={3}>
            {/* Export Format Selection */}
            <Card variant="outlined">
              <CardContent>
                <FormControl component="fieldset" fullWidth>
                  <FormLabel component="legend" sx={{ mb: 2, fontWeight: 600 }}>
                    Export Format
                  </FormLabel>
                  <RadioGroup
                    value={format}
                    onChange={(e) => setFormat(e.target.value as ExportFormat)}
                  >
                    {Object.entries(formatIcons).map(([formatKey, icon]) => (
                      <FormControlLabel
                        key={formatKey}
                        value={formatKey}
                        control={<Radio />}
                        label={
                          <Box sx={{ display: 'flex', alignItems: 'center', gap: 1, py: 1 }}>
                            {icon}
                            <Box>
                              <Typography variant="body1" fontWeight={500}>
                                {formatKey.toUpperCase()}
                              </Typography>
                              <Typography variant="body2" color="text.secondary">
                                {formatDescriptions[formatKey as ExportFormat]}
                              </Typography>
                            </Box>
                          </Box>
                        }
                        sx={{
                          mx: 0,
                          p: 1,
                          borderRadius: 1,
                          '&:hover': {
                            backgroundColor: 'action.hover',
                          },
                        }}
                      />
                    ))}
                  </RadioGroup>
                </FormControl>
              </CardContent>
            </Card>

            {/* File Settings */}
            <Card variant="outlined">
              <CardContent>
                <Typography variant="h6" sx={{ mb: 2, fontWeight: 600 }}>
                  File Settings
                </Typography>
                <Stack spacing={2}>
                  <TextField
                    label="Filename"
                    value={filename}
                    onChange={(e) => setFilename(e.target.value)}
                    placeholder={getDefaultFilename()}
                    fullWidth
                    helperText="Leave empty to use default filename"
                    InputProps={{
                      endAdornment: (
                        <Typography variant="body2" color="text.secondary">
                          .{format === 'excel' ? 'xlsx' : format}
                        </Typography>
                      ),
                    }}
                  />
                  
                  <Box sx={{ display: 'flex', gap: 2 }}>
                    <FormControlLabel
                      control={
                        <Switch
                          checked={includeTimestamp}
                          onChange={(e) => setIncludeTimestamp(e.target.checked)}
                        />
                      }
                      label="Include timestamp in filename"
                    />
                    
                    {format !== 'pdf' && (
                      <FormControlLabel
                        control={
                          <Switch
                            checked={includeHeaders}
                            onChange={(e) => setIncludeHeaders(e.target.checked)}
                          />
                        }
                        label="Include column headers"
                      />
                    )}
                  </Box>
                </Stack>
              </CardContent>
            </Card>

            {/* Date Range Filter */}
            {dataType !== 'charts' && (
              <Card variant="outlined">
                <CardContent>
                  <Box sx={{ display: 'flex', alignItems: 'center', gap: 1, mb: 2 }}>
                    <DateRangeIcon color="primary" />
                    <Typography variant="h6" fontWeight={600}>
                      Date Range Filter
                    </Typography>
                    <Tooltip title="Filter data by date range (optional)">
                      <InfoIcon fontSize="small" color="action" />
                    </Tooltip>
                  </Box>
                  
                  <Box sx={{ display: 'flex', gap: 2 }}>
                    <DatePicker
                      label="Start Date"
                      value={dateRange.start}
                      onChange={(date) => setDateRange(prev => ({ ...prev, start: date }))}
                      slotProps={{
                        textField: { fullWidth: true },
                      }}
                    />
                    <DatePicker
                      label="End Date"
                      value={dateRange.end}
                      onChange={(date) => setDateRange(prev => ({ ...prev, end: date }))}
                      slotProps={{
                        textField: { fullWidth: true },
                      }}
                    />
                  </Box>
                </CardContent>
              </Card>
            )}

            {/* Export Preview */}
            <Card variant="outlined" sx={{ bgcolor: 'background.default' }}>
              <CardContent>
                <Typography variant="h6" sx={{ mb: 2, fontWeight: 600 }}>
                  Export Preview
                </Typography>
                <Stack spacing={1}>
                  <Box sx={{ display: 'flex', justifyContent: 'space-between' }}>
                    <Typography variant="body2" color="text.secondary">
                      Format:
                    </Typography>
                    <Typography variant="body2" fontWeight={500}>
                      {format.toUpperCase()}
                    </Typography>
                  </Box>
                  <Box sx={{ display: 'flex', justifyContent: 'space-between' }}>
                    <Typography variant="body2" color="text.secondary">
                      Records:
                    </Typography>
                    <Typography variant="body2" fontWeight={500}>
                      {getFormatCount() || 'Sample data'}
                    </Typography>
                  </Box>
                  <Box sx={{ display: 'flex', justifyContent: 'space-between' }}>
                    <Typography variant="body2" color="text.secondary">
                      Filename:
                    </Typography>
                    <Typography variant="body2" fontWeight={500}>
                      {filename || getDefaultFilename()}
                      {includeTimestamp && '_YYYY-MM-DD_HH-mm-ss'}
                      .{format === 'excel' ? 'xlsx' : format}
                    </Typography>
                  </Box>
                </Stack>
              </CardContent>
            </Card>

            {/* Export Progress */}
            {isExporting && (
              <Card variant="outlined">
                <CardContent>
                  <Box sx={{ display: 'flex', alignItems: 'center', gap: 2, mb: 2 }}>
                    <DownloadIcon color="primary" />
                    <Typography variant="h6">Exporting...</Typography>
                  </Box>
                  <LinearProgress />
                  <Typography variant="body2" color="text.secondary" sx={{ mt: 1 }}>
                    Generating {format.toUpperCase()} file...
                  </Typography>
                </CardContent>
              </Card>
            )}

            {/* Export Result */}
            {exportResult && (
              <Alert
                severity={exportResult.success ? 'success' : 'error'}
                action={
                  exportResult.success ? (
                    <Button
                      color="inherit"
                      size="small"
                      onClick={handleClose}
                    >
                      CLOSE
                    </Button>
                  ) : (
                    <Button
                      color="inherit"
                      size="small"
                      onClick={() => setExportResult(null)}
                    >
                      RETRY
                    </Button>
                  )
                }
              >
                {exportResult.success ? (
                  <Box>
                    <Typography variant="body1" fontWeight={500}>
                      Export Successful!
                    </Typography>
                    <Typography variant="body2">
                      {exportResult.filename} 
                      {exportResult.size && ` (${exportResult.size})`}
                      {exportResult.recordCount && ` - ${exportResult.recordCount} records`}
                    </Typography>
                  </Box>
                ) : (
                  <Box>
                    <Typography variant="body1" fontWeight={500}>
                      Export Failed
                    </Typography>
                    <Typography variant="body2">
                      {exportResult.error}
                    </Typography>
                  </Box>
                )}
              </Alert>
            )}
          </Stack>
        </DialogContent>

        <DialogActions sx={{ p: 3, pt: 0 }}>
          <Button
            onClick={handleClose}
            variant="outlined"
            disabled={isExporting}
          >
            Cancel
          </Button>
          <Button
            onClick={handleExport}
            variant="contained"
            disabled={isExporting}
            startIcon={<DownloadIcon />}
          >
            {isExporting ? 'Exporting...' : `Export ${format.toUpperCase()}`}
          </Button>
        </DialogActions>
      </Dialog>
    </LocalizationProvider>
  );
};

export default ExportDialog; 