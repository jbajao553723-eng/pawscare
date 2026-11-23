using Microsoft.Data.SqlClient;
using PawCare.Desktop.Models;
using PawCare.Desktop.Services;

var testRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PawCare", "data", "test-runs", Guid.NewGuid().ToString("N"));
var databasePrefix = $"PawCareTest{Guid.NewGuid():N}"[..19];
var firstServer = Environment.GetEnvironmentVariable("PAWCARE_TEST_SQL_SERVER") ?? @".\SQLEXPRESS";
var secondServer = Environment.GetEnvironmentVariable("PAWCARE_TEST_SECOND_SQL_SERVER") ?? firstServer;
var paths = new DatabasePaths(testRoot, firstServer, databasePrefix);
var secondPaths = new DatabasePaths(Path.Combine(testRoot, "second-server"), secondServer,
    string.Equals(firstServer, secondServer, StringComparison.OrdinalIgnoreCase) ? databasePrefix + "Other" : databasePrefix);

try
{
    var master = new MasterDatabase(paths);
    master.Initialize();

    var tenants = master.GetTenants();
    Assert(DatabaseExists(paths, paths.MasterDatabaseName), "master SQL Server database created");
    Assert(tenants.Count == 3, "three tenant organizations registered");
    Assert(tenants.Single(x => x.Id == "tenant-a").SubscriptionPlanCode == "MAIN", "tenant A starts on Main Transaction");
    Assert(tenants.Single(x => x.Id == "tenant-b").SubscriptionPlanCode == "PROMO", "tenant B starts on Promotional");
    Assert(tenants.Single(x => x.Id == "tenant-c").SubscriptionPlanCode == "FULL", "tenant C starts on Full Clinic");
    Assert(tenants.Single(x => x.Id == "tenant-a").SubscriptionFeatures.HasFlag(SubscriptionFeatures.Clinical), "Main Transaction includes minimum clinical workflow");
    Assert(tenants.Single(x => x.Id == "tenant-a").SubscriptionFeatures.HasFlag(SubscriptionFeatures.Reports), "Main Transaction includes business intelligence reports");
    Assert(tenants.Single(x => x.Id == "tenant-a").SubscriptionFeatures.HasFlag(SubscriptionFeatures.Backup) &&
        !tenants.Single(x => x.Id == "tenant-a").SubscriptionFeatures.HasFlag(SubscriptionFeatures.Satisfaction) &&
        !tenants.Single(x => x.Id == "tenant-a").SubscriptionFeatures.HasFlag(SubscriptionFeatures.IncrementalSync),
        "Main Transaction keeps backup while advanced engagement and sync stay locked");
    Assert(tenants.Single(x => x.Id == "tenant-b").SubscriptionFeatures.HasFlag(SubscriptionFeatures.Promotions) &&
        tenants.Single(x => x.Id == "tenant-b").SubscriptionFeatures.HasFlag(SubscriptionFeatures.Satisfaction) &&
        tenants.Single(x => x.Id == "tenant-b").SubscriptionFeatures.HasFlag(SubscriptionFeatures.Reports) &&
        !tenants.Single(x => x.Id == "tenant-b").SubscriptionFeatures.HasFlag(SubscriptionFeatures.Branching), "Promotional adds engagement without branching");
    Assert(tenants.Single(x => x.Id == "tenant-c").SubscriptionFeatures.HasFlag(SubscriptionFeatures.IncrementalSync) &&
        tenants.Single(x => x.Id == "tenant-c").SubscriptionFeatures.HasFlag(SubscriptionFeatures.Inventory),
        "Full Clinic includes advanced sync and inventory");

    foreach (var tenant in tenants)
    {
        var tenantStore = new ClinicDataStore(tenant.DatabasePath, tenant.Id, "bootstrap", "Bootstrap", paths);
        tenantStore.Load();
        Assert(DatabaseExists(paths, tenant.DatabasePath), $"isolated SQL Server database created for {tenant.Code}");
    }
    Assert(AllDatabaseNames(paths).Count == 4, "four-database SQL Server topology created");

    var receptionist = RequireLogin(master, "receptionista", UserRole.Receptionist, "tenant-a");
    var manager = RequireLogin(master, "managera", UserRole.Manager, "tenant-a");
    var veterinarian = RequireLogin(master, "veterinariana", UserRole.Veterinarian, "tenant-a");
    var adminA = RequireLogin(master, "admina", UserRole.Admin, "tenant-a");
    var superAdmin = RequireLogin(master, "superadmin", UserRole.SuperAdmin, string.Empty);
    AssertThrows<UnauthorizedAccessException>(() => master.SaveBranch(superAdmin, null, "tenant-a", "NORTH", "North Branch", true),
        "Tenant A cannot branch on Main Transaction");
    AssertThrows<UnauthorizedAccessException>(() => master.SaveBranch(superAdmin, null, "tenant-b", "NORTH", "North Branch", true),
        "Tenant B cannot branch on Promotional");
    master.SaveBranch(superAdmin, null, "tenant-c", "NORTH", "North Branch", true);
    var tenantCNorth = master.GetBranches("tenant-c").Single(x => x.Code == "NORTH");
    Assert(tenantCNorth.DatabasePath == $"{paths.TenantDatabaseName("tenant-c")}_NORTH",
        "Tenant C Full Clinic branch uses a readable isolated database name");
    var promoPlan = master.GetSubscriptionPlans().Single(x => x.Code == "PROMO");
    master.SaveSubscriptionPlan(superAdmin, promoPlan, promoPlan.Code, promoPlan.Name, promoPlan.Features & ~SubscriptionFeatures.Reports, 1, true);
    Assert(master.GetSubscriptionPlans().Single(x => x.Code == "PROMO").Features.HasFlag(SubscriptionFeatures.Reports), "Promotional keeps its required business intelligence module");
    Assert(!RoleAccessPolicy.CanViewBusinessIntelligence(UserRole.Receptionist) && RoleAccessPolicy.CanViewBusinessIntelligence(UserRole.Manager) &&
        RoleAccessPolicy.CanViewBusinessIntelligence(UserRole.Admin) && !RoleAccessPolicy.CanViewBusinessIntelligence(UserRole.Veterinarian) &&
        !RoleAccessPolicy.CanViewBusinessIntelligence(UserRole.SuperAdmin), "business intelligence role policy is manager and admin only");
    Assert(manager.TenantName == receptionist.TenantName && veterinarian.TenantName == receptionist.TenantName,
        "master login resolves organization without a tenant selector");
    Assert(master.Authenticate("receptionista", "wrong-password") is null, "invalid password rejected");

    var tenantADatabase = paths.TenantDatabaseName("tenant-a");
    var tenantBDatabase = paths.TenantDatabaseName("tenant-b");
    var storeA = new ClinicDataStore(tenantADatabase, "tenant-a", receptionist.UserId, "receptionista", paths);
    storeA.Load();
    Assert(storeA.Data.Clients.Count >= 5, "seeded client profiles");
    Assert(storeA.Data.Appointments.Count >= 5, "seeded appointments");
    Assert(storeA.Data.Inquiries.Count >= 4, "seeded inquiry collection");
    var mainCharts = BusinessIntelligenceService.Build(storeA.Data, false, DateTime.Today);
    var promotionalCharts = BusinessIntelligenceService.Build(storeA.Data, true, DateTime.Today);
    var operationalKpis = BusinessIntelligenceService.BuildKpis(storeA.Data, false, DateTime.Today);
    var promotionalKpis = BusinessIntelligenceService.BuildKpis(storeA.Data, true, DateTime.Today);
    Assert(mainCharts.Count == 6 && mainCharts.Select(x => x.Title).Distinct().Count() == 6 && mainCharts.All(x => !string.IsNullOrWhiteSpace(x.Finding) && !string.IsNullOrWhiteSpace(x.RecommendedAction)),
        "Main Transaction BI produces six decision-ready graphs");
    Assert(promotionalCharts.Count == 6 && promotionalCharts[^1].Title == "Campaign conversion", "Promotional BI produces six graphs with campaign conversion");
    Assert(promotionalCharts.Any(x => x.Title == "Campaign reach and claims") && promotionalCharts.All(x => x.Title != "Appointment demand trend"),
        "Promotional BI uses campaign-specific dashboard evidence");
    Assert(operationalKpis.Count == 6 && promotionalKpis.Count == 6 && promotionalKpis.Any(x => x.Name == "Campaign conversion") &&
        promotionalKpis.All(x => !string.IsNullOrWhiteSpace(x.TargetDisplay) && !string.IsNullOrWhiteSpace(x.Formula)),
        "BI KPI layer supplies six measured outcomes with targets and formulas");

    var archivedAppointment = storeA.Data.Appointments
        .OrderByDescending(x => int.TryParse(x.Id.Split('-').LastOrDefault(), out var number) ? number : 0)
        .First();
    storeA.Data.Appointments.Remove(archivedAppointment);
    storeA.Save();
    Assert(storeA.GetArchivedAppointments().Any(x => x.Id == archivedAppointment.Id), "archived appointment remains recoverable");
    Assert(storeA.NextId("AP", 2093, storeA.Data.Appointments.Count) != archivedAppointment.Id, "new appointment IDs do not reuse archived IDs");
    storeA.Data.Appointments.Add(storeA.GetArchivedAppointments().Single(x => x.Id == archivedAppointment.Id));
    storeA.Save();
    Assert(storeA.GetArchivedAppointments().All(x => x.Id != archivedAppointment.Id) && storeA.Data.Appointments.Any(x => x.Id == archivedAppointment.Id),
        "archived appointment restores to the active schedule");
    Assert(storeA.GetAuditLogs().Any(x => x.Action == "Restore" && x.EntityId == archivedAppointment.Id), "appointment restore has a named audit event");

    const string isolatedSubject = "Tenant A SQL Server wellness inquiry";
    storeA.Data.Inquiries.Insert(0, NewInquiry(storeA, isolatedSubject));
    storeA.Data.Feedback.Insert(0, NewComplaint(storeA));
    storeA.Save();
    Assert(storeA.PendingSyncEvents() > 0, "local SQL transaction atomically queues outbox events");

    var storeB = new ClinicDataStore(tenantBDatabase, "tenant-b", "test", "Tenant B Tester", paths);
    storeB.Load();
    Assert(storeB.Data.Inquiries.All(x => x.Subject != isolatedSubject), "tenant data remains isolated by SQL Server database");

    using (var connection = OpenDatabase(paths, tenantADatabase))
    {
        Assert(TableCount(connection, "Inquiries") > 0, "inquiries use a dedicated transaction table");
        Assert(TableCount(connection, "Complaints") > 0, "complaints use a dedicated transaction table");
        Assert(TableCount(connection, "Feedback") > 0, "feedback uses a dedicated transaction table");
    }

    var concurrentOne = new ClinicDataStore(tenantADatabase, "tenant-a", "writer-1", "Writer One", paths);
    var concurrentTwo = new ClinicDataStore(tenantADatabase, "tenant-a", "writer-2", "Writer Two", paths);
    concurrentOne.Load();
    concurrentTwo.Load();
    concurrentOne.Data.Inquiries.Insert(0, NewInquiry(concurrentOne, "Writer one SQL record"));
    concurrentOne.Save();
    concurrentTwo.Data.Inquiries.Insert(0, NewInquiry(concurrentTwo, "Stale writer SQL record"));
    AssertThrows<DataConcurrencyException>(() => concurrentTwo.Save(), "stale SQL writer rejected instead of overwriting newer data");
    concurrentTwo.Load();
    Assert(concurrentTwo.Data.Inquiries.Any(x => x.Subject == "Writer one SQL record"), "conflicted store reloads committed SQL data");

    var sync = new CloudSyncService(paths, concurrentTwo);
    var incremental = sync.SyncIncremental();
    Assert(File.Exists(incremental.Destination), "incremental cloud package created from SQL outbox");
    Assert(concurrentTwo.PendingSyncEvents() == 0, "outbox marked synced only after package commit");
    var backup = sync.CreateCloudBackup();
    Assert(File.Exists(backup.LocalDestination) && File.Exists(backup.CloudDestination) &&
        Path.GetExtension(backup.CloudDestination) == ".bak" && backup.LocalDestination != backup.CloudDestination,
        "cloud backup preserves a local SQL backup and an independent cloud mirror copy");

    var managerStore = new ClinicDataStore(tenantADatabase, "tenant-a", manager.UserId, manager.Name, paths);
    managerStore.Load(); managerStore.SetActor(manager);
    var managedInquiry = managerStore.Data.Inquiries.First();
    managedInquiry.Status = "Follow-up";
    managedInquiry.Owner = manager.Name;
    managedInquiry.FollowUpDate = DateTime.Today.AddDays(1);
    managedInquiry.LastContactChannel = "Phone";
    managedInquiry.LastContactOutcome = "Reached - follow-up scheduled";
    managedInquiry.LastContactUtc = DateTime.UtcNow;
    managerStore.Save();
    var managerReload = new ClinicDataStore(tenantADatabase, "tenant-a", manager.UserId, manager.Name, paths);
    managerReload.Load();
    Assert(managerReload.Data.Inquiries.Single(x => x.Id == managedInquiry.Id).LastContactChannel == "Phone",
        "manager can record inquiry follow-through on Main Transaction");

    var veterinarianStore = new ClinicDataStore(tenantADatabase, "tenant-a", veterinarian.UserId, veterinarian.Name, paths);
    veterinarianStore.Load(); veterinarianStore.SetActor(veterinarian);
    var assignedVisit = veterinarianStore.Data.Appointments.First(x => x.Veterinarian == veterinarian.Name);
    var clinicalRecord = new ConsultationRecord
    {
        Id = veterinarianStore.NextId("CON", 100, veterinarianStore.Data.Consultations.Count),
        AppointmentId = assignedVisit.Id,
        Date = DateTime.Today,
        ClientName = assignedVisit.ClientName,
        PetName = assignedVisit.PetName,
        Veterinarian = veterinarian.Name,
        ChiefComplaint = "Regression test visit reason",
        Vitals = "Weight and temperature recorded",
        Assessment = "Stable for outpatient care",
        TreatmentPlan = "Supportive care and monitoring",
        Prescriptions = "Medication instructions recorded",
        FollowUpPlan = "Recheck in seven days",
        ClientInstructions = "Monitor appetite and contact PawCare if symptoms worsen.",
        Status = "In progress"
    };
    veterinarianStore.Data.Consultations.Add(clinicalRecord);
    veterinarianStore.Save();
    var veterinarianReload = new ClinicDataStore(tenantADatabase, "tenant-a", veterinarian.UserId, veterinarian.Name, paths);
    veterinarianReload.Load();
    Assert(veterinarianReload.Data.Consultations.Single(x => x.Id == clinicalRecord.Id).ClientInstructions.Contains("Monitor appetite"),
        "veterinarian clinical details persist on Main Transaction");

    var suffix = Guid.NewGuid().ToString("N")[..8];
    var vetUsername = $"branchvet{suffix}";
    master.SaveBranchStaff(adminA, null, vetUsername, "Dr. Branch Vet", UserRole.Veterinarian, "VetPass!42");
    RequireLogin(master, vetUsername, UserRole.Veterinarian, "tenant-a", "VetPass!42");
    var branchVet = master.GetStaff("tenant-a").Single(x => x.Username == vetUsername);
    master.SetAccountActive(adminA, branchVet, false);
    Assert(master.Authenticate(vetUsername, "VetPass!42") is null, "deactivated staff cannot sign in");
    master.SetAccountActive(adminA, master.GetStaff("tenant-a").Single(x => x.Username == vetUsername), true);
    RequireLogin(master, vetUsername, UserRole.Veterinarian, "tenant-a", "VetPass!42");
    AssertThrows<UnauthorizedAccessException>(
        () => master.SaveBranchStaff(adminA, null, $"forbidden{suffix}", "Forbidden Admin", UserRole.Admin, "Pass!123"),
        "branch admin cannot create another administrator");
    var branchAdminUsername = $"newadmin{suffix}";
    master.SaveBranchAdmin(superAdmin, null, "tenant-b", branchAdminUsername, "New Branch Admin", "AdminPass!42");
    RequireLogin(master, branchAdminUsername, UserRole.Admin, "tenant-b", "AdminPass!42");
    Assert(master.GetAuditLogs(limit: 100).Any(x => x.EntityType == "User"), "account changes recorded in master audit log");

    var secondMaster = new MasterDatabase(secondPaths);
    secondMaster.Initialize();
    Assert(DatabaseExists(secondPaths, secondPaths.MasterDatabaseName), "second server profile has its own master database");
    Assert(!string.Equals(firstServer, secondServer, StringComparison.OrdinalIgnoreCase) ||
        paths.MasterDatabaseName != secondPaths.MasterDatabaseName, "test server database locations are distinct");
    Assert(secondMaster.GetStaff("tenant-a").All(x => x.Username != vetUsername), "users are isolated between server profiles");
    var secondTenant = secondMaster.GetTenant("tenant-a");
    Assert(DatabaseExists(secondPaths, secondTenant.DatabasePath), "second server profile has its own tenant database");

    var newCode = $"TEST-{suffix.ToUpperInvariant()}";
    master.SaveTenant(superAdmin, null, newCode, "Test Organization", true);
    var newTenant = master.GetTenants().Single(x => x.Code == newCode);
    Assert(DatabaseExists(paths, newTenant.DatabasePath), "new organization gets an isolated database");
    var newTenantStore = new ClinicDataStore(newTenant.DatabasePath, newTenant.Id, paths: paths);
    newTenantStore.Load();
    Assert(newTenantStore.Data.Clients.Count == 0, "new organization starts without another branch's demo clients");
    master.SaveTenant(superAdmin, newTenant, newCode, "Test Organization", false);
    Assert(!master.GetTenants().Single(x => x.Id == newTenant.Id).IsActive, "organization deactivation persists");

    var promotionStore = new ClinicDataStore(tenantADatabase, "tenant-a", receptionist.UserId, receptionist.Name, paths);
    promotionStore.Load(); promotionStore.SetActor(receptionist);
    var promotion = new PromotionRecord { Id = promotionStore.NextId("PRO", 900, promotionStore.Data.Promotions.Count), Name = "Test approval", Code = "TEST", OfferValue = "10%", Audience = "All consented clients", Channel = "Email", StartDate = DateTime.Today, EndDate = DateTime.Today.AddDays(1) };
    promotionStore.Data.Promotions.Add(promotion);
    AssertThrows<UnauthorizedAccessException>(() => promotionStore.Save(), "Main Transaction subscription blocks promotions");
    promotionStore.Reload();
    var tenantA = master.GetTenant("tenant-a");
    master.SaveTenant(superAdmin, tenantA, tenantA.Code, tenantA.Name, true, "FULL");
    Assert(master.GetTenant("tenant-a").SubscriptionFeatures.HasFlag(SubscriptionFeatures.Promotions), "Super Admin can upgrade a company subscription");
    promotionStore.Data.Promotions.Add(promotion);
    promotionStore.Save();
    promotion.Approval = "Approved";
    AssertThrows<UnauthorizedAccessException>(() => promotionStore.Save(), "receptionist cannot approve a promotion");
    promotionStore.Reload();
    var adminStore = new ClinicDataStore(tenantADatabase, "tenant-a", adminA.UserId, adminA.Name, paths);
    adminStore.Load(); adminStore.SetActor(adminA);
    var adminPromotion = adminStore.Data.Promotions.Single(x => x.Id == promotion.Id);
    adminPromotion.Status = "Active";
    AssertThrows<InvalidOperationException>(() => adminStore.Save(), "unapproved campaign cannot activate");
    adminPromotion.Approval = "Approved"; adminPromotion.ApprovedBy = adminA.Name; adminPromotion.ApprovedUtc = DateTime.UtcNow;
    adminStore.Save();
    Assert(adminStore.GetAuditLogs().Any(x => x.Action == "Approve promotion" && x.EntityId == promotion.Id), "promotion approval has a named audit event");

    master.SaveBranch(adminA, null, "tenant-a", "SOUTH", "South Branch", true);
    var south = master.GetBranches("tenant-a").Single(x => x.Code == "SOUTH");
    var southStore = new ClinicDataStore(south.DatabasePath, "tenant-a", paths: paths, branchId: south.Id);
    southStore.Load();
    Assert(southStore.Data.Clients.Count == 0, "new branch has its own empty operational database");
    Assert(southStore.BranchId != adminStore.BranchId, "branch identifiers separate sync and outbox scope");

    VerifyPromotionWorkflow();
    VerifyClientEmailFlows();

    Console.WriteLine("PASS  PawCare SQL Server multi-tenant architecture tests");
    Console.WriteLine($"PASS  1 master + {tenants.Count} tenant databases; {incremental.EventCount} incremental events synced");
    return 0;
}
finally
{
    SqlConnection.ClearAllPools();
    DropTestDatabases(secondPaths);
    DropTestDatabases(paths);
    if (Directory.Exists(testRoot)) Directory.Delete(testRoot, true);
}

