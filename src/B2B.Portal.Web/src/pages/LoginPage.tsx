import { useEffect, useState } from 'react';
import {
  Body1, Button, Card, Spinner, Text, Title2, Title3, makeStyles, tokens,
} from '@fluentui/react-components';
import { api } from '../api/client';
import { storeToken } from '../auth/token';
import type { MockEntraUser } from '../types/domain';

const useStyles = makeStyles({
  shell: {
    minHeight: '100vh',
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'center',
    backgroundColor: tokens.colorNeutralBackground2,
    padding: '24px',
  },
  card: {
    width: '440px',
    maxWidth: '100%',
    padding: '32px',
    display: 'flex',
    flexDirection: 'column',
    gap: '16px',
  },
  userList: {
    display: 'flex',
    flexDirection: 'column',
    gap: '8px',
    maxHeight: '360px',
    overflowY: 'auto',
  },
  userButton: {
    justifyContent: 'flex-start',
    textAlign: 'left',
    height: 'auto',
    padding: '10px 12px',
  },
  muted: { color: tokens.colorNeutralForeground3 },
});

interface LoginPageProps {
  onLoggedIn: () => void;
}

/**
 * EntraIdMock-Login (Erweiterung 2026-08-30): Auswahl einer Mock-Entra-Mail statt Passwort,
 * Backend validiert die Existenz und stellt ein JWT aus (POST /api/auth/mock/login). Ersetzt
 * den frueheren "Dev Login"-Dropdown in AppLayout, das nur Header in localStorage setzte und
 * beim Sign-out sofort wieder auf einen Default-User zurueckfiel.
 */
export function LoginPage({ onLoggedIn }: LoginPageProps) {
  const styles = useStyles();
  const [users, setUsers] = useState<MockEntraUser[] | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [pendingMail, setPendingMail] = useState<string | null>(null);

  useEffect(() => {
    api.listMockEntraLoginUsers()
      .then(setUsers)
      .catch(() => setError('Mock-Entra-Benutzer konnten nicht geladen werden.'));
  }, []);

  const login = async (mail: string) => {
    setError(null);
    setPendingMail(mail);
    try {
      const result = await api.mockLogin(mail);
      storeToken(result.token);
      onLoggedIn();
    } catch {
      setError(`Login fuer ${mail} fehlgeschlagen.`);
    } finally {
      setPendingMail(null);
    }
  };

  return (
    <div className={styles.shell}>
      <Card className={styles.card}>
        <Title2>B2B Guest Governance Portal</Title2>
        <Title3>Anmeldung (EntraIdMock)</Title3>
        <Body1 className={styles.muted}>
          Kein Passwort im LOCAL_MOCK-Modus — waehle einen Mock-Entra-Benutzer.
        </Body1>

        {error && <Text style={{ color: tokens.colorPaletteRedForeground1 }}>{error}</Text>}

        {!users && !error && <Spinner label="Lade Mock-Entra-Benutzer..." />}

        {users && (
          <div className={styles.userList}>
            {users.map((user) => (
              <Button
                key={user.objectId}
                className={styles.userButton}
                appearance="outline"
                disabled={pendingMail !== null}
                onClick={() => login(user.mail)}
              >
                <div>
                  <Text weight="semibold">{user.displayName}</Text>
                  <br />
                  <Text size={200} className={styles.muted}>
                    {user.mail} · {user.userType} · {user.portalRoles.join(', ')}
                  </Text>
                </div>
                {pendingMail === user.mail && <Spinner size="tiny" style={{ marginLeft: 'auto' }} />}
              </Button>
            ))}
          </div>
        )}
      </Card>
    </div>
  );
}
