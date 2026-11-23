using Microsoft.EntityFrameworkCore;

namespace Pointage;

public record LocalClockInput(string EmployeeId, string Action);
public record ManualPointageInput(string EmployeeId, string CheckIn, string CheckOut);

public sealed class LocalClock(AppDb db)
{
    public async Task<AttendanceRecord> RecordManual(ManualPointageInput input, CancellationToken ct, Guid? entryId = null)
    {
        var start = TimeRules.RequireMoroccanLocalTime(input.CheckIn);
        var end = TimeRules.RequireMoroccanLocalTime(input.CheckOut);
        if (end <= start || end - start > TimeSpan.FromDays(2))
            throw new AppError("clock_time", "La sortie doit suivre l’entrée et le pointage doit durer au maximum 48 heures.");

        await using var gate = new ConnectionLock(db);
        await gate.Acquire(ct);
        if (!await db.Employees.AnyAsync(row => row.Id == input.EmployeeId && !row.Missing, ct))
            throw new AppError("employee", "Collaborateur introuvable dans l’annuaire importé.", 404);

        var entry = entryId.HasValue ? await FindEditable(entryId.Value, ct) : null;
        var local = db.Attendance.Where(row => row.EmployeeId == input.EmployeeId && row.Origin == "Local" && row.Status != "DeletedLocal");
        if (entryId.HasValue) local = local.Where(row => row.Id != entryId.Value);
        else
        {
            var existing = await local.FirstOrDefaultAsync(row => row.Workable == true && row.ClockIn == start && row.ClockOut == end, ct);
            if (existing != null) return existing;
        }
        if (await local.AnyAsync(row => row.ClockIn < end && (row.ClockOut == null || row.ClockOut > start), ct))
            throw new AppError("clock_overlap", "Cette plage chevauche un pointage local existant ou en cours.", 409);

        var date = TimeRules.BusinessDate(start, "Africa/Casablanca");
        if (entry == null) { entry = new AttendanceRecord(); db.Attendance.Add(entry); }
        entry.EmployeeId = input.EmployeeId; entry.Origin = "Local"; entry.Status = "LocalOnly"; entry.Workable = true;
        entry.Date = date; entry.ReferenceDate = date; entry.ClockIn = start; entry.ClockOut = end;
        entry.RawClockIn = start.ToString("O"); entry.RawClockOut = end.ToString("O");
        entry.CalculatedMinutes = (end - start).TotalMinutes;
        entry.TimeInterpretation = "Saisie manuelle en heure du Maroc";
        await db.SaveChangesAsync(ct);
        return entry;
    }

    public static IQueryable<AttendanceRecord> Editable(AppDb db) => db.Attendance.Where(row => row.Origin == "Local"
        && row.FactorialId == null && row.Workable == true && (row.Status == "LocalOnly" || row.Status == "Pending"));

    private async Task<AttendanceRecord> FindEditable(Guid id, CancellationToken ct) =>
        await Editable(db).SingleOrDefaultAsync(row => row.Id == id, ct)
        ?? throw new AppError("pointage", "Pointage local introuvable ou non modifiable.", 404);

    public async Task DeleteManual(Guid id, CancellationToken ct)
    {
        await using var gate = new ConnectionLock(db);
        await gate.Acquire(ct);
        var entry = await FindEditable(id, ct);
        entry.Status = "DeletedLocal";
        await db.SaveChangesAsync(ct);
    }

    public async Task Record(LocalClockInput input, DateTimeOffset now, CancellationToken ct)
    {
        await using var gate = new ConnectionLock(db);
        await gate.Acquire(ct);
        if (!await db.Employees.AnyAsync(row => row.Id == input.EmployeeId && !row.Missing, ct))
            throw new AppError("employee", "Collaborateur introuvable dans l’annuaire importé.", 404);

        var current = await db.Attendance.SingleOrDefaultAsync(row => row.EmployeeId == input.EmployeeId
            && row.Origin == "Local" && row.Status == "LocalOnly" && row.ClockOut == null, ct);
        var next = input.Action switch
        {
            "arrival" when current == null => true,
            "departure" when current?.Workable == true => (bool?)null,
            "pause" when current?.Workable == true => false,
            "resume" when current?.Workable == false => true,
            _ => throw new AppError("clock_state", "Action incompatible avec le pointage local en cours.", 409)
        };
        now = now.ToUniversalTime();
        if (current != null)
        {
            if (current.ClockIn == null || now < current.ClockIn)
                throw new AppError("clock_time", "L’heure de fin doit suivre l’heure de début.", 409);
            current.ClockOut = now;
            current.RawClockOut = now.ToString("O");
            current.CalculatedMinutes = (now - current.ClockIn.Value).TotalMinutes;
        }
        if (next.HasValue)
        {
            var connection = await db.Connections.SingleAsync(ct);
            var date = TimeRules.BusinessDate(now, connection.TimeZone);
            db.Attendance.Add(new AttendanceRecord
            {
                EmployeeId = input.EmployeeId, Origin = "Local", Status = "LocalOnly", Workable = next.Value,
                ClockIn = now, RawClockIn = now.ToString("O"), Date = date, ReferenceDate = current?.ReferenceDate ?? date,
                TimeInterpretation = "Horodatage enregistré par l’application"
            });
        }
        await db.SaveChangesAsync(ct);
    }
}
