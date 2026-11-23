using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Pointage;
using Xunit;
[assembly: CollectionBehavior(DisableTestParallelization = true)]
namespace Pointage.Tests;
public class IntegrationTests
{
    [Fact] public async Task AdminSessionAndCsrfProtectionGuardAllBusinessEndpoints()
    {
        var previousDemo=Environment.GetEnvironmentVariable("Demo");
        Environment.SetEnvironmentVariable("Demo","true");
        try {
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b => b.UseEnvironment("Development").ConfigureAppConfiguration((_,c)=>c.AddInMemoryCollection(new Dictionary<string,string?>{["Demo"]="true"})));
        using var client = factory.CreateClient(new() { AllowAutoRedirect=false });
        Assert.Equal(HttpStatusCode.Unauthorized,(await client.GetAsync("/api/connection")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,(await client.PostAsJsonAsync("/api/login",new {username="",password=""})).StatusCode);
        client.DefaultRequestHeaders.Add("X-Pointage-Request","1");
        Assert.Equal(HttpStatusCode.OK,(await client.PostAsJsonAsync("/api/login",new {username="",password=""})).StatusCode);
        var connection=await client.GetAsync("/api/connection"); Assert.Equal(HttpStatusCode.OK,connection.StatusCode);
        Assert.DoesNotContain("CompanyToken\":",await connection.Content.ReadAsStringAsync());
        var today=TimeRules.Today("Africa/Casablanca");
        var attendance=await client.GetAsync($"/api/attendance?start={today.AddDays(-30):yyyy-MM-dd}&end={today:yyyy-MM-dd}&page=1");
        Assert.Equal(HttpStatusCode.OK,attendance.StatusCode);
        using var page=System.Text.Json.JsonDocument.Parse(await attendance.Content.ReadAsStringAsync());
        Assert.True(page.RootElement.GetProperty("total").GetInt32()>25);
        var rejected=await client.PostAsJsonAsync("/api/outbound",new SendRequest(Guid.NewGuid(),"1","clock_in","2026-11-19T08:00:00Z",null,null,null));
        Assert.Equal(HttpStatusCode.Forbidden,rejected.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,(await client.GetAsync("/api/attendance?start=bad&end=2026-11-19")).StatusCode);
        } finally { Environment.SetEnvironmentVariable("Demo",previousDemo); }
    }
    [SqlServerFact] public async Task SqlServerMigrationConstraintsAndCrossSessionLock()
    {
        var connection=Environment.GetEnvironmentVariable("POINTAGE_TEST_DATABASE")!;
        var options=new DbContextOptionsBuilder<AppDb>().UseSqlServer(connection).Options;
        await using var db=new AppDb(options); await db.Database.MigrateAsync();
        await using var second=new AppDb(options);
        await using(var firstGate=new ConnectionLock(db))
        {
            await firstGate.Acquire(default);
            await using var secondGate=new ConnectionLock(second);
            Assert.Equal("busy",(await Assert.ThrowsAsync<AppError>(()=>secondGate.Acquire(default))).Code);
        }
        await using(var next=new ConnectionLock(second)) await next.Acquire(default);
        var id=Guid.NewGuid().ToString(); db.Attendance.Add(new(){FactorialId=id,EmployeeId="e",ReferenceDate="2026-11-19",Date="2026-11-19"}); await db.SaveChangesAsync();
        second.Attendance.Add(new(){FactorialId=id,EmployeeId="e",ReferenceDate="2026-11-19",Date="2026-11-19"});
        await Assert.ThrowsAsync<DbUpdateException>(()=>second.SaveChangesAsync());
        var rows=await db.Attendance.Where(x=>x.ReferenceDate.CompareTo("2026-11-01")>=0 && x.ReferenceDate.CompareTo("2026-11-30")<=0).ToListAsync(); Assert.NotEmpty(rows);
        await using var tx = await db.Database.BeginTransactionAsync();
        var number = Guid.NewGuid().ToString("N")[..10];
        var imported = new FactorialEmployee { Id = Guid.NewGuid().ToString(), CompanyIdentifier = number, Name = "Test SQL", Email = "test@example.invalid" };
        db.Employees.Add(imported); await db.SaveChangesAsync();
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO dbo.Pointage_factorial (Identification, Check_in, Check_out) VALUES ({number}, CONVERT(datetime, '2026-11-16T08:30:00', 126), CONVERT(datetime, '2026-11-16T18:00:00', 126))");
        var journal = await AttendanceJournal.Read(db, "2026-11-16", "2026-11-16", imported.Id, 1, default);
        using var result = System.Text.Json.JsonDocument.Parse(System.Text.Json.JsonSerializer.Serialize(journal));
        var line = Assert.Single(result.RootElement.GetProperty("data").EnumerateArray());
        Assert.Equal(number, line.GetProperty("identification").GetString());
        Assert.Equal("Test SQL", line.GetProperty("name").GetString());
        Assert.Equal("2026-11-16T08:30:00", line.GetProperty("RawClockIn").GetString());
        Assert.Equal("2026-11-16T18:00:00", line.GetProperty("RawClockOut").GetString());
        Assert.Equal(570, line.GetProperty("durationMinutes").GetDouble());
        db.Attendance.Add(new() { Origin = "Local", Status = "DeletedLocal", Workable = true, EmployeeId = imported.Id,
            Date = "2026-11-18", ReferenceDate = "2026-11-18" });
        await db.SaveChangesAsync();
        var entireJournal = await AttendanceJournal.Read(db, null, null, imported.Id, 1, default, allDates: true);
        using var entireResult = System.Text.Json.JsonDocument.Parse(System.Text.Json.JsonSerializer.Serialize(entireJournal));
        Assert.Single(entireResult.RootElement.GetProperty("data").EnumerateArray());
        await tx.RollbackAsync();
    }
}
public sealed class SqlServerFactAttribute:FactAttribute
{
    public SqlServerFactAttribute() { if(string.IsNullOrEmpty(Environment.GetEnvironmentVariable("POINTAGE_TEST_DATABASE"))) Skip="Définir POINTAGE_TEST_DATABASE vers une base SQL Server de test isolée."; }
}
