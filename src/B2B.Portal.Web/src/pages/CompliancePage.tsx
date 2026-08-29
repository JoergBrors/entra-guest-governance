import { Card, Text, Title2, makeStyles } from '@fluentui/react-components';

const useStyles = makeStyles({ card: { padding: '16px 20px', marginTop: '16px', borderRadius: 'var(--card-radius)' } });

export function CompliancePage() {
  const styles = useStyles();
  return (
    <div>
      <Title2>Compliance</Title2>
      <Card className={styles.card}>
        <Text>Compliance Findings: integration pending</Text>
      </Card>
    </div>
  );
}