static bool DatabaseExists(DatabasePaths paths, string databaseName)
{
    using var connection = new SqlConnection(paths.ServerConnectionString);
    connection.Open();
    using var command = connection.CreateCommand();
    command.CommandText = "SELECT COUNT(*) FROM sys.databases WHERE name=@name;";
    command.Parameters.AddWithValue("@name", databaseName);
    return Convert.ToInt32(command.ExecuteScalar()) == 1;
}

static IReadOnlyList<string> AllDatabaseNames(DatabasePaths paths)
{
    using var connection = new SqlConnection(paths.ServerConnectionString);
    connection.Open();
    using var command = connection.CreateCommand();
    command.CommandText = "SELECT name FROM sys.databases WHERE name LIKE @prefix ORDER BY name;";
    command.Parameters.AddWithValue("@prefix", paths.DatabasePrefix + "%");
    using var reader = command.ExecuteReader();
    var names = new List<string>();
    while (reader.Read()) names.Add(reader.GetString(0));
    return names;
}

static void DropTestDatabases(DatabasePaths paths)
{
    var names = AllDatabaseNames(paths).Where(x => x != paths.MasterDatabaseName).Append(paths.MasterDatabaseName).ToArray();
    using var connection = new SqlConnection(paths.ServerConnectionString);
    connection.Open();
    foreach (var name in names)
    {
        if (!name.StartsWith("PawCareTest", StringComparison.Ordinal) || !name.All(x => char.IsLetterOrDigit(x) || x == '_'))
            throw new InvalidOperationException("Refusing to remove a database outside the test prefix.");
        using var command = connection.CreateCommand();
        command.CommandTimeout = 120;
        command.CommandText = $"IF DB_ID(@name) IS NOT NULL BEGIN ALTER DATABASE [{name}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{name}]; END;";
        command.Parameters.AddWithValue("@name", name);
        command.ExecuteNonQuery();
    }
}

