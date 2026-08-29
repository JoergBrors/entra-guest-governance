import { NavLink, Outlet } from 'react-router-dom';
import { makeStyles, tokens, Text, Title3 } from '@fluentui/react-components';

const useStyles = makeStyles({
  shell: {
    display: 'grid',
    gridTemplateColumns: '240px 1fr',
    minHeight: '100vh',
  },
  nav: {
    borderRight: `1px solid ${tokens.colorNeutralStroke2}`,
    padding: '16px',
    display: 'flex',
    flexDirection: 'column',
    gap: '4px',
  },
  navLink: {
    padding: '8px 12px',
    borderRadius: tokens.borderRadiusMedium,
    textDecoration: 'none',
    color: tokens.colorNeutralForeground1,
  },
  navSection: {
    marginTop: '16px',
    marginBottom: '4px',
  },
  content: {
    padding: '24px 32px',
  },
  brand: {
    marginBottom: '20px',
  },
});

/**
 * Reduzierte Fachbereichsoberfläche (Blueprint 9 "Webportal und Bedienmodell").
 * "Meine Workloads" ist bewusst die User-Ansicht ohne Graph-/Governance-Details;
 * der Admin/Governance-Bereich ist separat gruppiert (Guest Pool, Reviews, Jobs, Audit).
 */
export function AppLayout() {
  const styles = useStyles();

  return (
    <div className={styles.shell}>
      <nav className={styles.nav}>
        <div className={styles.brand}>
          <Title3>B2B Guest Governance</Title3>
          <Text size={200}>LOCAL_MOCK</Text>
        </div>

        <NavLink to="/" end className={styles.navLink}>
          Dashboard
        </NavLink>
        <NavLink to="/my-workloads" className={styles.navLink}>
          Meine Workloads
        </NavLink>

        <Text weight="semibold" size={200} className={styles.navSection}>
          ADMIN / GOVERNANCE
        </Text>
        <NavLink to="/guest-pool" className={styles.navLink}>
          Guest Pool
        </NavLink>
        <NavLink to="/workloads" className={styles.navLink}>
          Workloads
        </NavLink>
        <NavLink to="/reviews" className={styles.navLink}>
          Reviews
        </NavLink>
        <NavLink to="/audit" className={styles.navLink}>
          Audit
        </NavLink>
      </nav>

      <main className={styles.content}>
        <Outlet />
      </main>
    </div>
  );
}
