export async function api<T = unknown>(path: string, method = 'GET', data?: unknown): Promise<T> {
  const response = await fetch('/api' + path, { method, credentials: 'same-origin', headers: { 'Content-Type': 'application/json', 'X-Pointage-Request': '1' }, body: data === undefined ? undefined : JSON.stringify(data) });
  const body = await response.text();
  let json; try { json = body ? JSON.parse(body) : null; } catch { throw new Error('Réponse serveur invalide. Vérifier que le backend est démarré.'); }
  if (!response.ok) throw new Error(json?.message ?? (response.status === 401 ? 'Session expirée : reconnectez-vous.' : `Erreur HTTP ${response.status}`));
  return json as T;
}