static UserSession RequireLogin(MasterDatabase master, string username, UserRole role, string tenantId, string password = "pawcare123")
{
    var session = master.Authenticate(username, password);
    Assert(session is not null, $"{username} authenticates");
    Assert(session!.Role == role && session.TenantId == tenantId, $"{username} resolves {role} and tenant");
    return session;
}

static InquiryRecord NewInquiry(ClinicDataStore store, string subject) => new()
{
    Id = store.NextId("INQ", 4000, store.Data.Inquiries.Count), ClientName = "Architecture Test Client",
    Contact = "architecture@example.test", PetName = "Pixel", Channel = "Email", Category = "New client",
    Subject = subject, Details = "SQL Server persistence and tenant-isolation verification record.", Priority = "Low",
    Status = "New", PreferredChannel = "Email", CommunicationConsent = true, Owner = "Alex Morgan",
    FollowUpDate = DateTime.Today.AddDays(1)
};

static FeedbackRecord NewComplaint(ClinicDataStore store) => new()
{
    Id = store.NextId("FB", 700, store.Data.Feedback.Count), Date = DateTime.Today,
    ClientName = "Architecture Test Client", Contact = "0917 000 0000", PetName = "Pixel", CaseType = "Complaint",
    Channel = "Phone", Rating = 2, Category = "Wait time", Priority = "High",
    Comment = "The appointment started later than expected.", Status = "Open", Escalation = "Manager review",
    ConsentToFollowUp = true, Owner = "Alex Morgan", FollowUpDate = DateTime.Today.AddDays(1),
    ResolutionNotes = "Manager callback requested."
};

