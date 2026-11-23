using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pointage.Api.Migrations
{
    /// <inheritdoc />
    public partial class InitialSqlServer : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "dbo");

            migrationBuilder.CreateTable(
                name: "Attendance",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FactorialId = table.Column<string>(type: "nvarchar(450)", nullable: true),
                    EmployeeId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Date = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ReferenceDate = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    RawClockIn = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RawClockOut = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ClockIn = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ClockOut = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    TimeInterpretation = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Workable = table.Column<bool>(type: "bit", nullable: true),
                    FactorialMinutes = table.Column<int>(type: "int", nullable: true),
                    CalculatedMinutes = table.Column<double>(type: "float", nullable: true),
                    Origin = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Fingerprint = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    RemoteSnapshot = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RemoteUpdatedAt = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Attendance", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Connections",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false),
                    Environment = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    AuthMode = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CompanyId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    AllowWrites = table.Column<bool>(type: "bit", nullable: false),
                    SyncMinutes = table.Column<int>(type: "int", nullable: false),
                    LookbackDays = table.Column<int>(type: "int", nullable: false),
                    TimeZone = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    LastSync = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    LastAttempt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Connections", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Employees",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CompanyIdentifier = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Email = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Missing = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Employees", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "LocalEmployees",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    PersonnelNumber = table.Column<string>(type: "nvarchar(450)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LocalEmployees", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Outbox",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EmployeeId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Action = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Payload = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    FactorialId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ErrorCode = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    AttendanceId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Outbox", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Pointage_factorial",
                schema: "dbo",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Identification = table.Column<string>(type: "varchar(10)", nullable: false),
                    Check_in = table.Column<DateTime>(type: "datetime", nullable: false),
                    Check_out = table.Column<DateTime>(type: "datetime", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Pointage_factorial", x => x.Id);
                    table.CheckConstraint("CK_Pointage_factorial_Chronology", "[Check_out] IS NULL OR [Check_out] >= [Check_in]");
                });

            migrationBuilder.CreateTable(
                name: "Runs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    FinishedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    Status = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Count = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Runs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Mappings",
                columns: table => new
                {
                    LocalEmployeeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FactorialEmployeeId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Method = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Mappings", x => x.LocalEmployeeId);
                    table.ForeignKey(
                        name: "FK_Mappings_Employees_FactorialEmployeeId",
                        column: x => x.FactorialEmployeeId,
                        principalTable: "Employees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Mappings_LocalEmployees_LocalEmployeeId",
                        column: x => x.LocalEmployeeId,
                        principalTable: "LocalEmployees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Errors",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RunId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Message = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Errors", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Errors_Runs_RunId",
                        column: x => x.RunId,
                        principalTable: "Runs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Attendance_EmployeeId_ReferenceDate",
                table: "Attendance",
                columns: new[] { "EmployeeId", "ReferenceDate" });

            migrationBuilder.CreateIndex(
                name: "IX_Attendance_FactorialId",
                table: "Attendance",
                column: "FactorialId",
                unique: true,
                filter: "[FactorialId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Attendance_Status",
                table: "Attendance",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_Errors_RunId",
                table: "Errors",
                column: "RunId");

            migrationBuilder.CreateIndex(
                name: "IX_LocalEmployees_PersonnelNumber",
                table: "LocalEmployees",
                column: "PersonnelNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Mappings_FactorialEmployeeId",
                table: "Mappings",
                column: "FactorialEmployeeId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Outbox_EmployeeId_Status",
                table: "Outbox",
                columns: new[] { "EmployeeId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_Pointage_factorial_Check_in_Id",
                schema: "dbo",
                table: "Pointage_factorial",
                columns: new[] { "Check_in", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_Runs_StartedAt",
                table: "Runs",
                column: "StartedAt");

            migrationBuilder.Sql("""
                EXEC(N'CREATE VIEW dbo.vw_Journal_Pointages AS
                SELECT CONCAT(N''app-'', CONVERT(nvarchar(36), Id)) AS [Key], EmployeeId, ReferenceDate,
                       RawClockIn, RawClockOut, ClockIn, ClockOut, Origin
                FROM dbo.Attendance
                WHERE (Workable IS NULL OR Workable = 1) AND Status <> N''MissingRemote''
                UNION ALL
                SELECT CONCAT(N''sql-'', Id), Identification, CONVERT(char(10), Check_in, 23),
                       CONVERT(varchar(23), Check_in, 126), CONVERT(varchar(23), Check_out, 126),
                       CAST(NULL AS datetimeoffset), CAST(NULL AS datetimeoffset), N''Sql''
                FROM dbo.Pointage_factorial');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP VIEW IF EXISTS dbo.vw_Journal_Pointages;");
            migrationBuilder.DropTable(
                name: "Attendance");

            migrationBuilder.DropTable(
                name: "Connections");

            migrationBuilder.DropTable(
                name: "Errors");

            migrationBuilder.DropTable(
                name: "Mappings");

            migrationBuilder.DropTable(
                name: "Outbox");

            migrationBuilder.DropTable(
                name: "Pointage_factorial",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "Runs");

            migrationBuilder.DropTable(
                name: "Employees");

            migrationBuilder.DropTable(
                name: "LocalEmployees");
        }
    }
}
