import { BrowserRouter, Routes, Route } from 'react-router-dom';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';
import { AppLayout } from './components/AppLayout';
import { DashboardPage } from './pages/DashboardPage';
import { MyWorkloadsPage } from './pages/MyWorkloadsPage';
import { GuestPoolPage } from './pages/GuestPoolPage';
import { WorkloadsAdminPage } from './pages/WorkloadsAdminPage';
import { ScenariosPage } from './pages/ScenariosPage';
import { ReviewsPage } from './pages/ReviewsPage';
import { AuditPage } from './pages/AuditPage';

export function App() {
  return (
    <FluentProvider theme={webLightTheme}>
      <BrowserRouter>
        <Routes>
          <Route element={<AppLayout />}>
            <Route index element={<DashboardPage />} />
            <Route path="my-workloads" element={<MyWorkloadsPage />} />
            <Route path="guest-pool" element={<GuestPoolPage />} />
            <Route path="workloads" element={<WorkloadsAdminPage />} />
            <Route path="workloads/:workloadId/scenarios" element={<ScenariosPage />} />
            <Route path="reviews" element={<ReviewsPage />} />
            <Route path="audit" element={<AuditPage />} />
          </Route>
        </Routes>
      </BrowserRouter>
    </FluentProvider>
  );
}
