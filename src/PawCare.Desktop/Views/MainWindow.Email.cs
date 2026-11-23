using System.Diagnostics;
using System.Windows;
using PawCare.Desktop.Controls;
using PawCare.Desktop.Models;
using PawCare.Desktop.Services;

namespace PawCare.Desktop.Views;

public partial class MainWindow
{
    private void OpenReminderEmail(ReminderRecord? reminder)
    {
        if (!RequireSubscribed("reminders")) return;
        if (reminder is null) { ShowEmailNotice("Select a reminder first.", MessageBoxImage.Information); return; }
        if (reminder.Channel != "Email") { ShowEmailNotice("This reminder uses another channel. SMS and Messenger delivery require a provider integration.", MessageBoxImage.Warning); return; }
        var client = Data.Clients.FirstOrDefault(x => Same(x.ClientName, reminder.ClientName) && Same(x.PetName, reminder.PetName));
        if (client is null) { ShowEmailNotice("Add a matching client profile with an email address first.", MessageBoxImage.Warning); return; }
        TryReviewDraft(() => ClientEmailService.Reminder(client, reminder), "Review reminder email", $"Reminder · {reminder.Type}");
    }
    private void OpenAppointmentEmail(AppointmentRecord? appointment)
    {
        if (!RequireSubscribed("appointments")) return;
        if (appointment is null)
        {
            ShowEmailNotice("Select an appointment first.", MessageBoxImage.Information);
            return;
        }

        var client = Data.Clients.FirstOrDefault(x => x.Id == appointment.ClientId)
            ?? Data.Clients.FirstOrDefault(x => Same(x.ClientName, appointment.ClientName) && Same(x.PetName, appointment.PetName));
        if (client is null)
        {
            ShowEmailNotice("No matching client profile was found. Link this appointment to a client profile with an email address first.", MessageBoxImage.Warning);
            return;
        }

        TryReviewDraft(
            () => ClientEmailService.AppointmentReminder(client, appointment),
            "Review appointment reminder",
            $"Appointment reminder · {appointment.PetName}");
    }

