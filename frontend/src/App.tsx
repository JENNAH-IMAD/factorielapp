import { useEffect, useState } from 'react';
import { CalendarDays, Clock3, LogOut, Pencil, RefreshCw, Settings2, Trash2 } from 'lucide-react';
import { api } from './api';
import { LocalClock, type PointageEdit } from './LocalClock';
import { Settings, type Employee } from './Settings';
import { dateInMorocco, duration, time } from './utils';

type Session = { authenticated: boolean; demo: boolean };
type JournalRow = {
  key: string; localId: string | null; employeeId: string | null; identification: string; name: string | null; email: string | null;
  referenceDate: string; clockIn: string | null; clockOut: string | null;
  rawClockIn: string | null; rawClockOut: string | null; origin: string; durationMinutes: number | null;
};
type Journal = { data: JournalRow[]; total: number; pageSize: number };

function dateLabel(value: string) { return value.split('-').reverse().join('/'); }
function clockLabel(value: string | null, raw: string | null) {
  return value ? time(value) : raw?.match(/(?:^|T)(\d{2}:\d{2})/)?.[1] ?? '—';
}
function localInput(timestamp: string | null) {
  return timestamp ? `${dateInMorocco(new Date(timestamp))}T${time(timestamp)}` : '';
}

export default function App() {
  const [session, setSession] = useState<Session | null>(null);
  const [employees, setEmployees] = useState<Employee[]>([]);
  const [journal, setJournal] = useState<Journal>({ data: [], total: 0, pageSize: 25 });
  const [start, setStart] = useState(dateInMorocco(new Date(Date.now() - 7 * 86400000)));
  const [end, setEnd] = useState(dateInMorocco());
  const [allDates, setAllDates] = useState(false);
  const [employee, setEmployee] = useState('');
  const [page, setPage] = useState(1);
  const [revision, refresh] = useState(0);
  const [error, setError] = useState('');
  const [notice, setNotice] = useState('');
  const [busy, setBusy] = useState(false);
  const [showClock, setShowClock] = useState(false);
  const [edit, setEdit] = useState<PointageEdit | undefined>();
  const [deleteRow, setDeleteRow] = useState<JournalRow | null>(null);
  const [screen, setScreen] = useState<'journal' | 'settings'>('journal');
  const validPeriod = !!start && !!end && start <= end;

  useEffect(() => { api<Session>('/session').then(setSession).catch(e => setError(e.message)); }, []);
  useEffect(() => {
    if (!session?.authenticated) return;
    let active = true;
    api<{ factorial: Employee[] }>('/employees').then(result => { if (active) setEmployees(result.factorial); })
      .catch(e => { if (active) setError(e.message); });
    return () => { active = false; };
  }, [session, revision]);
  useEffect(() => {
    if (!session?.authenticated || (!allDates && !validPeriod)) return;
    let active = true;
    api<Journal>(`/attendance/journal?${new URLSearchParams({ start, end, employee, page: String(page), allDates: String(allDates) })}`)
      .then(result => { if (active) setJournal(result); })
      .catch(e => { if (active) { setJournal({ data: [], total: 0, pageSize: 25 }); setError(e.message); } });
    return () => { active = false; };
  }, [session, revision, start, end, employee, page, allDates, validPeriod]);

  async function importFactorial() {
    setBusy(true); setError(''); setNotice('');
    try {
      const state = await api<{ demo: boolean; apiKeyConfigured: boolean; companyTokenConfigured: boolean; connection: { authMode: string } }>('/connection');
      if (!state.demo && !(state.connection.authMode === 'ApiKey' ? state.apiKeyConfigured : state.companyTokenConfigured))
        throw new Error('Identifiant Factorial manquant sur le serveur. Les données déjà importées restent consultables.');
      const directory = await api<{ status: string; count: number }>('/sync/employees', 'POST');
      if (directory.status === 'Failed') throw new Error('Import de l’annuaire impossible. Vérifiez la clé Factorial configurée sur le serveur.');
      const attendance = await api<{ status: string; count: number }>('/sync/attendance', 'POST', { start, end, employeeIds: employee ? [employee] : [] });
      if (attendance.status === 'Failed') throw new Error('Import des pointages impossible. Vérifiez la connexion et les droits Factorial.');
      setNotice(`${directory.count} collaborateurs et ${attendance.count} plages lus dans Factorial.`);
    } catch (e) { setError((e as Error).message); }
    finally { refresh(value => value + 1); setBusy(false); }
  }

  async function removePointage() {
    if (!deleteRow?.localId) return;
    setBusy(true); setError(''); setNotice('');
    try {
      await api(`/attendance/manual/${deleteRow.localId}`, 'DELETE');
      if (edit?.id === deleteRow.localId) { setEdit(undefined); setShowClock(false); }
      setDeleteRow(null); setPage(1); refresh(value => value + 1);
      setNotice('Pointage retiré du journal.');
    } catch (e) { setError((e as Error).message); }
    finally { setBusy(false); }
  }

  if (!session?.authenticated) return <main className="journal-login panel form-panel">
    <h1>Journal des pointages</h1>
    <p>{session?.demo ? 'Démonstration avec des données fictives.' : 'Connectez-vous pour consulter les pointages.'}</p>
    {error && <div className="flash error" role="alert">{error}</div>}
    <form onSubmit={async e => {
      e.preventDefault(); setError('');
      const form = new FormData(e.currentTarget);
      try { await api('/login', 'POST', { username: form.get('username') ?? '', password: form.get('password') ?? '' }); setSession(await api('/session')); }
      catch (err) { setError((err as Error).message); }
    }}>
      {!session?.demo && <><label>Identifiant<input name="username" required autoComplete="username"/></label><label>Mot de passe<input name="password" type="password" required autoComplete="current-password"/></label></>}
      <button disabled={!session} className="primary">{session?.demo ? 'Ouvrir la démonstration' : 'Se connecter'}</button>
    </form>
  </main>;

  return <main className="journal-page">
    <header className="journal-heading">
      <div><p className="eyebrow"><CalendarDays size={15}/> SUIVI DES PRÉSENCES</p><h1>{screen === 'journal' ? 'Journal des pointages' : 'Paramétrage'}</h1></div>
      <button className="secondary" onClick={async () => { await api('/logout', 'POST'); setSession(await api('/session')); }}><LogOut size={16}/> Déconnexion</button>
    </header>
    <nav className="journal-navigation" aria-label="Navigation principale">
      <button aria-current={screen === 'journal' ? 'page' : undefined} onClick={() => setScreen('journal')}><CalendarDays size={16}/> Journal</button>
      <button aria-current={screen === 'settings' ? 'page' : undefined} onClick={() => setScreen('settings')}><Settings2 size={17}/> Paramétrage</button>
    </nav>
    {session.demo && <div className="demo-banner">Démonstration locale · données fictives</div>}
    {error && <div className="flash error" role="alert">{error}</div>}
    {notice && <div className="flash success" role="status">{notice}</div>}
    {screen === 'settings' ? <Settings employees={employees} onImported={() => refresh(value => value + 1)}/> : <section className="panel">
      <div className="panel-title journal-actions">
        <div><h2>Entrées et sorties <span className="count">{journal.total}</span></h2><p>Heures affichées au Maroc. Factorial est en lecture seule.</p></div>
        <div><button className="primary" aria-expanded={showClock && !edit} onClick={() => { setShowClock(edit ? true : !showClock); setEdit(undefined); }}><Clock3 size={17}/> Ajouter un pointage</button>
          <button className="secondary" disabled={busy || allDates || !validPeriod} title={allDates ? 'Choisissez une période pour importer depuis Factorial' : undefined} onClick={importFactorial}><RefreshCw size={16} className={busy ? 'spinning' : ''}/>{busy ? 'Import en cours…' : 'Importer depuis Factorial'}</button></div>
      </div>
      {showClock && <div className="clock-in-journal"><LocalClock key={edit?.id ?? 'new'} employees={employees} edit={edit} onCancel={() => { setEdit(undefined); setShowClock(false); }} onSaved={async (date, person) => {
        if (!allDates && (!validPeriod || date < start || date > end)) { setStart(date); setEnd(date); }
        if (employee && employee !== person) setEmployee(person);
        if (edit) { setEdit(undefined); setShowClock(false); setNotice('Pointage mis à jour.'); }
        setPage(1);
        refresh(value => value + 1);
      }}/></div>}
      {deleteRow && <div className="journal-delete" role="group" aria-label="Confirmer la suppression">
        <p>Retirer le pointage de <strong>{deleteRow.name ?? deleteRow.identification}</strong> du {dateLabel(deleteRow.referenceDate)} ?</p>
        <button className="secondary" disabled={busy} onClick={() => setDeleteRow(null)}>Annuler</button>
        <button className="danger-button" disabled={busy} onClick={() => void removePointage()}>Confirmer la suppression</button>
      </div>}
      <div className="filters journal-filters">
        <label>Du<input type="date" value={start} disabled={allDates} onChange={e => { setStart(e.target.value); setPage(1); setError(''); }}/></label>
        <label>Au<input type="date" value={end} disabled={allDates} onChange={e => { setEnd(e.target.value); setPage(1); setError(''); }}/></label>
        <label>Collaborateur<select value={employee} onChange={e => { setEmployee(e.target.value); setPage(1); }}><option value="">Tous les collaborateurs</option>{employees.map(person => <option key={person.id} value={person.id}>{person.name}</option>)}</select></label>
        <button className="secondary" onClick={() => refresh(value => value + 1)}>Actualiser</button>
        <button className={allDates ? 'primary' : 'secondary'} aria-pressed={allDates} onClick={() => { setAllDates(value => !value); setPage(1); setError(''); }}>Toutes les dates</button>
      </div>
      <div className="table-scroll"><table className="journal-table">
        <thead><tr><th>ID Emp.</th><th>Email</th><th>Nom</th><th>Date</th><th>Check-In</th><th>Check-Out</th><th title="Intervalle entre l’entrée et la sortie de cette ligne">Durée</th><th>Actions</th></tr></thead>
        <tbody>{(allDates || validPeriod) && journal.data.map(row => {
          const entryDate = row.clockIn ? dateInMorocco(new Date(row.clockIn)) : row.rawClockIn?.slice(0, 10);
          const exitDate = row.clockOut ? dateInMorocco(new Date(row.clockOut)) : row.origin === 'Sql' ? row.rawClockOut?.slice(0, 10) : null;
          const isOpen = (row.clockIn || row.rawClockIn) && !row.clockOut && !row.rawClockOut;
          return <tr key={row.key} title={row.origin === 'Factorial' ? 'Import Factorial' : row.origin === 'Sql' ? 'Saisie SQL Server' : 'Pointage local'}>
            <td>{row.identification}</td><td>{row.email ?? '—'}</td><td>{row.name ?? '—'}</td><td>{dateLabel(row.referenceDate)}</td>
            <td>{clockLabel(row.clockIn, row.rawClockIn)}</td><td>{isOpen ? <span className="open-shift">En cours</span> : clockLabel(row.clockOut, row.rawClockOut)}{exitDate && exitDate !== entryDate && <small>{dateLabel(exitDate)}</small>}</td>
            <td>{duration(row.durationMinutes)}</td>
            <td>{row.localId && row.employeeId ? <div className="journal-row-actions">
              <button type="button" className="secondary" aria-label="Modifier le pointage" title="Modifier" onClick={() => {
                setEdit({ id: row.localId!, employeeId: row.employeeId!, checkIn: localInput(row.clockIn), checkOut: localInput(row.clockOut) });
                setShowClock(true); setDeleteRow(null); window.scrollTo({ top: 0, behavior: 'smooth' });
              }}><Pencil size={15}/></button>
              <button type="button" className="secondary delete-action" aria-label="Supprimer le pointage" title="Supprimer" onClick={() => { setDeleteRow(row); window.scrollTo({ top: 0, behavior: 'smooth' }); }}><Trash2 size={15}/></button>
            </div> : <span className="muted">Lecture seule</span>}</td>
          </tr>;
        })}</tbody>
      </table></div>
      {(!allDates && !validPeriod) ? <div className="empty">Sélectionnez une période valide.</div> : !journal.data.length && <div className="empty">
        <h3>Aucun pointage sur cette période</h3>
        <p>{employees.filter(person => !person.missing).length} collaborateurs disponibles dans l’annuaire{session.demo ? ' de démonstration' : ' importé depuis Factorial'}.</p>
        <button className="secondary" onClick={() => setScreen('settings')}>Voir les collaborateurs</button>
      </div>}
      <div className="table-footer"><span>{journal.total} pointage(s)</span><div>
        <button disabled={page === 1} onClick={() => setPage(page - 1)}>Précédent</button><span>Page {page}</span>
        <button disabled={page * journal.pageSize >= journal.total} onClick={() => setPage(page + 1)}>Suivant</button>
      </div></div>
    </section>}
  </main>;
}
