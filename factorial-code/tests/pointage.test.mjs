import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import { test } from "node:test";
import { compileFunction } from "node:vm";
import { FactorialClient } from "@factorialco/api-client";

const processFolder = new URL("../processes/calcul-du-pointage/", import.meta.url);
const source = await readFile(new URL("index.js", processFolder), "utf8");
const run = compileFunction(`return (async () => {\n${source}\n})();`, ["fcode"]);
const date = "2026-11-19";
const employee = { id: "42", company_id: "100", full_name: "Test **nom**" };
const identity = { company_id: "100", access_id: "60", actor: { type: "user" }, entry_point: { type: "form" } };

function shift(id, clockIn, clockOut, fields = {}) {
  return {
    id, employee_id: employee.id, company_id: employee.company_id,
    date, reference_date: date, workable: true, clock_in: clockIn, clock_out: clockOut,
    ...fields,
  };
}

function harness({ shifts = [], totals = [], secondPage, upstream } = {}) {
  const calls = [];
  const answer = (body, status = 200) => Response.json(body, { status });
  const client = new FactorialClient({
    baseUrl: "https://test.factorial.invalid",
    token: "local-test-token",
    async fetch(request) {
      const url = new URL(request.url);
      calls.push(url);
      assert.equal(request.method, "GET");
      assert.equal(url.hostname, "test.factorial.invalid");
      assert.equal(request.headers.get("authorization"), "Bearer local-test-token");
      if (upstream) {
        const response = upstream(url);
        if (response) return response;
      }
      if (url.pathname.endsWith(`/employees/employees/${employee.id}`)) return answer(employee);
      assert.deepEqual(url.searchParams.getAll("employee_ids[]"), [employee.id]);
      if (url.pathname.endsWith("/attendance/shifts")) {
        assert.equal(url.searchParams.get("half_day"), "false");
        assert.equal(url.searchParams.has("workable"), false);
        if (secondPage && !url.searchParams.has("after_id")) {
          return answer({ data: shifts, meta: { has_next_page: true, end_cursor: "123" } });
        }
        if (secondPage) assert.equal(url.searchParams.get("after_id"), "123");
        return answer({ data: secondPage ?? shifts, meta: { has_next_page: false } });
      }
      assert.ok(url.pathname.endsWith("/attendance/worked_times"));
      return answer({ data: totals, meta: { has_next_page: false } });
    },
  });
  return {
    calls,
    invoke(parameters = {}, context = identity) {
      return run({
        context: { parameters: { employeeId: employee.id, from: date, to: date, ...parameters }, factorial: context },
        import(name) {
          assert.equal(name, "FactorialClient");
          return { FactorialClient: { create: async () => client } };
        },
      });
    },
  };
}

test("configure un onglet privé, réservé aux administrateurs", async () => {
  const metadata = JSON.parse(await readFile(new URL("metadata.json", processFolder), "utf8"));
  assert.deepEqual(metadata.factorial.requiredPolicies, [["company.admin"]]);
  assert.equal(metadata.factorial.form.public, false);
  assert.equal(metadata.factorial.form.appTool, true);
  assert.equal(metadata.factorial.awaitResult, true);
  assert.equal(metadata.webhook.enabled, false);
});

test("lit toutes les pages en GET, sans doublons ni données d’une autre entreprise", async () => {
  const morning = shift("10", "08:30", "12:00", { minutes: 210 });
  const fixture = harness({
    shifts: [morning, shift("11", "12:00", "12:30", { workable: false, minutes: 30 })],
    secondPage: [morning, shift("13", "12:30", "18:00", { minutes: 330 }),
      shift("99", "00:00", "23:59", { company_id: "200" })],
    totals: [{ company_id: "100", employee_id: "42", date, time_unit: "minute", tracked_minutes: 570 }],
  });
  const result = await fixture.invoke();
  assert.equal(result.isTransient, true);
  assert.match(result.body.message, /Test \\\*\\\*nom\\\*\\\*/);
  assert.ok(result.body.message.includes(`| ${date} | 9 h 00 | 30 min | 9 h 30 | Plages terminées |`));
  assert.equal(result.body.message.match(/08:30/g)?.length, 1);
  assert.doesNotMatch(result.body.message, /23:59/);
  assert.equal(fixture.calls.length, 4);
});

