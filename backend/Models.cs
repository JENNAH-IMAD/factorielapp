using Microsoft.EntityFrameworkCore;
namespace Pointage;

public class FactorialConnection
{
    public int Id { get; set; } = 1;
    public string Environment { get; set; } = "Demo";
    public string AuthMode { get; set; } = "ApiKey";
    public string? CompanyId { get; set; }
    public bool AllowWrites { get; set; }
    public int SyncMinutes { get; set; } = 0;
    public int LookbackDays { get; set; } = 30;
    public string TimeZone { get; set; } = "Africa/Casablanca";
    public string Status { get; set; } = "Non testée";
    public DateTimeOffset? LastSync { get; set; }
    public DateTimeOffset? LastAttempt { get; set; }
}
public class LocalEmployee
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public string PersonnelNumber { get; set; } = "";
}
public class FactorialEmployee
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string? CompanyIdentifier { get; set; }
    public string? Email { get; set; }
    public bool Missing { get; set; }
}
public class EmployeeFactorialMapping
{
    public Guid LocalEmployeeId { get; set; }
    public string FactorialEmployeeId { get; set; } = "";
    public string Method { get; set; } = "Manual";
}
public class AttendanceRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string? FactorialId { get; set; }
    public string EmployeeId { get; set; } = "";
    public string Date { get; set; } = "";
    public string ReferenceDate { get; set; } = "";
    public string? RawClockIn { get; set; }
    public string? RawClockOut { get; set; }
    public DateTimeOffset? ClockIn { get; set; }
    public DateTimeOffset? ClockOut { get; set; }
    public string TimeInterpretation { get; set; } = "";
    public bool? Workable { get; set; }
    public int? FactorialMinutes { get; set; }
    public double? CalculatedMinutes { get; set; }
    public string Origin { get; set; } = "Factorial";
    public string Status { get; set; } = "Synced";
    public string Fingerprint { get; set; } = "";
    public string? RemoteSnapshot { get; set; }
    public string? RemoteUpdatedAt { get; set; }
}
public class SynchronizationRun
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Kind { get; set; } = "";
    public DateTimeOffset StartedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? FinishedAt { get; set; }
    public string Status { get; set; } = "Running";
    public int Count { get; set; }
}
public class SynchronizationError
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid RunId { get; set; }
    public string Code { get; set; } = "";
    public string Message { get; set; } = "";
}
public class OutboundOperation
{
    public Guid Id { get; set; }
    public string EmployeeId { get; set; } = "";
    public string Action { get; set; } = "";
    public string Payload { get; set; } = "";
    public string Status { get; set; } = "Pending";
    public string? FactorialId { get; set; }
    public string? ErrorCode { get; set; }
    public Guid? AttendanceId { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
public class AppDb(DbContextOptions<AppDb> options) : DbContext(options)
{
    public DbSet<FactorialConnection> Connections => Set<FactorialConnection>();
    public DbSet<LocalEmployee> LocalEmployees => Set<LocalEmployee>();
    public DbSet<FactorialEmployee> Employees => Set<FactorialEmployee>();
    public DbSet<EmployeeFactorialMapping> Mappings => Set<EmployeeFactorialMapping>();
    public DbSet<AttendanceRecord> Attendance => Set<AttendanceRecord>();
    public DbSet<SynchronizationRun> Runs => Set<SynchronizationRun>();
    public DbSet<SynchronizationError> Errors => Set<SynchronizationError>();
    public DbSet<OutboundOperation> Outbox => Set<OutboundOperation>();
    public DbSet<SqlAttendance> SqlAttendance => Set<SqlAttendance>();
    protected override void OnModelCreating(ModelBuilder m)
    {
        m.Entity<FactorialConnection>().Property(x => x.Id).ValueGeneratedNever();
        m.Entity<LocalEmployee>().HasIndex(x => x.PersonnelNumber).IsUnique();
        m.Entity<EmployeeFactorialMapping>().HasKey(x => x.LocalEmployeeId);
        m.Entity<EmployeeFactorialMapping>().HasIndex(x => x.FactorialEmployeeId).IsUnique();
        m.Entity<EmployeeFactorialMapping>().HasOne<LocalEmployee>().WithMany().HasForeignKey(x => x.LocalEmployeeId).OnDelete(DeleteBehavior.Restrict);
        m.Entity<EmployeeFactorialMapping>().HasOne<FactorialEmployee>().WithMany().HasForeignKey(x => x.FactorialEmployeeId).OnDelete(DeleteBehavior.Restrict);
        m.Entity<AttendanceRecord>().HasIndex(x => x.FactorialId).IsUnique();
        m.Entity<AttendanceRecord>().HasIndex(x => new { x.EmployeeId, x.ReferenceDate });
        m.Entity<AttendanceRecord>().HasIndex(x => x.Status);
        m.Entity<SynchronizationRun>().HasIndex(x => x.StartedAt);
        m.Entity<SynchronizationError>().HasOne<SynchronizationRun>().WithMany().HasForeignKey(x => x.RunId);
        m.Entity<OutboundOperation>().HasIndex(x => new { x.EmployeeId, x.Status });
        m.Entity<JournalEntry>().HasNoKey().ToView("vw_Journal_Pointages", "dbo");
        m.Entity<SqlAttendance>(s => {
            s.ToTable("Pointage_factorial", "dbo", table => table.HasCheckConstraint("CK_Pointage_factorial_Chronology", "[Check_out] IS NULL OR [Check_out] >= [Check_in]"));
            s.HasKey(x => x.Id);
            s.Property(x => x.Id).UseIdentityColumn();
            s.Property(x => x.Identification).HasColumnType("varchar(10)");
            s.Property(x => x.CheckIn).HasColumnName("Check_in").HasColumnType("datetime");
            s.Property(x => x.CheckOut).HasColumnName("Check_out").HasColumnType("datetime");
            s.HasIndex(x => new { x.CheckIn, x.Id });
        });
    }
}
