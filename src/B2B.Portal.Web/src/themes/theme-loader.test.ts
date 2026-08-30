import { describe, expect, it, vi } from 'vitest';

vi.mock('@fluentui/react-components', () => ({
  webLightTheme: {},
}));
import { DEFAULT_THEME_ID, loadPortalTheme, validatePortalThemeDefinition } from './theme-loader';
import { corporateVibrantTheme } from './corporate-vibrant.theme';
import { functionalMinimalTheme } from './functional-minimal.theme';

describe('theme-loader', () => {
  it('loads a valid configured theme', () => {
    const loaded = loadPortalTheme(functionalMinimalTheme.id);
    expect(loaded.definition.id).toBe('functional-minimal');
    expect((loaded.cssVariables as Record<string, string>)['--brand-primary']).toBe(functionalMinimalTheme.colors.brandPrimary);
  });

  it('falls back to a safe default for an unknown theme id', () => {
    const loaded = loadPortalTheme('unknown-theme');
    expect(loaded.definition.id).toBe(DEFAULT_THEME_ID);
  });

  it('validates both bundled themes', () => {
    expect(validatePortalThemeDefinition(corporateVibrantTheme)).toBe(true);
    expect(validatePortalThemeDefinition(functionalMinimalTheme)).toBe(true);
  });
});