test("gère un départ après minuit, un pointage ouvert, une journée vide et un vrai zéro", async () => {
  const lastDate = "2026-11-21";
  const fixture = harness({
    shifts: [shift("10", "22:00", "06:00", { minutes: 480 }), shift("11", "14:00", null, {
      date: lastDate, reference_date: lastDate, minutes: 0,
    })],
    totals: [{ company_id: "100", employee_id: "42", date: lastDate, time_unit: "minute", tracked_minutes: 0 }],
  });
  const { body } = await fixture.invoke({ to: lastDate });
  assert.ok(body.message.includes(`| ${date} | 8 h 00 | — | — | Plages terminées |`));
  assert.ok(body.message.includes("2026-11-20 06:00"));
  assert.ok(body.message.includes("| 2026-11-20 | — | — | — | Aucune plage |"));
  assert.ok(body.message.includes(`| ${lastDate} | Incomplet | — | 0 min | À vérifier |`));
  assert.ok(body.message.includes(`| ${lastDate} | Travail | 14:00 | — | — | Ouvert |`));
});

test("applique les changements d’heure du Maroc et identifie l’heure locale répétée", async () => {
  const rollback = "2026-02-15";
  const fixture = harness({ shifts: [
    shift("10", "01:00", "04:00", { date: rollback, reference_date: rollback }),
    shift("11", "02:30", "03:30", { date: rollback, reference_date: rollback, workable: false }),
  ] });
  const { body } = await fixture.invoke({ from: rollback, to: rollback });
  assert.ok(body.message.includes(`| ${rollback} | 4 h 00 | Incomplet | — | À vérifier |`));
});

test("convertit les horodatages avec décalage et fusionne les plages qui se chevauchent", async () => {
  const summerDate = "2026-04-15";
  const dates = { date: summerDate, reference_date: summerDate };
  const fixture = harness({ shifts: [
    shift("10", `${summerDate}T07:00:00Z`, `${summerDate}T10:00:00Z`, dates),
    shift("11", "10:00", "12:00", dates),
    shift("12", "12:00", "12:00", { ...dates, clock_in_with_seconds: "12:00:27", workable: false, minutes: 0 }),
  ] });
  const { body } = await fixture.invoke({ from: summerDate, to: summerDate });
  assert.ok(body.message.includes(`| ${summerDate} | 4 h 00 | 0 min | — | Plages terminées |`), body.message);
  assert.ok(body.message.includes(`| ${summerDate} | Travail | 08:00 | 11:00 |`));
});

test("refuse les paramètres ou contextes invalides avant l’appel API", async () => {
  const fixture = harness();
  for (const parameters of [{ from: "2026-02-30" }, { from: "2026-09-01" }, { employeeId: "../../1" }]) {
    assert.equal((await fixture.invoke(parameters)).status, 400);
  }
  assert.equal((await fixture.invoke({ company_id: "100" }, null)).status, 403);
  assert.equal((await fixture.invoke({}, { ...identity, entry_point: { type: "backend" } })).status, 403);
  assert.equal(fixture.calls.length, 0);
});

test("refuse un collaborateur d’une autre entreprise avant la lecture des pointages", async () => {
  const fixture = harness();
  assert.equal((await fixture.invoke({}, { ...identity, company_id: "200" })).status, 404);
  assert.equal(fixture.calls.length, 1);
});

test("signale les refus et limites de l’API sans exposer la réponse brute", async () => {
  for (const [upstreamStatus, expectedStatus] of [[401, 403], [403, 403], [429, 429], [500, 502]]) {
    const fixture = harness({ upstream(url) {
      if (url.pathname.endsWith("/attendance/shifts")) return Response.json({ message: "détail interne" }, { status: upstreamStatus });
    } });
    const result = await fixture.invoke();
    assert.equal(result.status, expectedStatus);
    assert.equal(result.isTransient, true);
    assert.doesNotMatch(result.body.errorMessage, /détail interne/);
  }
});

test("refuse un curseur répété au lieu de publier un rapport partiel", async () => {
  const fixture = harness({ upstream(url) {
    if (url.pathname.endsWith("/attendance/shifts")) {
      return Response.json({ data: [shift("10", "08:00", "10:00")], meta: { has_next_page: true, end_cursor: "123" } });
    }
  } });
  assert.equal((await fixture.invoke()).status, 502);
  assert.equal(fixture.calls.length, 4);
});
