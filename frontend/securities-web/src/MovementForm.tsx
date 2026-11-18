import {useRef, useState, type FormEvent} from 'react';

export const movementLabels = {credit: 'Зачислить бумаги', debit: 'Списать бумаги'};
export type MovementKind = keyof typeof movementLabels;
export type Movement = {
  sourceAccountId: string;
  securityId: string;
  quantity: string;
  movementParty: string;
  documentReference: string;
  externalId?: string;
};

type Props = {
  kind: MovementKind;
  fundName: string;
  accounts: {id: string; accountNumber: string}[];
  securities: {id: string; ticker: string; name: string}[];
  busy: boolean;
  onSubmit: (movement: Movement) => Promise<boolean>;
  onClose: () => void;
};

export function MovementForm({kind, fundName, accounts, securities, busy, onSubmit, onClose}: Props) {
  const [validation, setValidation] = useState('');
  const [sending, setSending] = useState(false);
  const inFlight = useRef(false);

  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (busy || inFlight.current) return;
    const form = event.currentTarget;
    const data = new FormData(form);
    const value = (name: string) => String(data.get(name) ?? '').trim();
    const quantity = value('quantity').replace(',', '.').replace(/^0+(?=\d)/, '');
    if (!/^\d{1,20}(\.\d{1,8})?$/.test(quantity) || !/[1-9]/.test(quantity)) {
      setValidation('Введите положительное количество. Допустимо до 20 цифр до разделителя и до 8 после него.');
      return;
    }
    const movementParty = value('movementParty');
    const documentReference = value('documentReference');
    if (!movementParty || !documentReference) {
      setValidation('Укажите сторону движения и документ-основание.');
      return;
    }
    setValidation('');
    inFlight.current = true;
    setSending(true);
    try {
      if (await onSubmit({
        sourceAccountId: value('account'),
        securityId: value('security'),
        quantity: quantity.includes('.') ? quantity.replace(/0+$/, '').replace(/\.$/, '') : quantity,
        movementParty,
        documentReference,
        externalId: value('externalId') || undefined,
      })) form.reset();
    } finally {
      inFlight.current = false;
      setSending(false);
    }
  }

  return <section className="panel narrow">
    <h2>{movementLabels[kind]}</h2>
    <p>Фонд: {fundName}. Чтобы выбрать другой фонд, воспользуйтесь списком «Текущий фонд».</p>
    {(!accounts.length || !securities.length) && <p>Для операции нужны активные счёт хранения и ценная бумага.</p>}
    <form onSubmit={submit}>
      <fieldset className="movement-fields" disabled={busy || sending}>
        <label>Счёт хранения
          <select required name="account" defaultValue="">
            <option value="">Выберите счёт</option>
            {accounts.map(account => <option key={account.id} value={account.id}>{account.accountNumber}</option>)}
          </select>
        </label>
        <label>Ценная бумага
          <select required name="security" defaultValue="">
            <option value="">Выберите бумагу</option>
            {securities.map(security => <option key={security.id} value={security.id}>{security.ticker} · {security.name}</option>)}
          </select>
        </label>
        <label>Количество<input required name="quantity" inputMode="decimal" placeholder="25,5"/></label>
        <label>Контрагент / сторона движения
          <input required name="movementParty" placeholder={kind === 'credit' ? 'Источник зачисления' : 'Получатель бумаг'}/>
        </label>
        <label>Документ-основание<input required name="documentReference"/></label>
        <label>Внешний идентификатор (необязательно)<input name="externalId"/></label>
        {validation && <p className="bad" role="alert">{validation}</p>}
        <div className="quick">
          <button className="primary" disabled={!accounts.length || !securities.length}>{movementLabels[kind]}</button>
          <button type="button" onClick={onClose}>Закрыть</button>
        </div>
      </fieldset>
    </form>
  </section>;
}
