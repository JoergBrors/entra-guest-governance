import { useEffect, useMemo, useState } from 'react';
import {
  Badge, Button, Card, Checkbox, Input, Select, Spinner, Table, TableBody, TableCell,
  TableHeader, TableHeaderCell, TableRow, Text, Title2, Title3, makeStyles, tokens,
} from '@fluentui/react-components';
import { AddRegular, DeleteRegular, SaveRegular } from '@fluentui/react-icons';
import { api } from '../api/client';
import type { MockEntraApplication, MockEntraGroup, MockEntraMembership, MockEntraUser } from '../types/domain';

type UserForm = Partial<MockEntraUser> & Pick<MockEntraUser, 'mail' | 'displayName'>;
type GroupForm = Partial<MockEntraGroup> & Pick<MockEntraGroup, 'displayName' | 'mailEnabled' | 'securityEnabled'>;
type ApplicationForm = Partial<MockEntraApplication> & Pick<MockEntraApplication, 'displayName'>;

const emptyUser: UserForm = { mail: '', displayName: '', accountEnabled: 'true', userType: 'Guest', portalRoles: ['User'] };
const emptyGroup: GroupForm = {
  displayName: '',
  mailEnabled: false,
  securityEnabled: true,
  groupTypes: [],
  resourceProvisioningOptions: [],
};
const emptyApplication: ApplicationForm = { displayName: '', appRoles: [] };

const useStyles = makeStyles({
  stack: { display: 'flex', flexDirection: 'column', gap: '16px', minWidth: 0 },
  grid: { display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(520px, 1fr))', gap: '16px', alignItems: 'start' },
  card: { padding: '16px 20px', borderRadius: 'var(--card-radius)', minWidth: 0 },
  full: { gridColumn: '1 / -1' },
  form: { display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(190px, 1fr))', gap: '8px', marginBottom: '12px' },
  actions: { display: 'flex', gap: '8px', alignItems: 'center', flexWrap: 'wrap' },
  tableWrap: { overflowX: 'auto', maxWidth: '100%' },
  cellText: { overflowWrap: 'anywhere', wordBreak: 'break-word', lineHeight: '20px' },
  nameCell: { minWidth: '180px', maxWidth: '260px', overflowWrap: 'anywhere', wordBreak: 'break-word' },
  compactCell: { minWidth: '120px', maxWidth: '180px', overflowWrap: 'anywhere', wordBreak: 'break-word' },
  actionCell: { minWidth: '150px', whiteSpace: 'nowrap' },
  membershipGrid: { display: 'grid', gridTemplateColumns: 'minmax(260px, 360px) minmax(360px, 1fr)', gap: '16px', alignItems: 'start' },
  memberList: {
    display: 'grid',
    gridTemplateColumns: 'repeat(auto-fit, minmax(260px, 1fr))',
    gap: '8px',
    maxHeight: '420px',
    overflowY: 'auto',
    border: `1px solid ${tokens.colorNeutralStroke2}`,
    borderRadius: '6px',
    padding: '10px',
  },
  memberItem: { display: 'flex', gap: '8px', alignItems: 'flex-start', minWidth: 0 },
  muted: { color: tokens.colorNeutralForeground3 },
});

