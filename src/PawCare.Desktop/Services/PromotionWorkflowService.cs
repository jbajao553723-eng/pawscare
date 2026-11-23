using PawCare.Desktop.Models;

namespace PawCare.Desktop.Services;

public static class PromotionWorkflowService
{
    private static readonly HashSet<string> Audiences =
    [
        "New puppy leads", "Senior pets (7+ years)", "Pets due for dental care",
        "Inactive clients (6+ months)", "All consented clients"
    ];

    private static readonly HashSet<string> Channels = ["Email", "SMS", "SMS + Email"];

    public static string? ValidateForSubmission(PromotionRecord promotion, IEnumerable<PromotionRecord> campaigns, DateTime today)
    {
        if (string.IsNullOrWhiteSpace(promotion.Name)) return "Enter a campaign name.";
        if (promotion.Name.Trim().Length > 120) return "Keep the campaign name to 120 characters or fewer.";
        if (string.IsNullOrWhiteSpace(promotion.Code)) return "Enter an offer code.";
        if (promotion.Code.Length is < 3 or > 24 || promotion.Code.Any(ch => !char.IsAsciiLetterOrDigit(ch) && ch is not '-' and not '_'))
            return "Use 3–24 letters, numbers, hyphens, or underscores for the offer code.";
        if (campaigns.Any(x => x.Id != promotion.Id && string.Equals(x.Code, promotion.Code, StringComparison.OrdinalIgnoreCase)))
            return $"Offer code {promotion.Code} is already used by another campaign.";
        if (string.IsNullOrWhiteSpace(promotion.OfferValue)) return "Describe the offer value, such as 20% off or ₱500 off.";
        if (!Audiences.Contains(promotion.Audience)) return "Choose a supported audience segment.";
        if (!Channels.Contains(promotion.Channel)) return "Choose Email, SMS, or SMS + Email as the delivery channel.";
        if (promotion.EndDate.Date < promotion.StartDate.Date) return "The end date must be on or after the start date.";
        if (promotion.EndDate.Date < today.Date) return "Move the campaign end date to today or a future date.";
        if (string.IsNullOrWhiteSpace(promotion.Goal)) return "Add a measurable campaign goal before submission.";
        if (promotion.Goal.Trim().Length > 600) return "Keep the campaign goal to 600 characters or fewer.";
        return null;
    }

    public static string? ValidateForPersistence(PromotionRecord promotion)
    {
        if (promotion.Reach < 0 || promotion.Claims < 0) return "Campaign reach and claims cannot be negative.";
        if (promotion.Claims > promotion.Reach) return "Campaign claims cannot exceed recorded reach.";
        var expectedConversion = promotion.Reach == 0 ? 0 : (int)Math.Round(promotion.Claims * 100d / promotion.Reach);
        if (promotion.Conversion != expectedConversion) return "Campaign conversion must match its recorded reach and claims.";
        if (promotion.Approval is not ("Draft" or "Needs review" or "Approved" or "Rejected")) return "The campaign has an invalid approval state.";
        if (promotion.Status is not ("Draft" or "Scheduled" or "Active" or "Completed")) return "The campaign has an invalid delivery status.";
        if (promotion.Approval != "Approved" && promotion.Status != "Draft") return "Only an approved campaign can be scheduled, activated, or completed.";
        if (promotion.Status is "Scheduled" or "Active" or "Completed" && promotion.Approval != "Approved") return "Approve the campaign before changing its delivery status.";
        if (promotion.Approval == "Rejected" && string.IsNullOrWhiteSpace(promotion.RejectionReason)) return "A rejected campaign needs a reason.";
        if (promotion.Approval == "Approved" && (string.IsNullOrWhiteSpace(promotion.ApprovedBy) || promotion.ApprovedUtc is null)) return "An approved campaign needs approver details.";
        if (promotion.EndDate.Date < promotion.StartDate.Date) return "The campaign schedule is invalid.";
        if (promotion.ContactedClientIds is not null && promotion.ContactedClientIds.Count != promotion.ContactedClientIds.Distinct(StringComparer.Ordinal).Count())
            return "The campaign contains duplicate delivery recipients.";
        return null;
    }

    public static void Submit(PromotionRecord promotion, IEnumerable<PromotionRecord> campaigns, string actorName, DateTime now)
    {
        var error = ValidateForSubmission(promotion, campaigns, now.Date);
        if (error is not null) throw new InvalidOperationException(error);
        if (promotion.Approval == "Needs review") throw new InvalidOperationException("This campaign is already waiting for review.");
        if (promotion.Approval == "Approved") throw new InvalidOperationException("This campaign is already approved. Revise its terms before submitting it again.");
        promotion.Approval = "Needs review";
        promotion.Status = "Draft";
        promotion.SubmittedBy = actorName;
        promotion.SubmittedUtc = now.ToUniversalTime();
        promotion.ApprovedBy = string.Empty;
        promotion.ApprovedUtc = null;
        promotion.RejectionReason = string.Empty;
    }

    public static void Approve(PromotionRecord promotion, IEnumerable<PromotionRecord> campaigns, string actorName, DateTime now)
    {
        if (promotion.Approval != "Needs review") throw new InvalidOperationException("Only a campaign waiting for review can be approved.");
        var error = ValidateForSubmission(promotion, campaigns, now.Date);
        if (error is not null) throw new InvalidOperationException(error);
        promotion.Approval = "Approved";
        promotion.ApprovedBy = actorName;
        promotion.ApprovedUtc = now.ToUniversalTime();
        promotion.RejectionReason = string.Empty;
        promotion.Status = promotion.StartDate.Date <= now.Date ? "Active" : "Scheduled";
        if (promotion.Status == "Active") promotion.LaunchedUtc ??= now.ToUniversalTime();
    }