static SqlConnection OpenDatabase(DatabasePaths paths, string databaseName)
{
    var connection = new SqlConnection(paths.DatabaseConnectionString(databaseName));
    connection.Open();
    return connection;
}

static long TableCount(SqlConnection connection, string table)
{
    if (!new[] { "Inquiries", "Complaints", "Feedback" }.Contains(table))
        throw new ArgumentOutOfRangeException(nameof(table));
    using var command = connection.CreateCommand();
    command.CommandText = $"SELECT COUNT(*) FROM dbo.[{table}];";
    return Convert.ToInt64(command.ExecuteScalar());
}

static void VerifyPromotionWorkflow()
{
    var now = DateTime.Now;
    var campaign = new PromotionRecord
    {
        Id = "PRO-WORKFLOW", Name = "Wellness Week", Code = "WELL20", OfferValue = "20% off",
        Audience = "All consented clients", Channel = "SMS + Email", StartDate = now.Date,
        EndDate = now.Date.AddDays(14), Goal = "Increase preventive wellness bookings.", Approval = "Draft", Status = "Draft"
    };
    PromotionWorkflowService.Submit(campaign, [campaign], "Reception Test", now);
    Assert(campaign.Approval == "Needs review" && campaign.SubmittedBy == "Reception Test", "campaign draft submits for named review");
    PromotionWorkflowService.Approve(campaign, [campaign], "Admin Test", now);
    Assert(campaign.Approval == "Approved" && campaign.Status == "Active" && campaign.LaunchedUtc is not null, "approved current campaign becomes active");

    var data = new ClinicData();
    data.Clients.Add(new ClientRecord { Id = "CL-PROMO-1", ClientName = "Consented Client", PetName = "Mochi", MarketingConsent = true, Email = "consented@example.com", Phone = "0917 000 0001" });
    data.Clients.Add(new ClientRecord { Id = "CL-PROMO-2", ClientName = "Private Client", PetName = "Milo", MarketingConsent = false, Email = "private@example.com", Phone = "0917 000 0002" });
    Assert(PromotionWorkflowService.MatchingAudience(data, campaign).Count == 1, "campaign audience excludes clients without marketing consent");
    Assert(PromotionWorkflowService.RecordContactedClients(campaign, ["CL-PROMO-1"]) == 1, "first campaign delivery records reach");
    Assert(PromotionWorkflowService.RecordContactedClients(campaign, ["CL-PROMO-1"]) == 0 && campaign.Reach == 1, "repeat delivery does not double-count reach");
    PromotionWorkflowService.RecordClaims(campaign, 1);
    Assert(campaign.Claims == 1 && campaign.Conversion == 100, "verified claim updates campaign conversion");
    PromotionWorkflowService.Complete(campaign, now.AddDays(1));
    Assert(campaign.Status == "Completed" && campaign.CompletedUtc is not null, "active campaign completes with timestamp");

    var duplicate = new PromotionRecord
    {
        Id = "PRO-DUPLICATE", Name = "Duplicate", Code = "WELL20", OfferValue = "10% off", Audience = "All consented clients",
        Channel = "Email", StartDate = now.Date, EndDate = now.Date.AddDays(5), Goal = "Validate unique campaign codes.", Approval = "Draft"
    };
    Assert(PromotionWorkflowService.ValidateForSubmission(duplicate, [campaign, duplicate], now.Date)?.Contains("already used") == true,
        "duplicate offer codes are rejected");
    Assert(ClientEmailService.PromotionSmsMessage(campaign).Contains("WELL20"), "SMS campaign handoff includes the offer code");
}

