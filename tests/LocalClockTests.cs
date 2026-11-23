using Microsoft.EntityFrameworkCore;
using Pointage;
using Xunit;

namespace Pointage.Tests;

public class LocalClockTests
{
    [Fact]
    public async Task ManualPointageCanBeAddedCorrectedAndRemovedWithoutAffectingFactorialRows()
    {
        var options = new DbContextOptionsBuilder<AppDb>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using var db = new AppDb(options);
        db.Employees.Add(new() { Id = "101", Name = "Collaborateur test" });
        var imported = new AttendanceRecord { FactorialId = "remote", EmployeeId = "101", Origin = "Factorial", Workable = true };
        db.Attendance.Add(imported);
        await db.SaveChangesAsync();
        var clock = new LocalClock(db);
        var input = new ManualPointageInput("101", "2026-01-15T22:30", "2026-01-16T06:15");
        var record = await clock.RecordManual(input, default);
        Assert.Equal("2026-01-15", record.ReferenceDate);
        Assert.Equal(465, record.CalculatedMinutes);
        Assert.Equal(record.Id, (await clock.RecordManual(input, default)).Id);
        await Assert.ThrowsAsync<AppError>(() => clock.RecordManual(input with { CheckOut = "2026-01-16T07:00" }, default));
        await clock.RecordManual(input with { CheckOut = "2026-01-16T07:00" }, default, record.Id);
        Assert.Equal(510, record.CalculatedMinutes);
        await Assert.ThrowsAsync<AppError>(() => clock.DeleteManual(imported.Id, default));
        await clock.DeleteManual(record.Id, default);
        Assert.Equal("DeletedLocal", record.Status);
        using var json = System.Text.Json.JsonDocument.Parse(System.Text.Json.JsonSerializer.Serialize(
            await AttendanceJournal.Read(db, null, null, "101", 1, default, allDates: true)));
        Assert.Single(json.RootElement.GetProperty("data").EnumerateArray());
        Assert.Empty(await db.Outbox.ToListAsync());
    }

    [Fact]
    public async Task RecordsLocalWorkAndPauseAcrossMidnightAndRejectsRepeatedArrival()
    {
        var options = new DbContextOptionsBuilder<AppDb>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using var db = new AppDb(options);
        db.Connections.Add(new());
        db.Employees.Add(new() { Id = "101", Name = "Collaborateur test" });
        await db.SaveChangesAsync();
        var clock = new LocalClock(db);
        var start = new DateTimeOffset(2026, 1, 15, 19, 0, 0, TimeSpan.Zero);
        Task Record(string action, int minutes) => clock.Record(new("101", action), start.AddMinutes(minutes), default);

        await Record("arrival", 0);
        Assert.Equal("clock_state", (await Assert.ThrowsAsync<AppError>(() => Record("arrival", 1))).Code);
        await Record("pause", 90);
        await Record("resume", 120);
        await Record("departure", 360);

        var rows = await db.Attendance.OrderBy(row => row.ClockIn).ToListAsync();
        Assert.Equal(3, rows.Count);
        Assert.All(rows, row => { Assert.Null(row.FactorialId); Assert.Equal("LocalOnly", row.Status); Assert.Equal("2026-01-15", row.ReferenceDate); });
        Assert.Equal("2026-01-16", TimeRules.BusinessDate(rows[^1].ClockOut!.Value, "Africa/Casablanca"));
        Assert.Equal(330, rows.Where(row => row.Workable == true).Sum(row => row.CalculatedMinutes));
        Assert.Equal(30, rows.Single(row => row.Workable == false).CalculatedMinutes);
        Assert.All(rows, row => Assert.NotNull(row.ClockOut));
        Assert.Empty(await db.Outbox.ToListAsync());
    }
}
