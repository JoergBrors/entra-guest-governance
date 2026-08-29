import {
  Badge, Button, Card, Field, Input, ProgressBar, Select, Table, TableBody, TableCell,
  TableHeader, TableHeaderCell, TableRow, Text, Title2, Title3, makeStyles,
} from '@fluentui/react-components';
import { listPortalThemes, loadPortalTheme } from '../themes/theme-loader';

const useStyles = makeStyles({
  grid: { display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(340px, 1fr))', gap: '16px', marginTop: '16px' },
  preview: {
    backgroundColor: 'var(--page-bg)',
    border: '1px solid var(--border-color)',
    borderRadius: 'var(--card-radius)',
    overflow: 'hidden',
  },
  shell: { display: 'grid', gridTemplateColumns: '160px 1fr', minHeight: '520px' },
  nav: { backgroundColor: 'var(--nav-bg)', color: 'var(--nav-fg)', padding: '12px', display: 'grid', alignContent: 'start', gap: '8px' },
  content: { padding: '16px', display: 'grid', gap: '12px' },
  row: { display: 'flex', gap: '8px', flexWrap: 'wrap', alignItems: 'center' },
  kpis: { display: 'grid', gridTemplateColumns: 'repeat(3, 1fr)', gap: '8px' },
  card: { padding: '12px', borderRadius: 'var(--card-radius)' },
  chart: { height: '56px', display: 'grid', gridTemplateColumns: '3fr 2fr 4fr 1fr', gap: '4px', alignItems: 'end' },
  bar: { backgroundColor: 'var(--brand-primary)', borderRadius: '3px 3px 0 0' },
});

export function DevThemePreviewPage() {
  const styles = useStyles();

  return (
    <div>
      <Title2>Theme Preview</Title2>
      <Text>Development-only Route für die beiden gebündelten Theme-Templates.</Text>
      <div className={styles.grid}>
        {listPortalThemes().map((theme) => {
          const loaded = loadPortalTheme(theme.id);
          return (
            <div key={theme.id} className={styles.preview} style={loaded.cssVariables}>
              <div className={styles.shell}>
                <nav className={styles.nav} aria-label={`${theme.displayName} Navigation`}>
                  <Title3>{theme.displayName}</Title3>
                  <Text>Start</Text>
                  <Text>Meine Workloads</Text>
                  <Text>Reviews</Text>
                  <Text>Audit</Text>
                </nav>
                <main className={styles.content}>
                  <Title3>{theme.branding.productName}</Title3>
                  <div className={styles.kpis}>
                    <Card className={styles.card}><Text>Gäste</Text><Title3>128</Title3></Card>
                    <Card className={styles.card}><Text>Reviews</Text><Title3>7</Title3></Card>
                    <Card className={styles.card}><Text>Jobs</Text><Title3>2</Title3></Card>
                  </div>
                  <div className={styles.row}>
                    <Badge color="success">Active: text + color</Badge>
                    <Badge color="warning">Review due</Badge>
                    <Badge color="danger">Blocked</Badge>
                    <Button appearance="primary">Aktion</Button>
                  </div>
                  <Table size={theme.density.table === 'compact' ? 'extra-small' : 'small'}>
                    <TableHeader>
                      <TableRow>
                        <TableHeaderCell>Gast</TableHeaderCell>
                        <TableHeaderCell>Status</TableHeaderCell>
                        <TableHeaderCell>Workload</TableHeaderCell>
                      </TableRow>
                    </TableHeader>
                    <TableBody>
                      <TableRow>
                        <TableCell>alex@example.invalid</TableCell>
                        <TableCell><Badge color="success">Active</Badge></TableCell>
                        <TableCell>Projekt Alpha</TableCell>
                      </TableRow>
                    </TableBody>
                  </Table>
                  <Field label="Formular">
                    <Input placeholder="Szenario Name" />
                  </Field>
                  <Select aria-label="Wizard Schritt"><option>1. Workload auswählen</option></Select>
                  <ProgressBar value={0.65} />
                  <div className={styles.chart} aria-label="Beispieldiagramm">
                    {theme.charts?.palette.slice(0, 4).map((color, index) => (
                      <div key={color} className={styles.bar} style={{ height: `${24 + index * 8}px`, backgroundColor: color }} />
                    ))}
                  </div>
                </main>
              </div>
            </div>
          );
        })}
      </div>
    </div>
  );
}

