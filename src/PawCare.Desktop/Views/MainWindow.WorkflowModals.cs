using System.Runtime.InteropServices;
using System.Windows;
using PawCare.Desktop.Controls;
using PawCare.Desktop.Models;
using PawCare.Desktop.Services;

namespace PawCare.Desktop.Views;

public partial class MainWindow
{
    private void OpenClientRecordModal(ClientRecord client)
    {
        var visits = Data.Appointments
            .Where(x => x.ClientId == client.Id || Same(x.ClientName, client.ClientName) && Same(x.PetName, client.PetName))
            .OrderByDescending(x => x.Date)
            .Take(5)
            .Select(x => $"{x.Visit} - {x.Service} - {x.Status}")
            .ToArray();
        var clinical = Data.Consultations
            .Where(x => Same(x.ClientName, client.ClientName) && Same(x.PetName, client.PetName))
            .OrderByDescending(x => x.Date)
            .FirstOrDefault();

        var dialog = new RecordDialog(this, $"{client.PetName}'s patient record",
            "Owner contact, patient identity, and recent care context are shown together without changing the source record.",
            [
                new("owner", "Client / owner", FieldKind.ReadOnly, client.ClientName),
                new("patient", "Patient", FieldKind.ReadOnly, client.PetName),
                new("phone", "Phone", FieldKind.ReadOnly, EmptyLabel(client.Phone)),
                new("email", "Email", FieldKind.ReadOnly, EmptyLabel(client.Email)),
                new("species", "Species and breed", FieldKind.ReadOnly, $"{EmptyLabel(client.Species)} / {EmptyLabel(client.Breed)}"),
                new("sex", "Sex", FieldKind.ReadOnly, EmptyLabel(client.Sex)),
                new("birthday", "Birthday", FieldKind.ReadOnly, client.Birthday?.ToString("MMM d, yyyy") ?? "Not recorded"),
                new("lastVisit", "Last visit", FieldKind.ReadOnly, client.LastVisit?.ToString("MMM d, yyyy") ?? "No completed visit recorded"),
                new("visits", "Recent appointment history", FieldKind.ReadOnlyMultiline, visits.Length == 0 ? "No appointment history found." : string.Join(Environment.NewLine, visits), SpanColumns: true),
                new("clinical", "Latest clinical summary", FieldKind.ReadOnlyMultiline,
                    clinical is null ? "No consultation record found." : $"{clinical.Date:MMM d, yyyy} - {clinical.Summary}\nTreatment: {EmptyLabel(clinical.TreatmentPlan)}\nFollow-up: {EmptyLabel(clinical.FollowUpPlan)}", SpanColumns: true)
            ], "Close", showCancel: false);
        dialog.ShowModal();
    }

    private void OpenClientContactModal(ClientRecord? client)
    {
        if (client is null) { ShowSelectionRequired("client profile"); return; }
        var channels = ContactChannels(client.Email, client.Phone, string.Empty);
        if (channels.Count == 0)
        {
            ShowWorkflowNotice("Contact information needed", "This client profile has no usable email address or phone number. Ask the receptionist to update the profile first.");
            return;
        }

        var dialog = new RecordDialog(this, "Prepare client contact",
            "Choose a channel and review the message. Email opens a draft; phone and SMS copy a ready-to-use handoff because provider delivery is not connected yet.",
            [
                new("client", "Client and patient", FieldKind.ReadOnly, $"{client.ClientName} - {client.PetName}", SpanColumns: true),
                new("channel", "Contact channel", FieldKind.Choice, channels[0], channels),
                new("destination", "Available contact", FieldKind.ReadOnly, ContactSummary(client.Email, client.Phone, string.Empty)),
                new("subject", "Purpose", Value: $"PawCare follow-up for {client.PetName}", SpanColumns: true),
                new("message", "Message or call guide", FieldKind.Multiline,
                    $"Hello {client.ClientName}, this is {_session.Name} from PawCare. We are following up regarding {client.PetName}. Please let us know a convenient time to speak with you.", SpanColumns: true)
            ], "Prepare contact");
        if (dialog.ShowModal() != true) return;

        var channel = dialog.Text("channel");
        if (channel == "Email")
        {
            ReviewAndOpenDraft(new ClientEmailDraft(client.Email.Trim(), dialog.Text("subject"), dialog.Text("message")), "Review client email", $"Client follow-up - {client.ClientName}");
        }
        else if (!TryCopyContactHandoff(channel, client.Phone, dialog.Text("message"))) return;

        _store.AddActivity("message", "Client contact prepared", $"{client.ClientName} - {channel}");
        _store.Save();
        ShowWorkflowNotice("Contact prepared", channel == "Email"
            ? "The email draft was handed to the default email application for final review and sending."
            : $"The {channel.ToLowerInvariant()} contact details and message were copied. Provider delivery remains pending, so complete the contact in the appropriate application.");
    }