    public static void Reject(PromotionRecord promotion, string actorName, string reason, DateTime now)
    {
        if (promotion.Approval != "Needs review") throw new InvalidOperationException("Only a campaign waiting for review can be rejected.");
        if (string.IsNullOrWhiteSpace(reason)) throw new InvalidOperationException("Enter the changes needed before rejecting this campaign.");
        promotion.Approval = "Rejected";
        promotion.Status = "Draft";
        promotion.ApprovedBy = actorName;
        promotion.ApprovedUtc = now.ToUniversalTime();
        promotion.RejectionReason = reason.Trim();
    }

    public static void ResetAfterMaterialEdit(PromotionRecord promotion)
    {
        promotion.Approval = "Draft";
        promotion.Status = "Draft";
        promotion.SubmittedBy = string.Empty;
        promotion.SubmittedUtc = null;
        promotion.ApprovedBy = string.Empty;
        promotion.ApprovedUtc = null;
        promotion.RejectionReason = string.Empty;
        promotion.LaunchedUtc = null;
        promotion.CompletedUtc = null;
    }

    public static void Start(PromotionRecord promotion, DateTime now)
    {
        if (promotion.Approval != "Approved") throw new InvalidOperationException("Approve this campaign before starting it.");
        if (promotion.Status == "Completed") throw new InvalidOperationException("A completed campaign cannot be restarted.");
        if (promotion.Status == "Active") throw new InvalidOperationException("This campaign is already active.");
        if (now.Date < promotion.StartDate.Date) throw new InvalidOperationException($"This campaign starts on {promotion.StartDate:MMMM d, yyyy}.");
        if (now.Date > promotion.EndDate.Date) throw new InvalidOperationException("This campaign has already passed its end date. Complete it or revise and resubmit it.");
        promotion.Status = "Active";
        promotion.LaunchedUtc ??= now.ToUniversalTime();
    }

    public static void Complete(PromotionRecord promotion, DateTime now)
    {
        if (promotion.Approval != "Approved" || promotion.Status != "Active") throw new InvalidOperationException("Only an active, approved campaign can be completed.");
        promotion.Status = "Completed";
        promotion.CompletedUtc = now.ToUniversalTime();
    }

    public static void RecordReach(PromotionRecord promotion, int count)
    {
        RequirePerformanceState(promotion);
        if (count <= 0) throw new InvalidOperationException("Enter a reach greater than zero.");
        promotion.Reach = checked(promotion.Reach + count);
        UpdateConversion(promotion);
    }

    public static void RecordClaims(PromotionRecord promotion, int count)
    {
        RequirePerformanceState(promotion);
        if (count <= 0) throw new InvalidOperationException("Enter a claim count greater than zero.");
        if (promotion.Claims + count > promotion.Reach) throw new InvalidOperationException("Record campaign reach before adding these claims.");
        promotion.Claims = checked(promotion.Claims + count);
        UpdateConversion(promotion);
    }

    public static int RecordContactedClients(PromotionRecord promotion, IEnumerable<string> clientIds)
    {
        RequirePerformanceState(promotion);
        promotion.ContactedClientIds ??= [];
        var existing = new HashSet<string>(promotion.ContactedClientIds, StringComparer.Ordinal);
        var added = clientIds.Where(x => !string.IsNullOrWhiteSpace(x) && existing.Add(x)).ToArray();
        promotion.ContactedClientIds.AddRange(added);
        promotion.Reach = checked(promotion.Reach + added.Length);
        UpdateConversion(promotion);
        return added.Length;
    }

    public static IReadOnlyList<ClientRecord> MatchingAudience(ClinicData data, PromotionRecord promotion) => data.Clients
        .Where(x => x.MarketingConsent && MatchesAudience(data, x, promotion))
        .OrderBy(x => x.ClientName).ThenBy(x => x.PetName).ToArray();

    public static bool MatchesAudience(ClinicData data, ClientRecord client, PromotionRecord promotion) => promotion.Audience switch
    {
        "All consented clients" => true,
        "New puppy leads" => client.Species == "Dog" && client.Birthday >= DateTime.Today.AddYears(-1),
        "Senior pets (7+ years)" => client.Birthday <= DateTime.Today.AddYears(-7),
        "Pets due for dental care" => data.Appointments.Any(x => x.ClientId == client.Id && x.Service.Contains("Dental", StringComparison.OrdinalIgnoreCase)),
        "Inactive clients (6+ months)" => client.LastVisit is null || client.LastVisit <= DateTime.Today.AddMonths(-6),
        _ => false
    };

    public static bool CanDeliver(PromotionRecord promotion, DateTime today) =>
        promotion.Approval == "Approved" && promotion.Status == "Active" &&
        today.Date >= promotion.StartDate.Date && today.Date <= promotion.EndDate.Date;

    private static void RequirePerformanceState(PromotionRecord promotion)
    {
        if (promotion.Approval != "Approved" || promotion.Status is not ("Active" or "Completed"))
            throw new InvalidOperationException("Campaign performance can be recorded only after an approved campaign starts.");
    }

    private static void UpdateConversion(PromotionRecord promotion) =>
        promotion.Conversion = promotion.Reach == 0 ? 0 : (int)Math.Round(promotion.Claims * 100d / promotion.Reach);
}
