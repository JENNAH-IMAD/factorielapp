import { useEffect, useState } from 'react';
import { RefreshCw, ShieldCheck } from 'lucide-react';
import { api } from './api';
import { labels, stamp } from './utils';

export type Employee = { id: string; name: string; companyIdentifier: string | null; email: string | null; missing: boolean };
type Connection = {
  environment: 'Production' | 'Demo'; authMode: 'ApiKey' | 'CompanyToken'; syncMinutes: number;
  lookbackDays: number; timeZone: string; companyId: string | null; status: string; lastSync: string | null;
};
type SettingsData = { connection: Connection; version: string; demo: boolean; apiKeyConfigured: boolean; companyTokenConfigured: boolean };
type Run = { id: string; kind: string; status: string; count: number; startedAt: string };
type History = { runs: Run[]; errors: { runId: string; message: string }[] };

export function Settings({ employees, onImported }: { employees: Employee[]; onImported: () => void }) {
  const [settings, setSettings] = useState<SettingsData | null>(null);
  const [draft, setDraft] = useState<Connection | null>(null);
  const [history, setHistory] = useState<History>({ runs: [], errors: [] });
  const [message, setMessage] = useState('');
  const [error, setError] = useState('');
  const [busy, setBusy] = useState('');
  const [search, setSearch] = useState('');
  const [page, setPage] = useState(1);

  useEffect(() => {
    let active = true;
    Promise.all([api<SettingsData>('/connection'), api<History>('/sync/history')])
      .then(([data, log]) => { if (active) { setSettings(data); setDraft(data.connection); setHistory(log); } })
      .catch(e => { if (active) setError(e.message); });
    return () => { active = false; };
  }, []);

  async function execute(label: string, run: () => Promise<string>) {
    setBusy(label); setError(''); setMessage('');
    try { setMessage(await run()); }
    catch (e) { setError((e as Error).message); }
    finally { setBusy(''); }
  }

  function save() {
    if (!draft) return;
    return execute('Enregistrement…', async () => {
      const saved = await api<Connection>('/connection', 'PUT', { ...draft, allowWrites: false });
      setDraft(saved);
      setSettings(previous => previous && { ...previous, connection: saved });
      return 'Paramétrage enregistré.';
    });
  }

  function readFactorial(path: string, label: string) {
    return execute(label, async () => {
      const run = await api<Run>(path, 'POST');
      const [data, log] = await Promise.all([api<SettingsData>('/connection'), api<History>('/sync/history')]);
      setSettings(data); setDraft(data.connection); setHistory(log);
      if (run.status !== 'Succeeded') throw new Error(log.errors.find(item => item.runId === run.id)?.message ?? 'La lecture de Factorial a échoué.');
      if (path === '/sync/employees') {
        onImported();
        return `${run.count} collaborateurs lus dans Factorial.`;
      }
      return 'Connexion Factorial vérifiée.';
    });
  }

  const directory = employees.filter(person => [person.name, person.companyIdentifier, person.email, person.id]
    .some(value => value?.toLocaleLowerCase('fr').includes(search.trim().toLocaleLowerCase('fr'))));
  const pageSize = 15;
  const pageCount = Math.max(1, Math.ceil(directory.length / pageSize));
  const currentPage = Math.min(page, pageCount);
  const dirty = !!settings && !!draft && JSON.stringify(settings.connection) !== JSON.stringify(draft);
  const canRead = !!settings && (settings.demo || (settings.connection.authMode === 'ApiKey' ? settings.apiKeyConfigured : settings.companyTokenConfigured));

  return <>
    {error && <div className="flash error" role="alert">{error}</div>}
    {message && <div className="flash success" role="status">{message}</div>}
    {busy && <div className="progress" role="status"><RefreshCw size={16} className="spinning"/>{busy}</div>}
    {settings && draft && <section className="panel form-panel connection-settings" aria-label="Connexion Factorial">
      <h2>Connexion Factorial</h2>
      <p>API {settings.version} · {settings.connection.status} · Dernier import {stamp(settings.connection.lastSync)}</p>
      <div className="info-box"><ShieldCheck size={20}/><span>{settings.demo ? 'Connexion simulée. Les collaborateurs de cet aperçu sont fictifs.' : canRead ? 'Identifiant serveur disponible. Accès à Factorial en lecture seule.' : 'Identifiant serveur manquant. Configurez-le avant de tester la connexion ou de lancer un import. Les collaborateurs déjà importés restent disponibles.'}</span></div>
      <form onSubmit={e => { e.preventDefault(); void save(); }}>
        <div className="form-grid">
          <label>Environnement<select value={draft.environment} disabled={!!settings.connection.companyId || !!busy || settings.demo} onChange={e => setDraft({ ...draft, environment: e.target.value as Connection['environment'] })}>
            <option value="Production">Production</option><option value="Demo">Démonstration Factorial</option>
          </select>{settings.connection.companyId && <small>Cette base est déjà liée à une entreprise.</small>}</label>
          <label>Authentification<select value={draft.authMode} disabled={!!busy || settings.demo} onChange={e => setDraft({ ...draft, authMode: e.target.value as Connection['authMode'] })}>
            <option value="ApiKey">Clé API</option><option value="CompanyToken">Jeton d’entreprise</option>
          </select>{!settings.demo && <small>{(draft.authMode === 'ApiKey' ? settings.apiKeyConfigured : settings.companyTokenConfigured) ? 'Identifiant configuré côté serveur' : 'Identifiant à configurer côté serveur'}</small>}</label>
          <label>Import automatique (minutes)<input type="number" min="0" max="1440" required value={draft.syncMinutes} disabled={!!busy} onChange={e => setDraft({ ...draft, syncMinutes: e.target.valueAsNumber })}/><small>0 désactive les imports. Fréquence minimale : 5 minutes.</small></label>
          <label>Période de l’import automatique (jours)<input type="number" min="1" max="366" required value={draft.lookbackDays} disabled={!!busy} onChange={e => setDraft({ ...draft, lookbackDays: e.target.valueAsNumber })}/></label>
          <label>Fuseau des heures sans décalage<input value={draft.timeZone} required disabled={!!busy} onChange={e => setDraft({ ...draft, timeZone: e.target.value })}/><small>Les heures du journal s’affichent au Maroc.</small></label>
        </div>
        <div className="form-actions">
          <button type="button" className="secondary" disabled={!!busy || dirty || !canRead} onClick={() => readFactorial('/connection/test', 'Test de la connexion…')}>Tester la connexion</button>
          <button className="primary" disabled={!!busy || !dirty}>Enregistrer</button>
        </div>
      </form>
    </section>}
    <section className="panel" aria-label="Collaborateurs Factorial">
      <div className="panel-title journal-actions"><div><h2>Collaborateurs Factorial <span className="count">{employees.filter(person => !person.missing).length}</span></h2><p>Annuaire de référence pour le journal et les saisies locales.</p></div>
        <button className="secondary" disabled={!!busy || !canRead || dirty} onClick={() => readFactorial('/sync/employees', 'Import des collaborateurs…')}><RefreshCw size={16}/> Importer les collaborateurs</button></div>
      <div className="filters journal-filters"><label>Rechercher<input type="search" value={search} onChange={e => { setSearch(e.target.value); setPage(1); }} placeholder="Nom, matricule ou email"/></label></div>
      <div className="table-scroll"><table className="journal-table"><thead><tr><th>ID Factorial</th><th>Matricule</th><th>Nom</th><th>Email</th><th>Annuaire</th></tr></thead>
        <tbody>{directory.slice((currentPage - 1) * pageSize, currentPage * pageSize).map(person => <tr key={person.id}>
          <td>{person.id}</td><td>{person.companyIdentifier ?? '—'}</td><td>{person.name}</td><td>{person.email ?? '—'}</td><td>{person.missing ? 'Absent du dernier import' : 'Disponible'}</td>
        </tr>)}</tbody></table></div>
      {!directory.length && <div className="empty">Aucun collaborateur à afficher.</div>}
      <div className="table-footer"><span>{directory.length} résultat(s)</span><div>
        <button disabled={currentPage === 1} onClick={() => setPage(currentPage - 1)}>Précédent</button><span>Page {currentPage} / {pageCount}</span>
        <button disabled={currentPage === pageCount} onClick={() => setPage(currentPage + 1)}>Suivant</button>
      </div></div>
    </section>
    <section className="panel" aria-label="Historique des imports">
      <div className="panel-title"><h2>Dernières opérations</h2></div>
      <div className="table-scroll"><table className="journal-table"><thead><tr><th>Opération</th><th>Date</th><th>État</th><th>Détail</th></tr></thead>
        <tbody>{history.runs.slice(0, 10).map(run => <tr key={run.id}>
          <td>{labels[run.kind] ?? run.kind}</td><td>{stamp(run.startedAt)}</td><td>{labels[run.status] ?? run.status}</td>
          <td>{history.errors.find(item => item.runId === run.id)?.message ?? (run.kind === 'Connection' ? '—' : `${run.count} élément(s)`)}</td>
        </tr>)}</tbody></table></div>
      {!history.runs.length && <div className="empty">Aucune opération enregistrée.</div>}
    </section>
  </>;
}
