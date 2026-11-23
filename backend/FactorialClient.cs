using System.Net;
using System.Text;
using System.Text.Json;
namespace Pointage;
public class FactorialClient(HttpClient http, ICredentialProvider credentials, IConfiguration config)
{
    public const string Version = "2026-10-01";
    private async Task<HttpResponseMessage> Send(FactorialConnection c, HttpMethod method, string path, string? json, CancellationToken ct)
    {
        var simulated = config.GetValue<bool>("Demo");
        if (!simulated && method != HttpMethod.Get)
            throw new AppError("read_only", "Le connecteur Factorial réel autorise uniquement la lecture.", 403);

        var host = c.Environment == "Production" ? "api.factorialhr.com" : "api.eu2.demo.factorial.dev";
        for (int attempt = 0; ; attempt++)
        {
            using var request = new HttpRequestMessage(method, $"https://{host}/api/{Version}/resources/{path}");
            if (!simulated) credentials.Apply(request, c);
            if (json != null) request.Content = new StringContent(json, Encoding.UTF8, "application/json");
            HttpResponseMessage response;
            try { response = await http.SendAsync(request, ct); }
            catch (HttpRequestException) { throw new AppError("network", "Factorial est injoignable. Une écriture peut avoir été reçue.", 502); }
            catch (TaskCanceledException) { throw new AppError("timeout", "Délai Factorial dépassé. Une écriture peut avoir été reçue.", 504); }
            if (response.IsSuccessStatusCode) return response;
            var status = (int)response.StatusCode;
            // Never automatically retry a write: an error response may follow a committed write.
            if (method == HttpMethod.Get && attempt < 3 && (status == 429 || status >= 500))
            {
                var retry = response.Headers.RetryAfter;
                var delay = retry?.Delta ?? (retry?.Date - DateTimeOffset.UtcNow) ?? TimeSpan.FromSeconds(Math.Pow(2, attempt + 1));
                response.Dispose();
                if (delay > TimeSpan.FromMinutes(2)) throw new AppError("rate_limit", "Limite Factorial atteinte. Réessayer plus tard.", 429);
                await Task.Delay(delay > TimeSpan.Zero ? delay : TimeSpan.Zero, ct);
                continue;
            }
            response.Dispose();
            throw new AppError($"factorial_{status}", status switch {
                401 => "Identifiant Factorial invalide ou expiré.", 403 => "Permissions Factorial insuffisantes.",
                429 => "Limite de débit Factorial atteinte.", 422 => "Pointage refusé par les règles Factorial.",
                _ => $"Factorial a retourné le statut HTTP {status}." }, status is 401 or 403 ? 502 : status);
        }
    }
    public async Task<List<T>> ReadAll<T>(FactorialConnection c, string path, CancellationToken ct)
    {
        List<T> all = []; HashSet<string> cursors = []; string? cursor = null;
        do
        {
            var query = path + (path.Contains('?') ? "&" : "?") + "limit=100" + (cursor == null ? "" : "&after_id=" + Uri.EscapeDataString(cursor));
            using var response = await Send(c, HttpMethod.Get, query, null, ct);
            var page = await response.Content.ReadFromJsonAsync<ApiPage<T>>(FactorialJson.Options, ct);
            if (page?.Data == null || page.Meta == null) throw new AppError("invalid_response", "Réponse paginée Factorial invalide.", 502);
            all.AddRange(page.Data);
            if (!page.Meta.HasNextPage) return all;
            cursor = page.Meta.EndCursor;
            if (string.IsNullOrWhiteSpace(cursor) || !cursors.Add(cursor) || cursors.Count > 10000)
                throw new AppError("invalid_cursor", "Pagination Factorial incohérente.", 502);
        } while (true);
    }
    public Task<List<EmployeeDto>> Employees(FactorialConnection c, CancellationToken ct) => ReadAll<EmployeeDto>(c, "employees/employees?only_active=false&only_managers=false", ct);
    public Task<List<ShiftDto>> Shifts(FactorialConnection c, string? start, string? end, string[] ids, CancellationToken ct)
    {
        var path = "attendance/shifts?half_day=false&sort_created_at_asc=false&breaks_with_time_configuration=true";
        if (start != null) path += "&start_on=" + Uri.EscapeDataString(start);
        if (end != null) path += "&end_on=" + Uri.EscapeDataString(end);
        foreach (var id in ids) path += "&employee_ids%5B%5D=" + Uri.EscapeDataString(id);
        return ReadAll<ShiftDto>(c, path, ct);
    }
    public async Task<ShiftDto> Write(FactorialConnection c, string action, string payload, CancellationToken ct)
    {
        using var response = await Send(c, HttpMethod.Post, "attendance/shifts" + (action == "completed" ? "" : "/" + action), payload, ct);
        return await response.Content.ReadFromJsonAsync<ShiftDto>(FactorialJson.Options, ct) ?? throw new AppError("invalid_response", "Réponse d’écriture Factorial absente.", 502);
    }
}