    private void OpenManagerInquiryModal(InquiryRecord inquiry)
    {
        var dialog = new RecordDialog(this, "Review inquiry follow-up",
            "Confirm priority, ownership, and the next promised response. The original conversation remains visible for context.",
            [
                new("subject", "Subject", FieldKind.ReadOnly, inquiry.Subject, SpanColumns: true),
                new("client", "Client and contact", FieldKind.ReadOnly, inquiry.ClientContact),
                new("patient", "Patient", FieldKind.ReadOnly, EmptyLabel(inquiry.PetName)),
                new("details", "Conversation record", FieldKind.ReadOnlyMultiline, inquiry.Details, SpanColumns: true),
                new("preferred", "Preferred response channel", FieldKind.ReadOnly, inquiry.PreferredChannel),
                new("consent", "Service follow-up consent", FieldKind.ReadOnly, inquiry.CommunicationConsent ? "Recorded" : "Not recorded"),
                new("priority", "Priority", FieldKind.Choice, inquiry.Priority, ["Low", "Medium", "High", "Urgent"]),
                new("status", "Status", FieldKind.Choice, inquiry.Status, ["New", "Open", "Follow-up", "Resolved", "Closed"]),
                new("owner", "Assigned owner", Value: inquiry.Owner, Help: "Name the person accountable for the next response."),
                new("followUp", "Next response date", FieldKind.Date, inquiry.FollowUpDate ?? DateTime.Today, Required: false),
                new("lastContact", "Last recorded contact", FieldKind.ReadOnly, inquiry.LastContact, Required: false)
            ], "Save review", ValidateManagerInquiry);
        if (dialog.ShowModal() != true) return;

        inquiry.Priority = dialog.Text("priority");
        inquiry.Status = dialog.Text("status");
        inquiry.Owner = dialog.Text("owner");
        inquiry.FollowUpDate = inquiry.Status is "Resolved" or "Closed" ? null : dialog.Date("followUp");
        PersistAndRefresh("inquiries", "message", "Inquiry follow-up reviewed", $"{inquiry.ClientName} - {inquiry.Status}");
    }

    private static string? ValidateManagerInquiry(IReadOnlyDictionary<string, object?> values)
    {
        var status = Convert.ToString(values["status"]) ?? string.Empty;
        var priority = Convert.ToString(values["priority"]) ?? string.Empty;
        var owner = Convert.ToString(values["owner"]) ?? string.Empty;
        var followUp = values["followUp"] as DateTime?;
        if (status is not ("Resolved" or "Closed") && string.IsNullOrWhiteSpace(owner))
            return "Assign an owner while this inquiry remains open.";
        if (status == "Follow-up" && followUp is null)
            return "Choose the next response date for a follow-up inquiry.";
        if (status is "New" or "Open" or "Follow-up" && followUp?.Date < DateTime.Today)
            return "Choose today or a future date for the next client response.";
        if (priority == "Urgent" && status is not ("Resolved" or "Closed") && followUp?.Date != DateTime.Today)
            return "Urgent inquiries require a response date of today.";
        return null;
    }

