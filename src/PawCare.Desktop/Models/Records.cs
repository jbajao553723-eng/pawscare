using System.Collections.ObjectModel;

namespace PawCare.Desktop.Models;

public enum UserRole
{
    Receptionist,
    Manager,
    Veterinarian,
    Admin,
    SuperAdmin
}

public static class RoleAccessPolicy
{
    public static bool CanViewBusinessIntelligence(UserRole role) =>
        role is UserRole.Manager or UserRole.Admin;
}

[Flags]
public enum SubscriptionFeatures
{
    None = 0,
    Collection = 1,
    Clients = 2,
    Appointments = 4,
    Reminders = 8,
    Promotions = 16,
    Clinical = 32,
    Inventory = 64,
    Reports = 128,
    Branching = 256,
    Backup = 512,
    Satisfaction = 1024,
    IncrementalSync = 2048,
    All = Collection | Clients | Appointments | Reminders | Promotions | Clinical | Inventory | Reports | Branching | Backup | Satisfaction | IncrementalSync
}

public sealed class SubscriptionPlanRecord
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public SubscriptionFeatures Features { get; set; }
    public int MaxBranches { get; set; } = 1;
    public bool IsActive { get; set; } = true;
    public string Modules => Features.ToString();
    public string Status => IsActive ? "Active" : "Inactive";
}

public sealed record UserSession(
    UserRole Role,
    string Name,
    string Initials,
    string Title,
    string UserId = "",
    string TenantId = "",
    string TenantName = "",
    string BranchId = "",
    string BranchName = "");

public sealed class DataConcurrencyException(string message) : Exception(message);

