using System.Net;
using System.Text.Json;
namespace Pointage;
// Development-only, isolated from the network and production credentials.
public sealed class DemoHandler : HttpMessageHandler
{
    private readonly List<ShiftDto> shifts = [];
    private int nextId = 100;
    public DemoHandler()
    {
        var today = TimeRules.Today("Africa/Casablanca");
        for (var i = 0; i < 7; i++)
        {
            var date = today.AddDays(-i - 1).ToString("yyyy-MM-dd");
            shifts.Add(new((nextId++).ToString(), "1", "demo-company", date, date, "08:30", "12:30", "08:30:00", true, 240, date + "T12:30:00Z"));
            shifts.Add(new((nextId++).ToString(), "1", "demo-company", date, date, "12:30", "13:15", null, false, 45, date + "T13:15:00Z"));
            shifts.Add(new((nextId++).ToString(), "1", "demo-company", date, date, "13:15", "17:30", null, true, 255, date + "T17:30:00Z"));
            shifts.Add(new((nextId++).ToString(), "2", "demo-company", date, date, "22:00", "06:00", null, true, 480, date + "T22:00:00Z"));
        }
        shifts.Add(new((nextId++).ToString(), "3", "demo-company", today.ToString("yyyy-MM-dd"), today.ToString("yyyy-MM-dd"), "09:00", null, null, true, null, DateTimeOffset.UtcNow.ToString("O")));
    }
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        lock (shifts) return Task.FromResult(Handle(request));
    }
    private HttpResponseMessage Handle(HttpRequestMessage request)
    {
        var path = request.RequestUri!.AbsolutePath;
        var query = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(request.RequestUri.Query);
        HttpResponseMessage Json(object data, HttpStatusCode status = HttpStatusCode.OK) => new(status) { Content = JsonContent.Create(data, options: FactorialJson.Options) };
        object Page<T>(IEnumerable<T> source)
        {
            var all = source.ToArray(); int offset = query.TryGetValue("after_id", out var cursor) && int.TryParse(cursor, out var parsed) ? parsed : 0;
            var data = all.Skip(offset).Take(10).ToList(); var more = offset + data.Count < all.Length;
            return new ApiPage<T>(data, new(more, more ? (offset + data.Count).ToString() : null));
        }
        if (path.EndsWith("credentials")) return Json(Page(new[] { new CredentialDto("demo-company") }));
        if (path.EndsWith("employees/employees")) return Json(Page(new[] {
            new EmployeeDto("1", "demo-company", "Salma Bennani", "MA-001"), new EmployeeDto("2", "demo-company", "Youssef El Amrani", "MA-002"),
            new EmployeeDto("3", "demo-company", "Lina Alaoui", "MA-003"), new EmployeeDto("4", "demo-company", "Adam Tazi", null) }));
        if (request.Method == HttpMethod.Get)
        {
            IEnumerable<ShiftDto> selected = shifts;
            if (query.TryGetValue("start_on", out var start)) selected = selected.Where(s => string.CompareOrdinal(s.ReferenceDate, start.ToString()) >= 0);
            if (query.TryGetValue("end_on", out var end)) selected = selected.Where(s => string.CompareOrdinal(s.ReferenceDate, end.ToString()) <= 0);
            if (query.TryGetValue("employee_ids[]", out var ids)) selected = selected.Where(s => ids.Contains(s.EmployeeId));
            return Json(Page(selected));
        }
        using var doc = JsonDocument.Parse(request.Content!.ReadAsStringAsync().GetAwaiter().GetResult());
        var p = doc.RootElement; var employee = p.GetProperty("employee_id").GetString()!;
        var action = path.Split('/').Last(); var now = p.GetProperty(action == "shifts" ? "clock_in" : "now").GetString()!;
        ShiftDto Make(bool workable, string? end) => new((nextId++).ToString(), employee, "demo-company", now[..10], now[..10], now, end, null, workable,
            end == null ? null : (int)(DateTimeOffset.Parse(end) - DateTimeOffset.Parse(now)).TotalMinutes, DateTimeOffset.UtcNow.ToString("O"));
        if (action is "shifts" or "clock_in") { var row = Make(true, action == "shifts" ? p.GetProperty("clock_out").GetString() : null); shifts.Add(row); return Json(row); }
        var open = shifts.LastOrDefault(s => s.EmployeeId == employee && s.ClockOut == null);
        if (open == null) return Json(new { }, HttpStatusCode.UnprocessableEntity);
        var closed = open with { ClockOut = now, UpdatedAt = DateTimeOffset.UtcNow.ToString("O") };
        shifts[shifts.IndexOf(open)] = closed;
        if (action is "break_start" or "break_end") { var row = Make(action == "break_end", null); shifts.Add(row); return Json(row); }
        return Json(closed);
    }
}
