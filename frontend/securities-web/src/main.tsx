import React, {useEffect,useState,useRef,FormEvent} from 'react';
import {createRoot} from 'react-dom/client';
import './style.css';
import {MovementForm, movementLabels, type Movement, type MovementKind} from './MovementForm';
import {ValuationBreakdown, type Valuation, type ValuationResult} from './ValuationBreakdown';
type Fund={id:string;code:string;name:string;managementCompanyId:string;status:string};
type FundDetails=Fund & {unitsOutstanding:string|null};
type Account={id:string;fundId:string;accountNumber:string;status:string};
type Security={id:string;ticker:string;name:string;isActive:boolean};
type Position={custodyAccountId:string;securityId:string;accountNumber:string;ticker:string;quantity:string;version:number};
type Operation={id:string;type:string;status:string;createdAt:string;reversesOperationId?:string;documentReference?:string};
type Run={id:string;createdAt:string;items:{id:string;custodyAccountId:string;securityId:string;expected:string;actual:string}[]};
const pages=['Обзор','Фонды','Карточка фонда','Позиции','Операции','Перевод','Сверка','Оценка'];
function App(){
 const [token,setToken]=useState(''),[role,setRole]=useState('Operator'),[page,setPage]=useState('Обзор'),[funds,setFunds]=useState<Fund[]>([]),[fundId,setFundId]=useState(''),[accounts,setAccounts]=useState<Account[]>([]),[securities,setSecurities]=useState<Security[]>([]),[companies,setCompanies]=useState<{id:string;name:string}[]>([]),[positions,setPositions]=useState<Position[]>([]),[operations,setOperations]=useState<Operation[]>([]),[runs,setRuns]=useState<Run[]>([]),[valuations,setValuations]=useState<Valuation[]>([]),[error,setError]=useState(''),[notice,setNotice]=useState(''),[busy,setBusy]=useState(false),[source,setSource]=useState(''),[target,setTarget]=useState(''),[security,setSecurity]=useState(''),[quantity,setQuantity]=useState(''),[key,setKey]=useState<string>(crypto.randomUUID()),[controls,setControls]=useState('[]'),[date,setDate]=useState(new Date().toISOString().slice(0,10)),[liabilities,setLiabilities]=useState('0'),[currency,setCurrency]=useState('RUB');
 const [movementKind,setMovementKind]=useState<MovementKind>();
 const [fundDetails,setFundDetails]=useState<FundDetails>();
 const [valuationResult,setValuationResult]=useState<ValuationResult>();
 const pendingMovements=useRef(new Map<string,string>());
 const canWrite=role==='Admin'||role==='Operator';
 const [draftToken,setDraftToken]=useState('');
 const current=useRef({token,fundId});current.current={token,fundId};
 const load=useRef(0),actions=useRef(0);
 const fund=funds.find(f=>f.id===fundId), ownAccounts=accounts.filter(a=>a.fundId===fundId);
 async function api<T>(path:string,body?:unknown,method?:string):Promise<T>{const r=await fetch('/api'+path,{method:method??(body?'POST':'GET'),headers:{'Content-Type':'application/json',...(token?{Authorization:'Bearer '+token}:{})},body:body?JSON.stringify(body):undefined});if(!r.ok){if(r.status===401&&current.current.token===token){setToken('');setFundId('');}const p=await r.json().catch(()=>({}));throw new Error(apiError(r.status,p));}return r.json();}
 async function action(work:()=>Promise<void>){const request=++actions.current,session=token,selected=fundId;setBusy(true);setError('');setNotice('');try{await work();}catch(e){if(session===current.current.token&&selected===current.current.fundId)setError(e instanceof Error?e.message:String(e));}finally{if(request===actions.current)setBusy(false);}}
 async function refresh(movementOnly=false){
  if(!fundId)return;
  const request=++load.current,selected=fundId,session=token;
  const [p,o,portfolio]=await Promise.all([
   api<Position[]>(`/funds/${selected}/positions`),
   api<Operation[]>(`/funds/${selected}/operations`),
   movementOnly?undefined:Promise.all([
    api<Run[]>(`/funds/${selected}/reconciliations`),
    api<Valuation[]>(`/funds/${selected}/valuations`),
    api<FundDetails>(`/funds/${selected}`),
   ]),
  ]);
  if(request!==load.current||selected!==current.current.fundId||session!==current.current.token)return;
  setPositions(p);setOperations(o);
  if(portfolio){setRuns(portfolio[0]);setValuations(portfolio[1]);setFundDetails(portfolio[2]);}
 }
 useEffect(()=>{let active=true;if(!token)return;void action(async()=>{const [f,a,s,c]=await Promise.all([api<Fund[]>('/funds'),api<Account[]>('/accounts'),api<Security[]>('/securities'),api<{id:string;name:string}[]>('/management-companies')]);if(!active)return;setFunds(f);setFundId(f[0]?.id??'');setAccounts(a);setSecurities(s);setCompanies(c);});return()=>{active=false;};},[token]);
 useEffect(()=>{++load.current;setPositions([]);setOperations([]);setRuns([]);setValuations([]);setFundDetails(undefined);setValuationResult(undefined);setSource('');setTarget('');if(token&&fundId)void action(refresh);},[fundId,token]);
 function applyToken(value:string){const jwt=value.trim();try{const payload=JSON.parse(atob(jwt.split('.')[1].replace(/-/g,'+').replace(/_/g,'/')));if(!payload.exp||payload.exp*1000<=Date.now())throw new Error();const claim=payload.role??payload['http://schemas.microsoft.com/ws/2008/06/identity/claims/role'];const roles=Array.isArray(claim)?claim:[claim];setRole(['Admin','Operator','Auditor'].find(r=>roles.includes(r))??'JWT');setFundId('');setToken(jwt);setDraftToken('');}catch{setError('JWT имеет неверный формат или срок действия истёк.');}}
 async function refreshAfterSave(movementOnly=false){try{await refresh(movementOnly);}catch(e){setError('Операция сохранена. Не удалось обновить данные: '+(e instanceof Error?e.message:String(e)));}}
 async function saveMovement(kind:MovementKind,movement:Movement){
  if(!canWrite||!fundId)return false;
  const signature=JSON.stringify([kind,movement]);
  let idempotencyKey=pendingMovements.current.get(signature);
  if(!idempotencyKey){
   idempotencyKey=crypto.randomUUID();
   pendingMovements.current.set(signature,idempotencyKey);
  }
  let saved=false;
  await action(async()=>{
   let operation:Operation;
   try{
    operation=await api<Operation>(`/operations/${kind}`,{...movement,idempotencyKey});
   }catch(error){
    throw new Error(`${error instanceof TypeError?'Нет ответа от API. Повторите отправку с теми же данными. '+error.message:error instanceof Error?error.message:'Ошибка отправки.'} Ключ запроса: ${idempotencyKey}.`);
   }
   saved=true;
   pendingMovements.current.delete(signature);
   if(current.current.token!==token||current.current.fundId!==fundId)return;
   setNotice(`${kind==='credit'?'Бумаги зачислены':'Бумаги списаны'}. Идентификатор операции: ${operation.id}`);
   await refreshAfterSave(true);
  });
  return saved;
 }
 const submit=(fn:()=>Promise<void>)=>(e:FormEvent)=>{e.preventDefault();void action(fn);};
 async function saveValuation(){
  setValuationResult(undefined);
  const result=await api<ValuationResult>('/valuations',{fundId,date,liabilities,currency});
  if(current.current.token!==token||current.current.fundId!==fundId)return;
  setValuationResult(result);
  setNotice('Оценка сохранена.');
  await refreshAfterSave();
 }
 function selectAccount(value:string,set:(s:string)=>void,label:string){return <label>{label}<select required value={value} onChange={e=>set(e.target.value)}><option value="">Выберите счёт</option>{ownAccounts.map(a=><option key={a.id} value={a.id}>{a.accountNumber}</option>)}</select></label>;}
 return <div className="layout"><aside><div className="brand"><span className="mark">S</span>Securities<span className="subbrand">Repository</span></div><p className="eyebrow">РАБОЧЕЕ МЕСТО</p><nav>{pages.map((p,i)=><button key={p} className={page===p?'active':''} onClick={()=>setPage(p)}><span>{String(i+1).padStart(2,'0')}</span>{p}</button>)}</nav><div className="aside-foot"><span className="dot"/>Демонстрационный контур<br/><small>Только вымышленные данные</small></div></aside><main><header><div><p className="eyebrow">УЧЁТ ИНВЕСТИЦИОННЫХ ФОНДОВ</p><h1>{page}</h1></div><div className="header-actions"><span className="badge">{role}</span>{token&&<button onClick={()=>{setToken('');setDraftToken('');setFundId('');setFunds([]);setAccounts([]);setSecurities([]);setCompanies([]);setNotice('');setError('');}}>Выйти</button>}{token&&<button onClick={()=>void action(refresh)} disabled={busy}>Обновить</button>}</div></header>
 {!token?<section className="panel login"><h2>Вход в демонстрационный контур</h2><p>Тестовые JWT доступны только в Development. Для другого окружения введите полученный JWT.</p><form onSubmit={submit(async()=>{const result=await api<{accessToken:string}>('/dev/token',{role});applyToken(result.accessToken);})}><label>Роль<select value={role} onChange={e=>setRole(e.target.value)}>{['Operator','Auditor','Admin'].map(r=><option key={r}>{r}</option>)}</select></label><button className="primary">Войти с тестовой ролью</button></form><label>Готовый JWT<input type="password" aria-label="JWT" value={draftToken} onChange={e=>setDraftToken(e.target.value)}/></label><button onClick={()=>{applyToken(draftToken);}}>Войти с JWT</button></section>:<>
 <div className="context"><label>Текущий фонд<select disabled={busy} value={fundId} onChange={e=>setFundId(e.target.value)}>{!funds.length&&<option value="">Фонды отсутствуют</option>}{funds.map(f=><option value={f.id} key={f.id}>{f.code} · {f.name}</option>)}</select></label><span className="muted">{companies.find(c=>c.id===fund?.managementCompanyId)?.name??'УК не выбрана'}</span></div>
 {page==='Обзор'&&<><div className="stats"><article><span>Фонды в системе</span><strong>{funds.length}</strong><small>Вымышленные справочники</small></article><article><span>Позиции фонда</span><strong>{positions.length}</strong><small>В разрезе счетов и бумаг</small></article><article><span>Операции в выборке</span><strong>{operations.length}</strong><small>Последние 100 записей</small></article></div><section className="panel"><h2>Контроль движения активов</h2><p>Позиции, неизменяемый журнал операций и ручная сверка доступны в одном рабочем месте.</p><div className="quick"><button className="primary" onClick={()=>setPage('Перевод')}>Перевести бумаги →</button><button onClick={()=>setPage('Сверка')}>Выполнить сверку</button></div></section><section className="panel"><h2>Последние операции</h2><OperationTable data={operations.slice(0,5)}/></section></>}
 {page==='Фонды'&&<section className="panel"><h2>Управляющие компании и фонды</h2><table><thead><tr><th>Код</th><th>Фонд</th><th>УК</th><th>Статус</th></tr></thead><tbody>{funds.map(f=><tr key={f.id} onClick={()=>{setFundId(f.id);setPage('Карточка фонда');}}><td>{f.code}</td><td>{f.name}</td><td>{companies.find(c=>c.id===f.managementCompanyId)?.name}</td><td><span className="badge">{f.status}</span></td></tr>)}</tbody></table>{!funds.length&&<Empty/>}</section>}
 {page==='Карточка фонда'&&<section className="panel"><h2>{fund?.name??'Фонд не выбран'}</h2><p>Код: {fund?.code} · Статус: {fund?.status}</p><p>Количество паёв <strong>{fundDetails?.id===fundId ? fundDetails?.unitsOutstanding ?? 'Нет данных' : '—'}</strong></p><h3>Счета хранения</h3>{ownAccounts.map(a=><p key={a.id}>{a.accountNumber}<small className="id">{a.id}</small></p>)}{!ownAccounts.length&&<Empty/>}</section>}
 {page==='Позиции'&&<section className="panel"><h2>Текущие остатки</h2><table><thead><tr><th>Счёт</th><th>Бумага</th><th>Количество</th><th>Версия</th></tr></thead><tbody>{positions.map(p=><tr key={p.custodyAccountId+p.securityId}><td>{p.accountNumber}</td><td>{p.ticker}</td><td className="number">{p.quantity}</td><td>{p.version}</td></tr>)}</tbody></table>{!positions.length&&<Empty/>}</section>}
 {page==='Операции'&&canWrite&&<section className="panel"><div className="quick">
   {(['credit','debit'] as const).map(kind=><button key={kind} type="button" className={movementKind===kind?'primary':''} aria-pressed={movementKind===kind} disabled={busy||fund?.status!=='Active'} onClick={()=>setMovementKind(kind)}>{movementLabels[kind]}</button>)}
 </div></section>}
 {page==='Операции'&&canWrite&&movementKind&&fund?.status==='Active'&&<MovementForm
   key={fundId+movementKind} kind={movementKind} fundName={fund.name}
   accounts={ownAccounts.filter(account=>account.status==='Active')}
   securities={securities.filter(security=>security.isActive)} busy={busy}
   onSubmit={movement=>saveMovement(movementKind,movement)} onClose={()=>setMovementKind(undefined)}/>}
 {page==='Операции'&&<section className="panel"><h2>История операций</h2><OperationTable data={operations}/>{canWrite&&<><h3>Корректирующая операция</h3><form onSubmit={submit(async()=>{const id=prompt('Идентификатор исходной операции');if(!id)return;const reason=prompt('Причина корректировки');if(!reason)return;await api(`/operations/${id}/reverse`,{idempotencyKey:crypto.randomUUID(),reason});setNotice('Созданы обратные проводки.');await refreshAfterSave();})}><button disabled={busy}>Выполнить корректировку</button></form></>}</section>}
 {page==='Перевод'&&<section className="panel narrow"><h2>Внутренний перевод</h2><p>Перевод между счетами одного фонда. Две проводки, сумма движения равна нулю.</p><form onSubmit={submit(async()=>{await api('/operations/transfer',{idempotencyKey:key,sourceAccountId:source,targetAccountId:target,securityId:security,quantity});setNotice('Перевод проведён. Для новой операции создайте новый ключ.');await refreshAfterSave();})}>{selectAccount(source,setSource,'Счёт отправителя')}{selectAccount(target,setTarget,'Счёт получателя')}<label>Ценная бумага<select required value={security} onChange={e=>setSecurity(e.target.value)}><option value="">Выберите бумагу</option>{securities.map(s=><option key={s.id} value={s.id}>{s.ticker} · {s.name}</option>)}</select></label><label>Количество<input required type="text" inputMode="decimal" pattern="[0-9]+(\.[0-9]{1,8})?" value={quantity} onChange={e=>setQuantity(e.target.value)}/></label><label>Ключ идемпотентности<input required value={key} onChange={e=>setKey(e.target.value)}/></label><div className="quick"><button className="primary" disabled={busy||!canWrite||!fundId}>Провести перевод</button><button type="button" onClick={()=>setKey(crypto.randomUUID())}>Новый ключ</button></div></form></section>}
 {page==='Сверка'&&<><section className="panel"><h2>Ручная сверка</h2><p>Передайте полный контрольный набор для фонда. Пропущенные позиции считаются нулевыми.</p><button onClick={()=>setControls(JSON.stringify(positions.map(p=>({custodyAccountId:p.custodyAccountId,securityId:p.securityId,quantity:p.quantity})),null,2))}>Заполнить текущими остатками</button><form onSubmit={submit(async()=>{const balances:unknown=JSON.parse(controls);if(!Array.isArray(balances)||balances.some(x=>!x||typeof x.quantity!=='string'||!/^\d+(\.\d{1,8})?$/.test(x.quantity)))throw new Error('Ожидается массив остатков с quantity в виде decimal-строки.');await api('/reconciliations',{fundId,balances});setNotice('Результаты сверки сохранены.');await refreshAfterSave();})}><label>Контрольные остатки (JSON)<textarea rows={8} value={controls} onChange={e=>setControls(e.target.value)}/></label><button className="primary" disabled={busy||!canWrite||!fundId}>Сверить</button></form></section>{runs.map(r=><section className="panel" key={r.id}><h2>{new Date(r.createdAt).toLocaleString('ru-RU')}</h2><table><thead><tr><th>Счёт / бумага</th><th>Ожидаемый</th><th>Фактический</th><th>Расхождение</th></tr></thead><tbody>{r.items.map(i=><tr key={i.id}><td>{accounts.find(a=>a.id===i.custodyAccountId)?.accountNumber} / {securities.find(s=>s.id===i.securityId)?.ticker}</td><td>{i.expected}</td><td>{i.actual}</td><td className={i.expected===i.actual?'good':'bad'}>{difference(i.actual,i.expected)}</td></tr>)}</tbody></table></section>)}</>}
 {page==='Оценка'&&<><section className="panel narrow"><h2>Упрощённая оценка портфеля</h2><p>По журналу на конец даты UTC и котировкам на выбранную дату. Это учебная оценка, не нормативный расчёт СЧА.</p><form onSubmit={submit(saveValuation)}><label>Дата<input required type="date" value={date} onChange={e=>setDate(e.target.value)}/></label><label>Обязательства<input required type="text" inputMode="decimal" pattern="[0-9]+(\.[0-9]{1,8})?" value={liabilities} onChange={e=>setLiabilities(e.target.value)}/></label><label>Валюта<input required minLength={3} maxLength={3} value={currency} onChange={e=>setCurrency(e.target.value.toUpperCase())}/></label><button className="primary" disabled={busy||!canWrite||!fundId}>Оценить</button></form></section>{valuationResult?.valuation.fundId===fundId&&<ValuationBreakdown result={valuationResult}/>}<section className="panel"><h2>Сохранённые оценки</h2><table><thead><tr><th>Дата</th><th>Активы</th><th>Обязательства</th><th>СЧА</th><th>Валюта</th></tr></thead><tbody>{valuations.map(v=><tr key={v.valuationDate}><td>{v.valuationDate}</td><td>{v.totalAssets}</td><td>{v.totalLiabilities}</td><td>{v.nav}</td><td>{v.currency}</td></tr>)}</tbody></table>{!valuations.length&&<Empty/>}</section></>}
 </>}{busy&&<p role="status">Загрузка…</p>}{error&&<div className="alert bad" role="alert">{error}</div>}{notice&&<div className="alert good" role="status">{notice}</div>}<footer>SECURITIES REPOSITORY <span>Учебный проект · PostgreSQL — источник истины</span></footer></main></div>;
}
function apiError(status:number,problem:Record<string,unknown>|null){
 const messages:Record<number,string>={
  400:'Проверьте данные запроса.',
  401:'Войдите заново.',
  403:'Недостаточно прав для выполнения действия.',
  404:'Запрошенная запись не найдена.',
  409:'Конфликт данных или ключа запроса.',
  422:'Действие отклонено по правилам учёта.',
  503:'Сервис временно недоступен.',
 };
 const validation=problem?.errors&&typeof problem.errors==='object'
  ? Object.entries(problem.errors).map(([field,details])=>`${field}: ${details}`).join('; ')
  : '';
 const diagnostics=[problem?.title,problem?.error,problem?.detail,validation,problem?.traceId]
  .filter(value=>typeof value==='string'&&value);
 return [messages[status]??'Ошибка API.',`HTTP ${status}.`,...new Set(diagnostics)].join(' ');
}
function difference(a:string,b:string){const scaled=(s:string)=>{const negative=s.startsWith('-');const [whole,fraction='']=s.replace(/^-/,'').split('.');const value=BigInt(whole)*100000000n+BigInt(fraction.padEnd(8,'0'));return negative?-value:value;};const delta=scaled(a)-scaled(b);const abs=delta<0n?-delta:delta;return (delta<0n?'-':'')+(abs/100000000n)+'.'+(abs%100000000n).toString().padStart(8,'0');}
function Empty(){return <p className="empty">Данных пока нет.</p>;}
function OperationTable({data}:{data:Operation[]}){return <><table><thead><tr><th>Дата</th><th>Тип</th><th>Идентификатор</th><th>Статус</th></tr></thead><tbody>{data.map(o=><tr key={o.id}><td>{new Date(o.createdAt).toLocaleString('ru-RU')}</td><td>{o.type}</td><td className="mono">{o.id}</td><td><span className="badge">{o.status}</span></td></tr>)}</tbody></table>{!data.length&&<Empty/>}</>;}
createRoot(document.getElementById('root')!).render(<React.StrictMode><App/></React.StrictMode>);