export function MockEntraPage() {
  const styles = useStyles();
  const [users, setUsers] = useState<MockEntraUser[] | null>(null);
  const [groups, setGroups] = useState<MockEntraGroup[] | null>(null);
  const [applications, setApplications] = useState<MockEntraApplication[] | null>(null);
  const [memberships, setMemberships] = useState<MockEntraMembership[] | null>(null);
  const [userForm, setUserForm] = useState<UserForm>(emptyUser);
  const [groupForm, setGroupForm] = useState<GroupForm>(emptyGroup);
  const [applicationForm, setApplicationForm] = useState<ApplicationForm>(emptyApplication);
  const [applicationRolesText, setApplicationRolesText] = useState('');
  const [selectedGroupId, setSelectedGroupId] = useState('');
  const [addMemberId, setAddMemberId] = useState('');
  const [selectedMemberIds, setSelectedMemberIds] = useState<string[]>([]);
  const [error, setError] = useState<string | null>(null);

  const reload = () => {
    setError(null);
    Promise.all([
      api.listMockEntraUsers(),
      api.listMockEntraGroups(),
      api.listMockEntraApplications(),
      api.listMockEntraMemberships(),
    ])
      .then(([u, g, a, m]) => {
        setUsers(u);
        setGroups(g);
        setApplications(a);
        setMemberships(m);
      })
      .catch((e: Error) => setError(e.message));
  };

  useEffect(reload, []);

  const userById = useMemo(
    () => new Map((users ?? []).map((user) => [user.objectId, user])),
    [users],
  );
  const selectedGroup = (groups ?? []).find((group) => group.objectId === selectedGroupId);
  const selectedGroupMemberships = (memberships ?? []).filter((membership) => membership.groupId === selectedGroupId);
  const selectedGroupMemberIds = new Set(selectedGroupMemberships.map((membership) => membership.entraObjectId));
  const addableUsers = (users ?? []).filter((user) => !selectedGroupMemberIds.has(user.objectId));

  const saveUser = async () => {
    await api.upsertMockEntraUser(userForm);
    setUserForm(emptyUser);
    reload();
  };

  const saveGroup = async () => {
    await api.upsertMockEntraGroup(groupForm);
    setGroupForm(emptyGroup);
    reload();
  };

  const editApplication = (application: MockEntraApplication) => {
    setApplicationForm(application);
    setApplicationRolesText(formatApplicationRoles(application));
  };

  const saveApplication = async () => {
    await api.upsertMockEntraApplication({
      ...applicationForm,
      appRoles: parseApplicationRoles(applicationRolesText),
    });
    setApplicationForm(emptyApplication);
    setApplicationRolesText('');
    reload();
  };

  const addUserToSelectedGroup = async () => {
    if (!selectedGroupId || !addMemberId) return;
    await api.upsertMockEntraMembership(selectedGroupId, addMemberId);
    setAddMemberId('');
    reload();
  };

  const removeSelectedUsersFromGroup = async () => {
    if (!selectedGroupId || selectedMemberIds.length === 0) return;
    await Promise.all(selectedMemberIds.map((memberId) => api.deleteMockEntraMembership(selectedGroupId, memberId)));
    setSelectedMemberIds([]);
    reload();
  };

  const removeAllUsersFromGroup = async () => {
    if (!selectedGroupId) return;
    await api.removeAllMockEntraGroupMembers(selectedGroupId);
    setSelectedMemberIds([]);
    reload();
  };

  if (error) return <Text>Fehler: {error}</Text>;
  if (!users || !groups || !applications || !memberships) return <Spinner label="Lade Mock Entra..." />;

  return (
    <div className={styles.stack}>
      <div>
        <Title2>Mock Entra Portal</Title2>
        <Text>Development-only Pflege des lokalen Benutzer-, Gruppen-, Application- und Membership-Stamms.</Text>
      </div>

      <div className={styles.grid}>
        <Card className={styles.card}>
          <Title3>Benutzer</Title3>
          <div className={styles.form}>
            <Input placeholder="Mail" value={userForm.mail} onChange={(_, d) => setUserForm({ ...userForm, mail: d.value })} />
            <Input placeholder="Display name" value={userForm.displayName} onChange={(_, d) => setUserForm({ ...userForm, displayName: d.value })} />
            <Input placeholder="Company" value={userForm.companyName ?? ''} onChange={(_, d) => setUserForm({ ...userForm, companyName: d.value })} />
            <Input placeholder="Sponsor" value={userForm.sponsor ?? ''} onChange={(_, d) => setUserForm({ ...userForm, sponsor: d.value })} />
            <Input placeholder="Department" value={userForm.department ?? ''} onChange={(_, d) => setUserForm({ ...userForm, department: d.value })} />
            <Input placeholder="Job title" value={userForm.jobTitle ?? ''} onChange={(_, d) => setUserForm({ ...userForm, jobTitle: d.value })} />
            <Input placeholder="Last login ISO" value={userForm.lastLoginAt ?? ''} onChange={(_, d) => setUserForm({ ...userForm, lastLoginAt: d.value })} />
            <Select value={userForm.userType ?? 'Guest'} onChange={(event) => setUserForm({ ...userForm, userType: event.target.value })}>
              <option value="Guest">Guest</option>
              <option value="Member">Member</option>
            </Select>
            <Input placeholder="Portal roles" value={(userForm.portalRoles ?? []).join(', ')} onChange={(_, d) => setUserForm({ ...userForm, portalRoles: splitList(d.value) })} />
          </div>
          <div className={styles.actions}>
            <Button icon={<SaveRegular />} appearance="primary" disabled={!userForm.mail || !userForm.displayName} onClick={saveUser}>Speichern</Button>
            <Button icon={<AddRegular />} onClick={() => setUserForm(emptyUser)}>Neu</Button>
          </div>
          <div className={styles.tableWrap}>
            <Table size="extra-small" style={{ minWidth: 980 }}>
              <TableHeader>
                <TableRow>
                  <TableHeaderCell>Name</TableHeaderCell>
                  <TableHeaderCell>Firma</TableHeaderCell>
                  <TableHeaderCell>UserType</TableHeaderCell>
                  <TableHeaderCell>Portalrollen</TableHeaderCell>
                  <TableHeaderCell>Sponsor</TableHeaderCell>
                  <TableHeaderCell>Last login</TableHeaderCell>
                  <TableHeaderCell>Aktion</TableHeaderCell>
                </TableRow>
              </TableHeader>
              <TableBody>
                {users.map((user) => (
                  <TableRow key={user.objectId}>
                    <TableCell className={styles.nameCell}>
                      <Text weight="semibold" block className={styles.cellText}>{user.displayName}</Text>
                      <Text size={200} block className={styles.cellText}>{user.mail}</Text>
                      <Text size={100} className={`${styles.muted} ${styles.cellText}`} block>{user.objectId}</Text>
                    </TableCell>
                    <TableCell className={styles.compactCell}>{user.companyName}</TableCell>
                    <TableCell><Badge>{user.userType}</Badge></TableCell>
                    <TableCell className={styles.compactCell}>{user.portalRoles.join(', ')}</TableCell>
                    <TableCell className={styles.compactCell}>{user.sponsor}</TableCell>
                    <TableCell className={styles.compactCell}>{formatDate(user.lastLoginAt)}</TableCell>
                    <TableCell className={styles.actionCell}>
                      <Button size="small" onClick={() => setUserForm(user)}>Bearbeiten</Button>
                      <Button size="small" icon={<DeleteRegular />} onClick={async () => { await api.deleteMockEntraUser(user.objectId); reload(); }} />
                    </TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          </div>
        </Card>

        <Card className={styles.card}>
          <Title3>Gruppen</Title3>
          <div className={styles.form}>
            <Input placeholder="Display name" value={groupForm.displayName} onChange={(_, d) => setGroupForm({ ...groupForm, displayName: d.value })} />
            <Input placeholder="Mail nickname" value={groupForm.mailNickname ?? ''} onChange={(_, d) => setGroupForm({ ...groupForm, mailNickname: d.value })} />
            <Input placeholder="Description" value={groupForm.description ?? ''} onChange={(_, d) => setGroupForm({ ...groupForm, description: d.value })} />
            <Input placeholder="Group types, comma separated" value={(groupForm.groupTypes ?? []).join(', ')} onChange={(_, d) => setGroupForm({ ...groupForm, groupTypes: splitList(d.value) })} />
            <Input placeholder="Provisioning options" value={(groupForm.resourceProvisioningOptions ?? []).join(', ')} onChange={(_, d) => setGroupForm({ ...groupForm, resourceProvisioningOptions: splitList(d.value) })} />
            <Checkbox label="Mail enabled" checked={groupForm.mailEnabled} onChange={(_, d) => setGroupForm({ ...groupForm, mailEnabled: Boolean(d.checked) })} />
            <Checkbox label="Security enabled" checked={groupForm.securityEnabled} onChange={(_, d) => setGroupForm({ ...groupForm, securityEnabled: Boolean(d.checked) })} />
          </div>
          <div className={styles.actions}>
            <Button icon={<SaveRegular />} appearance="primary" disabled={!groupForm.displayName} onClick={saveGroup}>Speichern</Button>
            <Button icon={<AddRegular />} onClick={() => setGroupForm(emptyGroup)}>Neu</Button>
          </div>
          <div className={styles.tableWrap}>
            <Table size="extra-small" style={{ minWidth: 720 }}>
              <TableHeader>
                <TableRow>
                  <TableHeaderCell>Name</TableHeaderCell>
                  <TableHeaderCell>Typen</TableHeaderCell>
                  <TableHeaderCell>Flags</TableHeaderCell>
                  <TableHeaderCell>Aktion</TableHeaderCell>
                </TableRow>
              </TableHeader>
              <TableBody>
                {groups.map((group) => (
                  <TableRow key={group.objectId}>
                    <TableCell className={styles.nameCell}>
                      <Text weight="semibold" block className={styles.cellText}>{group.displayName}</Text>
                      <Text size={100} className={`${styles.muted} ${styles.cellText}`} block>{group.objectId}</Text>
                    </TableCell>
                    <TableCell className={styles.compactCell}>{formatList(group.groupTypes) || 'Security'}</TableCell>
                    <TableCell className={styles.compactCell}>
                      {group.mailEnabled && <Badge>mail</Badge>} {group.securityEnabled && <Badge>security</Badge>}
                      {group.resourceProvisioningOptions.map((option) => <Badge key={option}>{option}</Badge>)}
                    </TableCell>
                    <TableCell className={styles.actionCell}>
                      <Button size="small" onClick={() => setGroupForm(group)}>Bearbeiten</Button>
                      <Button size="small" icon={<DeleteRegular />} onClick={async () => { await api.deleteMockEntraGroup(group.objectId); reload(); }} />
                    </TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          </div>
        </Card>

        <Card className={`${styles.card} ${styles.full}`}>
          <Title3>Gruppenmitgliedschaften</Title3>
          <div className={styles.membershipGrid}>
            <div className={styles.stack}>
              <Select
                value={selectedGroupId}
                onChange={(event) => {
                  setSelectedGroupId(event.target.value);
                  setSelectedMemberIds([]);
                  setAddMemberId('');
                }}
              >
                <option value="">Gruppe auswählen</option>
                {groups.map((group) => <option key={group.objectId} value={group.objectId}>{group.displayName}</option>)}
              </Select>
              {selectedGroup && (
                <>
                  <Text weight="semibold">{selectedGroup.displayName}</Text>
                  <Text size={200} className={styles.muted}>{selectedGroup.objectId}</Text>
                  <Select value={addMemberId} onChange={(event) => setAddMemberId(event.target.value)}>
                    <option value="">User hinzufügen</option>
                    {addableUsers.map((user) => <option key={user.objectId} value={user.objectId}>{user.displayName} ({user.mail})</option>)}
                  </Select>
                  <div className={styles.actions}>
                    <Button appearance="primary" disabled={!addMemberId} onClick={addUserToSelectedGroup}>User hinzufügen</Button>
                    <Button disabled={selectedMemberIds.length === 0} onClick={removeSelectedUsersFromGroup}>
                      Selektierte entfernen
                    </Button>
                    <Button disabled={selectedGroupMemberships.length === 0} onClick={removeAllUsersFromGroup}>
                      Alle entfernen
                    </Button>
                  </div>
                </>
              )}
            </div>

            <div>
              <Text weight="semibold" block>Mitglieder: {selectedGroupMemberships.length}</Text>
              <div className={styles.memberList}>
                {!selectedGroup && <Text>Bitte eine Gruppe auswählen.</Text>}
                {selectedGroup && selectedGroupMemberships.length === 0 && <Text>Keine Mitglieder in dieser Gruppe.</Text>}
                {selectedGroupMemberships.map((membership) => {
                  const user = userById.get(membership.entraObjectId);
                  return (
                    <label key={`${membership.groupId}-${membership.entraObjectId}`} className={styles.memberItem}>
                      <Checkbox
                        checked={selectedMemberIds.includes(membership.entraObjectId)}
                        onChange={(_, data) => setSelectedMemberIds((prev) =>
                          data.checked
                            ? [...new Set([...prev, membership.entraObjectId])]
                            : prev.filter((id) => id !== membership.entraObjectId))}
                      />
                      <span className={styles.cellText}>
                        <Text weight="semibold" block>{user?.displayName ?? membership.entraObjectId}</Text>
                        <Text size={200} block>{user?.mail ?? membership.entraObjectId}</Text>
                        <Text size={100} className={styles.muted} block>{membership.entraObjectId}</Text>
                      </span>
                    </label>
                  );
                })}
              </div>
            </div>
          </div>
        </Card>

        <Card className={styles.card}>
          <Title3>Applications</Title3>
          <div className={styles.form}>
            <Input placeholder="Display name" value={applicationForm.displayName} onChange={(_, d) => setApplicationForm({ ...applicationForm, displayName: d.value })} />
            <Input placeholder="App ID" value={applicationForm.appId ?? ''} onChange={(_, d) => setApplicationForm({ ...applicationForm, appId: d.value })} />
            <Input placeholder="App roles, z.B. Reader=Reader" value={applicationRolesText} onChange={(_, d) => setApplicationRolesText(d.value)} />
          </div>
          <div className={styles.actions}>
            <Button icon={<SaveRegular />} appearance="primary" disabled={!applicationForm.displayName} onClick={saveApplication}>Speichern</Button>
            <Button icon={<AddRegular />} onClick={() => { setApplicationForm(emptyApplication); setApplicationRolesText(''); }}>Neu</Button>
          </div>
          <div className={styles.tableWrap}>
            <Table size="extra-small" style={{ minWidth: 720 }}>
              <TableHeader>
                <TableRow>
                  <TableHeaderCell>Name</TableHeaderCell>
                  <TableHeaderCell>App ID</TableHeaderCell>
                  <TableHeaderCell>App-Rollen</TableHeaderCell>
                  <TableHeaderCell>Aktion</TableHeaderCell>
                </TableRow>
              </TableHeader>
              <TableBody>
                {applications.map((application) => (
                  <TableRow key={application.objectId}>
                    <TableCell className={styles.nameCell}>
                      <Text weight="semibold" block className={styles.cellText}>{application.displayName}</Text>
                      <Text size={100} className={`${styles.muted} ${styles.cellText}`} block>{application.objectId}</Text>
                    </TableCell>
                    <TableCell className={styles.compactCell}>{application.appId}</TableCell>
                    <TableCell className={styles.compactCell}>{application.appRoles.map((role) => role.displayName).join(', ')}</TableCell>
                    <TableCell className={styles.actionCell}>
                      <Button size="small" onClick={() => editApplication(application)}>Bearbeiten</Button>
                      <Button size="small" icon={<DeleteRegular />} onClick={async () => { await api.deleteMockEntraApplication(application.objectId); reload(); }} />
                    </TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          </div>
        </Card>
      </div>
    </div>
  );
}

function splitList(value: string): string[] {
  return value.split(',').map((item) => item.trim()).filter(Boolean);
}

function formatList(value: string[]): string {
  return value.join(', ');
}

function parseApplicationRoles(value: string) {
  return value
    .split(/[,\n;]/)
    .map((part) => part.trim())
    .filter(Boolean)
    .map((part) => {
      const [rawValue, rawDisplayName] = part.split('=').map((item) => item.trim());
      const roleValue = rawValue || rawDisplayName;
      const displayName = rawDisplayName || rawValue;
      return {
        id: `app-role-${roleValue.toLowerCase().replace(/[^a-z0-9]+/g, '-')}`,
        value: roleValue,
        displayName,
        description: displayName,
      };
    });
}

function formatApplicationRoles(application: MockEntraApplication): string {
  return application.appRoles.map((role) => `${role.value}=${role.displayName}`).join(', ');
}

function formatDate(value?: string | null): string {
  return value ? new Date(value).toLocaleString() : 'nie';
}
