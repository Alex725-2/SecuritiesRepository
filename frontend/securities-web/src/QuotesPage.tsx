import React, {useEffect, useRef, useState, type FormEvent} from 'react';

type Security = {id: string; ticker: string; name: string; currency: string};
type QuoteFields = {price: string; currency: string; source: string};
type Quote = QuoteFields & {securityId: string; priceDate: string};
type ApiRequest = <T>(path: string, body?: unknown, method?: string) => Promise<T>;

type Props = {
  securities: Security[];
  canWrite: boolean;
  request: ApiRequest;
};

export function QuotesPage({securities, canWrite, request}: Props) {
  const [securityId, setSecurityId] = useState('');
  const security = securities.find(item => item.id === securityId);

  return <>
    <section className="panel narrow">
      <label>Ценная бумага
        <select value={securityId} onChange={event => setSecurityId(event.target.value)}>
          <option value="">{securities.length ? 'Выберите бумагу' : 'Справочник бумаг пуст'}</option>
          {securities.map(item => <option key={item.id} value={item.id}>{item.ticker} · {item.name}</option>)}
        </select>
      </label>
    </section>
    {security && <SecurityQuotes key={security.id} security={security} canWrite={canWrite} request={request}/>}
  </>;
}

function SecurityQuotes({security, canWrite, request}: {
  security: Security;
  canWrite: boolean;
  request: ApiRequest;
}) {
  const path = `/securities/${security.id}/prices`;
  const [history, setHistory] = useState<Quote[]>();
  const [priceDate, setPriceDate] = useState(() => new Date().toISOString().slice(0, 10));
  const [draft, setDraft] = useState<QuoteFields>();
  const [notice, setNotice] = useState('');
  const [error, setError] = useState('');
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [reload, setReload] = useState(0);
  const dateInput = useRef<HTMLInputElement>(null);
  const existing = history?.find(quote => quote.priceDate === priceDate);
  const fields = draft ?? existing ?? {price: '', currency: security.currency, source: ''};

  useEffect(() => {
    let active = true;
    setLoading(true);
    setError('');
    request<Quote[]>(path).then(rows => {
      if (active) setHistory(rows);
    }).catch((error: unknown) => {
      if (active) setError(errorMessage(error));
    }).finally(() => {
      if (active) setLoading(false);
    });
    return () => { active = false; };
  }, [path, request, reload]);

  function selectDate(date: string) {
    setPriceDate(date);
    setDraft(undefined);
    setNotice('');
    setError('');
  }

  async function save(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (!canWrite || loading || saving || !history) return;
    setError('');
    setNotice('');
    const price = fields.price.trim().replace(',', '.').replace(/^0+(?=\d)/, '');
    if (!/^\d{1,20}(\.\d{1,8})?$/.test(price) || !/[1-9]/.test(price)) {
      setError('Введите положительную цену. Допустимо до 20 цифр до разделителя и до 8 после него.');
      return;
    }
    const source = fields.source.trim();
    if (!source) {
      setError('Укажите источник котировки.');
      return;
    }

    setSaving(true);
    try {
      await request<Quote>('/prices', {securityId: security.id, priceDate, price, currency: fields.currency, source}, 'PUT');
      setNotice('Котировка сохранена.');
      try {
        setHistory(await request<Quote[]>(path));
        setDraft(undefined);
      } catch (error) {
        setError('Котировка сохранена, но историю обновить не удалось. ' + errorMessage(error));
      }
    } catch (error) {
      setError(errorMessage(error));
    } finally {
      setSaving(false);
    }
  }

  return <>
    {notice && <div className="alert good" role="status">{notice}</div>}
    {error && <div className="alert bad" role="alert">{error}</div>}
    {canWrite && <section className="panel narrow">
      <h2>{existing ? 'Изменить котировку' : 'Добавить котировку'}</h2>
      <form onSubmit={save}>
        <fieldset className="movement-fields" disabled={loading || saving || !history}>
          <label>Дата котировки<input ref={dateInput} required type="date" value={priceDate}
            onChange={event => selectDate(event.target.value)}/></label>
          <label>Цена<input required inputMode="decimal" value={fields.price}
            onChange={event => setDraft({...fields, price: event.target.value})}/></label>
          <label>Валюта<input required pattern="[A-Z]{3}" maxLength={3} value={fields.currency}
            onChange={event => setDraft({...fields, currency: event.target.value.toUpperCase()})}/></label>
          <label>Источник<input required value={fields.source}
            onChange={event => setDraft({...fields, source: event.target.value})}/></label>
          <button className="primary">{saving ? 'Сохранение…' : 'Сохранить котировку'}</button>
        </fieldset>
      </form>
    </section>}
    <section className="panel">
      <h2>История котировок</h2>
      <button type="button" disabled={loading || saving} onClick={() => setReload(value => value + 1)}>
        Обновить историю
      </button>
      {loading && <p role="status">Загрузка котировок…</p>}
      {history?.length === 0 && !loading && <p className="empty">У этой бумаги пока нет котировок.</p>}
      {!!history?.length && <table>
        <thead><tr>
          <th scope="col">Дата</th>
          <th scope="col" className="number">Цена</th>
          <th scope="col">Валюта</th>
          <th scope="col">Источник</th>
          {canWrite && <th scope="col">Действия</th>}
        </tr></thead>
        <tbody>{history.map(quote => <tr key={quote.priceDate}>
          <td>{quote.priceDate}</td>
          <td className="number">{quote.price}</td>
          <td>{quote.currency}</td>
          <td>{quote.source}</td>
          {canWrite && <td><button type="button" disabled={loading || saving}
            aria-label={`Изменить котировку за ${quote.priceDate}`}
            onClick={() => {selectDate(quote.priceDate); dateInput.current?.focus();}}>Изменить</button></td>}
        </tr>)}</tbody>
      </table>}
    </section>
  </>;
}

function errorMessage(error: unknown) {
  return error instanceof Error ? error.message : String(error);
}
