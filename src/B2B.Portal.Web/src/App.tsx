import { useEffect, useMemo, useState } from 'react';
import { BrowserRouter, Navigate, Routes, Route } from 'react-router-dom';
import { FluentProvider, Spinner } from '@fluentui/react-components';
import { AppLayout } from './components/AppLayout';
import { LoginPage } from './pages/LoginPage';
import { DashboardPage } from './pages/DashboardPage';
import { MyWorkloadsPage } from './pages/MyWorkloadsPage';
import { GuestPoolPage } from './pages/GuestPoolPage';
import { WorkloadsAdminPage } from './pages/WorkloadsAdminPage';
import { ScenariosPage } from './pages/ScenariosPage';
import { ReviewsPage } from './pages/ReviewsPage';
import { AuditPage } from './pages/AuditPage';
import { GuestImportPage } from './pages/GuestImportPage';
import { WorkloadDetailPage } from './pages/WorkloadDetailPage';
import { GuestDetailPage } from './pages/GuestDetailPage';
import { AccessRequestPage } from './pages/AccessRequestPage';
import { JobsPage } from './pages/JobsPage';
import { WorkerOverviewPage } from './pages/WorkerOverviewPage';
import { ReminderPolicyPage } from './pages/ReminderPolicyPage';
import { MailMonitorPage } from './pages/MailMonitorPage';
import { CompliancePage } from './pages/CompliancePage';
import { DiscoveryPage } from './pages/DiscoveryPage';
import { DevThemePreviewPage } from './pages/DevThemePreviewPage';
import { MockEntraPage } from './pages/MockEntraPage';
import { api } from './api/client';
import type { UiConfiguration } from './types/domain';
import { loadPortalTheme } from './themes/theme-loader';
import { clearToken, getCurrentClaims, getToken } from './auth/token';

export function App() {
  const [configuration, setConfiguration] = useState<UiConfiguration | null>(null);
  const [themeVersion, setThemeVersion] = useState(0);
  // Erhoeht bei Login/Sign-out, um Konfiguration + Routing neu zu evaluieren.
  const [authVersion, setAuthVersion] = useState(0);

  useEffect(() => {
    api.uiConfiguration()
      .then(setConfiguration)
      .catch(() => setConfiguration({
        themeId: 'corporate-vibrant',
        platformTenantId: null,
        branding: { productName: 'B2B Guest Governance Portal' },
        user: undefined,
      }));
  }, [themeVersion, authVersion]);

  const loadedTheme = useMemo(
    () => loadPortalTheme(configuration?.themeId),
    [configuration?.themeId],
  );

  const handleThemeChange = (themeId: string) => {
    localStorage.setItem('portal-theme-id', themeId);
    setThemeVersion((value) => value + 1);
  };

  const handleSignOut = () => {
    clearToken();
    api.mockLogout().catch(() => { /* JWT ist zustandslos — clientseitiges Loeschen zaehlt. */ });
    setAuthVersion((value) => value + 1);
  };

  if (!configuration) {
    return <Spinner label="Lade UI-Konfiguration..." />;
  }

  // Kein Token -> echter Login-Screen statt stillem Fallback auf einen Default-User (das war
  // der urspruengliche Bug: Sign-out loeschte nur localStorage, client.ts fiel sofort wieder
  // auf DEV_PORTAL_USER_MAIL zurueck).
  const claims = getCurrentClaims();
  const isAuthenticated = getToken() !== null && claims !== null;

  return (
    <FluentProvider theme={loadedTheme.fluentTheme} style={loadedTheme.cssVariables}>
      <BrowserRouter>
        {!isAuthenticated ? (
          <Routes>
            <Route path="*" element={<LoginPage onLoggedIn={() => setAuthVersion((value) => value + 1)} />} />
          </Routes>
        ) : (
          <Routes>
            <Route
              element={(
                <AppLayout
                  productName={configuration.branding.productName}
                  userMail={claims.mail}
                  roles={claims.roles}
                  platformTenantId={claims.platformTenantId}
                  themeId={loadedTheme.definition.id}
                  onThemeChange={handleThemeChange}
                  onSignOut={handleSignOut}
                />
              )}
            >
              <Route index element={<DashboardPage />} />
              <Route
                path="my-workloads"
                element={(
                  <MyWorkloadsPage
                    canManageWorkloads={
                      claims.roles.includes('GovernanceAdmin')
                      || claims.roles.includes('WorkloadOwner')
                      || claims.roles.includes('ScenarioManager')
                    }
                  />
                )}
              />
              <Route path="access-request" element={<AccessRequestPage />} />
              <Route path="guest-pool" element={<GuestPoolPage />} />
              <Route path="guest-pool/:guestId" element={<GuestDetailPage />} />
              <Route path="workloads" element={<WorkloadsAdminPage />} />
              <Route path="workloads/:workloadId" element={<WorkloadDetailPage />} />
              <Route path="workloads/:workloadId/scenarios" element={<ScenariosPage />} />
              <Route path="guest-import" element={<GuestImportPage />} />
              <Route path="reviews" element={<ReviewsPage />} />
              <Route path="audit" element={<AuditPage />} />
              <Route path="compliance" element={<CompliancePage />} />
              <Route path="discovery" element={<DiscoveryPage />} />
              <Route path="jobs" element={<JobsPage />} />
              <Route path="worker" element={<WorkerOverviewPage />} />
              <Route path="reminder-policy" element={<ReminderPolicyPage />} />
              <Route path="mail-monitor" element={<MailMonitorPage />} />
              {import.meta.env.DEV && <Route path="dev/theme-preview" element={<DevThemePreviewPage />} />}
              {import.meta.env.DEV && <Route path="dev/mock-entra" element={<MockEntraPage />} />}
              <Route path="login" element={<Navigate to="/" replace />} />
            </Route>
          </Routes>
        )}
      </BrowserRouter>
    </FluentProvider>
  );
}
