using Microsoft.Data.SqlClient;
using PawCare.Desktop.Models;
using PawCare.Desktop.Services;
using System.Text.Json;

if (TryGetOption(args, "--seed-demo-clients", out var requestedCount))
{
    if (!int.TryParse(requestedCount, out var clientCount) || clientCount is < 1 or > 10_000)
        throw new ArgumentOutOfRangeException(nameof(requestedCount), "The demo client count must be between 1 and 10,000.");

    TryGetOption(args, "--tenant", out var requestedTenant);
    var seedPaths = new DatabasePaths();
    var seedMaster = new MasterDatabase(seedPaths);
    seedMaster.Initialize();
    var tenantId = string.IsNullOrWhiteSpace(requestedTenant) ? seedPaths.AssignedTenantId ?? "tenant-a" : requestedTenant;
    var tenant = seedMaster.GetTenant(tenantId);
    var branch = seedMaster.GetBranches(tenantId).FirstOrDefault(x => x.IsActive)
        ?? throw new InvalidOperationException($"Tenant {tenantId} has no active branch.");
    var store = new ClinicDataStore(branch.DatabasePath, tenantId, "demo-data-seeder", "Demo Data Seeder", seedPaths, branch.Id);
    store.Load();

    var addedClients = 0;
    var updatedClients = 0;
    var addedAppointments = 0;
    var updatedAppointments = 0;
    var addedInquiries = 0;
    var updatedInquiries = 0;
    for (var index = 1; index <= clientCount; index++)
    {
        var client = CreateDemoClient(index, tenantId);
        CountChange(UpsertDemoRecord(store.Data.Clients, client, x => x.Id), ref addedClients, ref updatedClients);
        CountChange(UpsertDemoRecord(store.Data.Appointments, CreateDemoAppointment(index, client), x => x.Id), ref addedAppointments, ref updatedAppointments);
        CountChange(UpsertDemoRecord(store.Data.Inquiries, CreateDemoInquiry(index, client), x => x.Id), ref addedInquiries, ref updatedInquiries);
    }

    store.AddActivity("clients", "Demo client data imported",
        $"{addedClients} clients added, {updatedClients} updated · {addedAppointments} appointments added, {updatedAppointments} updated · {addedInquiries} inquiries added, {updatedInquiries} updated");
    store.Save();
    Console.WriteLine($"SEEDED: {tenant.Code} / {branch.Code} / {branch.DatabasePath}");
    Console.WriteLine($"  Clients added: {addedClients}");
    Console.WriteLine($"  Clients updated: {updatedClients}");
    Console.WriteLine($"  Appointments added: {addedAppointments}");
    Console.WriteLine($"  Appointments updated: {updatedAppointments}");
    Console.WriteLine($"  Inquiries added: {addedInquiries}");
    Console.WriteLine($"  Inquiries updated: {updatedInquiries}");
    return;
}

