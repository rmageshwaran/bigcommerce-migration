import React, { createContext, useContext, useState, useEffect } from 'react';
import type { ReactNode } from 'react';
import { ThemeProvider, createTheme } from '@mui/material/styles';
import type { PaletteMode } from '@mui/material';

// Theme mode type
export type ThemeMode = 'light' | 'dark' | 'system';

// Theme context interface
interface ThemeContextType {
  mode: ThemeMode;
  actualMode: PaletteMode; // The actual mode being used (light/dark)
  setMode: (mode: ThemeMode) => void;
  toggleMode: () => void;
  isSystemMode: boolean;
}

// Create context
const ThemeContext = createContext<ThemeContextType | undefined>(undefined);

// Custom hook to use theme context
export const useThemeMode = (): ThemeContextType => {
  const context = useContext(ThemeContext);
  if (!context) {
    throw new Error('useThemeMode must be used within a ThemeContextProvider');
  }
  return context;
};

// Theme configuration
const getThemeConfig = (mode: PaletteMode) => {
  const isLight = mode === 'light';
  
  return createTheme({
    palette: {
      mode,
      primary: {
        main: isLight ? '#1976d2' : '#90caf9',
        light: isLight ? '#42a5f5' : '#bbdefb',
        dark: isLight ? '#1565c0' : '#64b5f6',
        contrastText: isLight ? '#ffffff' : '#000000',
      },
      secondary: {
        main: isLight ? '#dc004e' : '#f48fb1',
        light: isLight ? '#e91e63' : '#f8bbd9',
        dark: isLight ? '#c51162' : '#f06292',
        contrastText: isLight ? '#ffffff' : '#000000',
      },
      background: {
        default: isLight ? '#f5f5f5' : '#121212',
        paper: isLight ? '#ffffff' : '#1e1e1e',
      },
      text: {
        primary: isLight ? 'rgba(0, 0, 0, 0.87)' : 'rgba(255, 255, 255, 0.87)',
        secondary: isLight ? 'rgba(0, 0, 0, 0.6)' : 'rgba(255, 255, 255, 0.6)',
      },
      divider: isLight ? 'rgba(0, 0, 0, 0.12)' : 'rgba(255, 255, 255, 0.12)',
      action: {
        hover: isLight ? 'rgba(0, 0, 0, 0.04)' : 'rgba(255, 255, 255, 0.08)',
        selected: isLight ? 'rgba(0, 0, 0, 0.08)' : 'rgba(255, 255, 255, 0.12)',
        disabled: isLight ? 'rgba(0, 0, 0, 0.26)' : 'rgba(255, 255, 255, 0.3)',
        disabledBackground: isLight ? 'rgba(0, 0, 0, 0.12)' : 'rgba(255, 255, 255, 0.12)',
      },
      // Custom colors for migration dashboard
      success: {
        main: isLight ? '#2e7d32' : '#4caf50',
        light: isLight ? '#4caf50' : '#81c784',
        dark: isLight ? '#1b5e20' : '#388e3c',
        contrastText: '#ffffff',
      },
      warning: {
        main: isLight ? '#ed6c02' : '#ff9800',
        light: isLight ? '#ff9800' : '#ffb74d',
        dark: isLight ? '#e65100' : '#f57c00',
        contrastText: '#ffffff',
      },
      error: {
        main: isLight ? '#d32f2f' : '#f44336',
        light: isLight ? '#f44336' : '#e57373',
        dark: isLight ? '#c62828' : '#d32f2f',
        contrastText: '#ffffff',
      },
      info: {
        main: isLight ? '#0288d1' : '#29b6f6',
        light: isLight ? '#29b6f6' : '#4fc3f7',
        dark: isLight ? '#01579b' : '#0288d1',
        contrastText: '#ffffff',
      },
    },
    typography: {
      fontFamily: '"Roboto", "Helvetica", "Arial", sans-serif',
      h1: {
        fontWeight: isLight ? 300 : 400,
      },
      h2: {
        fontWeight: isLight ? 400 : 400,
      },
      h3: {
        fontWeight: isLight ? 500 : 500,
      },
      h4: {
        fontWeight: isLight ? 500 : 500,
      },
      h5: {
        fontWeight: isLight ? 500 : 500,
      },
      h6: {
        fontWeight: isLight ? 500 : 600,
      },
    },
    components: {
      // Card component customization
      MuiCard: {
        styleOverrides: {
          root: {
            backgroundColor: isLight ? '#ffffff' : '#1e1e1e',
            boxShadow: isLight 
              ? '0 2px 8px rgba(0, 0, 0, 0.1)' 
              : '0 2px 8px rgba(0, 0, 0, 0.3)',
            border: isLight ? 'none' : '1px solid rgba(255, 255, 255, 0.12)',
            transition: 'box-shadow 0.3s ease, border-color 0.3s ease',
            '&:hover': {
              boxShadow: isLight 
                ? '0 4px 16px rgba(0, 0, 0, 0.15)' 
                : '0 4px 16px rgba(0, 0, 0, 0.4)',
            },
          },
        },
      },
      // AppBar component customization
      MuiAppBar: {
        styleOverrides: {
          root: {
            backgroundColor: isLight ? '#1976d2' : '#1e1e1e',
            color: isLight ? '#ffffff' : 'rgba(255, 255, 255, 0.87)',
            boxShadow: isLight 
              ? '0 2px 4px rgba(0, 0, 0, 0.1)' 
              : '0 2px 4px rgba(0, 0, 0, 0.3)',
          },
        },
      },
      // Drawer component customization
      MuiDrawer: {
        styleOverrides: {
          paper: {
            backgroundColor: isLight ? '#ffffff' : '#1e1e1e',
            borderRight: isLight ? '1px solid rgba(0, 0, 0, 0.12)' : '1px solid rgba(255, 255, 255, 0.12)',
          },
        },
      },
      // Menu component customization
      MuiMenu: {
        styleOverrides: {
          paper: {
            backgroundColor: isLight ? '#ffffff' : '#1e1e1e',
            border: isLight ? 'none' : '1px solid rgba(255, 255, 255, 0.12)',
            boxShadow: isLight 
              ? '0 4px 20px rgba(0, 0, 0, 0.15)' 
              : '0 4px 20px rgba(0, 0, 0, 0.4)',
          },
        },
      },
      // Button component customization
      MuiButton: {
        styleOverrides: {
          root: {
            textTransform: 'none', // Disable uppercase transformation
            borderRadius: '8px',
            transition: 'all 0.2s ease',
          },
          contained: {
            boxShadow: isLight 
              ? '0 2px 4px rgba(0, 0, 0, 0.1)' 
              : '0 2px 4px rgba(0, 0, 0, 0.3)',
            '&:hover': {
              boxShadow: isLight 
                ? '0 4px 8px rgba(0, 0, 0, 0.15)' 
                : '0 4px 8px rgba(0, 0, 0, 0.4)',
            },
          },
        },
      },
      // Chip component customization
      MuiChip: {
        styleOverrides: {
          root: {
            borderRadius: '16px',
            transition: 'all 0.2s ease',
          },
          filled: {
            '&:hover': {
              boxShadow: isLight 
                ? '0 2px 4px rgba(0, 0, 0, 0.1)' 
                : '0 2px 4px rgba(0, 0, 0, 0.3)',
            },
          },
        },
      },
      // Linear Progress customization
      MuiLinearProgress: {
        styleOverrides: {
          root: {
            borderRadius: '4px',
            backgroundColor: isLight ? 'rgba(0, 0, 0, 0.1)' : 'rgba(255, 255, 255, 0.1)',
          },
          bar: {
            borderRadius: '4px',
          },
        },
      },
      // TextField customization
      MuiTextField: {
        styleOverrides: {
          root: {
            '& .MuiOutlinedInput-root': {
              borderRadius: '8px',
              transition: 'border-color 0.2s ease, box-shadow 0.2s ease',
              '&:hover': {
                '& .MuiOutlinedInput-notchedOutline': {
                  borderColor: isLight ? 'rgba(0, 0, 0, 0.4)' : 'rgba(255, 255, 255, 0.4)',
                },
              },
              '&.Mui-focused': {
                boxShadow: isLight 
                  ? '0 0 0 2px rgba(25, 118, 210, 0.2)' 
                  : '0 0 0 2px rgba(144, 202, 249, 0.2)',
              },
            },
          },
        },
      },
    },
    transitions: {
      duration: {
        shortest: 150,
        shorter: 200,
        short: 250,
        standard: 300,
        complex: 375,
        enteringScreen: 225,
        leavingScreen: 195,
      },
      easing: {
        easeInOut: 'cubic-bezier(0.4, 0, 0.2, 1)',
        easeOut: 'cubic-bezier(0.0, 0, 0.2, 1)',
        easeIn: 'cubic-bezier(0.4, 0, 1, 1)',
        sharp: 'cubic-bezier(0.4, 0, 0.6, 1)',
      },
    },
  });
};

