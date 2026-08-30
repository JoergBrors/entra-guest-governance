import type { Theme } from '@fluentui/react-components';
import type { CSSProperties } from 'react';

export interface PortalThemeDefinition {
  id: string;
  displayName: string;
  branding: {
    productName: string;
    logoUrl?: string;
    compactLogoUrl?: string;
    faviconUrl?: string;
  };
  colors: {
    brandPrimary: string;
    brandSecondary: string;
    accent: string;
    navigationBackground: string;
    navigationForeground: string;
    headerBackground: string;
    pageBackground: string;
    surfaceBackground: string;
    border: string;
    textPrimary: string;
    textSecondary: string;
    success: string;
    warning: string;
    error: string;
    info: string;
  };
  shape: {
    radiusSmall: number;
    radiusMedium: number;
    radiusLarge: number;
    cardRadius: number;
  };
  density: {
    spacing: 'compact' | 'normal' | 'comfortable';
    table: 'compact' | 'normal' | 'comfortable';
  };
  navigation: {
    width: number;
    compactWidth: number;
  };
  typography: {
    fontFamily: string;
    baseFontSize: number;
  };
  charts?: {
    palette: string[];
  };
}

export interface LoadedPortalTheme {
  definition: PortalThemeDefinition;
  fluentTheme: Theme;
  cssVariables: CSSProperties;
}
