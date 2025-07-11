import React, { useState } from 'react';
import {
  IconButton,
  Tooltip,
  Menu,
  MenuItem,
  ListItemIcon,
  ListItemText,
  Fade,
  Box,
  Typography,
  useTheme,
} from '@mui/material';
import {
  DarkMode as DarkModeIcon,
  LightMode as LightModeIcon,
  SettingsBrightness as SystemIcon,
  Check as CheckIcon,
} from '@mui/icons-material';
import { useThemeMode } from '../../contexts/ThemeContext';
import type { ThemeMode } from '../../contexts/ThemeContext';

interface ThemeMenuItemProps {
  mode: ThemeMode;
  currentMode: ThemeMode;
  icon: React.ReactNode;
  label: string;
  description: string;
  onClick: () => void;
}

const ThemeMenuItem: React.FC<ThemeMenuItemProps> = ({
  mode,
  currentMode,
  icon,
  label,
  description,
  onClick,
}) => {
  const isSelected = currentMode === mode;

  return (
    <MenuItem
      onClick={onClick}
      sx={{
        minWidth: 200,
        py: 1.5,
        position: 'relative',
        transition: 'background-color 0.2s ease',
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
            borderRadius: '50%',
            transition: 'all 0.2s ease',
            backgroundColor: isSelected ? 'primary.main' : 'transparent',
            color: isSelected ? 'primary.contrastText' : 'text.secondary',
          }}
        >
          {icon}
        </Box>
      </ListItemIcon>
      <ListItemText
        primary={
          <Box sx={{ display: 'flex', alignItems: 'center', gap: 1 }}>
            <Typography variant="body1" fontWeight={isSelected ? 600 : 400}>
              {label}
            </Typography>
            {isSelected && (
              <CheckIcon
                sx={{
                  fontSize: 16,
                  color: 'primary.main',
                  animation: 'fadeIn 0.2s ease',
                  '@keyframes fadeIn': {
                    from: { opacity: 0, transform: 'scale(0.8)' },
                    to: { opacity: 1, transform: 'scale(1)' },
                  },
                }}
              />
            )}
          </Box>
        }
        secondary={
          <Typography variant="body2" color="text.secondary" sx={{ mt: 0.5 }}>
            {description}
          </Typography>
        }
      />
    </MenuItem>
  );
};

const ThemeToggle: React.FC = () => {
  const theme = useTheme();
  const { mode, actualMode, setMode, isSystemMode } = useThemeMode();
  const [anchorEl, setAnchorEl] = useState<HTMLElement | null>(null);
  const open = Boolean(anchorEl);

  const handleClick = (event: React.MouseEvent<HTMLElement>) => {
    setAnchorEl(event.currentTarget);
  };

  const handleClose = () => {
    setAnchorEl(null);
  };

  const handleModeSelect = (selectedMode: ThemeMode) => {
    setMode(selectedMode);
    handleClose();
  };

  const getMainIcon = () => {
    if (isSystemMode) {
      return <SystemIcon />;
    }
    return actualMode === 'dark' ? <DarkModeIcon /> : <LightModeIcon />;
  };

  const getTooltipText = () => {
    if (isSystemMode) {
      return `System theme (${actualMode})`;
    }
    return `${actualMode === 'dark' ? 'Dark' : 'Light'} theme`;
  };

  const themeOptions: Array<{
    mode: ThemeMode;
    icon: React.ReactNode;
    label: string;
    description: string;
  }> = [
    {
      mode: 'light',
      icon: <LightModeIcon />,
      label: 'Light',
      description: 'Clean, bright appearance',
    },
    {
      mode: 'dark',
      icon: <DarkModeIcon />,
      label: 'Dark',
      description: 'Easy on the eyes in low light',
    },
    {
      mode: 'system',
      icon: <SystemIcon />,
      label: 'System',
      description: 'Follows your device settings',
    },
  ];

  return (
    <>
      <Tooltip title={getTooltipText()} arrow>
        <IconButton
          onClick={handleClick}
          color="inherit"
          sx={{
            transition: 'all 0.3s ease',
            transform: open ? 'rotate(180deg)' : 'rotate(0deg)',
            '&:hover': {
              backgroundColor: 'action.hover',
              transform: open ? 'rotate(180deg) scale(1.1)' : 'scale(1.1)',
            },
          }}
          aria-label="Toggle theme"
          aria-controls={open ? 'theme-menu' : undefined}
          aria-haspopup="true"
          aria-expanded={open ? 'true' : undefined}
        >
          <Box
            sx={{
              display: 'flex',
              alignItems: 'center',
              justifyContent: 'center',
              width: 24,
              height: 24,
              transition: 'all 0.3s ease',
              color: 'inherit',
            }}
          >
            {getMainIcon()}
          </Box>
        </IconButton>
      </Tooltip>

      <Menu
        id="theme-menu"
        anchorEl={anchorEl}
        open={open}
        onClose={handleClose}
        TransitionComponent={Fade}
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
            mt: 1,
            borderRadius: 2,
            minWidth: 240,
            boxShadow: theme.shadows[8],
            border: `1px solid ${theme.palette.divider}`,
            overflow: 'visible',
            '&::before': {
              content: '""',
              display: 'block',
              position: 'absolute',
              top: 0,
              right: 14,
              width: 10,
              height: 10,
              bgcolor: 'background.paper',
              transform: 'translateY(-50%) rotate(45deg)',
              border: `1px solid ${theme.palette.divider}`,
              borderBottom: 'none',
              borderRight: 'none',
            },
          },
        }}
        MenuListProps={{
          sx: {
            py: 1,
          },
        }}
      >
        <Box sx={{ px: 2, py: 1, borderBottom: `1px solid ${theme.palette.divider}` }}>
          <Typography variant="subtitle2" color="text.secondary" fontWeight={600}>
            Theme Settings
          </Typography>
        </Box>

        {themeOptions.map((option) => (
          <ThemeMenuItem
            key={option.mode}
            mode={option.mode}
            currentMode={mode}
            icon={option.icon}
            label={option.label}
            description={option.description}
            onClick={() => handleModeSelect(option.mode)}
          />
        ))}

        <Box
          sx={{
            px: 2,
            py: 1,
            mt: 1,
            borderTop: `1px solid ${theme.palette.divider}`,
            backgroundColor: 'background.default',
          }}
        >
          <Typography variant="caption" color="text.secondary">
            {isSystemMode
              ? `Currently using ${actualMode} mode (system preference)`
              : `${actualMode === 'dark' ? 'Dark' : 'Light'} mode active`}
          </Typography>
        </Box>
      </Menu>
    </>
  );
};

export default ThemeToggle; 