// Detect system theme preference
const getSystemTheme = (): PaletteMode => {
  if (typeof window !== 'undefined' && window.matchMedia) {
    return window.matchMedia('(prefers-color-scheme: dark)').matches ? 'dark' : 'light';
  }
  return 'light';
};

// Get stored theme preference
const getStoredTheme = (): ThemeMode => {
  if (typeof window !== 'undefined') {
    const stored = localStorage.getItem('themeMode');
    if (stored && ['light', 'dark', 'system'].includes(stored)) {
      return stored as ThemeMode;
    }
  }
  return 'system';
};

// Store theme preference
const storeTheme = (mode: ThemeMode): void => {
  if (typeof window !== 'undefined') {
    localStorage.setItem('themeMode', mode);
  }
};

// Props interface
interface ThemeContextProviderProps {
  children: ReactNode;
}

// Theme context provider component
export const ThemeContextProvider: React.FC<ThemeContextProviderProps> = ({ children }) => {
  const [mode, setModeState] = useState<ThemeMode>('system');
  const [systemTheme, setSystemTheme] = useState<PaletteMode>(getSystemTheme());

  // Calculate actual mode
  const actualMode: PaletteMode = mode === 'system' ? systemTheme : mode;

  // Initialize theme from storage
  useEffect(() => {
    const storedMode = getStoredTheme();
    setModeState(storedMode);
  }, []);

  // Listen for system theme changes
  useEffect(() => {
    if (typeof window !== 'undefined' && window.matchMedia) {
      const mediaQuery = window.matchMedia('(prefers-color-scheme: dark)');
      
      const handleChange = (e: MediaQueryListEvent) => {
        setSystemTheme(e.matches ? 'dark' : 'light');
      };

      mediaQuery.addEventListener('change', handleChange);
      return () => mediaQuery.removeEventListener('change', handleChange);
    }
  }, []);

  // Set mode function
  const setMode = (newMode: ThemeMode) => {
    setModeState(newMode);
    storeTheme(newMode);
  };

  // Toggle mode function
  const toggleMode = () => {
    const newMode = actualMode === 'light' ? 'dark' : 'light';
    setMode(newMode);
  };

  // Check if system mode
  const isSystemMode = mode === 'system';

  // Create theme
  const theme = getThemeConfig(actualMode);

  // Context value
  const contextValue: ThemeContextType = {
    mode,
    actualMode,
    setMode,
    toggleMode,
    isSystemMode,
  };

  return (
    <ThemeContext.Provider value={contextValue}>
      <ThemeProvider theme={theme}>
        {children}
      </ThemeProvider>
    </ThemeContext.Provider>
  );
}; 