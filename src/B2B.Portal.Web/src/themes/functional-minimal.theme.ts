import type { PortalThemeDefinition } from './theme.schema';

export const functionalMinimalTheme: PortalThemeDefinition = {
  id: 'functional-minimal',
  displayName: 'Functional Minimal',
  branding: { productName: 'B2B Guest Governance' },
  colors: {
    brandPrimary: '#2563A6',
    brandSecondary: '#52606D',
    accent: '#2563A6',
    navigationBackground: '#F3F4F6',
    navigationForeground: '#1F2933',
    headerBackground: '#FFFFFF',
    pageBackground: '#F8F9FA',
    surfaceBackground: '#FFFFFF',
    border: '#D6D9DD',
    textPrimary: '#202124',
    textSecondary: '#5F6368',
    success: '#2E7D32',
    warning: '#9A6700',
    error: '#B3261E',
    info: '#2563A6',
  },
  shape: { radiusSmall: 2, radiusMedium: 4, radiusLarge: 6, cardRadius: 4 },
  density: { spacing: 'compact', table: 'compact' },
  navigation: { width: 224, compactWidth: 64 },
  typography: { fontFamily: 'Segoe UI, Arial, sans-serif', baseFontSize: 13 },
  charts: { palette: ['#2563A6', '#52606D', '#6B7C8F', '#8A99A8'] },
};

