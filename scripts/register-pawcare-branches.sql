USE [MasterCRMJ];
GO

SET NOCOUNT ON;
SET XACT_ABORT ON;

IF OBJECT_ID(N'PawCareMaster.dbo.Tenants', N'U') IS NULL
    THROW 50001, 'Initialize the PawCare desktop databases before registering branches.', 1;

DECLARE @ServerName nvarchar(200) = N'.\SQLEXPRESS';

BEGIN TRANSACTION;

INSERT dbo.Companies (CompanyCode, CompanyName, IsActive, CreatedAt)
SELECT tenant.Code, tenant.Name, tenant.IsActive, tenant.CreatedUtc
FROM PawCareMaster.dbo.Tenants AS tenant
WHERE NOT EXISTS (
    SELECT 1
    FROM dbo.Companies WITH (UPDLOCK, HOLDLOCK)
    WHERE CompanyCode = tenant.Code
);

IF EXISTS (
    SELECT 1
    FROM PawCareMaster.dbo.Tenants AS tenant
    JOIN dbo.Companies AS company ON company.CompanyCode = tenant.Code
    JOIN dbo.CompanyDatabases AS registration WITH (UPDLOCK, HOLDLOCK)
        ON registration.ServerName = @ServerName AND registration.DatabaseName = tenant.DatabasePath
    WHERE registration.CompanyId <> company.CompanyId
)
    THROW 50002, 'A PawCare tenant database is already registered to a different company.', 1;

INSERT dbo.CompanyDatabases (CompanyId, ServerName, DatabaseName, IsActive)
SELECT company.CompanyId, @ServerName, tenant.DatabasePath, tenant.IsActive
FROM PawCareMaster.dbo.Tenants AS tenant
JOIN dbo.Companies AS company ON company.CompanyCode = tenant.Code
WHERE NOT EXISTS (
    SELECT 1
    FROM dbo.CompanyDatabases WITH (UPDLOCK, HOLDLOCK)
    WHERE ServerName = @ServerName AND DatabaseName = tenant.DatabasePath
);

COMMIT;

SELECT company.CompanyCode, company.CompanyName, registration.DatabaseName
FROM dbo.Companies AS company
JOIN dbo.CompanyDatabases AS registration ON registration.CompanyId = company.CompanyId
ORDER BY company.CompanyCode;
