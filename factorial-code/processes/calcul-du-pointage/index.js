const dayMs = 86_400_000;
const minuteMs = 60_000;
const zone = new Intl.DateTimeFormat("sv-SE", {
  timeZone: "Africa/Casablanca",
  year: "numeric", month: "2-digit", day: "2-digit",
  hour: "2-digit", minute: "2-digit", second: "2-digit", hourCycle: "h23",
});
const localTime = /^(?:[01]\d|2[0-3]):[0-5]\d(?::[0-5]\d)?$/;
const offsetTimestamp = /^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}(?::\d{2}(?:\.\d+)?)?(?:Z|[+-]\d{2}:?\d{2})$/i;

function dayValue(value) {
  if (typeof value !== "string" || !/^\d{4}-\d{2}-\d{2}$/.test(value)) return NaN;
  const instant = Date.parse(`${value}T00:00:00Z`);
  return Number.isFinite(instant) && dateKey(instant) === value ? instant : NaN;
}

function dateKey(instant) {
  return new Date(instant).toISOString().slice(0, 10);
}

function wallTime(instant) {
  const parts = Object.fromEntries(zone.formatToParts(instant).map(({ type, value }) => [type, value]));
  return `${parts.year}-${parts.month}-${parts.day}T${parts.hour}:${parts.minute}:${parts.second}`;
}

function resolveWallTime(date, clock) {
  const wall = `${date}T${clock.length === 5 ? `${clock}:00` : clock}`;
  const nominal = Date.parse(`${wall}Z`);
  const offsets = new Set([-dayMs, 0, dayMs].map((distance) => {
    const sample = nominal + distance;
    return Date.parse(`${wallTime(sample)}Z`) - sample;
  }));
  const matches = [...offsets].map((offset) => nominal - offset)
    .filter((instant) => wallTime(instant) === wall);
  return matches.length === 1 ? matches[0] : null;
}

function clockText(instant, fallback, referenceDate) {
  if (instant === null) return localTime.test(fallback ?? "") ? fallback : "—";
  const [date, clock] = wallTime(instant).split("T");
  return date === referenceDate ? clock.slice(0, 5) : `${date} ${clock.slice(0, 5)}`;
}

function segment(shift, referenceDate) {
  const departure = shift.clock_out;
  const arrival = localTime.test(departure ?? "") && departure.length === 5
    ? shift.clock_in || shift.clock_in_with_seconds
    : shift.clock_in_with_seconds || shift.clock_in;
  const shiftDate = Number.isFinite(dayValue(shift.date)) ? shift.date : referenceDate;
  const hasLocalClocks = localTime.test(arrival ?? "") && localTime.test(departure ?? "");
  let endDate = shiftDate;
  if (hasLocalClocks && arrival.padEnd(8, ":00") > departure.padEnd(8, ":00")) {
    endDate = dateKey(dayValue(shiftDate) + dayMs);
  }
  const instant = (value, date) => {
    if (localTime.test(value ?? "")) return resolveWallTime(date, value);
    if (offsetTimestamp.test(value ?? "") && Number.isFinite(Date.parse(value))) return Date.parse(value);
    return null;
  };
  const start = instant(arrival, shiftDate);
  const end = instant(departure, endDate);
  const complete = start !== null && end !== null && end >= start;
  return {
    kind: shift.workable === true ? "Travail" : shift.workable === false ? "Pause" : "Type inconnu",
    start, end, complete,
    arrival: clockText(start, arrival, referenceDate),
    departure: clockText(end, departure, referenceDate),
    reported: shift.minutes,
    status: !departure ? "Ouvert" : complete ? "Terminé" : "Horaires à vérifier",
  };
}

function sumSegments(segments) {
  if (!segments.length) return "—";
  if (segments.some((item) => !item.complete)) return "Incomplet";
  // Merge overlapping intervals to count each minute once.
  const ordered = [...segments].sort((a, b) => a.start - b.start);
  let total = 0;
  let coveredUntil = -Infinity;
  for (const { start, end } of ordered) {
    total += Math.max(0, end - Math.max(start, coveredUntil));
    coveredUntil = Math.max(end, coveredUntil);
  }
  return duration(Math.round(total / minuteMs));
}

function duration(minutes) {
  if (!Number.isSafeInteger(minutes) || minutes < 0) return "—";
  if (minutes < 60) return `${minutes} min`;
  return `${Math.floor(minutes / 60)} h ${String(minutes % 60).padStart(2, "0")}`;
}

function success(message) {
  return { isTransient: true, body: { message } };
}

function failure(status, errorMessage) {
  return { status, isTransient: true, body: { errorMessage } };
}

const { factorial, parameters = {} } = fcode.context;
const companyId = String(factorial?.company_id ?? "");
if (!companyId || !factorial?.access_id || factorial.actor?.type !== "user" || factorial.entry_point?.type !== "form") {
  return failure(403, "Ouvrez l’onglet Calcul du pointage depuis votre compte administrateur Factorial.");
}

const { employeeId, from, to } = parameters;
const firstDay = dayValue(from);
const lastDay = dayValue(to);
if (typeof employeeId !== "string" || !/^[1-9][0-9]{0,19}$/.test(employeeId) ||
    !Number.isFinite(firstDay) || !Number.isFinite(lastDay) ||
    firstDay > lastDay || lastDay - firstDay >= 31 * dayMs || from < "1900-01-02" || to > "9999-12-30") {
  return failure(400, "Choisissez un identifiant Factorial valide et une période de 1 à 31 jours.");
}

