using System.Text.Json;
using Microsoft.EntityFrameworkCore;
namespace Pointage;
public record SendRequest(Guid OperationId, string EmployeeId, string Action, string Now, string? End, string? BreakConfigurationId, Guid? AttendanceId);
public class OutboundService(AppDb db, FactorialClient client, SyncService sync)
{
    public async Task<OutboundOperation> Send(SendRequest input, CancellationToken ct)
    {
        if (input.OperationId == Guid.Empty || !new[] { "clock_in", "clock_out", "break_start", "break_end", "completed" }.Contains(input.Action)) throw new AppError("action", "Action ou identifiant d’opération invalide.");
        var start = TimeRules.RequireTimestamp(input.Now);
        DateTimeOffset? end = input.End == null ? null : TimeRules.RequireTimestamp(input.End);
        if (input.Action == "completed" && (end == null || end <= start)) throw new AppError("period", "Le départ doit suivre l’arrivée.");
        await using var gate = new ConnectionLock(db); await gate.Acquire(ct);
        var c = await db.Connections.SingleAsync(ct);
        if (!c.AllowWrites) throw new AppError("read_only", "L’envoi est désactivé.", 403);
        if (!await db.Mappings.AnyAsync(x => x.FactorialEmployeeId == input.EmployeeId, ct)) throw new AppError("unmapped", "Associer cet employé avant tout envoi.");
        var payload = input.Action == "completed"
            ? JsonSerializer.Serialize(new { employee_id = input.EmployeeId, date = TimeRules.BusinessDate(start, c.TimeZone), clock_in = input.Now, clock_out = input.End, workable = true, source = "api" })
            : JsonSerializer.Serialize(new { employee_id = input.EmployeeId, now = input.Now, time_settings_break_configuration_id = input.BreakConfigurationId }, new JsonSerializerOptions { DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull });
        var existing = await db.Outbox.FindAsync([input.OperationId], ct);
        if (existing != null)
        {
            if (existing.Payload != payload || existing.Action != input.Action || existing.AttendanceId != input.AttendanceId) throw new AppError("idempotency_conflict", "Cet identifiant d’opération correspond à un autre envoi.", 409);
            return existing; // An uncertain operation is NEVER blindly resent, even after process restart.
        }
        if (await db.Outbox.AnyAsync(x => x.EmployeeId == input.EmployeeId && (x.Status == "Sending" || x.Status == "Uncertain"), ct)) throw new AppError("uncertain", "Un envoi précédent doit être rapproché avant de continuer.", 409);
        AttendanceRecord? local = null;
        if (input.AttendanceId != null)
        {
            local = await db.Attendance.FindAsync([input.AttendanceId.Value], ct) ?? throw new AppError("record", "Pointage introuvable.", 404);
            if (local.Origin != "Local" || local.FactorialId != null || local.Status != "Pending" || local.EmployeeId != input.EmployeeId || local.ClockIn != start.ToUniversalTime() || local.ClockOut != end?.ToUniversalTime() || input.Action != "completed")
                throw new AppError("record", "Seule une plage locale en attente, identique à l’envoi, peut être transmise.", 409);
        }
        await sync.Verify(c, ct);
        var shifts = await client.Shifts(c, null, null, [input.EmployeeId], ct);
        if (shifts.Any(x => x.CompanyId != c.CompanyId)) throw new AppError("company_mismatch", "Entreprise Factorial incorrecte.");
        var duplicate = Match(shifts, input.Action, start, end, c.TimeZone);
        if (duplicate.Count > 0) throw new AppError("existing_shift", "Un pointage correspondant existe déjà dans Factorial. Synchroniser et vérifier avant tout envoi.", 409);
        var open = shifts.Where(x => x.ClockIn != null && x.ClockOut == null).ToArray();
        var canSend = input.Action switch {
            "clock_in" => open.Length == 0,
            "completed" => open.Length == 0 && !shifts.Any(s => Overlaps(s, start, end!.Value, c.TimeZone)),
            "clock_out" or "break_start" => open.Length == 1 && open[0].Workable == true,
            "break_end" => open.Length == 1 && open[0].Workable == false,
            _ => false
        };
        if (!canSend) throw new AppError("shift_state", "État Factorial incompatible : plage ouverte, pause ou chevauchement. Synchroniser puis vérifier.", 409);
        if (open.Length == 1 && (TimeRules.Parse(open[0], c.TimeZone).In is not { } opened || start < opened)) throw new AppError("shift_time", "L’heure doit suivre l’ouverture de la plage.");
        var operation = new OutboundOperation { Id = input.OperationId, EmployeeId = input.EmployeeId, Action = input.Action, Payload = payload, Status = "Sending", AttendanceId = input.AttendanceId };
        db.Outbox.Add(operation); await db.SaveChangesAsync(ct);
        try
        {
            var shift = await client.Write(c, input.Action, payload, ct);
            if (string.IsNullOrWhiteSpace(shift.Id) || shift.CompanyId != c.CompanyId || shift.EmployeeId != input.EmployeeId) throw new AppError("invalid_write_response", "Réponse d’écriture incohérente.", 502);
            operation.FactorialId = shift.Id; operation.Status = "Sent";
            local ??= await db.Attendance.SingleOrDefaultAsync(x => x.FactorialId == shift.Id, ct);
            if (local == null) { local = new AttendanceRecord { Origin = "Local" }; db.Attendance.Add(local); }
            TimeRules.Apply(local, shift, c.TimeZone);
            await db.SaveChangesAsync(CancellationToken.None);
        }
        catch (Exception e)
        {
            db.ChangeTracker.Clear();
            operation = await db.Outbox.SingleAsync(x => x.Id == input.OperationId, CancellationToken.None);
            operation.Status = "Uncertain"; operation.ErrorCode = (e as AppError)?.Code ?? "write_interrupted";
            await db.SaveChangesAsync(CancellationToken.None);
        }
        return operation;
    }
    public async Task<OutboundOperation> Reconcile(Guid id, CancellationToken ct)
    {
        await using var gate = new ConnectionLock(db); await gate.Acquire(ct);
        var op = await db.Outbox.FindAsync([id], ct) ?? throw new AppError("not_found", "Envoi introuvable.", 404);
        if (op.Status == "Sent") return op;
        var c = await db.Connections.SingleAsync(ct); await sync.Verify(c, ct);
        using var payload = JsonDocument.Parse(op.Payload);
        var p = payload.RootElement;
        var start = TimeRules.RequireTimestamp(p.GetProperty(op.Action == "completed" ? "clock_in" : "now").GetString()!);
        var end = op.Action == "completed" ? TimeRules.RequireTimestamp(p.GetProperty("clock_out").GetString()!) : (DateTimeOffset?)null;
        var shifts = await client.Shifts(c, null, null, [op.EmployeeId], ct);
        if (shifts.Any(x => x.CompanyId != c.CompanyId)) throw new AppError("company_mismatch", "Entreprise incorrecte.");
        var matches = Match(shifts, op.Action, start, end, c.TimeZone);
        if (matches.Count != 1) throw new AppError("reconcile_manual", "Aucune correspondance unique. Vérification manuelle dans Factorial requise ; aucun renvoi automatique.", 409);
        var match = matches[0]; op.FactorialId = match.Id; op.Status = "Sent"; op.ErrorCode = null;
        var local = op.AttendanceId == null ? null : await db.Attendance.FindAsync([op.AttendanceId.Value], ct);
        var imported = await db.Attendance.SingleOrDefaultAsync(x => x.FactorialId == match.Id, ct);
        if (local != null && imported != null && local.Id != imported.Id)
            throw new AppError("reconcile_manual", "Le pointage a déjà été importé. Rapprochement des deux lignes requis ; aucun renvoi.", 409);
        local ??= imported;
        if (local == null) { local = new AttendanceRecord { Origin = "Local" }; db.Attendance.Add(local); }
        TimeRules.Apply(local, match, c.TimeZone); await db.SaveChangesAsync(ct); return op;
    }
    public static List<ShiftDto> Match(IEnumerable<ShiftDto> shifts, string action, DateTimeOffset start, DateTimeOffset? end, string zone) => shifts.Where(s => {
        var t = TimeRules.Parse(s, zone);
        // Minute-only remote data cannot prove an event with nonzero seconds; keep it uncertain.
        return action switch {
            "completed" => s.Workable == true && t.In == start.ToUniversalTime() && t.Out == end?.ToUniversalTime(),
            "clock_in" => s.Workable == true && t.In == start.ToUniversalTime(),
            "clock_out" => s.Workable == true && t.Out == start.ToUniversalTime(),
            "break_start" => s.Workable == false && t.In == start.ToUniversalTime(),
            "break_end" => s.Workable == false && t.Out == start.ToUniversalTime(), _ => false };
    }).ToList();
    private static bool Overlaps(ShiftDto s, DateTimeOffset start, DateTimeOffset end, string zone)
    {
        var t = TimeRules.Parse(s, zone);
        // Unknown clock values on the same business day block writes conservatively.
        if (t.In == null) return s.ReferenceDate == start.ToString("yyyy-MM-dd");
        return t.In < end && (t.Out == null || t.Out > start);
    }
}
