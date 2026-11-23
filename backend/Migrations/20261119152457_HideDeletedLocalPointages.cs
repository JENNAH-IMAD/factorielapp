using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pointage.Api.Migrations
{
    /// <inheritdoc />
    public partial class HideDeletedLocalPointages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            SetJournalView(migrationBuilder, hideDeleted: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            SetJournalView(migrationBuilder, hideDeleted: false);
        }

        private static void SetJournalView(MigrationBuilder migrationBuilder, bool hideDeleted)
        {
            var predicate = hideDeleted ? "AND Status <> N''DeletedLocal''" : "";
            migrationBuilder.Sql($"""
                EXEC(N'ALTER VIEW dbo.vw_Journal_Pointages AS
                SELECT CONCAT(N''app-'', CONVERT(nvarchar(36), Id)) AS [Key], EmployeeId, ReferenceDate,
                       RawClockIn, RawClockOut, ClockIn, ClockOut, Origin
                FROM dbo.Attendance
                WHERE (Workable IS NULL OR Workable = 1) AND Status <> N''MissingRemote'' {predicate}
                UNION ALL
                SELECT CONCAT(N''sql-'', Id), Identification, CONVERT(char(10), Check_in, 23),
                       CONVERT(varchar(23), Check_in, 126), CONVERT(varchar(23), Check_out, 126),
                       CAST(NULL AS datetimeoffset), CAST(NULL AS datetimeoffset), N''Sql''
                FROM dbo.Pointage_factorial');
                """);
        }
    }
}
