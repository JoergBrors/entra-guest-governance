import { webLightTheme, type Theme } from '@fluentui/react-components';
import type { CSSProperties } from 'react';
import { corporateVibrantTheme } from './corporate-vibrant.theme';
import { functionalMinimalTheme } from './functional-minimal.theme';
import type { LoadedPortalTheme, PortalThemeDefinition } from './theme.schema';

const themes = [corporateVibrantTheme, functionalMinimalTheme];

export const DEFAULT_THEME_ID = corporateVibrantTheme.id;

export function listPortalThemes(): PortalThemeDefinition[] {
  return themes;
}

export function validatePortalThemeDefinition(theme: PortalThemeDefinition): boolean {
  return Boolean(
    theme.id &&
    theme.displayName &&
    theme.branding.productName &&
    theme.colors.brandPrimary &&
    theme.colors.navigationBackground &&
    theme.colors.pageBackground &&
    theme.navigation.width > 0 &&
    theme.navigation.compactWidth > 0 &&
    theme.typography.baseFontSize > 0,
  );
}

export function loadPortalTheme(themeId?: string | null): LoadedPortalTheme {
  const definition = themes.find((theme) => theme.id === themeId && validatePortalThemeDefinition(theme))
    ?? corporateVibrantTheme;

  const fluentTheme: Theme = {
    ...webLightTheme,
    colorBrandBackground: definition.colors.brandPrimary,
    colorBrandForeground1: definition.colors.brandPrimary,
    colorNeutralBackground1: definition.colors.surfaceBackground,
    colorNeutralBackground2: definition.colors.pageBackground,
    colorNeutralForeground1: definition.colors.textPrimary,
    colorNeutralForeground2: definition.colors.textSecondary,
    colorNeutralStroke2: definition.colors.border,
    borderRadiusSmall: `${definition.shape.radiusSmall}px`,
    borderRadiusMedium: `${definition.shape.radiusMedium}px`,
    borderRadiusLarge: `${definition.shape.radiusLarge}px`,
    fontFamilyBase: definition.typography.fontFamily,
    fontSizeBase300: `${definition.typography.baseFontSize}px`,
  } as Theme;

  const cssVariables = {
    '--brand-primary': definition.colors.brandPrimary,
    '--brand-secondary': definition.colors.brandSecondary,
    '--brand-accent': definition.colors.accent,
    '--nav-bg': definition.colors.navigationBackground,
    '--nav-fg': definition.colors.navigationForeground,
    '--header-bg': definition.colors.headerBackground,
    '--page-bg': definition.colors.pageBackground,
    '--surface-bg': definition.colors.surfaceBackground,
    '--border-color': definition.colors.border,
    '--text-primary': definition.colors.textPrimary,
    '--text-secondary': definition.colors.textSecondary,
    '--status-success': definition.colors.success,
    '--status-warning': definition.colors.warning,
    '--status-error': definition.colors.error,
    '--status-info': definition.colors.info,
    '--card-radius': `${definition.shape.cardRadius}px`,
    '--nav-width': `${definition.navigation.width}px`,
    '--base-font-size': `${definition.typography.baseFontSize}px`,
  } as CSSProperties;

  return { definition, fluentTheme, cssVariables };
}
