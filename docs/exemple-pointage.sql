IF DB_ID(N'API_Sage_Factorial') IS NULL
    CREATE DATABASE [API_Sage_Factorial];
GO
USE [API_Sage_Factorial];
GO
-- Appliquer docs/migrations.sql avant cet exemple. Les heures sont locales au Maroc.
-- La transaction est annulée par défaut afin de laisser le journal réel intact.
BEGIN TRANSACTION;
INSERT INTO dbo.Pointage_factorial (Identification, Check_in, Check_out)
VALUES ('0419', CONVERT(datetime, '2026-11-16T08:30:00', 126), CONVERT(datetime, '2026-11-16T18:00:00', 126));

SELECT Identification, Check_in, Check_out FROM dbo.Pointage_factorial WHERE Identification = '0419';
ROLLBACK TRANSACTION;
GO
