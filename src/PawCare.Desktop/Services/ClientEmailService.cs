using System.IO;
using System.Net.Mail;
using System.Text;
using PawCare.Desktop.Models;

namespace PawCare.Desktop.Services;

public sealed record ClientEmailDraft(string To, string Subject, string Body);

public static class ClientEmailService
{
    public static ClientEmailDraft Reminder(ClientRecord client, ReminderRecord reminder)
    {
        RequireEmail(client);
        return new ClientEmailDraft(client.Email.Trim(), reminder.Type, $"Hello {client.ClientName},\n\n{reminder.Message}\n\nPawCare Clinic");
    }
    public static bool IsValidAddress(string? address) =>
        MailAddress.TryCreate(address, out var parsed)
        && string.Equals(parsed.Address, address?.Trim(), StringComparison.OrdinalIgnoreCase);

    public static ClientEmailDraft AppointmentReminder(ClientRecord client, AppointmentRecord appointment)
    {
        RequireEmail(client);
        var subject = $"Upcoming PawCare appointment for {appointment.PetName} — {appointment.Date:MMMM d, yyyy}";
        var body = $"""
            Hello {client.ClientName},

            This is a friendly reminder about {appointment.PetName}'s upcoming appointment at PawCare Clinic.

            Date: {appointment.Date:MMMM d, yyyy}
            Time: {FormatTime(appointment.Time)}
            Service: {appointment.Service}
            Veterinarian: {appointment.Veterinarian}

            Please contact PawCare Clinic if you need to update or reschedule this visit.

            Warm regards,
            PawCare Clinic Management System
            """;
        return new ClientEmailDraft(client.Email.Trim(), subject, body);
    }

    public static ClientEmailDraft PromotionOffer(ClientRecord client, PromotionRecord promotion)
    {
        RequireEmail(client);
        if (!client.MarketingConsent)
            throw new InvalidOperationException("This client has not consented to promotional email.");

        var subject = $"PawCare offer: {promotion.Name} — {promotion.OfferValue}";
        var codeLine = string.IsNullOrWhiteSpace(promotion.Code) ? string.Empty : $"Offer code: {promotion.Code}\n";
        var body = $"""
            Hello {client.ClientName},

            PawCare has an offer that may help with {client.PetName}'s care:

            {promotion.Name}
            Offer: {promotion.OfferValue}
            {codeLine}Available: {promotion.StartDate:MMMM d, yyyy} to {promotion.EndDate:MMMM d, yyyy}

            {promotion.Goal}

            Please contact PawCare Clinic for eligibility, booking, and full offer details.

            Warm regards,
            PawCare Clinic Management System
            """;
        return new ClientEmailDraft(client.Email.Trim(), subject, body);
    }

    public static ClientEmailDraft PromotionCampaign(PromotionRecord promotion)
    {
        var subject = $"PawCare offer: {promotion.Name} — {promotion.OfferValue}";
        var body = $"Hello,\n\n{promotion.Name}\nOffer: {promotion.OfferValue}\nCode: {promotion.Code}\nAvailable: {promotion.StartDate:MMMM d, yyyy} to {promotion.EndDate:MMMM d, yyyy}\n\n{promotion.Goal}\n\nPlease contact PawCare Clinic for eligibility and booking.\n\nPawCare Clinic";
        return new ClientEmailDraft("", subject, body);
    }

    public static string PromotionSmsMessage(PromotionRecord promotion)
    {
        var code = string.IsNullOrWhiteSpace(promotion.Code) ? string.Empty : $" Use code {promotion.Code}.";
        return $"PawCare: {promotion.Name} — {promotion.OfferValue}.{code} Valid {promotion.StartDate:MMM d}–{promotion.EndDate:MMM d, yyyy}. Contact the clinic for eligibility and booking.";
    }

    public static ClientEmailDraft FeedbackFollowUp(ClientRecord client, FeedbackRecord feedback)
    {
        RequireEmail(client);
        if (!feedback.ConsentToFollowUp)
            throw new InvalidOperationException("The client has not consented to follow-up for this case.");

        var statusCopy = feedback.Status switch
        {
            "Escalated" => "Your case has been escalated for manager review, and our team is following up on it.",
            "Reviewed" => "Our team has reviewed your case and recorded the update below.",
            "Closed" => "Our team has completed its review and closed the case with the update below.",
            _ => "We received your case and are following up with you about it."
        };
        var resolution = string.IsNullOrWhiteSpace(feedback.ResolutionNotes)
            ? "We will share any further action or resolution as it becomes available."
            : $"Update from our team: {feedback.ResolutionNotes}";
        var subject = $"PawCare {feedback.CaseType.ToLowerInvariant()} update for {feedback.PetName}";
        var body = $"""
            Hello {client.ClientName},

            Thank you for contacting PawCare about {feedback.PetName}. {statusCopy}

            Case category: {feedback.Category}
            Case status: {feedback.Status}
            {resolution}

            You may reply to this email or contact PawCare Clinic if you have questions or more information to share.

            Warm regards,
            PawCare Clinic Management System
            """;
        return new ClientEmailDraft(client.Email.Trim(), subject, body);
    }

    public static Uri CreateMailToUri(ClientEmailDraft draft)
    {
        if (!IsValidAddress(draft.To))
            throw new InvalidOperationException("Enter a valid client email address.");

        var value = $"mailto:{draft.To.Trim()}?subject={Uri.EscapeDataString(draft.Subject)}&body={Uri.EscapeDataString(NormalizeLineEndings(draft.Body))}";
        return new Uri(value);
    }

    private static void RequireEmail(ClientRecord client)
    {
        if (!IsValidAddress(client.Email))
            throw new InvalidOperationException($"{client.ClientName} does not have a valid email address in the client profile.");
    }

    private static string FormatTime(string value) =>
        TimeSpan.TryParse(value, out var time) ? DateTime.Today.Add(time).ToString("h:mm tt") : value;

    private static string NormalizeLineEndings(string value)
    {
        var builder = new StringBuilder(value.Length + 16);
        using var reader = new StringReader(value.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n'));
        while (reader.ReadLine() is { } line)
        {
            if (builder.Length > 0) builder.Append("\r\n");
            builder.Append(line.TrimEnd());
        }
        return builder.ToString().Trim();
    }
}
