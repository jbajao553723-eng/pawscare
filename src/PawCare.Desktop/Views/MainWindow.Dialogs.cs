using PawCare.Desktop.Controls;
using PawCare.Desktop.Models;
using PawCare.Desktop.Services;
using System.Net.Mail;

namespace PawCare.Desktop.Views;

public partial class MainWindow
{
    private void OpenClientDialog(ClientRecord? existing = null)
    {
        if (!RequireSubscribed("clients")) return;
        var dialog = new RecordDialog(this, existing is null ? "Add client & patient" : "Client & patient profile",
            "Contact and patient details stay connected in one care record.",
            [
                new("client", "Client name", Value: existing?.ClientName),
                new("phone", "Mobile number", Value: existing?.Phone),
                new("email", "Email address", Value: existing?.Email),
                new("address", "City / address", Value: existing?.Address),
                new("pet", "Patient name", Value: existing?.PetName),
                new("species", "Species", FieldKind.Choice, existing?.Species ?? "Dog", ["Dog", "Cat", "Rabbit", "Bird", "Other"]),
                new("breed", "Breed", Value: existing?.Breed),
                new("sex", "Sex", FieldKind.Choice, existing?.Sex ?? "Male", ["Male", "Female", "Unknown"]),
                new("birthday", "Birthday", FieldKind.Date, existing?.Birthday, Required: false),
                new("consent", "Client consents to relevant clinic reminders and promotional communications.", FieldKind.CheckBox, existing?.MarketingConsent ?? false, Required: false, SpanColumns: true)
            ]);
        if (dialog.ShowModal() != true) return;

        var record = existing ?? new ClientRecord { Id = _store.NextId("CL", 1050, Data.Clients.Count) };
        record.ClientName = dialog.Text("client"); record.Phone = dialog.Text("phone"); record.Email = dialog.Text("email"); record.Address = dialog.Text("address");
        record.PetName = dialog.Text("pet"); record.Species = dialog.Text("species"); record.Breed = dialog.Text("breed"); record.Sex = dialog.Text("sex");
        record.Birthday = dialog.Date("birthday"); record.MarketingConsent = dialog.Bool("consent");
        if (existing is null) Data.Clients.Insert(0, record);
        PersistAndRefresh("clients", "●", existing is null ? "New client added" : "Client profile updated", $"{record.ClientName} · {record.PetName}");
    }

