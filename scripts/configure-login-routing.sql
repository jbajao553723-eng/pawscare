:setvar ServerA ".\SQLEXPRESS"
:setvar ServerB "(localdb)\PawCareServer2"
:setvar ServerC "(localdb)\PawCareServer3"

USE [MasterCRMJ];
GO
SET NOCOUNT ON;
SET XACT_ABORT ON;

BEGIN TRANSACTION;

UPDATE registration
SET ServerName = CASE company.CompanyCode
    WHEN 'PAW-A' THEN N'$(ServerA)'
    WHEN 'PAW-B' THEN N'$(ServerB)'
    WHEN 'PAW-C' THEN N'$(ServerC)'
END,
DatabaseName = CASE company.CompanyCode
    WHEN 'PAW-A' THEN N'PawCareTenantA'
    WHEN 'PAW-B' THEN N'PawCareTenantB'
    WHEN 'PAW-C' THEN N'PawCareTenantC'
END,
CredentialKey = N'LocalWindows',
IsActive = 1
FROM dbo.CompanyDatabases registration
JOIN dbo.Companies company ON company.CompanyId=registration.CompanyId
WHERE company.CompanyCode IN ('PAW-A','PAW-B','PAW-C');

MERGE dbo.LoginRoutes WITH (HOLDLOCK) AS target
USING (
    SELECT company.CompanyId, source.Username, UPPER(source.Username) AS NormalizedUsername,
           source.TenantId, CAST(1 AS bit) AS IsActive
    FROM (VALUES
        ('PAW-A','receptionista','tenant-a'), ('PAW-A','managera','tenant-a'),
        ('PAW-A','veterinariana','tenant-a'), ('PAW-A','admina','tenant-a'),
        ('PAW-B','receptionistb','tenant-b'), ('PAW-B','managerb','tenant-b'),
        ('PAW-B','veterinarianb','tenant-b'), ('PAW-B','adminb','tenant-b'),
        ('PAW-C','receptionistc','tenant-c'), ('PAW-C','managerc','tenant-c'),
        ('PAW-C','veterinarianc','tenant-c'), ('PAW-C','adminc','tenant-c'),
        ('PAW-A','superadmin','')
    ) source(CompanyCode,Username,TenantId)
    JOIN dbo.Companies company ON company.CompanyCode=source.CompanyCode
) AS source
ON target.NormalizedUsername=source.NormalizedUsername
WHEN MATCHED THEN UPDATE SET CompanyId=source.CompanyId, Username=source.Username,
    TenantId=source.TenantId, IsActive=1
WHEN NOT MATCHED THEN INSERT(CompanyId,Username,NormalizedUsername,TenantId,IsActive,CreatedAt)
    VALUES(source.CompanyId,source.Username,source.NormalizedUsername,source.TenantId,1,SYSUTCDATETIME());

COMMIT;

SELECT route.Username, company.CompanyCode, registration.ServerName, route.TenantId
FROM dbo.LoginRoutes route
JOIN dbo.Companies company ON company.CompanyId=route.CompanyId
JOIN dbo.CompanyDatabases registration ON registration.CompanyId=company.CompanyId AND registration.IsActive=1
WHERE route.IsActive=1
ORDER BY company.CompanyCode, route.Username;
GO
