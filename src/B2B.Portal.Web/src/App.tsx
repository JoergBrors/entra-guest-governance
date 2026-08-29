import { useEffect, useMemo, useState } from 'react';
import { BrowserRouter, Routes, Route } from 'react-router-dom';
import { FluentProvider, Spinner } from '@fluentui/react-components';
import { AppLayout } from './components/AppLayout';
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
import { CompliancePage } from './pages/CompliancePage';
import { DiscoveryPage } from './pages/DiscoveryPage';
import { DevThemePreviewPage } from './pages/DevThemePreviewPage';
import { api } from './api/client';
import type { UiConfiguration } from './types/domain';
import { loadPortalTheme } from './themes/theme-loader';

export function App() {
  const [configuration, setConfiguration] = useState<UiConfiguration | null>(null);
  const [themeVersion, setThemeVersion] = useState(0);

  useEffect(() => {
    api.uiConfiguration()
      .then(setConfiguration)
      .catch(() => setConfiguration({
        themeId: 'corporate-vibrant',
        platformTenantId: 'configuration required',
        branding: { productName: 'B2B Guest Governance Portal' },
        user: { mail: 'configuration required', roles: ['User'] },
      }));
  }, [themeVersion]);

  const loadedTheme = useMemo(
    () => loadPortalTheme(configuration?.themeId),
    [configuration?.themeId],
  );

  const handleThemeChange = (themeId: string) => {
    localStorage.setItem('portal-theme-id', themeId);
    setThemeVersion((value) => value + 1);
  };

  if (!configuration) {
    return <Spinner label="Lade UI-Konfiguration..." />;
  }

  return (
    <FluentProvider theme={loadedTheme.fluentTheme} style={loadedTheme.cssVariables}>
      <BrowserRouter>
        <Routes>
          <Route
            element={(
              <AppLayout
                productName={configuration.branding.productName}
                userMail={configuration.user.mail}
                roles={configuration.user.roles}
                platformTenantId={configuration.platformTenantId ?? 'configuration required'}
                themeId={loadedTheme.definition.id}
                onThemeChange={handleThemeChange}
              />
            )}
          >
            <Route index element={<DashboardPage />} />
            <Route path="my-workloads" element={<MyWorkloadsPage />} />
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
            {import.meta.env.DEV && <Route path="dev/theme-preview" element={<DevThemePreviewPage />} />}
          </Route>
        </Routes>
      </BrowserRouter>
    </FluentProvider>
  );
}