    private void OpenPromotionEmail(PromotionRecord? promotion)
    {
        if (!RequireSubscribed("promotions")) return;
        if (promotion is null)
        {
            ShowEmailNotice("Select a promotional offer first.", MessageBoxImage.Information);
            return;
        }
        if (!PromotionWorkflowService.CanDeliver(promotion, DateTime.Today) || !promotion.Channel.Contains("Email", StringComparison.OrdinalIgnoreCase))
        {
            ShowEmailNotice("The offer must be approved, active, scheduled for today, and include email delivery.", MessageBoxImage.Warning);
            return;
        }

        var recipients = PromotionWorkflowService.MatchingAudience(Data, promotion)
            .Where(x => ClientEmailService.IsValidAddress(x.Email))
            .OrderBy(x => x.ClientName)
            .ThenBy(x => x.PetName)
            .ToArray();
        if (recipients.Length == 0)
        {
            ShowEmailNotice("There are no client profiles with both a valid email address and promotional consent.", MessageBoxImage.Warning);
            return;
        }

        var choices = recipients.ToDictionary(
            x => $"{x.ClientName} — {x.PetName} · {x.Email}",
            x => x,
            StringComparer.Ordinal);
        var recipientDialog = new RecordDialog(this, "Choose offer recipient",
            "Only clients with a valid email address and recorded promotional consent are listed.",
            [new("recipient", "Consented client", FieldKind.Choice, choices.Keys.First(), choices.Keys.ToArray())],
            "Continue");
        if (recipientDialog.ShowModal() != true || !choices.TryGetValue(recipientDialog.Text("recipient"), out var client)) return;

        if (!TryReviewDraft(
            () => ClientEmailService.PromotionOffer(client, promotion),
            "Review promotional email",
            $"Promotional offer · {promotion.Name}")) return;
        var sent = MessageBox.Show(this, "After sending the draft in your email app, choose Yes to record this client as reached. Choose No if you did not send it.",
            "Confirm campaign delivery", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (sent != MessageBoxResult.Yes) return;
        var added = PromotionWorkflowService.RecordContactedClients(promotion, [client.Id]);
        PersistAndRefresh("promotions", "message", "Individual campaign email recorded", $"{promotion.Name} · {added} new client reached");
    }

    private void OpenCampaignDelivery(PromotionRecord? promotion)
    {
        if (promotion is null) { ShowEmailNotice("Select a promotional offer first.", MessageBoxImage.Information); return; }
        if (promotion.Channel.Contains("Email", StringComparison.OrdinalIgnoreCase)) OpenCampaignEmail(promotion);
        else if (promotion.Channel.Contains("SMS", StringComparison.OrdinalIgnoreCase)) OpenCampaignSmsHandoff(promotion);
        else ShowEmailNotice("This campaign does not have a supported delivery channel. Edit it and choose Email, SMS, or SMS + Email.", MessageBoxImage.Warning);
    }

    private void OpenCampaignEmail(PromotionRecord? promotion)
    {
        if (!RequireSubscribed("promotions")) return;
        if (promotion is null) { ShowEmailNotice("Select a promotional offer first.", MessageBoxImage.Information); return; }
        if (!PromotionWorkflowService.CanDeliver(promotion, DateTime.Today) || !promotion.Channel.Contains("Email", StringComparison.OrdinalIgnoreCase))
        {
            ShowEmailNotice("Approve and start this campaign within its scheduled dates, and make sure its channel includes email.", MessageBoxImage.Warning);
            return;
        }
        promotion.ContactedClientIds ??= [];
        var contacted = new HashSet<string>(promotion.ContactedClientIds, StringComparer.Ordinal);
        var recipients = PromotionWorkflowService.MatchingAudience(Data, promotion)
            .Where(x => ClientEmailService.IsValidAddress(x.Email) && !contacted.Contains(x.Id))
            .GroupBy(x => x.Email.Trim(), StringComparer.OrdinalIgnoreCase).Select(x => x.First())
            .OrderBy(x => x.ClientName).ThenBy(x => x.PetName).ToArray();
        if (recipients.Length == 0) { ShowEmailNotice("No consented clients with valid email addresses match this audience.", MessageBoxImage.Information); return; }
        var batches = recipients.Chunk(25).ToArray();
        var choices = Enumerable.Range(1, batches.Length).Select(x => $"Batch {x} of {batches.Length} ({batches[x - 1].Length} recipients)").ToArray();
        var dialog = new RecordDialog(this, "Prepare consented email batch", $"{recipients.Length} eligible clients have not yet been recorded as contacted. Each draft uses BCC to protect client addresses.",
            [new("batch", "Recipient batch", FieldKind.Choice, choices[0], choices)], "Open email draft");
        if (dialog.ShowModal() != true) return;
        var index = Array.IndexOf(choices, dialog.Text("batch"));
        if (index < 0) return;
        var draft = ClientEmailService.PromotionCampaign(promotion);
        var batch = batches[index];
        var query = $"bcc={Uri.EscapeDataString(string.Join(',', batch.Select(x => x.Email.Trim())))}&subject={Uri.EscapeDataString(draft.Subject)}&body={Uri.EscapeDataString(draft.Body)}";
        try
        {
            Process.Start(new ProcessStartInfo { FileName = $"mailto:?{query}", UseShellExecute = true });
            var sent = MessageBox.Show(this, "The protected BCC draft is open in your email app. After you send it, choose Yes to record this batch as reached. Choose No if you did not send it.",
                "Confirm campaign delivery", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (sent != MessageBoxResult.Yes) return;
            var added = PromotionWorkflowService.RecordContactedClients(promotion, batch.Select(x => x.Id));
            PersistAndRefresh("promotions", "message", "Campaign email batch recorded", $"{promotion.Name} · {added} clients reached");
        }
        catch (System.ComponentModel.Win32Exception)
        { ShowEmailNotice("No default email application could open this campaign draft.", MessageBoxImage.Error); }
    }

    private void OpenCampaignSmsHandoff(PromotionRecord promotion)
    {
        if (!PromotionWorkflowService.CanDeliver(promotion, DateTime.Today))
        {
            ShowEmailNotice("Approve and start this campaign within its scheduled dates before preparing an SMS audience.", MessageBoxImage.Warning);
            return;
        }
        promotion.ContactedClientIds ??= [];
        var contacted = new HashSet<string>(promotion.ContactedClientIds, StringComparer.Ordinal);
        var recipients = PromotionWorkflowService.MatchingAudience(Data, promotion)
            .Where(x => !string.IsNullOrWhiteSpace(x.Phone) && !contacted.Contains(x.Id))
            .OrderBy(x => x.ClientName).ThenBy(x => x.PetName).ToArray();
        if (recipients.Length == 0)
        {
            ShowEmailNotice("No remaining consented clients with phone numbers match this audience.", MessageBoxImage.Information);
            return;
        }
        var batches = recipients.Chunk(25).ToArray();
        var choices = Enumerable.Range(1, batches.Length).Select(x => $"Batch {x} of {batches.Length} ({batches[x - 1].Length} recipients)").ToArray();
        var dialog = new RecordDialog(this, "Prepare consented SMS batch", "Copy a deduplicated recipient list and message for your approved SMS provider, then confirm only after sending.",
            [new("batch", "Recipient batch", FieldKind.Choice, choices[0], choices)], "Copy SMS handoff");
        if (dialog.ShowModal() != true) return;
        var index = Array.IndexOf(choices, dialog.Text("batch"));
        if (index < 0) return;
        var batch = batches[index];
        var handoff = $"Recipients:\r\n{string.Join("\r\n", batch.Select(x => $"{x.Phone} — {x.ClientName}"))}\r\n\r\nMessage:\r\n{ClientEmailService.PromotionSmsMessage(promotion)}";
        try
        {
            Clipboard.SetText(handoff);
            var sent = MessageBox.Show(this, "The SMS recipient list and message were copied. After sending them through your approved provider, choose Yes to record this batch as reached.",
                "Confirm campaign delivery", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (sent != MessageBoxResult.Yes) return;
            var added = PromotionWorkflowService.RecordContactedClients(promotion, batch.Select(x => x.Id));
            PersistAndRefresh("promotions", "message", "Campaign SMS batch recorded", $"{promotion.Name} · {added} clients reached");
        }
        catch (System.Runtime.InteropServices.ExternalException)
        {
            ShowEmailNotice("Windows could not copy the SMS handoff. Try again after closing another clipboard application.", MessageBoxImage.Error);
        }
    }

    private void OpenFeedbackEmail(FeedbackRecord? feedback)
    {
        if (feedback is null)
        {
            ShowEmailNotice("Select a feedback or escalation case first.", MessageBoxImage.Information);
            return;
        }
        if (!feedback.ConsentToFollowUp)
        {
            ShowEmailNotice("This case does not have client consent for follow-up. Open the case and record consent before contacting the client.", MessageBoxImage.Warning);
            return;
        }

        var client = ResolveFeedbackClient(feedback);
        if (client is null)
        {
            ShowEmailNotice("No valid client email was found. Add an email to the matching client profile or to this case's contact field.", MessageBoxImage.Warning);
            return;
        }

        TryReviewDraft(
            () => ClientEmailService.FeedbackFollowUp(client, feedback),
            feedback.Status == "Escalated" ? "Review escalation follow-up" : "Review feedback follow-up",
            $"{feedback.CaseType} follow-up · {feedback.ClientName} · {feedback.Status}");
    }

    private ClientRecord? ResolveFeedbackClient(FeedbackRecord feedback)
    {
        var profile = Data.Clients.FirstOrDefault(x => Same(x.ClientName, feedback.ClientName) && Same(x.PetName, feedback.PetName))
            ?? Data.Clients.FirstOrDefault(x => Same(x.ClientName, feedback.ClientName));
        if (profile is not null && ClientEmailService.IsValidAddress(profile.Email)) return profile;
        if (!ClientEmailService.IsValidAddress(feedback.Contact)) return null;

        return new ClientRecord
        {
            ClientName = feedback.ClientName,
            PetName = feedback.PetName,
            Email = feedback.Contact.Trim()
        };
    }

    private bool TryReviewDraft(Func<ClientEmailDraft> draftFactory, string title, string activityDetail)
    {
        try
        {
            return ReviewAndOpenDraft(draftFactory(), title, activityDetail);
        }
        catch (InvalidOperationException exception)
        {
            ShowEmailNotice(exception.Message, MessageBoxImage.Warning);
            return false;
        }
    }

    private bool ReviewAndOpenDraft(ClientEmailDraft draft, string title, string activityDetail)
    {
        var dialog = new RecordDialog(this, title,
            "Review the recipient and message. PawCare will open this draft in the computer's default email application; press Send there to deliver it.",
            [
                new("to", "Client email", FieldKind.ReadOnly, draft.To, SpanColumns: true),
                new("subject", "Subject", Value: draft.Subject, SpanColumns: true),
                new("body", "Message", FieldKind.Multiline, draft.Body, SpanColumns: true)
            ],
            "Open email draft");
        if (dialog.ShowModal() != true) return false;

        var reviewed = new ClientEmailDraft(dialog.Text("to"), dialog.Text("subject"), dialog.Text("body"));
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = ClientEmailService.CreateMailToUri(reviewed).AbsoluteUri,
                UseShellExecute = true
            });
            _store.AddActivity("message", "Email draft opened", $"{activityDetail} · {reviewed.To}");
            _store.Save();
            ShowEmailNotice("The email draft was opened for final review. Press Send in your email application to deliver it to the client.", MessageBoxImage.Information);
            return true;
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            ShowEmailNotice("PawCare could not open an email draft. Confirm that Outlook, Windows Mail, or another default email application is configured, then try again.", MessageBoxImage.Error);
            return false;
        }
    }

    private void ShowEmailNotice(string message, MessageBoxImage icon) =>
        ShowWorkflowNotice(icon == MessageBoxImage.Error ? "Client email unavailable" : "Client email", message);

    private static bool Same(string? left, string? right) =>
        string.Equals(left?.Trim(), right?.Trim(), StringComparison.OrdinalIgnoreCase);
}
