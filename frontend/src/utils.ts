export const zone = 'Africa/Casablanca';
export function dateInMorocco(date = new Date()) {
  const parts = new Intl.DateTimeFormat('en-CA', { timeZone: zone, year: 'numeric', month: '2-digit', day: '2-digit' }).formatToParts(date);
  return ['year', 'month', 'day'].map(t => parts.find(p => p.type === t)?.value).join('-');
}
export function time(value: string | null) { return value ? new Intl.DateTimeFormat('fr-MA', { timeZone: zone, hour: '2-digit', minute: '2-digit' }).format(new Date(value)) : '—'; }
export function duration(minutes: number | null) { if (minutes == null) return '—'; const n = Math.round(minutes); return `${Math.floor(n / 60)} h ${String(n % 60).padStart(2, '0')}`; }
export function stamp(value?: string | null) { return value ? new Intl.DateTimeFormat('fr-FR', { timeZone: zone, day: '2-digit', month: 'short', hour: '2-digit', minute: '2-digit' }).format(new Date(value)) : 'Jamais'; }
export function day(value: string) { return new Intl.DateTimeFormat('fr-FR', { day: '2-digit', month: 'short', timeZone: 'UTC' }).format(new Date(value + 'T12:00:00Z')); }
export const labels: Record<string,string> = { Synced: 'Synchronisé', LocalOnly: 'Pointage local', Pending: 'À envoyer', MissingRemote: 'Absent de Factorial', Conflict: 'Conflit', LocalChanged: 'Modifié localement', Running: 'En cours', Succeeded: 'Terminée', Failed: 'Échec', Interrupted: 'Interrompue', Sent: 'Envoyé', Sending: 'À vérifier', Uncertain: 'À rapprocher', Employees: 'Employés', Attendance: 'Pointages', Connection: 'Test de connexion', clock_in: 'Arrivée', clock_out: 'Départ', break_start: 'Début de pause', break_end: 'Fin de pause', completed: 'Plage terminée' };
