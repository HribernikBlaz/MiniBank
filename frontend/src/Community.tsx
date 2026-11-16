import { useEffect, useState, type FormEvent, type ReactNode } from 'react';
import { api } from './api';
import { message, money } from './Banking';

export type AccountChoice = { id: string; number: string; label: string };
type GroupSummary = { id: string; name: string; organizer: string; status: string };
type Member = { id: string; name: string; active: boolean };
type GroupDetail = { id: string; name: string; organizer: boolean; members: Member[] };
type Share = { userId: string; amount: string; decision: string; reason: string | null };
type Expense = {
  id: string; title: string; category: string; date: string; payerId: string; accountId: string;
  total: string; status: string; version: number; revised: boolean; shares: Share[];
};
const expenseStatus: Record<string, string> = {
  DRAFT: 'Osnutek', PENDING_CONFIRMATION: 'Čaka na potrditve', DISPUTED: 'Ugovor', CONFIRMED: 'Potrjeno',
  PENDING: 'Čaka', ACCEPTED: 'Potrjeno',
};

export function Community({ userId, accounts, obligations }: {
  userId: string; accounts: AccountChoice[]; obligations?: (groupId: string) => ReactNode;
}) {
  const [groups, setGroups] = useState<GroupSummary[]>([]);
  const [selected, setSelected] = useState('');
  const [revision, setRevision] = useState(0);
  const [error, setError] = useState('');
  const [busy, setBusy] = useState(false);
  useEffect(() => {
    let current = true;
    api<GroupSummary[]>('/groups').then(data => { if (current) setGroups(data); })
      .catch(e => { if (current) setError(message(e)); });
    return () => { current = false; };
  }, [revision]);

  async function change(path: string, input: object, after?: () => void) {
    setBusy(true);
    setError('');
    try {
      const result = await api<{ id: string } | undefined>(path, input);
      if (result?.id) setSelected(result.id);
      after?.();
      setRevision(n => n + 1);
    } catch (e) {
      setError(message(e));
    } finally {
      setBusy(false);
    }
  }

  return <>
    <section><h2>Skupine</h2>
      <form onSubmit={event => {
        event.preventDefault();
        const form = event.currentTarget;
        void change('/groups', { name: new FormData(form).get('name') }, () => form.reset());
      }}>
        <label>Ime nove skupine<input name="name" required maxLength={100}/></label>
        <button disabled={busy}>Ustvari skupino</button>
      </form>
      {error && <p role="alert" className="error">{error}</p>}
      {groups.length === 0 && <p>Za zdaj nimaš skupin ali povabil.</p>}
      {groups.map(group => group.status === 'INVITED'
        ? <div className="entry" key={group.id}><p>Povabilo v skupino <b>{group.name}</b> · organizator {group.organizer}</p>
            <div className="actions">
              <button disabled={busy} onClick={() => change(`/groups/${group.id}/invitation`, { accept: true }, () => setSelected(group.id))}>Sprejmi</button>
              <button disabled={busy} onClick={() => change(`/groups/${group.id}/invitation`, { accept: false })}>Zavrni</button>
            </div>
          </div>
        : <button key={group.id} aria-pressed={selected === group.id} onClick={() => setSelected(group.id)}>{group.name}</button>)}
    </section>
    {selected && groups.some(group => group.id === selected && group.status === 'ACTIVE') &&
      <GroupWorkspace key={selected} id={selected} userId={userId} accounts={accounts} obligations={obligations}/>}
  </>;
}

