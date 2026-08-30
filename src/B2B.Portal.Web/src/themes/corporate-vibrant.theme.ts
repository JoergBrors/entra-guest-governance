import type { PortalThemeDefinition } from './theme.schema';

export const corporateVibrantTheme: PortalThemeDefinition = {
  id: 'corporate-vibrant',
  displayName: 'Corporate Vibrant',
  branding: { productName: 'B2B Guest Governance Portal' },
  colors: {
    brandPrimary: '#1457C9',
    brandSecondary: '#6B4EFF',
    accent: '#00A4EF',
    navigationBackground: '#082A4A',
    navigationForeground: '#FFFFFF',
    headerBackground: '#FFFFFF',
    pageBackground: '#F4F7FB',
    surfaceBackground: '#FFFFFF',
    border: '#D9E2EC',
    textPrimary: '#172B4D',
    textSecondary: '#5E6C84',
    success: '#138A4B',
    warning: '#E89A16',
    error: '#D64545',
    info: '#1677FF',
  },
  shape: { radiusSmall: 4, radiusMedium: 8, radiusLarge: 14, cardRadius: 12 },
  density: { spacing: 'comfortable', table: 'normal' },
  navigation: { width: 248, compactWidth: 72 },
  typography: { fontFamily: 'Segoe UI, Arial, sans-serif', baseFontSize: 14 },
  charts: { palette: ['#1457C9', '#00A4EF', '#26A269', '#6B4EFF', '#E89A16', '#D64545'] },
};

