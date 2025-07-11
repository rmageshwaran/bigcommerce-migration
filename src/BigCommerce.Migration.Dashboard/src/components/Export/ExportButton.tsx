import React, { useState } from 'react';
import {
  Button,
  IconButton,
  Menu,
  MenuItem,
  ListItemIcon,
  ListItemText,
  Tooltip,
  Box,
  Typography,
  Divider,
} from '@mui/material';
import {
  Download as DownloadIcon,
  TableChart as ExcelIcon,
  PictureAsPdf as PdfIcon,
  InsertDriveFile as CsvIcon,
  MoreVert as MoreIcon,
  GetApp as ExportIcon,
} from '@mui/icons-material';
import ExportDialog from './ExportDialog';
import type { ExportFormat } from '../../services/exportService';

interface ExportButtonProps {
  dataType: 'migrations' | 'entities' | 'errors' | 'performance' | 'charts';
  data?: unknown[];
  chartElement?: HTMLElement;
  title?: string;
  variant?: 'button' | 'icon' | 'menu';
  size?: 'small' | 'medium' | 'large';
  disabled?: boolean;
  showQuickExport?: boolean;
  defaultFormat?: ExportFormat;
}

const ExportButton: React.FC<ExportButtonProps> = ({
  dataType,
  data,
  chartElement,
  title,
  variant = 'button',
  size = 'medium',
  disabled = false,
  showQuickExport = true,
  defaultFormat = 'excel',
}) => {
  const [dialogOpen, setDialogOpen] = useState(false);
  const [menuAnchorEl, setMenuAnchorEl] = useState<HTMLElement | null>(null);
  const menuOpen = Boolean(menuAnchorEl);

  const handleDialogOpen = () => {
    setDialogOpen(true);
    setMenuAnchorEl(null);
  };

  const handleDialogClose = () => {
    setDialogOpen(false);
  };

  const handleMenuOpen = (event: React.MouseEvent<HTMLElement>) => {
    setMenuAnchorEl(event.currentTarget);
  };

  const handleMenuClose = () => {
    setMenuAnchorEl(null);
  };

  const quickExportOptions = [
    {
      format: 'excel' as ExportFormat,
      icon: <ExcelIcon />,
      label: 'Export to Excel',
      description: 'XLSX format with formatting',
    },
    {
      format: 'csv' as ExportFormat,
      icon: <CsvIcon />,
      label: 'Export to CSV',
      description: 'Comma-separated values',
    },
    {
      format: 'pdf' as ExportFormat,
      icon: <PdfIcon />,
      label: 'Export to PDF',
      description: 'Portable document format',
    },
  ];

  const getRecordCount = () => {
    if (!data) return 0;
    return Array.isArray(data) ? data.length : 0;
  };

  const renderButton = () => {
    switch (variant) {
      case 'icon':
        return (
          <Tooltip title={`Export ${dataType}`}>
            <IconButton
              onClick={showQuickExport ? handleMenuOpen : handleDialogOpen}
              disabled={disabled}
              size={size}
              color="primary"
            >
              <DownloadIcon />
            </IconButton>
          </Tooltip>
        );

      case 'menu':
        return (
          <Tooltip title={`Export ${dataType}`}>
            <IconButton
              onClick={handleMenuOpen}
              disabled={disabled}
              size={size}
            >
              <MoreIcon />
            </IconButton>
          </Tooltip>
        );

      default:
        return (
          <Button
            onClick={showQuickExport ? handleMenuOpen : handleDialogOpen}
            disabled={disabled}
            size={size}
            variant="outlined"
            startIcon={<DownloadIcon />}
            sx={{
              transition: 'all 0.2s ease',
              '&:hover': {
                transform: 'translateY(-1px)',
                boxShadow: 2,
              },
            }}
          >
            Export
          </Button>
        );
    }
  };

  return (
    <>
      {renderButton()}

      {/* Quick Export Menu */}
      {showQuickExport && (
        <Menu
          anchorEl={menuAnchorEl}
          open={menuOpen}
          onClose={handleMenuClose}
          transformOrigin={{
            vertical: 'top',
            horizontal: 'right',
          }}
          anchorOrigin={{
            vertical: 'bottom',
            horizontal: 'right',
          }}
          PaperProps={{
            sx: {
              minWidth: 250,
              borderRadius: 2,
              boxShadow: 3,
            },
          }}
        >
          {/* Header */}
          <Box sx={{ px: 2, py: 1, borderBottom: 1, borderColor: 'divider' }}>
            <Typography variant="subtitle2" color="text.secondary" fontWeight={600}>
              Quick Export
            </Typography>
            {getRecordCount() > 0 && (
              <Typography variant="caption" color="text.secondary">
                {getRecordCount()} records
              </Typography>
            )}
          </Box>

          {/* Quick Export Options */}
          {quickExportOptions.map((option) => (
            <MenuItem
              key={option.format}
              onClick={() => {
                // TODO: Implement quick export with default settings
                handleDialogOpen();
              }}
              sx={{
                py: 1.5,
                px: 2,
                '&:hover': {
                  backgroundColor: 'action.hover',
                },
              }}
            >
              <ListItemIcon sx={{ minWidth: 40 }}>
                <Box
                  sx={{
                    display: 'flex',
                    alignItems: 'center',
                    justifyContent: 'center',
                    width: 32,
                    height: 32,
                    borderRadius: 1,
                    backgroundColor: 'primary.light',
                    color: 'primary.contrastText',
                  }}
                >
                  {option.icon}
                </Box>
              </ListItemIcon>
              <ListItemText
                primary={
                  <Typography variant="body1" fontWeight={500}>
                    {option.label}
                  </Typography>
                }
                secondary={
                  <Typography variant="body2" color="text.secondary">
                    {option.description}
                  </Typography>
                }
              />
            </MenuItem>
          ))}

          <Divider />

          {/* Advanced Options */}
          <MenuItem
            onClick={handleDialogOpen}
            sx={{
              py: 1.5,
              px: 2,
              backgroundColor: 'action.selected',
              '&:hover': {
                backgroundColor: 'action.focus',
              },
            }}
          >
            <ListItemIcon sx={{ minWidth: 40 }}>
              <Box
                sx={{
                  display: 'flex',
                  alignItems: 'center',
                  justifyContent: 'center',
                  width: 32,
                  height: 32,
                  borderRadius: 1,
                  backgroundColor: 'secondary.light',
                  color: 'secondary.contrastText',
                }}
              >
                <ExportIcon />
              </Box>
            </ListItemIcon>
            <ListItemText
              primary={
                <Typography variant="body1" fontWeight={600}>
                  Advanced Export
                </Typography>
              }
              secondary={
                <Typography variant="body2" color="text.secondary">
                  Custom options, filters, and settings
                </Typography>
              }
            />
          </MenuItem>
        </Menu>
      )}

      {/* Export Dialog */}
      <ExportDialog
        open={dialogOpen}
        onClose={handleDialogClose}
        dataType={dataType}
        title={title}
        data={data}
        chartElement={chartElement}
      />
    </>
  );
};

export default ExportButton; 