    private void OpenInquiryContactModal(InquiryRecord? inquiry)
    {
        if (inquiry is null) { ShowSelectionRequired("inquiry"); return; }
        if (!inquiry.CommunicationConsent)
        {
            ShowWorkflowNotice("Follow-up consent needed", "This inquiry does not contain recorded service follow-up consent. Ask the receptionist to confirm and record consent before contacting the client.");
            return;
        }
        var profile = Data.Clients.FirstOrDefault(x => Same(x.ClientName, inquiry.ClientName));
        var email = ClientEmailService.IsValidAddress(inquiry.Contact) ? inquiry.Contact : profile?.Email ?? string.Empty;
        var phone = inquiry.Contact.Count(char.IsDigit) >= 7 ? inquiry.Contact : profile?.Phone ?? string.Empty;
        var messenger = inquiry.PreferredChannel == "Messenger" ? inquiry.Contact : string.Empty;
        var channels = ContactChannels(email, phone, messenger);
        if (channels.Count == 0)
        {
            ShowWorkflowNotice("Contact information needed", "No usable email address, phone number, or Messenger contact was found for this inquiry.");
            return;
        }
        var preferred = channels.Contains(inquiry.PreferredChannel) ? inquiry.PreferredChannel : channels[0];
        var dialog = new RecordDialog(this, "Contact inquiry client",
            "Prepare the promised response and record its outcome. SMS and Messenger copy a handoff while provider integration remains pending.",
            [
                new("client", "Client", FieldKind.ReadOnly, inquiry.ClientName),
                new("subject", "Inquiry", FieldKind.ReadOnly, inquiry.Subject),
                new("channel", "Response channel", FieldKind.Choice, preferred, channels),
                new("destination", "Available contact", FieldKind.ReadOnly, ContactSummary(email, phone, messenger)),
                new("message", "Response", FieldKind.Multiline,
                    $"Hello {inquiry.ClientName}, this is {_session.Name} from PawCare following up about {inquiry.Subject}. {inquiry.Details}", SpanColumns: true),
                new("outcome", "Contact outcome", FieldKind.Choice, "Response prepared", ["Response prepared", "Reached - awaiting decision", "Reached - follow-up scheduled", "Unable to reach"]),
                new("followUp", "Next follow-up", FieldKind.Date, inquiry.FollowUpDate ?? DateTime.Today.AddDays(1), Required: false)
            ], "Record contact");
        if (dialog.ShowModal() != true) return;

        var channel = dialog.Text("channel");
        if (channel == "Email")
            ReviewAndOpenDraft(new ClientEmailDraft(email.Trim(), $"PawCare follow-up: {inquiry.Subject}", dialog.Text("message")), "Review inquiry email", $"Inquiry follow-up - {inquiry.ClientName}");
        else if (!TryCopyContactHandoff(channel, channel == "Messenger" ? messenger : phone, dialog.Text("message"))) return;

        inquiry.LastContactChannel = channel;
        inquiry.LastContactOutcome = dialog.Text("outcome");
        inquiry.LastContactUtc = DateTime.UtcNow;
        inquiry.Owner = string.IsNullOrWhiteSpace(inquiry.Owner) ? _session.Name : inquiry.Owner;
        inquiry.Status = inquiry.Status is "Resolved" or "Closed" ? inquiry.Status : "Follow-up";
        inquiry.FollowUpDate = inquiry.Status is "Resolved" or "Closed" ? null : dialog.Date("followUp");
        PersistAndRefresh("inquiries", "message", "Inquiry contact recorded", $"{inquiry.ClientName} - {channel} - {inquiry.LastContactOutcome}");
    }

