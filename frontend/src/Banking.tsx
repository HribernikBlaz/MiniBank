import { useEffect, useRef, useState, type FormEvent } from 'react';
import { api } from './api';

export const money = (value: string) =>
  new Intl.NumberFormat('sl-SI', { style: 'currency', currency: 'EUR' }).format(Number(value));
export const message = (error: unknown) => error instanceof Error ? error.message : 'Zahteva ni uspela.';
export const statusLabel: Record<string, string> = {
  PENDING_APPROVAL: 'Čaka na odobritev', EXECUTED: 'Izvedeno', REJECTED: 'Zavrnjeno', FAILED: 'Neuspešno',
};

type Sent = { id: string; amount: string; purpose: string; status: string; reason: string | null; createdAt: string };
type QueueEntry = Sent & { sender: string; recipient: string; source: string; target: string; reviewedAt: string | null };

function Pages({ page, rows, change }: { page: number; rows: number; change: (page: number) => void }) {
  return <div className="actions">
    <button disabled={page === 1} onClick={() => change(page - 1)}>Prejšnja</button>
    <span>Stran {page}</span>
    <button disabled={rows < 20} onClick={() => change(page + 1)}>Naslednja</button>
  </div>;
}

export function useRequestKeys() {
  const keys = useRef(new Map<string, string>());
  return (path: string, data: object) => {
    const fingerprint = path + JSON.stringify(data);
    let key = keys.current.get(fingerprint);
    if (!key) {
      key = crypto.randomUUID();
      keys.current.set(fingerprint, key);
    }
    return { key, complete: () => keys.current.delete(fingerprint) };
  };
}

export function TransferPanel({ accountId, onChange }: { accountId: string; onChange: () => void }) {
  const [rows, setRows] = useState<Sent[]>([]);
  const [page, setPage] = useState(1);
  const [version, setVersion] = useState(0);
  const [busy, setBusy] = useState(false);
  const [notice, setNotice] = useState('');
  const [error, setError] = useState('');
  const requestKey = useRequestKeys();

  useEffect(() => {
    let cancel = false;
    api<Sent[]>(`/transfers/sent/${accountId}?page=${page}`)
      .then(result => { if (!cancel) setRows(result); })
      .catch(e => { if (!cancel) setError(message(e)); });
    return () => { cancel = true; };
  }, [accountId, page, version]);

  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const form = event.currentTarget;
    const fields = new FormData(form);
    const input = {
      sourceAccountId: accountId,
      targetAccount: String(fields.get('target')).trim(),
      amount: String(fields.get('amount')).trim().replace(',', '.'),
      purpose: String(fields.get('purpose')).trim(),
    };
    const request = requestKey('/transfers', input);
    setBusy(true);
    setError('');
    setNotice('');
    try {
      const result = await api<Sent>('/transfers', input, request.key);
      request.complete();
      form.reset();
      setNotice(statusLabel[result.status] ?? result.status);
      setPage(1);
      setVersion(n => n + 1);
      onChange();
    } catch (e) {
      setError(message(e));
    } finally {
      setBusy(false);
    }
  }

  return <section><h2>Novo nakazilo</h2>
    <form onSubmit={submit}>
      <label>Prejemnikov račun<input name="target" required maxLength={32}/></label>
      <label>Znesek v EUR<input name="amount" inputMode="decimal" required pattern="[0-9]+([.,][0-9]{1,2})?" maxLength={16}/></label>
      <label>Namen<input name="purpose" required maxLength={140}/></label>
      <button className="primary" disabled={busy}>Oddaj nakazilo</button>
    </form>
    {notice && <p role="status">{notice}</p>}
    {error && <p role="alert" className="error">{error}</p>}
    <div className="actions"><h3>Oddana nakazila</h3><button onClick={() => { setVersion(n => n + 1); onChange(); }}>Osveži</button></div>
    {rows.length === 0 ? <p>Na tej strani ni oddanih nakazil.</p> : <ul>{rows.map(row =>
      <li key={row.id}>{money(row.amount)} · {row.purpose} · {statusLabel[row.status]}{row.reason && ` · ${row.reason}`}</li>
    )}</ul>}
    <Pages page={page} rows={rows.length} change={setPage}/>
  </section>;
}

export function ReviewQueue() {
  const [rows, setRows] = useState<QueueEntry[]>([]);
  const [page, setPage] = useState(1);
  const [version, setVersion] = useState(0);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState('');
  const [notice, setNotice] = useState('');
  const [reviewed, setReviewed] = useState(false);
  useEffect(() => {
    let cancel = false;
    api<QueueEntry[]>(`/review/transfers?page=${page}&reviewed=${reviewed}`)
      .then(result => { if (!cancel) setRows(result); })
      .catch(e => { if (!cancel) setError(message(e)); });
    return () => { cancel = true; };
  }, [page, version, reviewed]);

  async function decide(id: string, approve: boolean, reason?: string) {
    setBusy(true);
    setError('');
    setNotice('');
    try {
      const result = await api<{ status: string }>(`/review/transfers/${id}/decision`, { approve, reason });
      setNotice(statusLabel[result.status]);
    } catch (e) {
      setError(message(e));
    } finally {
      setVersion(n => n + 1);
      setBusy(false);
    }
  }

  return <section><div className="actions"><h2>Odobritve nakazil</h2><button onClick={() => setVersion(n => n + 1)}>Osveži</button></div>
    <div className="actions">
      <button aria-pressed={!reviewed} onClick={() => { setReviewed(false); setPage(1); }}>Čakalna vrsta</button>
      <button aria-pressed={reviewed} onClick={() => { setReviewed(true); setPage(1); }}>Moje odločitve</button>
    </div>
    {notice && <p role="status">{notice}</p>}
    {error && <p role="alert" className="error">{error}</p>}
    {rows.length === 0 && <p>Na tej strani ni {reviewed ? 'obravnavanih' : 'čakajočih'} nakazil.</p>}
    {rows.map(row => <article className="entry" key={row.id}>
      <strong>{money(row.amount)} · {row.purpose}</strong>
      <p>{row.sender} ({row.source}) → {row.recipient} ({row.target})</p>
      <p>{new Date(row.createdAt).toLocaleString('sl-SI')}</p>
      {reviewed ? <p>{statusLabel[row.status]}{row.reason && ` · ${row.reason}`}
        {row.reviewedAt && ` · ${new Date(row.reviewedAt).toLocaleString('sl-SI')}`}</p> : <>
      <button disabled={busy} onClick={() => decide(row.id, true)}>Odobri</button>
      <form onSubmit={event => {
        event.preventDefault();
        void decide(row.id, false, String(new FormData(event.currentTarget).get('reason')));
      }}>
        <label>Razlog zavrnitve<input name="reason" required maxLength={500}/></label>
        <button disabled={busy}>Zavrni</button>
      </form></>}
    </article>)}
    <Pages page={page} rows={rows.length} change={setPage}/>
  </section>;
}
