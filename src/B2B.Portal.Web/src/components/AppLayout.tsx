import { useEffect, useState } from 'react';
import { NavLink, Outlet } from 'react-router-dom';
import { makeStyles, tokens, Text, Title3, Input, Select, Button } from '@fluentui/react-components';
import { SignOutRegular } from '@fluentui/react-icons';
import { api } from '../api/client';
import { listPortalThemes } from '../themes/theme-loader';
import type { MockEntraUser } from '../types/domain';

const useStyles = makeStyles({
  shell: {
    display: 'grid',
    gridTemplateColumns: 'var(--nav-width) 1fr',
    minHeight: '100vh',
    backgroundColor: 'var(--page-bg)',
  },
  nav: {
    backgroundColor: 'var(--nav-bg)',
    color: 'var(--nav-fg)',
    borderRight: `1px solid var(--border-color)`,
    padding: '16px',
    display: 'flex',
    flexDirection: 'column',
    gap: '4px',
  },
  navLink: {
    padding: '8px 12px',
    borderRadius: tokens.borderRadiusSmall,
    textDecoration: 'none',
    color: 'var(--nav-fg)',
    ':global(.active)': {
      backgroundColor: 'rgba(255,255,255,0.16)',
      fontWeight: 600,
    },
    ':focus-visible': {
      outlineStyle: 'solid',
      outlineWidth: '2px',
      outlineColor: tokens.colorStrokeFocus2,
    },
  },
  navSection: {
    marginTop: '16px',
    marginBottom: '4px',
  },
  content: {
    padding: '24px 32px',
  },
  header: {
    height: '56px',
    backgroundColor: 'var(--header-bg)',
    borderBottom: `1px solid var(--border-color)`,
    display: 'flex',
    alignItems: 'center',
    gap: '12px',
    padding: '0 24px',
  },
  mainArea: {
    minWidth: 0,
  },
  spacer: {
    flexGrow: 1,
  },
  brand: {
    marginBottom: '20px',
  },
  brandText: {
    color: 'var(--nav-fg)',
  },
});

interface AppLayoutProps {
  productName: string;
  userMail: string;
  roles: string[];
  platformTenantId: string;
  themeId: string;
  onThemeChange: (themeId: string) => void;
  onUserChange: () => void;
}

/**
 * Reduzierte Fachbereichsoberfläche (Blueprint 9 "Webportal und Bedienmodell").
 * "Meine Workloads" ist bewusst die User-Ansicht ohne Graph-/Governance-Details;
 * der Admin/Governance-Bereich ist separat gruppiert (Guest Pool, Reviews, Jobs, Audit).
 */
export function AppLayout({ productName, userMail, roles, platformTenantId, themeId, onThemeChange, onUserChange }: AppLayoutProps) {
  const styles = useStyles();
  const [loginUsers, setLoginUsers] = useState<MockEntraUser[]>([]);
  const isAdmin = roles.includes('GovernanceAdmin');
  const isReviewer = isAdmin || roles.includes('Reviewer');
  const canManageWorkloads = isAdmin || roles.includes('WorkloadOwner') || roles.includes('ScenarioManager');
  const themes = listPortalThemes();

  useEffect(() => {
    if (!import.meta.env.DEV) return;
    api.listMockEntraLoginUsers().then(setLoginUsers).catch(() => setLoginUsers([]));
  }, [userMail]);

  const switchUser = (mail: string) => {
    const user = loginUsers.find((u) => u.mail === mail);
    if (!user) return;
    localStorage.setItem('portal-user-mail', user.mail);
    localStorage.setItem('portal-user-roles', user.portalRoles.join(','));
    onUserChange();
  };

  const signOut = () => {
    localStorage.removeItem('portal-user-mail');
    localStorage.removeItem('portal-user-roles');
    onUserChange();
  };

  return (
    <div className={styles.shell}>
      <nav className={styles.nav} aria-label="Hauptnavigation">
        <div className={styles.brand}>
          <Title3 className={styles.brandText}>{productName}</Title3>
          <Text className={styles.brandText} size={200}>Tenant: {platformTenantId}</Text>
        </div>

        <NavLink to="/" end className={styles.navLink}>
          Start
        </NavLink>
        <NavLink to="/my-workloads" className={styles.navLink}>
          Meine Workloads
        </NavLink>
        <NavLink to="/access-request" className={styles.navLink}>
          Anträge
        </NavLink>
        {isReviewer && (
          <NavLink to="/reviews" className={styles.navLink}>
            Meine Reviews
          </NavLink>
        )}

        {(isAdmin || canManageWorkloads) && (
          <>
            <Text weight="semibold" size={200} className={styles.navSection}>
              GOVERNANCE
            </Text>
            {isAdmin && (
              <NavLink to="/guest-pool" className={styles.navLink}>
                Guest Pool
              </NavLink>
            )}
            <NavLink to="/workloads" className={styles.navLink}>
              Workloads
            </NavLink>
            {isAdmin && (
              <>
                <NavLink to="/guest-import" className={styles.navLink}>
                  Gäste-Import
                </NavLink>
                <NavLink to="/compliance" className={styles.navLink}>
                  Compliance
                </NavLink>
                <NavLink to="/discovery" className={styles.navLink}>
                  Ressourcen / Discovery
                </NavLink>
                <NavLink to="/jobs" className={styles.navLink}>
                  Jobs
                </NavLink>
                <NavLink to="/audit" className={styles.navLink}>
                  Audit
                </NavLink>
              </>
            )}
          </>
        )}
        {import.meta.env.DEV && (
          <>
            <Text weight="semibold" size={200} className={styles.navSection}>
              DEVELOPMENT
            </Text>
            <NavLink to="/dev/theme-preview" className={styles.navLink}>
              Theme Preview
            </NavLink>
            {isAdmin && (
              <NavLink to="/dev/mock-entra" className={styles.navLink}>
                Mock Entra
              </NavLink>
            )}
          </>
        )}
      </nav>

      <div className={styles.mainArea}>
        <header className={styles.header}>
          <Input aria-label="Suche" placeholder="Suche" />
          <div className={styles.spacer} />
          {import.meta.env.DEV && (
            <Select
              aria-label="Theme"
              value={themeId}
              onChange={(event) => onThemeChange(event.target.value)}
            >
              {themes.map((theme) => (
                <option key={theme.id} value={theme.id}>{theme.displayName}</option>
              ))}
            </Select>
          )}
          {import.meta.env.DEV && (
            <Select aria-label="Dev Login" value={userMail} onChange={(event) => switchUser(event.target.value)}>
              {loginUsers.map((user) => (
                <option key={user.objectId} value={user.mail}>
                  {user.displayName} ({user.userType})
                </option>
              ))}
            </Select>
          )}
          <Text size={200}>{roles.join(', ')}</Text>
          <Button appearance="subtle" icon={<SignOutRegular />} onClick={signOut}>{userMail}</Button>
        </header>
        <main className={styles.content}>
          <Outlet />
        </main>
      </div>
    </div>
  );
}
