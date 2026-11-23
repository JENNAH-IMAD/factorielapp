using Microsoft.EntityFrameworkCore;
using NodaTime;
using NodaTime.Text;
namespace Pointage;

public sealed class SqlAttendance
{
    public long Id { get; set; }
    public string Identification { get; set; } = "";
    public DateTime CheckIn { get; set; }
    public DateTime? CheckOut { get; set; }
}

public sealed class JournalEntry
{
    public string Key { get; set; } = "";
    public string EmployeeId { get; set; } = "";
    public string ReferenceDate { get; set; } = "";
    public string? RawClockIn { get; set; }
    public string? RawClockOut { get; set; }
    public DateTimeOffset? ClockIn { get; set; }
    public DateTimeOffset? ClockOut { get; set; }
    public string Origin { get; set; } = "";
}

public static class AttendanceJournal
{
    private static double? DurationMinutes(JournalEntry row)
    {
        DateTimeOffset? start = row.ClockIn;
        DateTimeOffset? end = row.ClockOut;
        if (row.Origin == "Sql")
        {
            // The SQL intake stores Moroccan local civil time without an offset.
            var zone = DateTimeZoneProviders.Tzdb["Africa/Casablanca"];
            DateTimeOffset? Resolve(string? value)
            {
                if (value == null) return null;
                var time = LocalDateTimePattern.ExtendedIso.Parse(value);
                if (!time.Success) return null;
                var mapping = zone.MapLocal(time.Value);
                return mapping.Count == 1 ? mapping.Single().ToDateTimeOffset() : null;
            }
            start = Resolve(row.RawClockIn);
            end = Resolve(row.RawClockOut);
        }
        return start is { } from && end is { } to && to >= from ? (to - from).TotalMinutes : null;
    }

    public static async Task<object> Read(AppDb db, string? start, string? end, string? employee, int page, CancellationToken ct, bool allDates = false)
    {
        if (!allDates) TimeRules.ValidatePeriod(start ?? "", end ?? "");
        if (page is < 1 or > 100000) throw new AppError("page", "Page invalide.");
        var directory = await db.Employees.AsNoTracking().ToListAsync(ct);
        var byId = directory.ToDictionary(row => row.Id, StringComparer.Ordinal);
        var byIdentifier = directory.Where(row => !string.IsNullOrEmpty(row.CompanyIdentifier))
            .ToLookup(row => row.CompanyIdentifier!, StringComparer.Ordinal);
        var entries = db.Database.IsSqlServer()
            ? db.Set<JournalEntry>().AsNoTracking()
            : db.Attendance.AsNoTracking().Where(row => row.Workable != false && row.Status != "MissingRemote" && row.Status != "DeletedLocal")
                .Select(row => new JournalEntry { Key = row.Id.ToString(), EmployeeId = row.EmployeeId,
                    ReferenceDate = row.ReferenceDate, ClockIn = row.ClockIn, ClockOut = row.ClockOut,
                    RawClockIn = row.RawClockIn, RawClockOut = row.RawClockOut, Origin = row.Origin });
        if (!allDates) entries = entries.Where(row => row.ReferenceDate.CompareTo(start) >= 0 && row.ReferenceDate.CompareTo(end) <= 0);
        if (!string.IsNullOrWhiteSpace(employee))
        {
            var person = byId.GetValueOrDefault(employee);
            var identifier = person?.CompanyIdentifier;
            var matchByNumber = identifier != null && byIdentifier[identifier].Count() == 1;
            entries = entries.Where(row => (row.Origin != "Sql" && row.EmployeeId == employee)
                || (row.Origin == "Sql" && (row.EmployeeId == employee || (matchByNumber && row.EmployeeId == identifier))));
        }
        const int size = 25;
        var total = await entries.CountAsync(ct);
        var pageRows = await entries.OrderByDescending(row => row.ReferenceDate).ThenBy(row => row.EmployeeId)
            .ThenBy(row => row.RawClockIn).ThenBy(row => row.Key).Skip((page - 1) * size).Take(size).ToListAsync(ct);
        static Guid? ReadLocalId(JournalEntry row) => row.Origin == "Local"
            && Guid.TryParse(row.Key.StartsWith("app-", StringComparison.Ordinal) ? row.Key[4..] : row.Key, out var id) ? id : null;
        var candidates = pageRows.Select(ReadLocalId).OfType<Guid>().ToArray();
        var editable = candidates.Length == 0 ? new Dictionary<Guid, string>()
            : await LocalClock.Editable(db).Where(row => candidates.Contains(row.Id)).ToDictionaryAsync(row => row.Id, row => row.EmployeeId, ct);
        var rows = pageRows.Select(row =>
        {
            var employeeRow = row.Origin == "Sql"
                ? byIdentifier[row.EmployeeId].Take(2).ToArray() is [var match] ? match
                    : !byIdentifier.Contains(row.EmployeeId) ? byId.GetValueOrDefault(row.EmployeeId) : null
                : byId.GetValueOrDefault(row.EmployeeId);
            var candidate = ReadLocalId(row);
            var canEdit = candidate.HasValue && editable.ContainsKey(candidate.Value);
            return new { row.Key, localId = canEdit ? candidate : null, employeeId = canEdit ? row.EmployeeId : null,
                identification = row.Origin == "Sql" ? row.EmployeeId : employeeRow?.CompanyIdentifier ?? row.EmployeeId,
                name = employeeRow?.Name, email = employeeRow?.Email, row.ReferenceDate, row.ClockIn, row.ClockOut,
                row.RawClockIn, row.RawClockOut, row.Origin, durationMinutes = DurationMinutes(row) };
        }).ToArray();
        return new { total, page, pageSize = size, data = rows };
    }
}
