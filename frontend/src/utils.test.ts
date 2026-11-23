import { describe, expect, it } from 'vitest';
import { dateInMorocco, duration, time } from './utils';
describe('affichage Maroc', () => {
  it('distingue absence et zéro', () => { expect(duration(null)).toBe('—'); expect(duration(0)).toBe('0 h 00'); });
  it('utilise les règles du fuseau au Ramadan', () => { expect(time('2026-01-15T08:00:00Z')).toBe('09:00'); expect(time('2026-03-01T08:00:00Z')).toBe('08:00'); });
  it('affiche le lendemain après minuit local', () => { expect(dateInMorocco(new Date('2026-01-18T23:30:00Z'))).toBe('2026-01-19'); });
});