public sealed class TenantRecord
{
    public string Id { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string DatabasePath { get; set; } = string.Empty;
    public string SyncEndpoint { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public string SubscriptionPlanCode { get; set; } = "FULL";
    public string SubscriptionPlanName { get; set; } = "Full";
    public SubscriptionFeatures SubscriptionFeatures { get; set; } = SubscriptionFeatures.All;
    public int MaxBranches { get; set; } = 1;
    public string Status => IsActive ? "Active" : "Inactive";
}

public sealed class BranchRecord
{
    public string Id { get; set; } = string.Empty;
    public string TenantId { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string DatabasePath { get; set; } = string.Empty;
    public bool IsPrimary { get; set; }
    public bool IsActive { get; set; }
    public string Status => IsActive ? "Active" : "Inactive";
}

public sealed class BranchDirectoryRecord
{
    public required TenantRecord Tenant { get; init; }
    public required BranchRecord Branch { get; init; }
    public string TenantId => Tenant.Id;
    public string Organization => $"{Tenant.Code} · {Tenant.Name}";
    public string Plan => Tenant.SubscriptionPlanName;
    public string Code => Branch.Code;
    public string Name => Branch.Name;
    public string DatabasePath => Branch.DatabasePath;
    public string Type => Branch.IsPrimary ? "Primary" : "Additional";
    public string Status => Branch.Status;
}

public sealed class StaffAccountRecord
{
    public string UserId { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string Initials { get; set; } = string.Empty;
    public UserRole Role { get; set; }
    public string TenantId { get; set; } = string.Empty;
    public string TenantName { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public long Version { get; set; }
    public string RoleLabel => Role == UserRole.SuperAdmin ? "Super Admin" : Role.ToString();
    public string Status => IsActive ? "Active" : "Inactive";
}

public sealed class AuditLogRecord
{
    public long Id { get; set; }
    public DateTime OccurredUtc { get; set; }
    public string ActorUsername { get; set; } = string.Empty;
    public string TenantId { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string EntityType { get; set; } = string.Empty;
    public string EntityId { get; set; } = string.Empty;
    public string Details { get; set; } = string.Empty;
    public string Occurred => OccurredUtc.ToLocalTime().ToString("MMM d, yyyy h:mm tt");
}

public sealed record SyncStatus(int PendingEvents, DateTime? LastIncrementalSyncUtc, DateTime? LastFullBackupUtc, string State)
{
    public string LastIncremental => LastIncrementalSyncUtc?.ToLocalTime().ToString("MMM d, yyyy h:mm tt") ?? "Never";
    public string LastBackup => LastFullBackupUtc?.ToLocalTime().ToString("MMM d, yyyy h:mm tt") ?? "Never";
}

public sealed class ClientRecord
{
    public string Id { get; set; } = string.Empty;
    public string ClientName { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public bool MarketingConsent { get; set; }
    public DateTime? LastVisit { get; set; }
    public string PetName { get; set; } = string.Empty;
    public string Species { get; set; } = string.Empty;
    public string Breed { get; set; } = string.Empty;
    public string Sex { get; set; } = string.Empty;
    public DateTime? Birthday { get; set; }
    public string Patient => $"{PetName} · {Breed}";
    public string ConsentLabel => MarketingConsent ? "Opted in" : "No consent";
    public string Display => $"{ClientName} — {PetName}";
}

public sealed class AppointmentRecord
{
    public string Id { get; set; } = string.Empty;
    public DateTime Date { get; set; } = DateTime.Today;
    public string Time { get; set; } = "09:00";
    public string ClientId { get; set; } = string.Empty;
    public string ClientName { get; set; } = string.Empty;
    public string PetName { get; set; } = string.Empty;
    public string Service { get; set; } = string.Empty;
    public string Veterinarian { get; set; } = string.Empty;
    public string Status { get; set; } = "Scheduled";
    public string Notes { get; set; } = string.Empty;
    public string Visit => $"{Date:MMM d, yyyy} · {FormatTime(Time)}";
    public string ClientPatient => $"{PetName} · {ClientName}";

    private static string FormatTime(string value) =>
        TimeSpan.TryParse(value, out var time) ? DateTime.Today.Add(time).ToString("h:mm tt") : value;
}

public sealed class InquiryRecord
{
    public string Id { get; set; } = string.Empty;
    public DateTime Created { get; set; } = DateTime.Today;
    public string ClientName { get; set; } = string.Empty;
    public string Contact { get; set; } = string.Empty;
    public string PetName { get; set; } = string.Empty;
    public string Channel { get; set; } = "Phone";
    public string Category { get; set; } = "Appointment";
    public string Subject { get; set; } = string.Empty;
    public string Details { get; set; } = string.Empty;
    public string Priority { get; set; } = "Medium";
    public string Status { get; set; } = "New";
    public string PreferredChannel { get; set; } = "SMS";
    public bool CommunicationConsent { get; set; }
    public string Owner { get; set; } = string.Empty;
    public DateTime? FollowUpDate { get; set; }
    public string LastContactChannel { get; set; } = string.Empty;
    public string LastContactOutcome { get; set; } = string.Empty;
    public DateTime? LastContactUtc { get; set; }
    public string ClientContact => $"{ClientName} · {Contact}";
    public string Source => $"{Channel} · prefers {PreferredChannel}";
    public string FollowUp => FollowUpDate?.ToString("MMM d, yyyy") ?? "—";
    public string LastContact => LastContactUtc?.ToLocalTime().ToString("MMM d, yyyy h:mm tt") ?? "Not contacted";
}

public sealed class ReminderRecord
{
    public string Id { get; set; } = string.Empty;
    public string ClientName { get; set; } = string.Empty;
    public string PetName { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string Channel { get; set; } = "SMS";
    public DateTime DueDate { get; set; } = DateTime.Today.AddDays(1);
    public string Status { get; set; } = "Scheduled";
    public string Message { get; set; } = string.Empty;
    public string ClientPatient => $"{ClientName} · {PetName}";
}

public sealed class PromotionRecord
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string OfferValue { get; set; } = string.Empty;
    public string Audience { get; set; } = string.Empty;
    public string Channel { get; set; } = string.Empty;
    public DateTime StartDate { get; set; } = DateTime.Today;
    public DateTime EndDate { get; set; } = DateTime.Today.AddDays(30);
    public string Status { get; set; } = "Draft";
    public string Approval { get; set; } = "Needs review";
    public string SubmittedBy { get; set; } = string.Empty;
    public DateTime? SubmittedUtc { get; set; }
    public string ApprovedBy { get; set; } = string.Empty;
    public DateTime? ApprovedUtc { get; set; }
    public string RejectionReason { get; set; } = string.Empty;
    public DateTime? LaunchedUtc { get; set; }
    public DateTime? CompletedUtc { get; set; }
    public string Goal { get; set; } = string.Empty;
    public int Reach { get; set; }
    public int Claims { get; set; }
    public int Conversion { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public List<string> ContactedClientIds { get; set; } = [];
    public string Schedule => $"{StartDate:MMM d} – {EndDate:MMM d, yyyy}";
    public string Performance => $"{Reach} reached · {Claims} claims · {Conversion}%";
    public string ReviewStatus => Approval switch
    {
        "Approved" when !string.IsNullOrWhiteSpace(ApprovedBy) => $"Approved by {ApprovedBy}",
        "Rejected" when !string.IsNullOrWhiteSpace(RejectionReason) => $"Rejected: {RejectionReason}",
        "Needs review" when !string.IsNullOrWhiteSpace(SubmittedBy) => $"Submitted by {SubmittedBy}",
        _ => Approval
    };
}

public sealed class FeedbackRecord
{
    public string Id { get; set; } = string.Empty;
    public DateTime Date { get; set; } = DateTime.Today;
    public string ClientName { get; set; } = string.Empty;
    public string Contact { get; set; } = string.Empty;
    public string PetName { get; set; } = string.Empty;
    public string CaseType { get; set; } = "Feedback";
    public string Channel { get; set; } = "Phone";
    public int Rating { get; set; } = 5;
    public string Category { get; set; } = string.Empty;
    public string Priority { get; set; } = "Medium";
    public string Comment { get; set; } = string.Empty;
    public string Status { get; set; } = "Open";
    public string Escalation { get; set; } = "None";
    public bool ConsentToFollowUp { get; set; }
    public string Owner { get; set; } = string.Empty;
    public DateTime? FollowUpDate { get; set; }
    public string ResolutionNotes { get; set; } = string.Empty;
    public string LastContactChannel { get; set; } = string.Empty;
    public string LastContactOutcome { get; set; } = string.Empty;
    public DateTime? LastContactUtc { get; set; }
    public string ClientPatient => $"{ClientName} · {PetName}";
    public string ClientContact => $"{ClientName} · {Contact}";
    public string FollowUp => FollowUpDate?.ToString("MMM d, yyyy") ?? "—";
    public string LastContact => LastContactUtc?.ToLocalTime().ToString("MMM d, yyyy h:mm tt") ?? "Not contacted";
    public string Stars => new string('★', Rating) + new string('☆', 5 - Rating);
}

public sealed class ActivityRecord
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Icon { get; set; } = "●";
    public string Title { get; set; } = string.Empty;
    public string Detail { get; set; } = string.Empty;
    public string Time { get; set; } = string.Empty;
}

public sealed class ConsultationRecord
{
    public string Id { get; set; } = string.Empty;
    public string AppointmentId { get; set; } = string.Empty;
    public DateTime Date { get; set; } = DateTime.Today;
    public string ClientName { get; set; } = string.Empty;
    public string PetName { get; set; } = string.Empty;
    public string Veterinarian { get; set; } = string.Empty;
    public string ChiefComplaint { get; set; } = string.Empty;
    public string Vitals { get; set; } = string.Empty;
    public string Assessment { get; set; } = string.Empty;
    public string TreatmentPlan { get; set; } = string.Empty;
    public string Prescriptions { get; set; } = string.Empty;
    public string Vaccinations { get; set; } = string.Empty;
    public string MedicalNotes { get; set; } = string.Empty;
    public string FollowUpPlan { get; set; } = string.Empty;
    public string ClientInstructions { get; set; } = string.Empty;
    public string Status { get; set; } = "Draft";
    public string PatientVisit => $"{PetName} · {ClientName}";
    public string Summary => string.IsNullOrWhiteSpace(Assessment) ? "Assessment pending" : Assessment;
}

public sealed class InventoryRecord
{
    public string Id { get; set; } = string.Empty;
    public string ItemName { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public int ReorderLevel { get; set; }
    public string Unit { get; set; } = string.Empty;
    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
    public string StockStatus => Quantity <= ReorderLevel ? "Reorder" : "In stock";
    public string Updated => UpdatedUtc.ToLocalTime().ToString("MMM d, h:mm tt");
}

public sealed class ClinicData
{
    public ObservableCollection<ClientRecord> Clients { get; set; } = [];
    public ObservableCollection<AppointmentRecord> Appointments { get; set; } = [];
    public ObservableCollection<InquiryRecord> Inquiries { get; set; } = [];
    public ObservableCollection<ReminderRecord> Reminders { get; set; } = [];
    public ObservableCollection<PromotionRecord> Promotions { get; set; } = [];
    public ObservableCollection<FeedbackRecord> Feedback { get; set; } = [];
    public ObservableCollection<ConsultationRecord> Consultations { get; set; } = [];
    public ObservableCollection<InventoryRecord> Inventory { get; set; } = [];
    public ObservableCollection<ActivityRecord> Activities { get; set; } = [];
}
