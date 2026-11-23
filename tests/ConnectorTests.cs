using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Pointage;
using Xunit;
namespace Pointage.Tests;
public class ConnectorTests
{
    [Fact] public async Task SimulatorMatchesDocumentedResponseShape()
    {
        var client = new FactorialClient(new HttpClient(new DemoHandler()), new ServerCredentialProvider(Config), Config);
        var credentials = await client.ReadAll<CredentialDto>(new(), "api_public/credentials", default);
        Assert.Equal("demo-company", Assert.Single(credentials).CompanyId);
        Assert.Equal(4, (await client.Employees(new(), default)).Count);
    }
    static IConfiguration Config => new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?> { ["Demo"] = "true" }).Build();
    static FactorialConnection Connection => new() { CompanyId = "c" };
    static ShiftDto Shift(string? start = "22:00", string? end = "06:00", string date = "2026-11-18") => new("s", "e", "c", date, date, start, end, null, true, null, "2026-11-19T10:00:00Z");
    static AppDb Database() => new(new DbContextOptionsBuilder<AppDb>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    static FactorialClient Client(Func<HttpRequestMessage, HttpResponseMessage> respond) => new(new HttpClient(new Handler(respond)), new ServerCredentialProvider(Config), Config);
    static HttpResponseMessage Json(string body, HttpStatusCode status = HttpStatusCode.OK) => new(status) { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json") };
    [Theory]
    [InlineData("Production")]
    [InlineData("Demo")]
    public async Task RealConnectionRejectsWritesBeforeCallingTransport(string environment)
    {
        var config = new ConfigurationBuilder().Build();
        var calls = 0;
        using var http = new HttpClient(new Handler(_ => { calls++; return Json("{}"); }));
        var client = new FactorialClient(http, new ServerCredentialProvider(config), config);
        var connection = new FactorialConnection { Environment = environment, AllowWrites = true };
        var error = await Assert.ThrowsAsync<AppError>(() => client.Write(connection, "completed", "{}", default));
        Assert.Equal(403, error.Status);
        Assert.Equal(0, calls);
    }
    [Fact] public async Task PaginationUsesOpaqueCursorAndReadsEveryPage()
    {
        var requests = new List<string>();
        var client = Client(r => { requests.Add(r.RequestUri!.Query); return Json(requests.Count == 1 ? "{\"data\":[{\"company_id\":\"a\"}],\"meta\":{\"has_next_page\":true,\"end_cursor\":\"a+/=\"}}" : "{\"data\":[{\"company_id\":\"b\"}],\"meta\":{\"has_next_page\":false}}"); });
        var rows = await client.ReadAll<CredentialDto>(Connection, "api_public/credentials", default);
        Assert.Equal(2, rows.Count); Assert.Contains("after_id=a%2B%2F%3D", requests[1]);
    }
    [Fact] public async Task RepeatedCursorFailsInsteadOfLooping()
    {
        var client = Client(_ => Json("{\"data\":[],\"meta\":{\"has_next_page\":true,\"end_cursor\":\"x\"}}"));
        Assert.Equal("invalid_cursor", (await Assert.ThrowsAsync<AppError>(() => client.ReadAll<CredentialDto>(Connection, "employees/employees", default))).Code);
    }
    [Fact] public void OpenAndOvernightRemainDistinct()
    {
        var open = TimeRules.Parse(Shift(end:null), "Africa/Casablanca"); Assert.NotNull(open.In); Assert.Null(open.Out);
        var night = TimeRules.Parse(Shift(), "Africa/Casablanca"); Assert.Equal(8, (night.Out - night.In)!.Value.TotalHours);
        Assert.Contains("lendemain", night.Interpretation);
    }
    [Fact] public void MoroccoRamadanOffsetIsNotHardcoded()
    {
        var winter = TimeRules.Parse(Shift("08:00", "09:00", "2024-01-15"), "Africa/Casablanca");
        var ramadan = TimeRules.Parse(Shift("08:00", "09:00", "2024-03-20"), "Africa/Casablanca");
        Assert.Equal(7, winter.In!.Value.Hour); Assert.Equal(8, ramadan.In!.Value.Hour);
    }
    [Fact] public void AmbiguousMoroccoClockIsPreservedWithoutInventingInstant()
    {
        var time = TimeRules.Parse(Shift("02:30", "04:00", "2024-03-10"), "Africa/Casablanca");
        Assert.Null(time.In); Assert.Contains("ambiguë", time.Interpretation);
    }
    [Fact] public async Task AuthErrorDoesNotRetryOrLeakResponse()
    {
        int calls=0; var client = Client(_ => { calls++; return Json("SECRET-IN-ERROR", HttpStatusCode.Unauthorized); });
        var error = await Assert.ThrowsAsync<AppError>(() => client.Employees(Connection, default));
        Assert.Equal(1,calls); Assert.Equal("factorial_401",error.Code); Assert.DoesNotContain("SECRET",error.Message);
    }
    [Fact] public async Task RateLimitGetRetriesButPostNeverDoes()
    {
        int calls=0; var client = Client(_ => { calls++; var r = calls == 1 ? Json("{}", HttpStatusCode.TooManyRequests) : Json("{\"data\":[],\"meta\":{\"has_next_page\":false}}"); r.Headers.RetryAfter = new(TimeSpan.Zero); return r; });
        await client.Employees(Connection, default); Assert.Equal(2,calls);
        calls=0; await Assert.ThrowsAsync<AppError>(() => client.Write(Connection,"clock_in","{}",default)); Assert.Equal(1,calls);
    }
    [Fact] public async Task SyncUpdatesCorrectionsPreservesMissingAndOnlyMapsUniqueNumber()
    {
        await using var db=Database(); db.Connections.Add(Connection);
        var local = new LocalEmployee { Name="Same name", PersonnelNumber="A" }; db.LocalEmployees.Add(local); await db.SaveChangesAsync();
        int round=0;
        var client = Client(r => {
            object data = r.RequestUri!.AbsolutePath.EndsWith("credentials") ? new[] { new CredentialDto("c") } : r.RequestUri.AbsolutePath.Contains("employees") ? new[] { new EmployeeDto("e","c","Other name","A"),new EmployeeDto("f","c","Same name","B") } : round==2 ? Array.Empty<ShiftDto>() : new[] { Shift(end:round==0?"06:00":"07:00") };
            return Json(System.Text.Json.JsonSerializer.Serialize(new { data, meta=new { has_next_page=false } }, FactorialJson.Options));
        });
        var sync = new SyncService(db,client); await sync.Run("Employees",null,default);
        Assert.Equal("e",(await db.Mappings.SingleAsync()).FactorialEmployeeId);
        var period = new SyncRequest("2026-11-18","2026-11-19",[]);
        await sync.Run("Attendance",period,default); var id=(await db.Attendance.SingleAsync()).Id;
        round=1; await sync.Run("Attendance",period,default);
        var record=await db.Attendance.SingleAsync(); Assert.Equal(id,record.Id); Assert.Equal("07:00",record.RawClockOut);
        round=2; await sync.Run("Attendance",period,default); Assert.Equal("MissingRemote",record.Status); Assert.Single(await db.Attendance.ToListAsync());
    }
    [Fact] public async Task CompanyMismatchImportsNothing()
    {
        await using var db=Database(); db.Connections.Add(Connection); await db.SaveChangesAsync();
        var client=Client(_=>Json("{\"data\":[{\"company_id\":\"another\"}],\"meta\":{\"has_next_page\":false}}"));
        var run=await new SyncService(db,client).Run("Employees",null,default);
        Assert.Equal("Failed",run.Status); Assert.Empty(await db.Employees.ToListAsync()); Assert.Equal("company_mismatch",(await db.Errors.SingleAsync()).Code);
    }
    [Fact] public async Task LostWriteResponseIsJournaledAndRepeatedOperationNeverResends()
    {
        await using var db=Database(); var connection=Connection; connection.AllowWrites=true; db.Connections.Add(connection);
        var local=new LocalEmployee { Name="Employee", PersonnelNumber="1" }; db.LocalEmployees.Add(local); db.Employees.Add(new(){Id="e"}); db.Mappings.Add(new(){LocalEmployeeId=local.Id,FactorialEmployeeId="e"}); await db.SaveChangesAsync();
        int posts=0; var client=Client(r=>{ if(r.Method==HttpMethod.Post){posts++;throw new HttpRequestException();} return Json(r.RequestUri!.AbsolutePath.EndsWith("credentials")?"{\"data\":[{\"company_id\":\"c\"}],\"meta\":{\"has_next_page\":false}}":"{\"data\":[],\"meta\":{\"has_next_page\":false}}"); });
        var service=new OutboundService(db,client,new(db,client)); var input=new SendRequest(Guid.NewGuid(),"e","clock_in","2026-11-19T08:00:00+01:00",null,null,null);
        Assert.Equal("Uncertain",(await service.Send(input,default)).Status);
        Assert.Equal("Uncertain",(await service.Send(input,default)).Status); Assert.Equal(1,posts);
        await Assert.ThrowsAsync<AppError>(()=>service.Send(input with { OperationId=Guid.NewGuid() },default)); Assert.Equal(1,posts);
    }
    [Fact] public void MinuteOnlyResponseCannotProveSecondPrecisionWrite()
    {
        Assert.Empty(OutboundService.Match([Shift("08:00","09:00")],"clock_in",DateTimeOffset.Parse("2026-11-18T08:00:37+01:00"),null,"Africa/Casablanca"));
    }
    private sealed class Handler(Func<HttpRequestMessage,HttpResponseMessage> respond):HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)=>Task.FromResult(respond(request)); }
}