static void VerifyClientEmailFlows()
{
    var client = new ClientRecord { ClientName = "Email Test Client", PetName = "Pixel", Email = "client@example.com", MarketingConsent = true };
    var appointment = ClientEmailService.AppointmentReminder(client, new AppointmentRecord
    {
        ClientName = client.ClientName, PetName = client.PetName, Date = DateTime.Today.AddDays(2), Time = "13:30",
        Service = "Annual wellness exam", Veterinarian = "Dr. Mia Garcia"
    });
    Assert(appointment.To == client.Email && appointment.Body.Contains("Pixel") && appointment.Body.Contains("Annual wellness exam"), "appointment email draft content");

    var offer = ClientEmailService.PromotionOffer(client, new PromotionRecord
    {
        Name = "Healthy Pets Week", Code = "CARE20", OfferValue = "20% off", StartDate = DateTime.Today, EndDate = DateTime.Today.AddDays(7)
    });
    Assert(offer.Body.Contains("CARE20") && offer.Subject.Contains("20% off"), "consented promotional email draft");
    client.MarketingConsent = false;
    AssertThrows<InvalidOperationException>(() => ClientEmailService.PromotionOffer(client, new PromotionRecord()), "promotion email consent enforcement");
    client.MarketingConsent = true;

    var feedback = ClientEmailService.FeedbackFollowUp(client, new FeedbackRecord
    {
        ClientName = client.ClientName, PetName = client.PetName, CaseType = "Complaint", Category = "Wait time",
        Status = "Escalated", ConsentToFollowUp = true, ResolutionNotes = "A manager will call tomorrow."
    });
    Assert(feedback.Body.Contains("manager review") && feedback.Body.Contains("A manager will call tomorrow."), "manager escalation email draft");
    Assert(ClientEmailService.CreateMailToUri(feedback).AbsoluteUri.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase), "default email application link");
}

static void Assert(bool condition, string label)
{
    if (!condition) throw new InvalidOperationException($"FAIL  {label}");
    Console.WriteLine($"PASS  {label}");
}

static void AssertThrows<TException>(Action action, string label) where TException : Exception
{
    try { action(); }
    catch (TException) { Console.WriteLine($"PASS  {label}"); return; }
    throw new InvalidOperationException($"FAIL  {label}");
}
