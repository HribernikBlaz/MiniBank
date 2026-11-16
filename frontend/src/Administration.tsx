import { useEffect, useState, type FormEvent } from 'react';
import { api } from './api';
import { message } from './Banking';

type User = { id: string; email: string; name: string; role: string; active: boolean };

function Roles() {
  return <><option value="CLIENT">Komitent</option><option value="EMPLOYEE">Uslužbenec</option><option value="ADMIN">Administrator</option></>;
}

export function Administration({ currentId, onAccessChanged }: { currentId: string; onAccessChanged: () => void }) {
  const [users, setUsers] = useState<User[]>([]);
  const [revision, setRevision] = useState(0);
  const [notice, setNotice] = useState('');
  const [error, setError] = useState('');
  const [busy, setBusy] = useState(false);
  useEffect(() => {
    let current = true;
    api<User[]>('/admin/users').then(value => { if (current) setUsers(value); })
      .catch(e => { if (current) setError(message(e)); });
    return () => { current = false; };
  }, [revision]);

  async function save(event: FormEvent<HTMLFormElement>, user?: User) {
    event.preventDefault();
    const form = event.currentTarget;
    const data = new FormData(form);
    const role = data.get('role');
    setBusy(true);
    setError('');
    setNotice('');
    try {
      if (user) {
        const active = data.get('active') === 'on';
        await api(`/admin/users/${user.id}/access`, { role, active });
        if (user.id === currentId && (role !== user.role || active !== user.active)) onAccessChanged();
      } else {
        await api('/admin/users', { name: data.get('name'), email: data.get('email'), password: data.get('password'), role });
        form.reset();
      }
      setNotice(user ? 'Dostop je posodobljen.' : 'Uporabniški dostop je ustvarjen.');
      setRevision(n => n + 1);
    } catch (e) {
      setError(message(e));
    } finally {
      setBusy(false);
    }
  }

  return <section><h2>Upravljanje dostopov</h2>
    {notice && <p role="status">{notice}</p>}
    {error && <p role="alert" className="error">{error}</p>}
    <h3>Nov dostop</h3>
    <form onSubmit={save}>
      <div className="form-grid">
        <label>Ime<input name="name" required maxLength={100}/></label>
        <label>E-pošta<input name="email" type="email" required maxLength={254}/></label>
        <label>Začetno geslo<input name="password" type="password" autoComplete="new-password" required minLength={12} maxLength={256}/></label>
        <label>Vloga<select name="role"><Roles/></select></label>
      </div>
      <button disabled={busy}>Ustvari dostop</button>
    </form>
    <h3>Uporabniki</h3>
    {users.map(user => <article className="entry" key={`${user.id}:${user.role}:${user.active}`}>
      <strong>{user.name} · {user.email}</strong>
      <form onSubmit={event => save(event, user)}>
        <label>Vloga<select name="role" defaultValue={user.role}><Roles/></select></label>
        <label className="check"><input name="active" type="checkbox" defaultChecked={user.active}/>Aktiven dostop</label>
        <button disabled={busy}>Shrani dostop</button>
      </form>
    </article>)}
  </section>;
}