try {
  const { FactorialClient } = fcode.import("FactorialClient");
  const client = await FactorialClient.create();
  const { data: employee, response, error } = await client.employees.employees.get({
    path: { id: employeeId }, throwOnError: false, responseStyle: "fields",
  });
  if (response?.status !== 404 && (error || (response && !response.ok))) return apiFailure(response?.status);
  if (!employee || String(employee.id) !== employeeId || String(employee.company_id) !== companyId) {
    return failure(404, "Collaborateur introuvable dans l’annuaire de cette entreprise.");
  }

  const scope = { "employee_ids[]": [employeeId] };
  const shifts = new Map();
  const totals = new Map();
  const belongsToEmployee = (row) => String(row.employee_id) === employeeId && String(row.company_id) === companyId;
  const readShifts = async () => {
    for await (const row of readPages(client.attendance.shifts, {
      ...scope,
      start_on: dateKey(firstDay - dayMs), end_on: dateKey(lastDay + dayMs),
      half_day: false, sort_created_at_asc: true,
    })) {
      if (!belongsToEmployee(row)) continue;
      const date = Number.isFinite(dayValue(row.reference_date)) ? row.reference_date : row.date;
      if (date >= from && date <= to && Number.isFinite(dayValue(date))) {
        shifts.set(String(row.id), { date, ...segment(row, date) });
      }
    }
  };
  const readTotals = async () => {
    for await (const row of readPages(client.attendance.workedTimes, {
      ...scope, start_on: from, end_on: to,
      include_time_range_category: false, include_non_attendable_employees: false,
    })) {
      if (belongsToEmployee(row) && row.date >= from && row.date <= to) totals.set(row.date, row);
    }
  };
  await Promise.all([readShifts(), readTotals()]);

  const byDay = Map.groupBy(shifts.values(), (shift) => shift.date);
  const name = escapeMarkdown(employee.full_name || `Collaborateur ${employeeId}`);
  const days = [
    "| Date | Travail calculé | Pauses calculées | Temps suivi Factorial | Statut |",
    "| --- | --- | --- | --- | --- |",
  ];
  const detail = [
    "| Date de référence | Type | Arrivée | Départ | Durée Factorial de la plage | Statut |",
    "| --- | --- | --- | --- | --- | --- |",
  ];
  for (let day = firstDay; day <= lastDay; day += dayMs) {
    const date = dateKey(day);
    const entries = byDay.get(date) ?? [];
    const total = totals.get(date);
    const work = entries.filter((item) => item.kind === "Travail");
    const breaks = entries.filter((item) => item.kind === "Pause");
    const unknown = entries.some((item) => item.kind === "Type inconnu");
    const status = !entries.length ? "Aucune plage"
      : unknown || entries.some((item) => !item.complete) ? "À vérifier" : "Plages terminées";
    const tracked = total?.time_unit === "minute" ? duration(total.tracked_minutes) : "—";
    days.push(`| ${date} | ${unknown ? "À vérifier" : sumSegments(work)} | ${unknown ? "À vérifier" : sumSegments(breaks)} | ${tracked} | ${status} |`);
    entries.sort((a, b) => (a.start ?? Infinity) - (b.start ?? Infinity));
    for (const row of entries) {
      const reported = row.status === "Ouvert" ? "—" : duration(row.reported);
      detail.push(`| ${date} | ${row.kind} | ${row.arrival} | ${row.departure} | ${reported} | ${row.status} |`);
    }
  }
  const explanation = "Travail calculé = durée des plages de travail, hors plages de pause distinctes. " +
    "Temps suivi Factorial = total quotidien `tracked_minutes` fourni par Factorial lorsqu’il est exprimé en minutes. " +
    "Les heures sont affichées en Africa/Casablanca. Le symbole — indique une valeur indisponible.";
  return success([
    `## ${name} — ${from} au ${to}`,
    days.join("\n"),
    shifts.size ? `### Détail des pointages\n\n${detail.join("\n")}` : "Aucune plage de pointage renvoyée par Factorial sur cette période.",
    explanation,
  ].join("\n\n"));
} catch (error) {
  return apiFailure(error?.status ?? error?.statusCode ?? error?.response?.status);
}

async function* readPages(resource, query) {
  const seen = new Set();
  let cursor;
  do {
    const filters = cursor ? { ...query, after_id: cursor } : query;
    const { data, response, error } = await resource.list({ query: filters, throwOnError: false, responseStyle: "fields" });
    if (error || (response && !response.ok) || !Array.isArray(data?.data)) {
      throw Object.assign(new Error("Lecture Factorial impossible"), { status: response?.status });
    }
    yield* data.data;
    if (!data.meta?.has_next_page) return;
    cursor = data.meta.end_cursor;
    if (!cursor || seen.has(cursor)) throw new Error("Pagination Factorial invalide");
    seen.add(cursor);
  } while (cursor);
}

function apiFailure(status) {
  if (status === 401 || status === 403) {
    return failure(403, "Vérifiez les autorisations de lecture Collaborateurs et Suivi du temps de l’application Factorial.");
  }
  if (status === 429) return failure(429, "Factorial limite les requêtes. Réessayez dans quelques instants.");
  return failure(502, "Factorial n’a pas pu fournir les données de pointage. Réessayez dans quelques instants.");
}

function escapeMarkdown(value) {
  return String(value).replace(/[\r\n\u2028\u2029]/g, " ").replace(/[\\`*_{}\[\]()#+\-.!|<>&~]/g, "\\$&");
}
