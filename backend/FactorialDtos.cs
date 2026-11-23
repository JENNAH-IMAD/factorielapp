using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.DataProtection;
namespace Pointage;
public record ApiMeta(bool HasNextPage, string? EndCursor);
public record ApiPage<T>(List<T> Data, ApiMeta Meta);
public record CredentialDto(string CompanyId);
public record EmployeeDto(string Id, string CompanyId, string? FullName, string? CompanyIdentifier, string? Email = null);
public record ShiftDto(string Id, string EmployeeId, string CompanyId, string Date, string ReferenceDate,
    string? ClockIn, string? ClockOut, string? ClockInWithSeconds, bool? Workable, int? Minutes, string? UpdatedAt);
public static class FactorialJson
{
    public static readonly JsonSerializerOptions Options = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };
}
public sealed class AppError(string code, string message, int status = 400) : Exception(message)
{
    public string Code { get; } = code;
    public int Status { get; } = status;
}
public interface ICredentialProvider { void Apply(HttpRequestMessage request, FactorialConnection connection); }
public sealed class ServerCredentialProvider(IConfiguration config, IDataProtectionProvider? protection = null) : ICredentialProvider
{
    public string? Resolve(FactorialConnection connection)
    {
        var configured = config[connection.AuthMode == "ApiKey" ? "Factorial:ApiKey" : "Factorial:CompanyToken"];
        if (!string.IsNullOrWhiteSpace(configured)) return configured;
        return connection.AuthMode == "ApiKey" && connection.Environment == "Production" && !config.GetValue<bool>("Demo")
            ? LocalFactorialKey.Read(protection) : null;
    }

    public void Apply(HttpRequestMessage request, FactorialConnection connection)
    {
        var secret = Resolve(connection);
        if (string.IsNullOrWhiteSpace(secret)) throw new AppError("credential_missing", "Identifiant Factorial absent de la configuration serveur.");
        if (connection.AuthMode == "ApiKey") request.Headers.Add("x-api-key", secret);
        else request.Headers.Authorization = new("Bearer", secret);
    }
}