function GroupWorkspace({ id, userId, accounts, obligations }: {
  id: string; userId: string; accounts: AccountChoice[]; obligations?: (groupId: string) => ReactNode;
}) {
  const [detail, setDetail] = useState<GroupDetail>();
  const [expenses, setExpenses] = useState<Expense[]>([]);
  const [revision, setRevision] = useState(0);
  const [editing, setEditing] = useState('');
  const [error, setError] = useState('');
  const [busy, setBusy] = useState(false);
  const root = `/groups/${id}`;
  useEffect(() => {
    let current = true;
    Promise.all([api<GroupDetail>(root), api<Expense[]>(`${root}/expenses`)])
      .then(([group, items]) => { if (current) { setDetail(group); setExpenses(items); } })
      .catch(e => { if (current) setError(message(e)); });
    return () => { current = false; };
  }, [root, revision]);

  async function change(path: string, input: object, after?: (result?: { id: string }) => void) {
    setError('');
    setBusy(true);
    try {
      const result = await api<{ id: string } | undefined>(`${root}${path}`, input);
      after?.(result);
    } catch (e) {
      setError(message(e));
    } finally {
      setRevision(n => n + 1);
      setBusy(false);
    }
  }

  const draft = expenses.find(e => e.id === editing && e.status === 'DRAFT');
  const name = (id: string) => detail?.members.find(person => person.id === id)?.name ?? 'Član';

  return <>
    <section>
      <div className="actions"><h2>{detail?.name ?? 'Skupina'}</h2><button onClick={() => setRevision(n => n + 1)}>Osveži</button></div>
      {error && <p role="alert" className="error">{error}</p>}
      {detail && <>
        <p>Člani: {detail.members.map(member => member.name).join(', ')}</p>
        {detail.organizer && <form onSubmit={event => {
          event.preventDefault();
          const form = event.currentTarget;
          void change('/invite', { email: new FormData(form).get('email') }, () => form.reset());
        }}>
          <label>E-pošta komitenta<input name="email" type="email" required maxLength={254}/></label>
          <button disabled={busy}>Povabi</button>
        </form>}
        <h3>{draft ? `Uredi osnutek #${draft.version}` : 'Nov strošek'}</h3>
        {accounts.length === 0 ? <p>Za strošek potrebuješ račun za povračilo.</p> : <ExpenseForm
          key={draft?.id ?? 'new'} draft={draft} accounts={accounts} userId={userId}
          members={detail.members} busy={busy} save={(input, reset) =>
            change(draft ? `/expenses/${draft.id}/edit` : '/expenses', input, () => { reset(); setEditing(''); })}/>}
        {draft && <button onClick={() => setEditing('')}>Zapri urejanje</button>}
        <h3>Predlogi in zgodovina</h3>
        {expenses.length === 0 && <p>V skupini še ni predlogov.</p>}
        {expenses.map(expense => {
          const mine = expense.payerId === userId;
          const share = expense.shares.find(item => item.userId === userId);
          const path = `/expenses/${expense.id}`;
          return <article className="entry" key={expense.id}>
            <strong>{expense.title} · {money(expense.total)}</strong>
            <p>{name(expense.payerId)} · {expense.category} · {expense.date} · različica {expense.version} · {expenseStatus[expense.status]}</p>
            <ul>{expense.shares.map(item => <li key={item.userId}>
              {name(item.userId)} · {money(item.amount)} · {expenseStatus[item.decision]}{item.reason && ` · ${item.reason}`}
            </li>)}</ul>
            {mine && expense.status === 'DRAFT' && <div className="actions">
              <button disabled={busy} onClick={() => setEditing(expense.id)}>Uredi osnutek</button>
              <button disabled={busy} onClick={() => change(`${path}/submit`, {})}>Oddaj v potrditev</button>
            </div>}
            {mine && expense.status === 'DISPUTED' && !expense.revised &&
              <button disabled={busy} onClick={() => change(`${path}/revise`, {}, result => setEditing(result?.id ?? ''))}>Ustvari novo različico</button>}
            {!mine && expense.status === 'PENDING_CONFIRMATION' && share?.decision === 'PENDING' && <>
              <button disabled={busy} onClick={() => change(`${path}/decision`, { accept: true })}>Potrdi svoj delež</button>
              <form onSubmit={event => {
                event.preventDefault();
                void change(`${path}/decision`, { accept: false, reason: new FormData(event.currentTarget).get('reason') });
              }}>
                <label>Razlog ugovora<input name="reason" required maxLength={500}/></label>
                <button disabled={busy}>Ugovarjaj</button>
              </form>
            </>}
          </article>;
        })}
      </>}
    </section>
    {detail && obligations?.(id)}
  </>;
}

function ExpenseForm({ draft, accounts, members, userId, busy, save }: {
  draft?: Expense; accounts: AccountChoice[]; members: Member[]; userId: string; busy: boolean;
  save: (input: object, reset: () => void) => void;
}) {
  const [equal, setEqual] = useState(!draft);
  const [participants, setParticipants] = useState(() => new Set(draft?.shares.map(item => item.userId) ?? [userId]));
  const today = new Date();
  const localDate = new Date(today.getTime() - today.getTimezoneOffset() * 60_000).toISOString().slice(0, 10);
  function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const form = event.currentTarget;
    const fields = new FormData(form);
    save({
      title: fields.get('title'), category: fields.get('category'), date: fields.get('date'),
      total: String(fields.get('total')).trim().replace(',', '.'), accountId: fields.get('account'), equal,
      shares: members.filter(member => member.active && participants.has(member.id))
        .map(member => ({ userId: member.id, amount: equal ? undefined : String(fields.get(member.id)).trim().replace(',', '.') })),
    }, () => { form.reset(); setEqual(true); setParticipants(new Set([userId])); });
  }
  return <form onSubmit={submit}>
    <div className="form-grid">
      <label>Naziv<input name="title" required maxLength={140} defaultValue={draft?.title}/></label>
      <label>Kategorija<input name="category" required maxLength={80} defaultValue={draft?.category}/></label>
      <label>Datum plačila<input name="date" type="date" max={localDate} required defaultValue={draft?.date ?? localDate}/></label>
      <label>Znesek v EUR<input name="total" inputMode="decimal" required defaultValue={draft?.total}/></label>
      <label>Račun za povračilo<select name="account" required defaultValue={draft?.accountId}>
        {accounts.map(account => <option key={account.id} value={account.id}>{account.label} ({account.number})</option>)}
      </select></label>
      <label>Delitev<select value={equal ? 'equal' : 'manual'} onChange={event => setEqual(event.target.value === 'equal')}>
        <option value="equal">Enakomerno</option><option value="manual">Ročno</option>
      </select></label>
    </div>
    {equal && <p className="note">Preostali centi se razdelijo po vrstnem redu ID-jev udeležencev.</p>}
    <fieldset><legend>Udeleženci</legend>{members.filter(member => member.active).map(member => <div key={member.id}>
      <label className="check"><input type="checkbox" checked={participants.has(member.id)} disabled={member.id === userId}
        onChange={event => {
          const checked = event.target.checked;
          setParticipants(previous => {
            const next = new Set(previous);
            if (checked) next.add(member.id); else next.delete(member.id);
            return next;
          });
        }}/>{member.name}</label>
      {!equal && participants.has(member.id) && <label>Delež v EUR za {member.name}
        <input inputMode="decimal" name={member.id} required defaultValue={draft?.shares.find(s => s.userId === member.id)?.amount}/>
      </label>}
    </div>)}</fieldset>
    <button disabled={busy} className="primary">Shrani osnutek</button>
  </form>;
}
