import { useEffect, useState, type FormEvent } from 'react';
import { createRoot } from 'react-dom/client';
import { api, ApiError, refreshCsrf } from './api';
import { money, ReviewQueue, TransferPanel } from './Banking';
import { Community } from './Community';
import './style.css';
import './mvp.css';

type User = { id: string; name: string; role: string };
type Account = { id: string; number: string; label: string; balance: string };
type Transaction = { id: string; amount: string; direction: 'IN' | 'OUT'; purpose: string; date: string };
function App() {
  const [user, setUser] = useState<User | null>(null);
  const [loading, setLoading] = useState(true);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState('');
  const [accounts, setAccounts] = useState<Account[]>([]);
  const [selected, setSelected] = useState('');
  const [transactions, setTransactions] = useState<Transaction[]>([]);
  const [historyLoading, setHistoryLoading] = useState(false);
  const [revision, setRevision] = useState(0);
  const reloadBanking = () => setRevision(value => value + 1);
  function failed(e: unknown) {
    setError(e instanceof Error ? e.message : 'Povezava ni uspela');
    if (e instanceof ApiError && e.status === 401) { setUser(null); setAccounts([]); setTransactions([]); }
  }
  useEffect(() => { api<User>('/auth/me').then(setUser).catch(e => {
    if (!(e instanceof ApiError && e.status === 401)) failed(e);
  }).finally(() => setLoading(false)); }, []);
  useEffect(() => {
    let active = true;
    if (user?.role !== 'CLIENT') {
      setAccounts([]); setTransactions([]); setSelected(''); setLoading(false);
    }
    if (user?.role === 'CLIENT') {
      api<Account[]>('/accounts').then(a => {
        if (!active) return;
        setAccounts(a);
        setSelected(current => a.some(account => account.id === current) ? current : a[0]?.id || '');
      })
        .catch(e => { if (active) failed(e); }).finally(() => { if (active) setLoading(false); });
    }
    return () => { active = false; };
  }, [user, revision]);
  useEffect(() => {
    let active = true; setTransactions([]);
    if (selected) {
      setHistoryLoading(true);
      api<Transaction[]>(`/accounts/${selected}/transactions`).then(t => { if (active) setTransactions(t); })
        .catch(e => { if (active) failed(e); }).finally(() => { if (active) setHistoryLoading(false); });
    }
    return () => { active = false; };
  }, [selected, revision]);
  async function login(e: FormEvent<HTMLFormElement>) {
    e.preventDefault(); const form = e.currentTarget; const data = new FormData(form); setBusy(true); setError('');
    try {
      await refreshCsrf();
      const current = await api<User>('/auth/login', { email: data.get('email'), password: data.get('password') });
      form.reset(); await refreshCsrf(); setUser(current);
    } catch (e) { failed(e); } finally { setBusy(false); }
  }
  async function logout() {
    setBusy(true); setError('');
    try { await api('/auth/logout', {}); setUser(null); setAccounts([]); setTransactions([]); await refreshCsrf(); }
    catch (e) { failed(e); } finally { setBusy(false); }
  }
  return <><header><a className="brand" href="/">◈ MiniBank</a><span>Učna simulacija · EUR</span>{user && <button disabled={busy} onClick={logout}>Odjava</button>}</header>
    <main>{error && <p role="alert" className="error">{error}</p>}
    {loading ? <p role="status">Nalaganje …</p> : !user ? <section className="login"><p className="eyebrow">DOBRODOŠLI</p><h1>Vaša banka.<br/>Vaš pregled.</h1><p>Prijavite se z ustvarjenim dostopom.</p><form onSubmit={login}>
      <label>E-pošta<input name="email" type="email" autoComplete="username" required maxLength={254}/></label>
      <label>Geslo<input name="password" type="password" autoComplete="current-password" required/></label>
      <button className="primary" disabled={busy}>{busy ? 'Prijavljanje …' : 'Prijava'}</button></form></section>
    : <><p className="eyebrow">{user.role === 'CLIENT' ? 'OSEBNE FINANCE' : 'DELOVNI PROSTOR'}</p><h1>Pozdravljeni, {user.name}.</h1>
      {user.role === 'EMPLOYEE' ? <ReviewQueue/> : user.role === 'ADMIN' ? <section><h2>Administracija</h2></section> : <>
        <h2>Moji računi</h2>{accounts.length === 0 ? <p>Nimate odprtega računa.</p> : <div className="accounts">{accounts.map(a => <button key={a.id} className={`account ${selected === a.id ? 'selected' : ''}`} onClick={() => { setError(''); setSelected(a.id); }} aria-pressed={selected === a.id}><span>{a.label}</span><strong>{money(a.balance)}</strong><small>{a.number}</small></button>)}</div>}
        {selected && <section><h2>Zadnja izvedena nakazila</h2><p>Največ 20 nakazil za izbrani račun.</p>{historyLoading ? <p role="status">Nalaganje nakazil …</p> : transactions.length === 0 ? <p>Za ta račun še ni izvedenih nakazil.</p> : <div className="table-scroll"><table><thead><tr><th>Datum</th><th>Namen</th><th>Smer</th><th>Znesek</th></tr></thead><tbody>{transactions.map(t => <tr key={t.id}><td>{new Date(t.date).toLocaleString('sl-SI')}</td><td>{t.purpose}</td><td>{t.direction === 'IN' ? 'Priliv' : 'Odliv'}</td><td>{t.direction === 'IN' ? '+' : '−'}{money(t.amount)}</td></tr>)}</tbody></table></div>}</section>}
        {selected && <TransferPanel key={selected} accountId={selected} onChange={reloadBanking}/>}
        <Community userId={user.id} accounts={accounts}/>
      </> }</> }</main><footer>MiniBank · Izmišljeni podatki za učenje</footer></>;
}
createRoot(document.getElementById('root')!).render(<App/>);
