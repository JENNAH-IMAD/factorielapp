IF OBJECT_ID(N'[__EFMigrationsHistory]') IS NULL
BEGIN
    CREATE TABLE [__EFMigrationsHistory] (
        [MigrationId] nvarchar(150) NOT NULL,
        [ProductVersion] nvarchar(32) NOT NULL,
        CONSTRAINT [PK___EFMigrationsHistory] PRIMARY KEY ([MigrationId])
    );
END;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261119144032_InitialSqlServer'
)
BEGIN
    CREATE TABLE [Attendance] (
        [Id] uniqueidentifier NOT NULL,
        [FactorialId] nvarchar(450) NULL,
        [EmployeeId] nvarchar(450) NOT NULL,
        [Date] nvarchar(max) NOT NULL,
        [ReferenceDate] nvarchar(450) NOT NULL,
        [RawClockIn] nvarchar(max) NULL,
        [RawClockOut] nvarchar(max) NULL,
        [ClockIn] datetimeoffset NULL,
        [ClockOut] datetimeoffset NULL,
        [TimeInterpretation] nvarchar(max) NOT NULL,
        [Workable] bit NULL,
        [FactorialMinutes] int NULL,
        [CalculatedMinutes] float NULL,
        [Origin] nvarchar(max) NOT NULL,
        [Status] nvarchar(450) NOT NULL,
        [Fingerprint] nvarchar(max) NOT NULL,
        [RemoteSnapshot] nvarchar(max) NULL,
        [RemoteUpdatedAt] nvarchar(max) NULL,
        CONSTRAINT [PK_Attendance] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261119144032_InitialSqlServer'
)
BEGIN
    CREATE TABLE [Connections] (
        [Id] int NOT NULL,
        [Environment] nvarchar(max) NOT NULL,
        [AuthMode] nvarchar(max) NOT NULL,
        [CompanyId] nvarchar(max) NULL,
        [AllowWrites] bit NOT NULL,
        [SyncMinutes] int NOT NULL,
        [LookbackDays] int NOT NULL,
        [TimeZone] nvarchar(max) NOT NULL,
        [Status] nvarchar(max) NOT NULL,
        [LastSync] datetimeoffset NULL,
        [LastAttempt] datetimeoffset NULL,
        CONSTRAINT [PK_Connections] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261119144032_InitialSqlServer'
)
BEGIN
    CREATE TABLE [Employees] (
        [Id] nvarchar(450) NOT NULL,
        [Name] nvarchar(max) NOT NULL,
        [CompanyIdentifier] nvarchar(max) NULL,
        [Email] nvarchar(max) NULL,
        [Missing] bit NOT NULL,
        CONSTRAINT [PK_Employees] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261119144032_InitialSqlServer'
)
BEGIN
    CREATE TABLE [LocalEmployees] (
        [Id] uniqueidentifier NOT NULL,
        [Name] nvarchar(max) NOT NULL,
        [PersonnelNumber] nvarchar(450) NOT NULL,
        CONSTRAINT [PK_LocalEmployees] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261119144032_InitialSqlServer'
)
BEGIN
    CREATE TABLE [Outbox] (
        [Id] uniqueidentifier NOT NULL,
        [EmployeeId] nvarchar(450) NOT NULL,
        [Action] nvarchar(max) NOT NULL,
        [Payload] nvarchar(max) NOT NULL,
        [Status] nvarchar(450) NOT NULL,
        [FactorialId] nvarchar(max) NULL,
        [ErrorCode] nvarchar(max) NULL,
        [AttendanceId] uniqueidentifier NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        CONSTRAINT [PK_Outbox] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261119144032_InitialSqlServer'
)
BEGIN
    CREATE TABLE [dbo].[Pointage_factorial] (
        [Id] bigint NOT NULL IDENTITY,
        [Identification] varchar(10) NOT NULL,
        [Check_in] datetime NOT NULL,
        [Check_out] datetime NULL,
        CONSTRAINT [PK_Pointage_factorial] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_Pointage_factorial_Chronology] CHECK ([Check_out] IS NULL OR [Check_out] >= [Check_in])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261119144032_InitialSqlServer'
)
BEGIN
    CREATE TABLE [Runs] (
        [Id] uniqueidentifier NOT NULL,
        [Kind] nvarchar(max) NOT NULL,
        [StartedAt] datetimeoffset NOT NULL,
        [FinishedAt] datetimeoffset NULL,
        [Status] nvarchar(max) NOT NULL,
        [Count] int NOT NULL,
        CONSTRAINT [PK_Runs] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261119144032_InitialSqlServer'
)
BEGIN
    CREATE TABLE [Mappings] (
        [LocalEmployeeId] uniqueidentifier NOT NULL,
        [FactorialEmployeeId] nvarchar(450) NOT NULL,
        [Method] nvarchar(max) NOT NULL,
        CONSTRAINT [PK_Mappings] PRIMARY KEY ([LocalEmployeeId]),
        CONSTRAINT [FK_Mappings_Employees_FactorialEmployeeId] FOREIGN KEY ([FactorialEmployeeId]) REFERENCES [Employees] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_Mappings_LocalEmployees_LocalEmployeeId] FOREIGN KEY ([LocalEmployeeId]) REFERENCES [LocalEmployees] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261119144032_InitialSqlServer'
)
BEGIN
    CREATE TABLE [Errors] (
        [Id] uniqueidentifier NOT NULL,
        [RunId] uniqueidentifier NOT NULL,
        [Code] nvarchar(max) NOT NULL,
        [Message] nvarchar(max) NOT NULL,
        CONSTRAINT [PK_Errors] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Errors_Runs_RunId] FOREIGN KEY ([RunId]) REFERENCES [Runs] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261119144032_InitialSqlServer'
)
BEGIN
    CREATE INDEX [IX_Attendance_EmployeeId_ReferenceDate] ON [Attendance] ([EmployeeId], [ReferenceDate]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261119144032_InitialSqlServer'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_Attendance_FactorialId] ON [Attendance] ([FactorialId]) WHERE [FactorialId] IS NOT NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261119144032_InitialSqlServer'
)
BEGIN
    CREATE INDEX [IX_Attendance_Status] ON [Attendance] ([Status]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261119144032_InitialSqlServer'
)
BEGIN
    CREATE INDEX [IX_Errors_RunId] ON [Errors] ([RunId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261119144032_InitialSqlServer'
)
BEGIN
    CREATE UNIQUE INDEX [IX_LocalEmployees_PersonnelNumber] ON [LocalEmployees] ([PersonnelNumber]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261119144032_InitialSqlServer'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Mappings_FactorialEmployeeId] ON [Mappings] ([FactorialEmployeeId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261119144032_InitialSqlServer'
)
BEGIN
    CREATE INDEX [IX_Outbox_EmployeeId_Status] ON [Outbox] ([EmployeeId], [Status]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261119144032_InitialSqlServer'
)
BEGIN
    CREATE INDEX [IX_Pointage_factorial_Check_in_Id] ON [dbo].[Pointage_factorial] ([Check_in], [Id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261119144032_InitialSqlServer'
)
BEGIN
    CREATE INDEX [IX_Runs_StartedAt] ON [Runs] ([StartedAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261119144032_InitialSqlServer'
)
BEGIN
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
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261119144032_InitialSqlServer'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261119144032_InitialSqlServer', N'10.0.0');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261119152457_HideDeletedLocalPointages'
)
BEGIN
    EXEC(N'ALTER VIEW dbo.vw_Journal_Pointages AS
    SELECT CONCAT(N''app-'', CONVERT(nvarchar(36), Id)) AS [Key], EmployeeId, ReferenceDate,
           RawClockIn, RawClockOut, ClockIn, ClockOut, Origin
    FROM dbo.Attendance
    WHERE (Workable IS NULL OR Workable = 1) AND Status <> N''MissingRemote'' AND Status <> N''DeletedLocal''
    UNION ALL
    SELECT CONCAT(N''sql-'', Id), Identification, CONVERT(char(10), Check_in, 23),
           CONVERT(varchar(23), Check_in, 126), CONVERT(varchar(23), Check_out, 126),
           CAST(NULL AS datetimeoffset), CAST(NULL AS datetimeoffset), N''Sql''
    FROM dbo.Pointage_factorial');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261119152457_HideDeletedLocalPointages'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261119152457_HideDeletedLocalPointages', N'10.0.0');
END;

COMMIT;
GO
