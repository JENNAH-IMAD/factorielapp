using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using NodaTime;
using NodaTime.Text;
namespace Pointage;
public static class TimeRules
{
    public static string Fingerprint(ShiftDto shift) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(shift, FactorialJson.Options))));
    public static (DateTimeOffset? In, DateTimeOffset? Out, string Interpretation) Parse(ShiftDto s, string zoneId)
    {
        var zone = DateTimeZoneProviders.Tzdb[zoneId];
        var incoming = s.ClockIn?.Contains('T') == true ? s.ClockIn : s.ClockInWithSeconds ?? s.ClockIn;
        var hasFull = incoming?.Contains('T') == true;
        DateTimeOffset? ParseOne(string? value, DateOnly date)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;
            if (value.Contains('T'))
            {
                if (!System.Text.RegularExpressions.Regex.IsMatch(value, @"(Z|[+-]\d{2}:\d{2})$")) return null;
                return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dto) ? dto.ToUniversalTime() : null;
            }
            if (!TimeOnly.TryParseExact(value, ["HH:mm", "HH:mm:ss"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var time)) return null;
            var local = DateTime.SpecifyKind(date.ToDateTime(time), DateTimeKind.Unspecified);
            var mapping = zone.MapLocal(LocalDateTime.FromDateTime(local));
            if (mapping.Count != 1) return null;
            return mapping.Single().ToDateTimeOffset().ToUniversalTime();
        }
        if (!DateOnly.TryParseExact(s.Date, "yyyy-MM-dd", out var date)) return (null, null, "Date non interprétable");
        var clockIn = ParseOne(incoming, date);
        var clockOut = ParseOne(s.ClockOut, date);
        bool overnight = false;
        if (clockIn != null && clockOut < clockIn && s.ClockOut?.Contains('T') == false)
        { clockOut = ParseOne(s.ClockOut, date.AddDays(1)); overnight = true; }
        var interpretation = hasFull ? "Horodatage avec décalage" : $"Fuseau supposé : {zoneId}";
        if (overnight) interpretation += " ; départ supposé le lendemain";
        if ((incoming != null && clockIn == null) || (s.ClockOut != null && clockOut == null)) interpretation = "Heure ambiguë, inexistante ou non interprétable : vérifier dans Factorial";
        return (clockIn, clockOut, interpretation);
    }
    public static void Apply(AttendanceRecord r, ShiftDto s, string zone)
    {
        var times = Parse(s, zone);
        r.FactorialId = s.Id; r.EmployeeId = s.EmployeeId; r.Date = s.Date; r.ReferenceDate = s.ReferenceDate;
        r.RawClockIn = s.ClockInWithSeconds ?? s.ClockIn; r.RawClockOut = s.ClockOut;
        r.ClockIn = times.In; r.ClockOut = times.Out; r.TimeInterpretation = times.Interpretation;
        r.Workable = s.Workable; r.FactorialMinutes = s.Minutes;
        r.CalculatedMinutes = times.In != null && times.Out != null ? (times.Out - times.In).Value.TotalMinutes : null;
        r.RemoteUpdatedAt = s.UpdatedAt; r.Fingerprint = Fingerprint(s); r.RemoteSnapshot = JsonSerializer.Serialize(s, FactorialJson.Options); r.Status = "Synced";
    }
    public static void ValidatePeriod(string start, string end)
    {
        if (!DateOnly.TryParseExact(start, "yyyy-MM-dd", out var a) || !DateOnly.TryParseExact(end, "yyyy-MM-dd", out var b) || a > b || b.DayNumber - a.DayNumber > 366)
            throw new AppError("period", "Choisir une période valide de 367 jours au maximum.");
    }
    public static DateOnly Today(string zone) => DateOnly.FromDateTime(SystemClock.Instance.GetCurrentInstant().InZone(DateTimeZoneProviders.Tzdb[zone]).LocalDateTime.ToDateTimeUnspecified());
    public static DateTimeOffset RequireMoroccanLocalTime(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new AppError("timestamp", "Renseignez la date et l’heure d’entrée et de sortie.");
        var parsed = LocalDateTimePattern.CreateWithInvariantCulture("uuuu-MM-dd'T'HH:mm").Parse(value);
        if (!parsed.Success) throw new AppError("timestamp", "Date ou heure de pointage invalide.");
        var mapping = DateTimeZoneProviders.Tzdb["Africa/Casablanca"].MapLocal(parsed.Value);
        if (mapping.Count != 1) throw new AppError("timestamp", "Cette heure est ambiguë ou inexistante au Maroc. Choisissez une autre heure.");
        return mapping.Single().ToDateTimeOffset().ToUniversalTime();
    }
    public static string BusinessDate(DateTimeOffset instant, string zone) => Instant.FromDateTimeOffset(instant).InZone(DateTimeZoneProviders.Tzdb[zone]).Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    public static DateTimeOffset RequireTimestamp(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || !System.Text.RegularExpressions.Regex.IsMatch(value, @"T.*(Z|[+-]\d{2}:\d{2})$") || !DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var result))
            throw new AppError("timestamp", "Un horodatage ISO 8601 avec décalage est obligatoire.");
        return result;
    }
}