    private void OpenAppointmentDialog(AppointmentRecord? existing = null)
    {
        if (!RequireSubscribed("appointments")) return;
        if (_session.Role is not (UserRole.Receptionist or UserRole.Admin)) return;
        var clientOptions = Data.Clients.Select(x => x.Display).ToArray();
        if (clientOptions.Length == 0)
        {
            System.Windows.MessageBox.Show(this, "Add a client and patient profile before booking an appointment.", "Client profile needed", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
            return;
        }
        var selectedDisplay = existing is null ? clientOptions.FirstOrDefault() : Data.Clients.FirstOrDefault(x => x.Id == existing.ClientId)?.Display;
        var veterinarianOptions = _master.GetStaff(_session.TenantId, UserRole.Veterinarian)
            .Where(x => x.IsActive)
            .Select(x => x.FullName.Trim())
            .Append(existing?.Veterinarian ?? string.Empty)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (veterinarianOptions.Length == 0)
        {
            System.Windows.MessageBox.Show(this, "Add or activate a veterinarian account before booking an appointment.", "Veterinarian needed", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
            return;
        }
        var dialog = new RecordDialog(this, existing is null ? "Book appointment" : "Update appointment",
            "Record the visit details needed by the front desk and care team.",
            [
                new("client", "Client & patient", FieldKind.Choice, selectedDisplay, clientOptions),
                new("service", "Service", FieldKind.Choice, existing?.Service ?? "Annual wellness exam", ["Annual wellness exam", "Core vaccination", "Dermatology follow-up", "Dental assessment", "Senior care assessment", "Nail trim & wellness"]),
                new("date", "Date", FieldKind.Date, existing?.Date ?? DateTime.Today),
                new("time", "Appointment time", FieldKind.Time12, existing?.Time ?? "09:00", Help: "Choose the hour, minutes, and AM or PM."),
                new("vet", "Veterinarian", FieldKind.Choice, existing?.Veterinarian ?? veterinarianOptions[0], veterinarianOptions),
                new("status", "Status", FieldKind.Choice, existing?.Status ?? "Scheduled", ["Scheduled", "Confirmed", "Checked in", "Pending", "Completed", "Cancelled"]),
                new("notes", "Appointment notes", FieldKind.Multiline, existing?.Notes, Required: false, SpanColumns: true)
            ]);
        if (dialog.ShowModal() != true) return;

        var client = Data.Clients.First(x => x.Display == dialog.Text("client"));
        var record = existing ?? new AppointmentRecord { Id = _store.NextId("AP", 2093, Data.Appointments.Count) };
        record.ClientId = client.Id; record.ClientName = client.ClientName; record.PetName = client.PetName; record.Service = dialog.Text("service");
        record.Date = dialog.Date("date") ?? DateTime.Today; record.Time = dialog.Text("time"); record.Veterinarian = dialog.Text("vet");
        record.Status = dialog.Text("status"); record.Notes = dialog.Text("notes");
        if (existing is null) Data.Appointments.Add(record);
        PersistAndRefresh("appointments", "▣", existing is null ? "Appointment booked" : "Appointment updated", $"{record.PetName} · {record.Service}");
    }

    private void OpenInquiryDialog(InquiryRecord? existing = null)
    {
        if (!RequireSubscribed("inquiries")) return;
        if (_session.Role != UserRole.Receptionist) return;
        var dialog = new RecordDialog(this, existing is null ? "Log client inquiry" : "Inquiry record",
            "Structured fields turn every conversation into useful follow-up and campaign data.",
            [
                new("client", "Client or lead name", Value: existing?.ClientName, Help: "Use the registered client name when available, or enter the caller's name for a new lead."),
                new("contact", "Contact detail", Value: existing?.Contact, Help: "Enter the phone number, email address, or Messenger name used for the response."),
                new("pet", "Patient name", Value: existing?.PetName, Required: false, Help: "Optional when the caller has not registered a patient yet."),
                new("channel", "Inquiry channel", FieldKind.Choice, existing?.Channel ?? "Phone", ["Phone", "Website", "Messenger", "Email", "Walk-in", "SMS"]),
                new("category", "Category", FieldKind.Choice, existing?.Category ?? "Appointment", ["Appointment", "New client", "Post-visit care", "Billing", "Services & pricing", "Promotion", "Other"]),
                new("priority", "Priority", FieldKind.Choice, existing?.Priority ?? "Medium", ["Low", "Medium", "High", "Urgent"], Help: "Urgent concerns should be escalated to the clinical team."),
                new("subject", "Subject", Value: existing?.Subject, SpanColumns: true),
                new("details", "Conversation details", FieldKind.Multiline, existing?.Details, Help: "Capture the question, guidance given, and any promise made.", SpanColumns: true),
                new("preferred", "Preferred response channel", FieldKind.Choice, existing?.PreferredChannel ?? "SMS", ["Phone", "SMS", "Email", "Messenger"]),
                new("followUp", "Follow-up date", FieldKind.Date, existing?.FollowUpDate ?? DateTime.Today.AddDays(1), Required: false, Help: "Required for Follow-up status. Urgent open inquiries must be followed up today."),
                new("status", "Status", FieldKind.Choice, existing?.Status ?? "New", ["New", "Open", "Follow-up", "Resolved", "Closed"]),
                new("owner", "Assigned owner", Value: existing?.Owner ?? _session.Name),
                new("consent", "Client consented to receive service follow-up messages on the preferred response channel.", FieldKind.CheckBox, existing?.CommunicationConsent ?? false, Required: false, SpanColumns: true)
            ], validate: ValidateInquiry);
        if (dialog.ShowModal() != true) return;

        var duplicate = Data.Inquiries.FirstOrDefault(x => x != existing && x.Status is not "Resolved" and not "Closed" &&
            string.Equals(x.Contact.Trim(), dialog.Text("contact").Trim(), StringComparison.OrdinalIgnoreCase) &&
            string.Equals(x.Subject.Trim(), dialog.Text("subject").Trim(), StringComparison.OrdinalIgnoreCase));
        if (duplicate is not null && System.Windows.MessageBox.Show(this,
            $"A similar open inquiry already exists ({duplicate.Id}). Save another record anyway?",
            "Possible duplicate inquiry", System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Question) != System.Windows.MessageBoxResult.Yes)
            return;

        var record = existing ?? new InquiryRecord { Id = _store.NextId("INQ", 3022, Data.Inquiries.Count), Created = DateTime.Today };
        record.ClientName = dialog.Text("client"); record.Contact = dialog.Text("contact"); record.PetName = dialog.Text("pet");
        record.Channel = dialog.Text("channel"); record.Category = dialog.Text("category"); record.Priority = dialog.Text("priority");
        record.Subject = dialog.Text("subject"); record.Details = dialog.Text("details"); record.PreferredChannel = dialog.Text("preferred");
        record.Status = dialog.Text("status");
        record.FollowUpDate = record.Status is "Resolved" or "Closed" ? null : dialog.Date("followUp");
        record.Owner = dialog.Text("owner");
        record.CommunicationConsent = dialog.Bool("consent");
        if (existing is null) Data.Inquiries.Insert(0, record);
        PersistAndRefresh("inquiries", "✉", existing is null ? "Inquiry logged" : "Inquiry updated", $"{record.ClientName} · {record.Category}");
    }

    private static string? ValidateInquiry(IReadOnlyDictionary<string, object?> values)
    {
        var status = Convert.ToString(values["status"]) ?? string.Empty;
        var priority = Convert.ToString(values["priority"]) ?? string.Empty;
        var preferred = Convert.ToString(values["preferred"]) ?? string.Empty;
        var contact = (Convert.ToString(values["contact"]) ?? string.Empty).Trim();
        var followUp = values["followUp"] as DateTime?;

        if (preferred == "Email" && !MailAddress.TryCreate(contact, out _))
            return "Enter a valid email address for an email response.";
        if (preferred is "Phone" or "SMS" && contact.Count(char.IsDigit) < 7)
            return "Enter a valid phone number for a phone or SMS response.";
        if (status == "Follow-up" && followUp is null)
            return "Choose a follow-up date before setting this inquiry to Follow-up.";
        if (status is "New" or "Open" or "Follow-up" && followUp?.Date < DateTime.Today)
            return "Choose today or a future date for an open inquiry follow-up.";
        if (priority == "Urgent" && status is not ("Resolved" or "Closed") && followUp?.Date != DateTime.Today)
            return "Urgent open inquiries need a follow-up date of today so they appear in the immediate queue.";
        return null;
    }

    private void OpenReminderDialog(ReminderRecord? existing = null)
    {
        if (!RequireSubscribed("reminders")) return;
        var dialog = new RecordDialog(this, existing is null ? "Schedule reminder" : "Edit reminder",
            "Plan the next client touchpoint and keep communication consistent.",
            [
                new("client", "Client name", Value: existing?.ClientName),
                new("pet", "Patient name", Value: existing?.PetName),
                new("type", "Reminder type", FieldKind.Choice, existing?.Type ?? "Appointment reminder", ["Appointment reminder", "Post-visit check-in", "Vaccination reminder", "Medication follow-up", "Wellness recall"]),
                new("channel", "Channel", FieldKind.Choice, existing?.Channel ?? "SMS", ["SMS", "Email", "Phone", "Messenger"]),
                new("due", "Due date", FieldKind.Date, existing?.DueDate ?? DateTime.Today.AddDays(1)),
                new("status", "Status", FieldKind.Choice, existing?.Status ?? "Scheduled", ["Pending", "Scheduled", "Sent"]),
                new("message", "Message", FieldKind.Multiline, existing?.Message, SpanColumns: true)
            ]);
        if (dialog.ShowModal() != true) return;
        var record = existing ?? new ReminderRecord { Id = _store.NextId("REM", 813, Data.Reminders.Count) };
        record.ClientName = dialog.Text("client"); record.PetName = dialog.Text("pet"); record.Type = dialog.Text("type");
        record.Channel = dialog.Text("channel"); record.DueDate = dialog.Date("due") ?? DateTime.Today.AddDays(1); record.Status = dialog.Text("status"); record.Message = dialog.Text("message");
        if (existing is null) Data.Reminders.Insert(0, record);
        PersistAndRefresh("reminders", "♢", existing is null ? "Reminder scheduled" : "Reminder updated", $"{record.ClientName} · {record.Type}");
    }

    private void OpenPromotionDialog(PromotionRecord? existing = null)
    {
        if (!RequireSubscribed("promotions")) return;
        if (_session.Role is not (UserRole.Receptionist or UserRole.Admin)) return;
        if (existing?.Status is "Active" or "Completed")
        {
            OpenPromotionSummary(existing);
            return;
        }
        var dialog = new RecordDialog(this, existing is null ? "Create promotional offer" : "Manage promotional offer",
            existing?.Approval == "Rejected"
                ? $"Revise the campaign, then submit it again. Review note: {existing.RejectionReason}"
                : "Define a measurable, consent-aware campaign, save it as a draft, then submit it for approval.",
            [
                new("name", "Campaign name", Value: existing?.Name),
                new("code", "Offer code", Value: existing?.Code),
                new("value", "Offer value", Value: existing?.OfferValue, Help: "Example: 20% off or ₱500 off"),
                new("audience", "Audience segment", FieldKind.Choice, existing?.Audience ?? "New puppy leads", ["New puppy leads", "Senior pets (7+ years)", "Pets due for dental care", "Inactive clients (6+ months)", "All consented clients"]),
                new("channel", "Delivery channel", FieldKind.Choice, existing?.Channel ?? "SMS + Email", ["SMS + Email", "Email", "SMS"]),
                new("status", "Delivery status", FieldKind.ReadOnly, existing?.Status ?? "Draft"),
                new("start", "Start date", FieldKind.Date, existing?.StartDate ?? DateTime.Today.AddDays(2)),
                new("end", "End date", FieldKind.Date, existing?.EndDate ?? DateTime.Today.AddDays(32)),
                new("approval", "Review status", FieldKind.ReadOnly, existing?.ReviewStatus ?? "Draft"),
                new("createdBy", "Created by", FieldKind.ReadOnly, existing?.CreatedBy ?? _session.Name),
                new("goal", "Campaign goal", FieldKind.Multiline, existing?.Goal, SpanColumns: true),
                new("reach", "Clients reached", FieldKind.ReadOnly, existing?.Reach ?? 0, Required: false),
                new("claims", "Claims", FieldKind.ReadOnly, existing?.Claims ?? 0, Required: false),
                new("conversion", "Conversion %", FieldKind.ReadOnly, existing?.Conversion ?? 0, Required: false),
                new("consent", "Send only to clients with valid communication consent for the selected channel.", FieldKind.CheckBox, true, SpanColumns: true)
            ],
            validate: values =>
            {
                if (values["consent"] is not true) return "Confirm that this campaign will use only consented client records.";
                var candidate = new PromotionRecord
                {
                    Id = existing?.Id ?? string.Empty,
                    Name = Convert.ToString(values["name"])?.Trim() ?? string.Empty,
                    Code = (Convert.ToString(values["code"]) ?? string.Empty).Trim().ToUpperInvariant(),
                    OfferValue = Convert.ToString(values["value"])?.Trim() ?? string.Empty,
                    Audience = Convert.ToString(values["audience"]) ?? string.Empty,
                    Channel = Convert.ToString(values["channel"]) ?? string.Empty,
                    StartDate = values["start"] as DateTime? ?? DateTime.Today,
                    EndDate = values["end"] as DateTime? ?? DateTime.Today,
                    Goal = Convert.ToString(values["goal"])?.Trim() ?? string.Empty
                };
                return PromotionWorkflowService.ValidateForSubmission(candidate, Data.Promotions, DateTime.Today);
            });
        if (dialog.ShowModal() != true) return;
        var start = dialog.Date("start") ?? DateTime.Today;
        var end = dialog.Date("end") ?? DateTime.Today.AddDays(30);
        var record = existing ?? new PromotionRecord { Id = _store.NextId("PRO", 111, Data.Promotions.Count), Approval = "Draft", Status = "Draft", CreatedBy = _session.Name };
        var changedTerms = existing is not null && (record.Name != dialog.Text("name") || record.Code != dialog.Text("code").ToUpperInvariant() || record.OfferValue != dialog.Text("value") || record.Audience != dialog.Text("audience") || record.Channel != dialog.Text("channel") || record.StartDate != start || record.EndDate != end || record.Goal != dialog.Text("goal"));
        record.Name = dialog.Text("name"); record.Code = dialog.Text("code").ToUpperInvariant(); record.OfferValue = dialog.Text("value");
        record.Audience = dialog.Text("audience"); record.Channel = dialog.Text("channel");
        record.StartDate = start; record.EndDate = end;
        if (changedTerms && record.Approval != "Draft") PromotionWorkflowService.ResetAfterMaterialEdit(record);
        record.Goal = dialog.Text("goal");
        if (existing is null) Data.Promotions.Insert(0, record);
        PersistAndRefresh("promotions", "promotion", existing is null ? "Campaign draft created" : "Campaign draft updated", $"{record.Name} · {record.Audience}");
    }

    private void OpenPromotionSummary(PromotionRecord record)
    {
        var review = record.Approval == "Rejected" ? record.RejectionReason : record.ReviewStatus;
        var dialog = new RecordDialog(this, record.Name, "Review campaign terms, delivery state, and measured performance.",
            [
                new("code", "Offer code", FieldKind.ReadOnly, record.Code),
                new("offer", "Offer", FieldKind.ReadOnly, record.OfferValue),
                new("audience", "Audience", FieldKind.ReadOnly, record.Audience),
                new("channel", "Channel", FieldKind.ReadOnly, record.Channel),
                new("schedule", "Schedule", FieldKind.ReadOnly, record.Schedule),
                new("status", "Delivery status", FieldKind.ReadOnly, record.Status),
                new("review", "Review status", FieldKind.ReadOnly, review, SpanColumns: true),
                new("goal", "Campaign goal", FieldKind.ReadOnlyMultiline, record.Goal, SpanColumns: true),
                new("reach", "Clients reached", FieldKind.ReadOnly, record.Reach),
                new("claims", "Verified claims", FieldKind.ReadOnly, record.Claims),
                new("conversion", "Conversion", FieldKind.ReadOnly, $"{record.Conversion}%")
            ], "Close", showCancel: false);
        dialog.ShowModal();
    }

    private void DecidePromotion(PromotionRecord? record, bool approve)
    {
        if (!RequireSubscribed("promotions")) return;
        if (_session.Role != UserRole.Admin) return;
        if (record is null) { ShowSelectionRequired("promotional offer"); return; }
        if (record.Approval != "Needs review")
        {
            System.Windows.MessageBox.Show(this, "Select a campaign that is waiting for review.", "Campaign review", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
            return;
        }
        try
        {
            if (!approve)
            {
                var reason = new RecordDialog(this, "Request campaign changes", "Explain exactly what must change before this campaign can be submitted again.",
                    [new("reason", "Required changes", FieldKind.Multiline, record.RejectionReason, SpanColumns: true)], "Request changes");
                if (reason.ShowModal() != true) return;
                PromotionWorkflowService.Reject(record, _session.Name, reason.Text("reason"), DateTime.Now);
            }
            else PromotionWorkflowService.Approve(record, Data.Promotions, _session.Name, DateTime.Now);
            PersistAndRefresh("promotions", "check", approve ? "Campaign approved" : "Campaign changes requested", $"{record.Name} · {_session.Name}");
        }
        catch (InvalidOperationException exception)
        {
            System.Windows.MessageBox.Show(this, exception.Message, "Campaign review", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
        }
    }

    private void SubmitPromotion(PromotionRecord? record)
    {
        if (!RequireSubscribed("promotions")) return;
        if (_session.Role is not (UserRole.Receptionist or UserRole.Admin)) return;
        if (record is null) { ShowSelectionRequired("campaign draft"); return; }
        try
        {
            PromotionWorkflowService.Submit(record, Data.Promotions, _session.Name, DateTime.Now);
            PersistAndRefresh("promotions", "promotion", "Campaign submitted for review", $"{record.Name} · {_session.Name}");
        }
        catch (InvalidOperationException exception)
        {
            System.Windows.MessageBox.Show(this, exception.Message, "Submit campaign", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
        }
    }

    private void StartPromotion(PromotionRecord? record)
    {
        if (!RequireSubscribed("promotions")) return;
        if (record is null) { ShowSelectionRequired("approved campaign"); return; }
        try
        {
            PromotionWorkflowService.Start(record, DateTime.Now);
            PersistAndRefresh("promotions", "promotion", "Campaign started", record.Name);
        }
        catch (InvalidOperationException exception)
        {
            System.Windows.MessageBox.Show(this, exception.Message, "Start campaign", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
        }
    }

    private void CompletePromotion(PromotionRecord? record)
    {
        if (!RequireSubscribed("promotions")) return;
        if (record is null) { ShowSelectionRequired("active campaign"); return; }
        try
        {
            PromotionWorkflowService.Complete(record, DateTime.Now);
            PersistAndRefresh("promotions", "check", "Campaign completed", $"{record.Name} · {record.Performance}");
        }
        catch (InvalidOperationException exception)
        {
            System.Windows.MessageBox.Show(this, exception.Message, "Complete campaign", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
        }
    }

    private void PreviewPromotionAudience(PromotionRecord? record)
    {
        if (record is null) { ShowSelectionRequired("promotional offer"); return; }
        var audience = PromotionWorkflowService.MatchingAudience(Data, record);
        var emailed = audience.Count(x => ClientEmailService.IsValidAddress(x.Email));
        var smsReady = audience.Count(x => !string.IsNullOrWhiteSpace(x.Phone));
        var alreadyReached = audience.Count(x => record.ContactedClientIds?.Contains(x.Id, StringComparer.Ordinal) == true);
        var dialog = new RecordDialog(this, "Campaign audience preview", $"Live eligibility for {record.Audience}. No message is sent from this preview.",
            [
                new("eligible", "Consented clients", FieldKind.ReadOnly, audience.Count),
                new("email", "Valid email", FieldKind.ReadOnly, emailed),
                new("sms", "Phone available", FieldKind.ReadOnly, smsReady),
                new("reached", "Already contacted", FieldKind.ReadOnly, alreadyReached),
                new("remaining", "Remaining audience", FieldKind.ReadOnly, Math.Max(0, audience.Count - alreadyReached)),
                new("rule", "Segment rule", FieldKind.ReadOnlyMultiline, AudienceRule(record.Audience), SpanColumns: true)
            ], "Close", showCancel: false);
        dialog.ShowModal();
    }

    private void RecordPromotionMetric(PromotionRecord? record, bool claim)
    {
        if (!RequireSubscribed("promotions")) return;
        if (record is null) { ShowSelectionRequired("promotional offer"); return; }
        var dialog = new RecordDialog(this, claim ? "Record verified claims" : "Record external reach",
            claim ? "Enter redemptions verified against this campaign's offer code." : "Add clients contacted outside the prepared email or SMS batches.",
            [new("count", "Number to add", FieldKind.Number, 1)], "Record");
        if (dialog.ShowModal() != true) return;
        var count = dialog.Number("count");
        try
        {
            if (claim) PromotionWorkflowService.RecordClaims(record, count);
            else PromotionWorkflowService.RecordReach(record, count);
            PersistAndRefresh("promotions", "promotion", claim ? "Campaign claims recorded" : "External campaign reach recorded", $"{record.Name} · {record.Performance}");
        }
        catch (InvalidOperationException exception)
        {
            System.Windows.MessageBox.Show(this, exception.Message, "Campaign performance", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
        }
    }

    private static string AudienceRule(string audience) => audience switch
    {
        "New puppy leads" => "Consented dog owners whose patient birthday is within the last year.",
        "Senior pets (7+ years)" => "Consented owners with a patient aged seven years or older.",
        "Pets due for dental care" => "Consented clients linked to a dental appointment in this branch.",
        "Inactive clients (6+ months)" => "Consented clients with no recorded visit in the last six months.",
        _ => "All client profiles with recorded promotional consent."
    };

    private void OpenFeedbackDialog(FeedbackRecord? existing = null)
    {
        if (!RequireSubscribed("feedback")) return;
        var dialog = new RecordDialog(this, existing is null ? "Record complaint or feedback" : "Review complaint or feedback",
            "Capture the client experience, ownership, escalation, and promised follow-up in one record.",
            [
                new("client", "Client name", Value: existing?.ClientName),
                new("contact", "Contact detail", Value: existing?.Contact, Help: "Use the phone number, email address, or Messenger name the client expects PawCare to use."),
                new("pet", "Patient name", Value: existing?.PetName),
                new("caseType", "Case type", FieldKind.Choice, existing?.CaseType ?? "Complaint", ["Complaint", "Feedback", "Compliment"]),
                new("channel", "Collection channel", FieldKind.Choice, existing?.Channel ?? "Phone", ["Phone", "Email", "SMS", "Messenger", "Website", "Walk-in"]),
                new("rating", "Rating", FieldKind.Choice, (existing?.Rating ?? 5).ToString(), ["5", "4", "3", "2", "1"]),
                new("category", "Category", FieldKind.Choice, existing?.Category ?? "Service", ["Service", "Veterinarian", "Wait time", "Billing", "Facilities", "Follow-up"]),
                new("priority", "Priority", FieldKind.Choice, existing?.Priority ?? "Medium", ["Low", "Medium", "High", "Urgent"], Help: "Urgent complaints should be escalated to the manager immediately."),
                new("status", "Status", FieldKind.Choice, existing?.Status ?? "Open", ["Open", "Escalated", "Reviewed", "Closed"]),
                new("escalation", "Escalation", FieldKind.Choice, existing?.Escalation ?? "None", ["None", "Manager review", "Urgent"]),
                new("owner", "Assigned owner", Value: existing?.Owner ?? _session.Name, Help: "The person accountable for the next client update."),
                new("followUp", "Follow-up date", FieldKind.Date, existing?.FollowUpDate ?? DateTime.Today.AddDays(1), Required: false, Help: "Required while a case is escalated; use today for urgent concerns."),
                new("lastContact", "Last client contact", FieldKind.ReadOnly, existing?.LastContact ?? "Not contacted", Required: false),
                new("comment", "Client narrative", FieldKind.Multiline, existing?.Comment, Help: "Record the client’s concern or feedback in their own words and any immediate response given.", SpanColumns: true),
                new("resolution", "Resolution and internal notes", FieldKind.Multiline, existing?.ResolutionNotes, Required: false, Help: "Document investigation, actions promised, and the final resolution.", SpanColumns: true),
                new("consent", "Client consents to be contacted about this complaint or feedback case.", FieldKind.CheckBox, existing?.ConsentToFollowUp ?? false, Required: false, SpanColumns: true)
            ], validate: ValidateFeedbackCase);
        if (dialog.ShowModal() != true) return;
        var record = existing ?? new FeedbackRecord { Id = _store.NextId("FB", 513, Data.Feedback.Count), Date = DateTime.Today };
        record.ClientName = dialog.Text("client"); record.Contact = dialog.Text("contact"); record.PetName = dialog.Text("pet");
        record.CaseType = dialog.Text("caseType"); record.Channel = dialog.Text("channel"); record.Rating = int.Parse(dialog.Text("rating"));
        record.Category = dialog.Text("category"); record.Priority = dialog.Text("priority"); record.Status = dialog.Text("status");
        record.Escalation = dialog.Text("escalation"); record.Owner = dialog.Text("owner"); record.FollowUpDate = dialog.Date("followUp");
        record.Comment = dialog.Text("comment"); record.ResolutionNotes = dialog.Text("resolution"); record.ConsentToFollowUp = dialog.Bool("consent");
        if (record.Escalation != "None" && record.Status == "Open") record.Status = "Escalated";
        if (record.Status is "Reviewed" or "Closed") record.FollowUpDate = null;
        if (existing is null) Data.Feedback.Insert(0, record);
        var destination = _session.Role == UserRole.Receptionist ? "feedback-intake" : "feedback";
        PersistAndRefresh(destination, "△", existing is null ? $"{record.CaseType} recorded" : $"{record.CaseType} updated", $"{record.ClientName} · {record.Category} · {record.Priority}");
    }

    private static string? ValidateFeedbackCase(IReadOnlyDictionary<string, object?> values)
    {
        var status = Convert.ToString(values["status"]) ?? string.Empty;
        var caseType = Convert.ToString(values["caseType"]) ?? string.Empty;
        var priority = Convert.ToString(values["priority"]) ?? string.Empty;
        var escalation = Convert.ToString(values["escalation"]) ?? string.Empty;
        var owner = Convert.ToString(values["owner"]) ?? string.Empty;
        var resolution = Convert.ToString(values["resolution"]) ?? string.Empty;
        var followUp = values["followUp"] as DateTime?;

        if (status == "Closed" && string.IsNullOrWhiteSpace(resolution))
            return "Record the investigation, action taken, and final resolution before closing this case.";
        if (caseType == "Complaint" && priority == "Urgent" && escalation == "None")
            return "Choose Manager review or Urgent escalation for an urgent complaint.";
        if (status == "Escalated" || escalation != "None")
        {
            if (string.IsNullOrWhiteSpace(owner)) return "Assign an owner before escalating this case.";
            if (followUp is null) return "Choose the next client follow-up date before escalating this case.";
        }
        if (status is "Open" or "Escalated" && followUp?.Date < DateTime.Today)
            return "Choose today or a future date for an open case follow-up.";
        if (priority == "Urgent" && status is not ("Reviewed" or "Closed") && followUp?.Date != DateTime.Today)
            return "Urgent cases need a follow-up date of today.";
        return null;
    }
}
