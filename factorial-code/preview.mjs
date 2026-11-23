import { readFile } from "node:fs/promises";
import { createServer } from "node:http";
import { compileFunction } from "node:vm";

const folder = new URL("./processes/calcul-du-pointage/", import.meta.url);
const source = await readFile(new URL("index.js", folder), "utf8");
const schema = JSON.parse(await readFile(new URL("parametersSchema.json", folder), "utf8"));
const run = compileFunction(`return (async () => {\n${source}\n})();`, ["fcode"]);

const employee = { id: "42", company_id: "1", full_name: "Camille Martin (fictif)" };
const row = { employee_id: employee.id, company_id: employee.company_id };
const samples = [
  ["1", "2026-11-16", "08:30", "12:00", 210, true],
  ["2", "2026-11-16", "12:00", "12:30", 30, false],
  ["3", "2026-11-16", "12:30", "18:00", 330, true],
  ["4", "2026-11-17", "22:00", "06:00", 480, true],
  ["5", "2026-11-19", "09:00", null, null, true],
];
const shifts = samples.map(([id, date, clock_in, clock_out, minutes, workable]) => ({
  ...row, id, date, reference_date: date, clock_in, clock_out, minutes, workable,
}));
const totals = [
  { ...row, date: "2026-11-16", time_unit: "minute", tracked_minutes: 540 },
  { ...row, date: "2026-11-17", time_unit: "minute", tracked_minutes: 480 },
];
const list = (items) => async () => ({ data: { data: items, meta: { has_next_page: false } } });
const client = {
  employees: { employees: { get: async ({ path }) => ({ data: path.id === employee.id ? employee : undefined }) } },
  attendance: { shifts: { list: list(shifts) }, workedTimes: { list: list(totals) } },
};
const defaults = { employeeId: employee.id, from: "2026-11-16", to: "2026-11-20" };

function escapeHtml(value) {
  const entities = { "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" };
  return String(value).replace(/[&<>"']/g, (character) => entities[character]);
}

function renderReport(message) {
  return message.split("\n\n").map((block) => {
    if (block.startsWith("## ")) return `<h2>${escapeHtml(block.slice(3).replace(/\\([\\`*_{}\[\]()#+\-.!|<>&~])/g, "$1"))}</h2>`;
    if (block.startsWith("### ")) return `<h3>${escapeHtml(block.slice(4))}</h3>`;
    if (!block.startsWith("|")) return `<p>${escapeHtml(block.replaceAll("`", ""))}</p>`;
    const [head, , ...body] = block.split("\n");
    const cells = (line, tag) => line.trim().slice(1, -1).split("|")
      .map((cell) => `<${tag}>${escapeHtml(cell.trim())}</${tag}>`).join("");
    return `<div class="table"><table><thead><tr>${cells(head, "th")}</tr></thead><tbody>${body
      .map((line) => `<tr>${cells(line, "td")}</tr>`).join("")}</tbody></table></div>`;
  }).join("");
}

function renderPage(parameters, result) {
  const fields = Object.entries(schema.properties).map(([key, field]) => {
    const options = field.format === "date" ? 'type="date"' : `inputmode="numeric" pattern="${escapeHtml(field.pattern)}"`;
    return `<label>${escapeHtml(field.title)}<input name="${key}" ${options} value="${escapeHtml(parameters[key])}" required></label>`;
  }).join("");
  const report = result?.body.message ? renderReport(result.body.message) : "";
  const error = result?.body.errorMessage ? `<p role="alert">${escapeHtml(result.body.errorMessage)}</p>` : "";
  return `<!doctype html><html lang="fr"><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1">
<title>${escapeHtml(schema.title)} · Aperçu</title><style>
:root {color-scheme:light; font:16px/1.55 system-ui,sans-serif; color:#202434; background:#f5f4f7}
* {box-sizing:border-box} body {margin:0} main {max-width:1080px; margin:0 auto; padding:32px 20px}
h1 {margin:4px 0; font-size:clamp(1.5rem,4vw,2.25rem)} h2 {margin-top:0; font-size:1.25rem} h3 {margin:24px 0 8px}
.preview {color:#613566; font-weight:650} section {margin-top:20px; background:white; padding:24px; border-radius:14px; border:1px solid #e1dce5}
form {display:flex; gap:12px; flex-wrap:wrap; align-items:end} label {display:grid; gap:5px; font-size:.9rem; font-weight:600}
input,button {font:inherit; border-radius:8px; padding:9px 12px; min-height:42px} input {border:1px solid #bfb7c6; color:inherit; background:white}
button {border:1px solid #673d70; color:#fff; background:#673d70; font-weight:650; cursor:pointer}
.table {overflow:auto} table {width:100%; border-collapse:collapse; font-size:.9rem}
td,th {text-align:left; padding:9px 12px; border-bottom:1px solid #ebe7ed; white-space:nowrap} th {background:#f4eef5}
p {margin:8px 0 14px} [role=alert] {color:#892b36}
</style><main><header><span class="preview">Aperçu local · Données fictives</span><h1>${escapeHtml(schema.title)}</h1></header>
<section aria-label="Période et collaborateur"><p>${escapeHtml(schema.description)}</p><p>Le collaborateur de démonstration porte l’ID 42.</p>
<form>${fields}<button>Afficher les pointages</button></form>${error}</section>${report ? `<section aria-label="Rapport">${report}</section>` : ""}
</main></html>`;
}

const host = "127.0.0.1";
const port = Number(process.env.POINTAGE_PREVIEW_PORT || 5082);
createServer(async (request, response) => {
  try {
    const url = new URL(request.url, `http://${host}:${port}`);
    if (request.method !== "GET" || url.pathname !== "/") {
      response.writeHead(404).end();
      return;
    }
    const parameters = Object.fromEntries(Object.entries(defaults).map(([key, value]) => [key, url.searchParams.get(key) ?? value]));
    const result = await run({
      context: { parameters, factorial: { company_id: "1", access_id: "1", actor: { type: "user" }, entry_point: { type: "form" } } },
      import: () => ({ FactorialClient: { create: async () => client } }),
    });
    response.writeHead(result.status ?? 200, { "Content-Type": "text/html; charset=utf-8", "Cache-Control": "no-store" });
    response.end(renderPage(parameters, result));
  } catch {
    response.writeHead(500, { "Content-Type": "text/plain; charset=utf-8" }).end("Aperçu indisponible.");
  }
}).listen(port, host, () => console.log(`Aperçu local : http://${host}:${port}/`));
