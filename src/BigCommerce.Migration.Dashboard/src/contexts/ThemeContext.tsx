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
        main: isLight ? '#4285f4' : '#5a95f5',
        light: isLight ? '#6aa3f5' : '#7db0f7',
        dark: isLight ? '#3367d6' : '#2c5aa0',
        contrastText: '#ffffff',
      },
      secondary: {
        main: isLight ? '#34a853' : '#5bb974',
        light: isLight ? '#57c267' : '#7cc488',
        dark: isLight ? '#137333' : '#0d5016',
        contrastText: '#ffffff',
      },
      background: {
        default: isLight ? '#f8f9fa' : '#121212',
        paper: isLight ? '#ffffff' : '#1e1e1e',
      },
      text: {
        primary: isLight ? '#202124' : 'rgba(255, 255, 255, 0.87)',
        secondary: isLight ? '#5f6368' : 'rgba(255, 255, 255, 0.6)',
      },
      divider: isLight ? '#e8eaed' : 'rgba(255, 255, 255, 0.12)',
      action: {
        hover: isLight ? 'rgba(60, 64, 67, 0.08)' : 'rgba(255, 255, 255, 0.08)',
        selected: isLight ? 'rgba(66, 133, 244, 0.12)' : 'rgba(255, 255, 255, 0.12)',
        disabled: isLight ? 'rgba(60, 64, 67, 0.26)' : 'rgba(255, 255, 255, 0.3)',
        disabledBackground: isLight ? 'rgba(60, 64, 67, 0.12)' : 'rgba(255, 255, 255, 0.12)',
      },
      // Custom colors for migration dashboard
      success: {
        main: isLight ? '#34a853' : '#4caf50',
        light: isLight ? '#57c267' : '#81c784',
        dark: isLight ? '#137333' : '#388e3c',
        contrastText: '#ffffff',
      },
      warning: {
        main: isLight ? '#fbbc04' : '#ff9800',
        light: isLight ? '#fcc934' : '#ffb74d',
        dark: isLight ? '#f29900' : '#f57c00',
        contrastText: isLight ? '#202124' : '#ffffff',
      },
      error: {
        main: isLight ? '#ea4335' : '#f44336',
        light: isLight ? '#ee675c' : '#e57373',
        dark: isLight ? '#d33b2c' : '#d32f2f',
        contrastText: '#ffffff',
      },
      info: {
        main: isLight ? '#4285f4' : '#29b6f6',
        light: isLight ? '#6aa3f5' : '#4fc3f7',
        dark: isLight ? '#3367d6' : '#0288d1',
        contrastText: '#ffffff',
      },
    },
    typography: {
      fontFamily: '"Google Sans", "Roboto", "Helvetica", "Arial", sans-serif',
      h1: {
        fontWeight: isLight ? 400 : 400,
        fontSize: '2.125rem',
      },
      h2: {
        fontWeight: isLight ? 400 : 400,
        fontSize: '1.875rem',
      },
      h3: {
        fontWeight: isLight ? 500 : 500,
        fontSize: '1.5rem',
      },
      h4: {
        fontWeight: isLight ? 500 : 500,
        fontSize: '1.25rem',
      },
      h5: {
        fontWeight: isLight ? 500 : 500,
        fontSize: '1.125rem',
      },
      h6: {
        fontWeight: isLight ? 600 : 600,
        fontSize: '1rem',
      },
      body1: {
        fontSize: '0.875rem',
        lineHeight: 1.5,
      },
      body2: {
        fontSize: '0.75rem',
        lineHeight: 1.4,
      },
    },
    components: {
      // Card component customization
      MuiCard: {
        styleOverrides: {
          root: {
            backgroundColor: isLight ? '#ffffff' : '#1e1e1e',
            boxShadow: isLight 
              ? '0 1px 2px 0 rgba(60, 64, 67, 0.3), 0 1px 3px 1px rgba(60, 64, 67, 0.15)' 
              : '0 2px 8px rgba(0, 0, 0, 0.3)',
            border: isLight ? '1px solid #e8eaed' : '1px solid rgba(255, 255, 255, 0.12)',
            borderRadius: '8px',
            transition: 'box-shadow 0.3s ease, border-color 0.3s ease',
            '&:hover': {
              boxShadow: isLight 
                ? '0 1px 3px 0 rgba(60, 64, 67, 0.3), 0 4px 8px 3px rgba(60, 64, 67, 0.15)' 
                : '0 4px 16px rgba(0, 0, 0, 0.4)',
            },
          },
        },
      },
      // AppBar component customization
      MuiAppBar: {
        styleOverrides: {
          root: {
            backgroundColor: isLight ? '#ffffff' : '#1e1e1e',
            color: isLight ? '#202124' : 'rgba(255, 255, 255, 0.87)',
            boxShadow: isLight 
              ? '0 1px 2px 0 rgba(60, 64, 67, 0.3), 0 1px 3px 1px rgba(60, 64, 67, 0.15)' 
              : '0 2px 4px rgba(0, 0, 0, 0.3)',
            borderBottom: isLight ? '1px solid #e8eaed' : '1px solid rgba(255, 255, 255, 0.12)',
          },
        },
      },
      // Tab component customization
      MuiTab: {
        styleOverrides: {
          root: {
            textTransform: 'none',
            fontWeight: 500,
            fontSize: '0.875rem',
            minHeight: '48px',
            color: isLight ? '#5f6368' : 'rgba(255, 255, 255, 0.6)',
            '&.Mui-selected': {
              color: isLight ? '#4285f4' : '#5a95f5',
              fontWeight: 600,
            },
          },
        },
      },
      MuiTabs: {
        styleOverrides: {
          root: {
            '& .MuiTabs-indicator': {
              backgroundColor: isLight ? '#4285f4' : '#5a95f5',
              height: '3px',
            },
          },
        },
      },
      // Button component customization
      MuiButton: {
        styleOverrides: {
          root: {
            textTransform: 'none',
            borderRadius: '6px',
            fontWeight: 500,
            fontSize: '0.875rem',
            transition: 'all 0.2s ease',
            boxShadow: 'none',
            '&:hover': {
              boxShadow: isLight 
                ? '0 1px 2px 0 rgba(60, 64, 67, 0.3), 0 1px 3px 1px rgba(60, 64, 67, 0.15)' 
                : '0 4px 8px rgba(0, 0, 0, 0.4)',
            },
          },
          contained: {
            boxShadow: 'none',
            '&:hover': {
              boxShadow: isLight 
                ? '0 1px 3px 0 rgba(60, 64, 67, 0.3), 0 4px 8px 3px rgba(60, 64, 67, 0.15)' 
                : '0 4px 8px rgba(0, 0, 0, 0.4)',
            },
          },
          outlined: {
            borderColor: isLight ? '#dadce0' : 'rgba(255, 255, 255, 0.23)',
            '&:hover': {
              borderColor: isLight ? '#4285f4' : '#5a95f5',
              backgroundColor: isLight ? 'rgba(66, 133, 244, 0.04)' : 'rgba(90, 149, 245, 0.08)',
            },
          },
        },
      },
      // Chip component customization
      MuiChip: {
        styleOverrides: {
          root: {
            borderRadius: '16px',
            fontSize: '0.75rem',
            fontWeight: 500,
            transition: 'all 0.2s ease',
          },
          filled: {
            '&:hover': {
              boxShadow: isLight 
                ? '0 1px 2px 0 rgba(60, 64, 67, 0.3)' 
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
              borderRadius: '6px',
              fontSize: '0.875rem',
              backgroundColor: isLight ? '#ffffff' : '#2d2d2d',
              transition: 'border-color 0.2s ease, box-shadow 0.2s ease',
              '& .MuiOutlinedInput-notchedOutline': {
                borderColor: isLight ? '#dadce0' : 'rgba(255, 255, 255, 0.23)',
              },
              '&:hover .MuiOutlinedInput-notchedOutline': {
                borderColor: isLight ? '#4285f4' : '#5a95f5',
              },
              '&.Mui-focused': {
                '& .MuiOutlinedInput-notchedOutline': {
                  borderColor: isLight ? '#4285f4' : '#5a95f5',
                  borderWidth: '2px',
                },
              },
            },
          },
        },
      },
      // FormControl customization
      MuiFormControl: {
        styleOverrides: {
          root: {
            '& .MuiInputLabel-root': {
              fontSize: '0.875rem',
              color: isLight ? '#5f6368' : 'rgba(255, 255, 255, 0.6)',
              '&.Mui-focused': {
                color: isLight ? '#4285f4' : '#5a95f5',
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