import React from 'react';

export type Valuation = {
  fundId: string;
  valuationDate: string;
  totalAssets: string;
  totalLiabilities: string;
  nav: string;
  currency: string;
};

export type ValuationResult = {
  valuation: Valuation;
  unitsOutstanding: string;
  unitPrice: string;
  positions: {
    securityId: string;
    ticker: string;
    name: string;
    quantity: string;
    price: string;
    positionValue: string;
  }[];
};

export function ValuationBreakdown({result}: {result: ValuationResult}) {
  const {valuation, positions, unitsOutstanding, unitPrice} = result;
  const {valuationDate, totalAssets, totalLiabilities, nav, currency} = valuation;

  return <section className="panel">
    <h2>Расшифровка оценки на {valuationDate}</h2>
    <h3>Состав активов</h3>
    {positions.length > 0 ? <>
      <table>
        <thead><tr>
          <th scope="col">Ценная бумага</th>
          <th scope="col" className="number">Количество</th>
          <th scope="col" className="number">Цена, {currency}</th>
          <th scope="col" className="number">Стоимость позиции, {currency}</th>
        </tr></thead>
        <tbody>{positions.map(position => <tr key={position.securityId}>
          <td>{position.name || position.ticker || position.securityId}
            {position.name && position.ticker && <small className="id">{position.ticker}</small>}
          </td>
          <td className="number">{position.quantity}</td>
          <td className="number">{position.price}</td>
          <td className="number">{position.positionValue}</td>
        </tr>)}</tbody>
      </table>
      <p className="muted">Сумма стоимостей позиций округляется до 8 знаков после запятой.</p>
    </> : <p>На дату оценки нет позиций с ненулевым количеством.</p>}
    <p>Итого активы <strong>{totalAssets} {currency}</strong></p>
    <p>Обязательства <strong>{totalLiabilities} {currency}</strong></p>
    <p>СЧА (активы минус обязательства) <strong>{nav} {currency}</strong></p>
    <p>Количество паёв на дату оценки <strong>{unitsOutstanding}</strong></p>
    <p>Расчётная стоимость пая (СЧА ÷ количество паёв) <strong>{unitPrice} {currency}</strong></p>
  </section>;
}
