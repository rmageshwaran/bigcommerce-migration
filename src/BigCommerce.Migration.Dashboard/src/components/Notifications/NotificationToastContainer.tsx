import React from 'react';
import { ToastContainer } from 'react-toastify';
import 'react-toastify/dist/ReactToastify.css';
import { useThemeMode } from '../../contexts/ThemeContext';

const NotificationToastContainer: React.FC = () => {
  const { actualMode } = useThemeMode();

  return (
    <ToastContainer
      position="top-right"
      autoClose={4000}
      hideProgressBar={false}
      newestOnTop={true}
      closeOnClick
      rtl={false}
      pauseOnFocusLoss
      draggable
      pauseOnHover
      theme={actualMode}
      style={{
        fontSize: '14px'
      }}
      toastStyle={{
        borderRadius: '8px',
        boxShadow: actualMode === 'dark' 
          ? '0 4px 12px rgba(0, 0, 0, 0.4)' 
          : '0 4px 12px rgba(0, 0, 0, 0.15)',
        fontFamily: '"Roboto", "Helvetica", "Arial", sans-serif',
        backgroundColor: actualMode === 'dark' ? '#1e1e1e' : '#ffffff',
        color: actualMode === 'dark' ? 'rgba(255, 255, 255, 0.87)' : 'rgba(0, 0, 0, 0.87)',
        border: actualMode === 'dark' ? '1px solid rgba(255, 255, 255, 0.12)' : 'none'
      }}
    />
  );
};

export default NotificationToastContainer; 