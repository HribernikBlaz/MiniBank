import { useEffect, useState } from 'react';
import { api } from './api';
import { message, money, statusLabel, useRequestKeys } from './Banking';
import type { AccountChoice } from './Community';

type Debt = {
  id: string; title: string; debtor: string; creditor: string;
  amount: string; owes: boolean; status: 'OPEN' | 'PAYMENT_PENDING' | 'SETTLED';
};
type Ledger = { payable: string; receivable: string; net: string; items: Debt[] };
const debtLabel = { OPEN: 'Odprto', PAYMENT_PENDING: 'Plačilo čaka na odobritev', SETTLED: 'Poravnano' };

export function Obligations({ groupId, accounts, onChange, revision }: {
  groupId: string; accounts: AccountChoice[]; onChange: () => void; revision: number;
}) {
  const [ledger, setLedger] = useState<Ledger>();
  const [filter, setFilter] = useState('debts');
  const [version, setVersion] = useState(0);
  const [busy, setBusy] = useState(false);
  const [notice, setNotice] = useState('');
  const [error, setError] = useState('');
  const requestKey = useRequestKeys();
  const path = `/groups/${groupId}/obligations`;
  useEffect(() => {
    let current = true;
    api<Ledger>(path).then(value => { if (current) setLedger(value); })
      .catch(e => { if (current) setError(message(e)); });
    return () => { current = false; };
  }, [path, version, revision]);

  async function pay(debt: Debt, sourceAccountId: string) {
    setBusy(true);
    setError('');
    setNotice('');
    const endpoint = `${path}/${debt.id}/pay`;
    const input = { sourceAccountId };
    const request = requestKey(endpoint, input);
    try {
      const result = await api<{ status: string }>(endpoint, input, request.key);
      request.complete();
      setNotice(statusLabel[result.status]);
      onChange();
    } catch (e) {
      setError(message(e));
    } finally {
      setVersion(n => n + 1);
      setBusy(false);
    }
  }

  const items = ledger?.items.filter(debt => filter === 'settled' ? debt.status === 'SETTLED'
    : debt.status !== 'SETTLED' && debt.owes === (filter === 'debts'));
  return <section>
    <div className="actions"><h2>Obveznosti skupine</h2><button onClick={() => setVersion(n => n + 1)}>Osveži</button></div>
    {ledger && <p>Dolguješ <strong>{money(ledger.payable)}</strong> · Pričakuješ <strong>{money(ledger.receivable)}</strong>
      {' · '}Neto položaj <strong>{money(ledger.net)}</strong></p>}
    <label>Prikaži<select value={filter} onChange={event => setFilter(event.target.value)}>
      <option value="debts">Moji dolgovi</option><option value="claims">Moje terjatve</option><option value="settled">Poravnane postavke</option>
    </select></label>
    {notice && <p role="status">{notice}</p>}
    {error && <p role="alert" className="error">{error}</p>}
    {items?.length === 0 && <p>Za izbrani filter ni obveznosti.</p>}
    {items?.map(debt => <article className="entry" key={debt.id}>
      <strong>{debt.title} · {money(debt.amount)}</strong>
      <p>{debt.debtor} → {debt.creditor} · {debtLabel[debt.status]}</p>
      {debt.owes && debt.status === 'OPEN' && (accounts.length === 0
        ? <p>Za plačilo potrebuješ račun.</p>
        : <form onSubmit={event => {
            event.preventDefault();
            void pay(debt, String(new FormData(event.currentTarget).get('source')));
          }}>
            <label>Račun za plačilo<select name="source" required>
              {accounts.map(account => <option key={account.id} value={account.id}>{account.label} ({account.number})</option>)}
            </select></label>
            <button disabled={busy}>Poravnaj {money(debt.amount)}</button>
          </form>)}
    </article>)}
  </section>;
}