if (args.Contains("--verify-login-routing", StringComparer.OrdinalIgnoreCase))
{
    var servers = DatabasePaths.ConfiguredServers();
    var directoryProfile = servers[0];
    var directoryPaths = new DatabasePaths(serverName: directoryProfile.Instance,
        databasePrefix: directoryProfile.DatabasePrefix, mirrorDirectory: directoryProfile.MirrorDirectory);
    var directory = new LoginRouteDirectory(directoryPaths);
    var expected = new Dictionary<string, string>
    {
        ["receptionista"] = "tenant-a", ["managera"] = "tenant-a",
        ["veterinariana"] = "tenant-a", ["admina"] = "tenant-a",
        ["receptionistb"] = "tenant-b", ["managerb"] = "tenant-b",
        ["veterinarianb"] = "tenant-b", ["adminb"] = "tenant-b",
        ["receptionistc"] = "tenant-c", ["managerc"] = "tenant-c",
        ["veterinarianc"] = "tenant-c", ["adminc"] = "tenant-c",
        ["superadmin"] = ""
    };
    foreach (var account in expected)
    {
        var route = directory.Resolve(account.Key)
            ?? throw new InvalidOperationException($"No route exists for {account.Key}.");
        var profile = servers.SingleOrDefault(x =>
            string.Equals(x.Instance, route.ServerName, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"The route for {account.Key} points to an unconfigured server.");
        var routedPaths = new DatabasePaths(serverName: profile.Instance, databasePrefix: profile.DatabasePrefix,
            mirrorDirectory: profile.MirrorDirectory);
        var session = new MasterDatabase(routedPaths).Authenticate(account.Key, "pawcare123")
            ?? throw new InvalidOperationException($"{account.Key} did not authenticate on its routed server.");
        if (session.Role != PawCare.Desktop.Models.UserRole.SuperAdmin &&
            !string.Equals(session.TenantId, account.Value, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"{account.Key} authenticated in the wrong tenant.");
        Console.WriteLine($"ROUTE OK: {account.Key} -> {profile.Name} -> {session.TenantName} -> {session.Role}");
    }
    Console.WriteLine("Central username routing verification complete.");
    return;
}

var paths = new DatabasePaths();
Console.WriteLine($"Initializing {paths.ServerName}...");
var master = new MasterDatabase(paths);
master.Initialize();

foreach (var tenant in master.GetTenants())
{
    foreach (var branch in master.GetBranches(tenant.Id).Where(branch => branch.IsActive))
    {
        new ClinicDataStore(branch.DatabasePath, tenant.Id, paths: paths, branchId: branch.Id).Load();
        Console.WriteLine($"  {tenant.Code} / {branch.Code}: {branch.DatabasePath}");
    }
}

var expectedDatabases = new List<string> { paths.MasterDatabaseName };
expectedDatabases.AddRange(new[] { "tenant-a", "tenant-b", "tenant-c" }
    .Where(paths.ManagesTenant)
    .Select(paths.TenantDatabaseName));
foreach (var name in expectedDatabases)
{
    using var verificationConnection = new SqlConnection(paths.DatabaseConnectionString(name));
    verificationConnection.Open();
    using var command = verificationConnection.CreateCommand();
    command.CommandTimeout = 120;
    command.CommandText = "SELECT DB_NAME();";
    if (!string.Equals(Convert.ToString(command.ExecuteScalar()), name, StringComparison.OrdinalIgnoreCase))
        throw new InvalidOperationException($"Database {name} was not created or is offline.");
    Console.WriteLine($"  ONLINE: {name}");
}

Console.WriteLine("Server database initialization complete.");

static bool TryGetOption(string[] arguments, string name, out string value)
{
    var index = Array.FindIndex(arguments, x => string.Equals(x, name, StringComparison.OrdinalIgnoreCase));
    value = index >= 0 && index + 1 < arguments.Length ? arguments[index + 1] : string.Empty;
    return index >= 0;
}

static int UpsertDemoRecord<T>(IList<T> records, T generated, Func<T, string> idSelector)
{
    var existingIndex = -1;
    for (var index = 0; index < records.Count; index++)
    {
        if (idSelector(records[index]) != idSelector(generated)) continue;
        existingIndex = index;
        break;
    }

    if (existingIndex < 0)
    {
        records.Add(generated);
        return 1;
    }

    if (JsonSerializer.Serialize(records[existingIndex]) == JsonSerializer.Serialize(generated)) return 0;
    records[existingIndex] = generated;
    return 2;
}

static void CountChange(int change, ref int added, ref int updated)
{
    if (change == 1) added++;
    if (change == 2) updated++;
}

static ClientRecord CreateDemoClient(int index, string tenantId)
{
    var (firstNames, lastNames, addresses, phoneGroup, emailTag) = DemoIdentitySet(tenantId);
    string[] petNames = ["Mochi", "Bantay", "Mingming", "Oreo", "Choco", "Cloud", "Max", "Bella", "Simba", "Coco",
        "Luna", "Milo", "Pepper", "Atlas", "Kiko", "Nala", "Bruno", "Sushi", "Tala", "Pochi"];
    string[] species = ["Dog", "Cat", "Dog", "Cat", "Rabbit"];
    var firstName = firstNames[(index - 1) % firstNames.Length];
    var lastName = lastNames[(index - 1) / firstNames.Length % lastNames.Length];
    var petName = petNames[(index * 7 - 1) % petNames.Length];
    var animal = species[(index - 1) % species.Length];
    var breed = animal switch
    {
        "Cat" => index % 2 == 0 ? "Domestic Shorthair" : "Persian mix",
        "Rabbit" => "Mini Rex",
        _ => index % 3 == 0 ? "Shih Tzu" : index % 3 == 1 ? "Aspin" : "Labrador mix"
    };
    return new ClientRecord
    {
        Id = $"CL-DEMO-{index:0000}",
        ClientName = $"{firstName} {lastName}",
        Phone = $"09{10 + index % 90:00} {phoneGroup * 100 + index % 100:000} {1000 + index * 37 % 9000:0000}",
        Email = $"{firstName.ToLowerInvariant()}.{lastName.Replace(" ", string.Empty).ToLowerInvariant()}{index:000}.{emailTag}@example.com",
        Address = addresses[(index - 1) % addresses.Length],
        MarketingConsent = index % 4 != 0,
        LastVisit = index % 3 == 0 ? DateTime.Today.AddDays(-(index % 60 + 1)) : null,
        PetName = petName,
        Species = animal,
        Breed = breed,
        Sex = index % 2 == 0 ? "Female" : "Male",
        Birthday = DateTime.Today.AddYears(-(1 + index % 12)).AddDays(-(index % 300))
    };
}

static (string[] FirstNames, string[] LastNames, string[] Addresses, int PhoneGroup, string EmailTag) DemoIdentitySet(string tenantId) => tenantId switch
{
    "tenant-b" =>
    (
        ["Aira", "Bea", "Cedric", "Denise", "Elijah", "Fiona", "Gabriel", "Hannah", "Joaquin", "Katrina",
            "Lawrence", "Mae", "Nathaniel", "Olive", "Rafael", "Sofia", "Timothy", "Valerie", "Xavier", "Zoe"],
        ["Alcantara", "Bernardo", "Coronel", "Dominguez", "Esguerra", "Fajardo", "Guevarra", "Hilario", "Ilagan", "Jacinto"],
        ["Bonifacio Global City", "Pembo, Taguig", "Ususan, Taguig", "Western Bicutan, Taguig", "Pinagsama, Taguig",
            "Fort Bonifacio, Taguig", "Tuktukan, Taguig", "Bagumbayan, Taguig", "Lower Bicutan, Taguig", "Central Signal Village, Taguig"],
        2,
        "taguig"
    ),
    "tenant-c" =>
    (
        ["Adrian", "Camille", "Daniel", "Erica", "Francis", "Giselle", "Harvey", "Isabelle", "Joshua", "Kyla",
            "Miguel", "Nicole", "Oscar", "Patricia", "Queenie", "Roberto", "Selena", "Tristan", "Ulysses", "Winona"],
        ["Katigbak", "Lacsamana", "Manalo", "Navarro", "Ocampo", "Panganiban", "Quizon", "Ramos", "Soriano", "Tolentino"],
        ["Diliman, Quezon City", "Cubao, Quezon City", "Fairview, Quezon City", "New Manila, Quezon City", "Novaliches, Quezon City",
            "Kamuning, Quezon City", "Tandang Sora, Quezon City", "Batasan Hills, Quezon City", "Project 4, Quezon City", "Loyola Heights, Quezon City"],
        3,
        "quezon"
    ),
    _ =>
    (
        ["Andrea", "Bianca", "Carlo", "Diana", "Enzo", "Faith", "Gian", "Hazel", "Ivan", "Janelle",
            "Kevin", "Lara", "Marco", "Nina", "Paolo", "Rina", "Samuel", "Trisha", "Vincent", "Yasmin"],
        ["Aquino", "Bautista", "Castillo", "Dela Cruz", "Evangelista", "Flores", "Gonzales", "Hernandez", "Ignacio", "Jimenez"],
        ["Poblacion, Makati", "Bel-Air, Makati", "San Lorenzo, Makati", "Legazpi Village, Makati", "Salcedo Village, Makati",
            "Bangkal, Makati", "Guadalupe Nuevo, Makati", "Olympia, Makati", "Palanan, Makati", "Tejeros, Makati"],
        1,
        "makati"
    )
};

static AppointmentRecord CreateDemoAppointment(int index, ClientRecord client)
{
    string[] services = ["Annual wellness exam", "Core vaccination", "Dermatology follow-up", "Dental assessment", "Senior care assessment", "Nail trim & wellness"];
    string[] veterinarians = ["Dr. Mia Garcia", "Dr. Luis Mendoza"];
    string[] times = ["08:30", "09:00", "10:30", "11:00", "13:00", "14:30", "15:00", "16:30"];
    var date = DateTime.Today.AddDays(index % 60 - 14);
    return new AppointmentRecord
    {
        Id = $"AP-DEMO-{index:0000}",
        Date = date,
        Time = times[(index - 1) % times.Length],
        ClientId = client.Id,
        ClientName = client.ClientName,
        PetName = client.PetName,
        Service = services[(index - 1) % services.Length],
        Veterinarian = veterinarians[(index - 1) % veterinarians.Length],
        Status = date.Date < DateTime.Today ? "Completed" : index % 3 == 0 ? "Confirmed" : "Scheduled",
        Notes = index % 5 == 0 ? "Demo record: client requested a reminder before the visit." : "Demo record for appointment workflow testing."
    };
}

static InquiryRecord CreateDemoInquiry(int index, ClientRecord client)
{
    string[] channels = ["Phone", "Website", "Messenger", "Email", "Walk-in", "SMS"];
    string[] categories = ["Appointment", "New client", "Post-visit care", "Billing", "Services & pricing", "Other"];
    string[] subjects = ["Ask about available appointment times", "Confirm vaccination requirements", "Request service price estimate",
        "Follow up after recent visit", "Ask about clinic preparation", "Update contact information"];
    var status = index % 5 == 0 ? "Resolved" : index % 3 == 0 ? "Follow-up" : "Open";
    return new InquiryRecord
    {
        Id = $"INQ-DEMO-{index:0000}",
        Created = DateTime.Today.AddDays(-(index % 30)),
        ClientName = client.ClientName,
        Contact = index % 2 == 0 ? client.Email : client.Phone,
        PetName = client.PetName,
        Channel = channels[(index - 1) % channels.Length],
        Category = categories[(index - 1) % categories.Length],
        Subject = subjects[(index - 1) % subjects.Length],
        Details = $"Demo inquiry for {client.PetName}. Client requested clear next steps and available clinic options.",
        Priority = index % 17 == 0 ? "High" : index % 4 == 0 ? "Low" : "Medium",
        Status = status,
        PreferredChannel = index % 2 == 0 ? "Email" : "SMS",
        CommunicationConsent = true,
        Owner = status == "Resolved" ? "Front Desk Team" : "Reception Team",
        FollowUpDate = status == "Resolved" ? null : DateTime.Today.AddDays(index % 7)
    };
}