    private void OpenFeedbackContactModal(FeedbackRecord? feedback)
    {
        if (feedback is null) { ShowSelectionRequired("client case"); return; }
        if (!feedback.ConsentToFollowUp)
        {
            ShowWorkflowNotice("Follow-up consent needed", "Open this case and record the client's consent for service follow-up before preparing contact.");
            return;
        }

        var profile = Data.Clients.FirstOrDefault(x => Same(x.ClientName, feedback.ClientName) && Same(x.PetName, feedback.PetName))
            ?? Data.Clients.FirstOrDefault(x => Same(x.ClientName, feedback.ClientName));
        var email = ClientEmailService.IsValidAddress(feedback.Contact) ? feedback.Contact : profile?.Email ?? string.Empty;
        var phone = feedback.Contact.Count(char.IsDigit) >= 7 ? feedback.Contact : profile?.Phone ?? string.Empty;
        var messenger = feedback.Channel == "Messenger" ? feedback.Contact : string.Empty;
        var channels = ContactChannels(email, phone, messenger);
        if (channels.Count == 0)
        {
            ShowWorkflowNotice("Contact information needed", "No usable email address, phone number, or Messenger contact was found. Update the client profile or case contact first.");
            return;
        }

        var message = feedback.Status == "Escalated"
            ? $"Hello {feedback.ClientName}, this is {_session.Name}, the manager at PawCare. I am following up personally regarding your {feedback.Category.ToLowerInvariant()} concern for {feedback.PetName}. We are reviewing the case and would like to discuss the next steps with you."
            : $"Hello {feedback.ClientName}, this is {_session.Name} from PawCare. Thank you for sharing feedback about {feedback.PetName}. We would like to follow up and make sure your concern is addressed.";
        var dialog = new RecordDialog(this, "Contact client about case",
            "Use the recorded consent and case context to prepare a clear response, then document the result and next commitment.",
            [
                new("case", "Case", FieldKind.ReadOnly, $"{feedback.CaseType} - {feedback.Category} - {feedback.Priority}"),
                new("client", "Client and patient", FieldKind.ReadOnly, $"{feedback.ClientName} - {feedback.PetName}"),
                new("channel", "Contact channel", FieldKind.Choice, channels[0], channels),
                new("destination", "Available contact", FieldKind.ReadOnly, ContactSummary(email, phone, messenger)),
                new("message", "Message or call guide", FieldKind.Multiline, message, SpanColumns: true),
                new("outcome", "Contact outcome", FieldKind.Choice, "Response prepared", ["Response prepared", "Reached - resolution explained", "Reached - follow-up scheduled", "Unable to reach"]),
                new("followUp", "Next follow-up", FieldKind.Date, feedback.FollowUpDate ?? DateTime.Today.AddDays(1), Required: false),
                new("notes", "Manager contact notes", FieldKind.Multiline, string.Empty, Required: false, Help: "Record facts discussed, commitments made, or why the client could not be reached.", SpanColumns: true)
            ], "Record contact", values => values["outcome"]?.ToString() == "Reached - follow-up scheduled" && values["followUp"] is null
                ? "Choose the date promised to the client." : null);
        if (dialog.ShowModal() != true) return;

        var channel = dialog.Text("channel");
        if (channel == "Email")
            ReviewAndOpenDraft(new ClientEmailDraft(email.Trim(), $"PawCare case follow-up: {feedback.PetName}", dialog.Text("message")), "Review case follow-up email", $"{feedback.CaseType} follow-up - {feedback.ClientName}");
        else if (!TryCopyContactHandoff(channel, channel == "Messenger" ? messenger : phone, dialog.Text("message"))) return;

        feedback.LastContactChannel = channel;
        feedback.LastContactOutcome = dialog.Text("outcome");
        feedback.LastContactUtc = DateTime.UtcNow;
        feedback.Owner = string.IsNullOrWhiteSpace(feedback.Owner) ? _session.Name : feedback.Owner;
        feedback.FollowUpDate = dialog.Date("followUp");
        if (feedback.Escalation != "None" && feedback.Status == "Open") feedback.Status = "Escalated";
        var contactNote = $"[{DateTime.Now:MMM d, yyyy h:mm tt}] {channel} - {feedback.LastContactOutcome}. {dialog.Text("notes")}".Trim();
        feedback.ResolutionNotes = string.IsNullOrWhiteSpace(feedback.ResolutionNotes) ? contactNote : $"{feedback.ResolutionNotes}{Environment.NewLine}{contactNote}";
        PersistAndRefresh("feedback", "message", "Client contact recorded", $"{feedback.ClientName} - {channel} - {feedback.LastContactOutcome}");
    }

    private bool TryCopyContactHandoff(string channel, string destination, string message)
    {
        try
        {
            Clipboard.SetText($"{channel}: {destination}{Environment.NewLine}{Environment.NewLine}{message}");
            return true;
        }
        catch (ExternalException)
        {
            ShowWorkflowNotice("Clipboard unavailable", "PawCare could not copy the contact handoff. Keep this modal open and copy the destination and message manually.");
            return false;
        }
    }

    private void ShowWorkflowNotice(string title, string message)
    {
        var dialog = new RecordDialog(this, title, "PawCare kept your current workspace and record selection unchanged.",
            [new("message", "Details", FieldKind.ReadOnlyMultiline, message, SpanColumns: true)], "Close", showCancel: false);
        dialog.ShowModal();
    }

    private static List<string> ContactChannels(string email, string phone, string messenger)
    {
        var result = new List<string>();
        if (ClientEmailService.IsValidAddress(email)) result.Add("Email");
        if (phone.Count(char.IsDigit) >= 7) { result.Add("Phone"); result.Add("SMS"); }
        if (!string.IsNullOrWhiteSpace(messenger)) result.Add("Messenger");
        return result.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static string ContactSummary(string email, string phone, string messenger)
    {
        var values = new List<string>();
        if (ClientEmailService.IsValidAddress(email)) values.Add($"Email: {email.Trim()}");
        if (phone.Count(char.IsDigit) >= 7) values.Add($"Phone: {phone.Trim()}");
        if (!string.IsNullOrWhiteSpace(messenger)) values.Add($"Messenger: {messenger.Trim()}");
        return values.Count == 0 ? "No usable contact recorded" : string.Join(" | ", values);
    }

    private static string EmptyLabel(string? value) => string.IsNullOrWhiteSpace(value) ? "Not recorded" : value.Trim();
}
