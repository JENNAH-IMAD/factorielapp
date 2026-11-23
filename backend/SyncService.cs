using Microsoft.EntityFrameworkCore;
namespace Pointage;
public record SyncRequest(string Start, string End, string[] EmployeeIds);
public class SyncService(AppDb db, FactorialClient client)
{
    public async Task Verify(FactorialConnection c, CancellationToken ct)
    {
        var credentials = await client.ReadAll<CredentialDto>(c, "api_public/credentials", ct);
        var companies = credentials.Select(x => x.CompanyId).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct().ToArray();
        if (companies.Length != 1) throw new AppError("identity", "Identité Factorial absente ou ambiguë ; vérifier l’environnement.");
        if (c.CompanyId != null && c.CompanyId != companies[0]) throw new AppError("company_mismatch", "Cette base est liée à une autre entreprise Factorial. Utiliser une base distincte.", 409);
        c.CompanyId = companies[0]; c.Status = "Connectée";
    }
    public async Task<SynchronizationRun> Run(string kind, SyncRequest? period, CancellationToken ct)
    {
        if (period != null) TimeRules.ValidatePeriod(period.Start, period.End);
        await using var gate = new ConnectionLock(db); await gate.Acquire(ct);
        var c = await db.Connections.SingleAsync(ct);
        var run = new SynchronizationRun { Kind = kind }; db.Runs.Add(run);
        c.LastAttempt = DateTimeOffset.UtcNow;
        // A previous run with no lock owner was interrupted; never leave it falsely running.
        foreach (var old in await db.Runs.Where(x => x.Status == "Running").ToListAsync(ct)) { old.Status = "Interrupted"; old.FinishedAt = DateTimeOffset.UtcNow; }
        await db.SaveChangesAsync(ct);
        try
        {
            await Verify(c, ct);
            if (kind == "Employees")
            {
                var incoming = await client.Employees(c, ct);
                if (incoming.Any(x => x.CompanyId != c.CompanyId)) throw new AppError("company_mismatch", "Réponse d’une autre entreprise refusée.");
                var existing = await db.Employees.ToDictionaryAsync(x => x.Id, ct);
                foreach (var row in existing.Values) row.Missing = true;
                foreach (var e in incoming)
                {
                    if (!existing.TryGetValue(e.Id, out var row)) { row = new FactorialEmployee { Id = e.Id }; db.Employees.Add(row); existing[e.Id] = row; }
                    row.Name = e.FullName ?? $"Employé {e.Id}"; row.CompanyIdentifier = e.CompanyIdentifier; row.Email = e.Email; row.Missing = false;
                }
                var locals = await db.LocalEmployees.ToListAsync(ct);
                var mappings = await db.Mappings.ToListAsync(ct);
                foreach (var local in locals.Where(x => !mappings.Any(m => m.LocalEmployeeId == x.Id)))
                {
                    var matches = incoming.Where(e => !string.IsNullOrWhiteSpace(e.CompanyIdentifier) && e.CompanyIdentifier == local.PersonnelNumber).ToArray();
                    if (matches.Length != 1 || mappings.Any(m => m.FactorialEmployeeId == matches[0].Id)) continue;
                    var mapping = new EmployeeFactorialMapping { LocalEmployeeId = local.Id, FactorialEmployeeId = matches[0].Id, Method = "PersonnelNumber" };
                    db.Mappings.Add(mapping); mappings.Add(mapping);
                }
                run.Count = incoming.Count;
            }
            else if (kind == "Attendance" && period != null)
            {
                var incoming = await client.Shifts(c, period.Start, period.End, period.EmployeeIds, ct);
                if (incoming.Any(x => x.CompanyId != c.CompanyId)) throw new AppError("company_mismatch", "Réponse d’une autre entreprise refusée.");
                var existing = await db.Attendance.Where(x => x.FactorialId != null).ToDictionaryAsync(x => x.FactorialId!, ct);
                foreach (var s in incoming)
                {
                    if (!existing.TryGetValue(s.Id, out var record)) { record = new AttendanceRecord(); db.Attendance.Add(record); existing[s.Id] = record; }
                    if (record.Status is "LocalChanged" or "Conflict")
                    {
                        if (record.Fingerprint != TimeRules.Fingerprint(s)) { record.Status = "Conflict"; record.RemoteSnapshot = System.Text.Json.JsonSerializer.Serialize(s, FactorialJson.Options); }
                    }
                    else TimeRules.Apply(record, s, c.TimeZone);
                }
                var seen = incoming.Select(x => x.Id).ToHashSet();
                foreach (var record in existing.Values.Where(x => !seen.Contains(x.FactorialId!) && string.CompareOrdinal(x.ReferenceDate, period.Start) >= 0 && string.CompareOrdinal(x.ReferenceDate, period.End) <= 0 && (period.EmployeeIds.Length == 0 || period.EmployeeIds.Contains(x.EmployeeId))))
                    record.Status = record.Status is "LocalChanged" or "Conflict" ? "Conflict" : "MissingRemote";
                run.Count = incoming.Count;
            }
            if (kind != "Connection") c.LastSync = DateTimeOffset.UtcNow;
            run.Status = "Succeeded";
            await db.SaveChangesAsync(ct);
        }
        catch (Exception e)
        {
            // Drop partially staged data after failure; retain only the sanitized failure journal.
            db.ChangeTracker.Clear();
            run = await db.Runs.SingleAsync(x => x.Id == run.Id, CancellationToken.None);
            c = await db.Connections.SingleAsync(CancellationToken.None);
            var error = e as AppError;
            run.Status = "Failed"; c.Status = "Erreur de synchronisation";
            db.Errors.Add(new SynchronizationError { RunId = run.Id, Code = error?.Code ?? "internal", Message = error?.Message ?? "Synchronisation interrompue ; consulter la configuration serveur." });
        }
        run.FinishedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(CancellationToken.None);
        return run;
    }
}
public sealed class SyncWorker(IServiceScopeFactory scopes) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                using var scope = scopes.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDb>();
                var c = await db.Connections.SingleAsync(stoppingToken);
                if (c.SyncMinutes == 0 || c.LastAttempt > DateTimeOffset.UtcNow.AddMinutes(-c.SyncMinutes)) continue;
                var sync = scope.ServiceProvider.GetRequiredService<SyncService>();
                await sync.Run("Employees", null, stoppingToken);
                var today = TimeRules.Today(c.TimeZone);
                await sync.Run("Attendance", new(today.AddDays(-c.LookbackDays).ToString("yyyy-MM-dd"), today.ToString("yyyy-MM-dd"), []), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch { /* Errors from Run are persisted, transient lock/database failures retry next tick. */ }
        }
    }
}
