import { useEffect, useState } from 'react';
import { api } from './api';
import { dateInMorocco, time } from './utils';

type Employee = { id: string; name: string; missing: boolean };
type ClockState = { state: 'idle' | 'working' | 'paused'; since: string | null };
export type PointageEdit = { id: string; employeeId: string; checkIn: string; checkOut: string };

export function LocalClock({ employees, edit, onCancel, onSaved }: {
  employees: Employee[]; edit?: PointageEdit; onCancel: () => void; onSaved: (date: string, person: string) => Promise<void>;
}) {
  const [employee, select] = useState(edit?.employeeId ?? '');
  const [mode, setMode] = useState<'manual' | 'live'>('manual');
  const [clock, setClock] = useState<ClockState | null>(null);
  const [error, setError] = useState('');
  const [busy, setBusy] = useState(false);
  const [saved, setSaved] = useState(false);
  useEffect(() => {
    let active = true;
    setClock(null);
    setError('');
    setSaved(false);
    if (employee && mode === 'live') api<ClockState>(`/attendance/local-clock/${encodeURIComponent(employee)}`)
      .then(value => { if (active) setClock(value); })
      .catch(e => { if (active) setError(e.message); });
    return () => { active = false; };
  }, [employee, mode]);

  async function record(action: string) {
    setBusy(true);
    setError('');
    try {
      await api('/attendance/local-clock', 'POST', { employeeId: employee, action });
      setClock(await api<ClockState>(`/attendance/local-clock/${encodeURIComponent(employee)}`));
      await onSaved(dateInMorocco(), employee);
    } catch (e) { setError((e as Error).message); }
    finally { setBusy(false); }
  }

  async function addManual(form: HTMLFormElement) {
    const values = new FormData(form);
    setBusy(true); setError(''); setSaved(false);
    try {
      const item = await api<{ referenceDate: string }>(edit ? `/attendance/manual/${edit.id}` : '/attendance/manual', edit ? 'PUT' : 'POST', {
        employeeId: employee, checkIn: values.get('checkIn'), checkOut: values.get('checkOut'),
      });
      form.reset();
      setSaved(true);
      await onSaved(item.referenceDate, employee);
    } catch (e) { setError((e as Error).message); }
    finally { setBusy(false); }
  }

  return <section className="panel form-panel" aria-label="Pointage local">
    <h2>{edit ? 'Modifier le pointage' : 'Ajouter un pointage'}</h2>
    {!edit && <div className="pointage-mode">
      <button type="button" className={mode === 'manual' ? 'primary' : 'secondary'} aria-pressed={mode === 'manual'} disabled={busy} onClick={() => setMode('manual')}>Saisie manuelle</button>
      <button type="button" className={mode === 'live' ? 'primary' : 'secondary'} aria-pressed={mode === 'live'} disabled={busy} onClick={() => setMode('live')}>En temps réel</button>
    </div>}
    <p>{mode === 'manual' ? 'Choisissez les dates et heures d’entrée et de sortie au Maroc.' : 'Les arrivées, pauses et départs sont enregistrés à l’heure de la saisie.'}</p>
    <label>Collaborateur à pointer<select value={employee} disabled={busy} onChange={e => select(e.target.value)}>
      <option value="">Sélectionner un collaborateur</option>
      {employees.filter(e => !e.missing || e.id === edit?.employeeId).map(e => <option key={e.id} value={e.id}>{e.name}</option>)}
    </select></label>
    {error && <p role="alert">{error}</p>}
    {saved && <p role="status">Pointage enregistré dans le journal.</p>}
    {mode === 'manual' && <form onSubmit={e => { e.preventDefault(); void addManual(e.currentTarget); }}>
      <div className="form-grid">
        <label>Date et heure d’entrée<input name="checkIn" type="datetime-local" defaultValue={edit?.checkIn} required disabled={busy}/></label>
        <label>Date et heure de sortie<input name="checkOut" type="datetime-local" defaultValue={edit?.checkOut} required disabled={busy}/></label>
      </div>
      <div className="form-actions"><button type="button" className="secondary" disabled={busy} onClick={onCancel}>Annuler</button><button className="primary" disabled={!employee || busy}>{edit ? 'Enregistrer les modifications' : 'Enregistrer le pointage'}</button></div>
    </form>}
    {clock && <><p role="status">{clock.state === 'idle' ? 'Aucun pointage local ouvert.' : `${clock.state === 'paused' ? 'En pause' : 'En poste'} depuis ${time(clock.since)}.`}</p>
      <div className="form-actions">
        {clock.state === 'idle' && <button className="primary" disabled={busy} onClick={() => record('arrival')}>Enregistrer l’arrivée</button>}
        {clock.state === 'working' && <><button className="secondary" disabled={busy} onClick={() => record('pause')}>Commencer la pause</button><button className="primary" disabled={busy} onClick={() => record('departure')}>Enregistrer le départ</button></>}
        {clock.state === 'paused' && <button className="primary" disabled={busy} onClick={() => record('resume')}>Terminer la pause</button>}
      </div></>}
  </section>;
}
