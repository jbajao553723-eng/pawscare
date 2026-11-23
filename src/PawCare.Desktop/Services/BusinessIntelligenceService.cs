using PawCare.Desktop.Models;

namespace PawCare.Desktop.Services;

public enum BusinessChartType
{
    Line,
    Column,
    HorizontalBar,
    StackedHorizontal
}

public sealed record BusinessIntelligencePoint(
    string Label,
    double PrimaryValue,
    double SecondaryValue = 0,
    string Detail = "");

public sealed record BusinessIntelligenceChart(
    string Title,
    string Subtitle,
    string BusinessQuestion,
    string PrimaryMetric,
    string SecondaryMetric,
    string Unit,
    BusinessChartType ChartType,
    IReadOnlyList<BusinessIntelligencePoint> Points,
    int SampleSize,
    string Finding,
    string RecommendedAction,
    string Source);

public enum BusinessKpiStatus
{
    OnTrack,
    Watch,
    NeedsAttention,
    NoData
}

public sealed record BusinessKpi(
    string Name,
    string DisplayValue,
    string TargetDisplay,
    string Period,
    string Formula,
    BusinessKpiStatus Status,
    int SampleSize);

public static class BusinessIntelligenceService
{
    public static IReadOnlyList<BusinessIntelligenceChart> Build(ClinicData data, bool includesPromotions, DateTime asOfDate)
    {
        var today = asOfDate.Date;
        return includesPromotions
            ?
            [
                CampaignReachAndClaims(data),
                CampaignApprovalPipeline(data),
                AudiencePerformance(data),
                CampaignChannelEffectiveness(data),
                ClientExperience(data),
                CampaignConversion(data)
            ]
            :
            [
                AppointmentDemandTrend(data, today),
                AppointmentPipeline(data),
                ServiceDemand(data),
                InquiryBacklog(data),
                ChannelEffectiveness(data),
                ClientExperience(data)
            ];
    }

    public static IReadOnlyList<BusinessKpi> BuildKpis(ClinicData data, bool includesPromotions, DateTime asOfDate)
    {
        return includesPromotions ? BuildPromotionKpis(data, asOfDate.Date) : BuildOperationalKpis(data, asOfDate.Date);
    }

    private static IReadOnlyList<BusinessKpi> BuildPromotionKpis(ClinicData data, DateTime today)
    {
        var totalReach = data.Promotions.Sum(x => x.Reach);
        var totalClaims = data.Promotions.Sum(x => x.Claims);
        var conversion = Percentage(totalClaims, totalReach);
        var consentedClients = data.Clients.Where(x => x.MarketingConsent).Select(x => x.Id).ToHashSet(StringComparer.Ordinal);
        var contactedClients = data.Promotions
            .SelectMany(x => x.ContactedClientIds ?? [])
            .Where(consentedClients.Contains)
            .Distinct(StringComparer.Ordinal)
            .Count();
        if (contactedClients == 0 && totalReach > 0)
            contactedClients = Math.Min(totalReach, consentedClients.Count);
        var coverage = Percentage(contactedClients, consentedClients.Count);
        var approvals = data.Promotions
            .Where(x => x.SubmittedUtc.HasValue && x.ApprovedUtc.HasValue && x.ApprovedUtc >= x.SubmittedUtc)
            .Select(x => (x.ApprovedUtc!.Value - x.SubmittedUtc!.Value).TotalHours)
            .ToArray();
        var approvalHours = approvals.Length == 0 ? 0 : approvals.Average();
        var satisfaction = data.Feedback.Count == 0 ? 0 : data.Feedback.Average(x => x.Rating);
        var active = data.Promotions.Count(x => x.Approval == "Approved" && x.Status == "Active" && x.StartDate.Date <= today && x.EndDate.Date >= today);

        return
        [
            HigherKpi("Campaign conversion", conversion, 10, "%", "Claims / reached clients x 100", totalReach, "All recorded campaigns"),
            HigherKpi("Consented audience reached", coverage, 60, "%", "Unique consented clients contacted / consented clients x 100", consentedClients.Count, "Current client directory"),
            HigherKpi("Verified claims", totalClaims, 20, "", "Sum of verified campaign claims", data.Promotions.Count, "All recorded campaigns", "0"),
            HigherKpi("Active approved campaigns", active, 1, "", "Approved campaigns active today", data.Promotions.Count, $"As of {today:MMM d, yyyy}", "0"),
            LowerKpi("Approval turnaround", approvalHours, 24, " hrs", "Average time from submission to approval", approvals.Length, "Approved campaigns", "0.0"),
            HigherKpi("Client satisfaction", satisfaction, 4, "/5", "Average recorded feedback rating", data.Feedback.Count, "All feedback responses", "0.0")
        ];
    }

    private static IReadOnlyList<BusinessKpi> BuildOperationalKpis(ClinicData data, DateTime today)
    {
        var openInquiries = data.Inquiries.Count(x => !IsClosed(x.Status));
        var resolved = data.Inquiries.Count(x => IsClosed(x.Status));
        var resolution = Percentage(resolved, data.Inquiries.Count);
        var completion = Percentage(data.Appointments.Count(x => x.Status == "Completed"), data.Appointments.Count);
        var satisfaction = data.Feedback.Count == 0 ? 0 : data.Feedback.Average(x => x.Rating);
        var nextThirtyDays = data.Appointments.Count(x => x.Status != "Cancelled" && x.Date.Date >= today && x.Date.Date <= today.AddDays(30));

        return
        [
            HigherKpi("Inquiry resolution", resolution, 80, "%", "Resolved and closed inquiries / all inquiries x 100", data.Inquiries.Count, "All inquiry records"),
            HigherKpi("Appointment completion", completion, 75, "%", "Completed appointments / all appointments x 100", data.Appointments.Count, "All appointment records"),
            HigherKpi("Client satisfaction", satisfaction, 4, "/5", "Average recorded feedback rating", data.Feedback.Count, "All feedback responses", "0.0"),
            HigherKpi("Upcoming visits", nextThirtyDays, 1, "", "Non-cancelled visits in the next 30 days", data.Appointments.Count, $"{today:MMM d} - {today.AddDays(30):MMM d, yyyy}", "0"),
            LowerKpi("Open inquiries", openInquiries, 10, "", "Inquiries not resolved or closed", data.Inquiries.Count, "Current inquiry queue", "0"),
            HigherKpi("Active client profiles", data.Clients.Count, 1, "", "Count of active client profiles", data.Clients.Count, "Current client directory", "0")
        ];
    }

    private static BusinessKpi HigherKpi(string name, double value, double target, string suffix, string formula, int sampleSize, string period, string format = "0.#")
    {
        var status = sampleSize == 0 ? BusinessKpiStatus.NoData
            : value >= target ? BusinessKpiStatus.OnTrack
            : value >= target * 0.8 ? BusinessKpiStatus.Watch
            : BusinessKpiStatus.NeedsAttention;
        return new BusinessKpi(name, sampleSize == 0 ? "-" : $"{value.ToString(format)}{suffix}", $">= {target.ToString(format)}{suffix}", period, formula, status, sampleSize);
    }

    private static BusinessKpi LowerKpi(string name, double value, double target, string suffix, string formula, int sampleSize, string period, string format = "0.#")
    {
        var status = sampleSize == 0 ? BusinessKpiStatus.NoData
            : value <= target ? BusinessKpiStatus.OnTrack
            : value <= target * 1.25 ? BusinessKpiStatus.Watch
            : BusinessKpiStatus.NeedsAttention;
        return new BusinessKpi(name, sampleSize == 0 ? "-" : $"{value.ToString(format)}{suffix}", $"<= {target.ToString(format)}{suffix}", period, formula, status, sampleSize);
    }

    private static BusinessIntelligenceChart AppointmentDemandTrend(ClinicData data, DateTime today)
    {
        var start = StartOfWeek(today.AddDays(-14));
        var points = Enumerable.Range(0, 9).Select(index =>
        {
            var week = start.AddDays(index * 7);
            var count = data.Appointments.Count(x => x.Status != "Cancelled" && x.Date.Date >= week && x.Date.Date < week.AddDays(7));
            return new BusinessIntelligencePoint(week.ToString("MMM d"), count, Detail: $"{count} non-cancelled bookings");
        }).ToArray();
        var peak = points.OrderByDescending(x => x.PrimaryValue).First();
        return new BusinessIntelligenceChart(
            "Appointment demand trend",
            $"Weekly booked visits from {start:MMM d} to {start.AddDays(62):MMM d, yyyy}",
            "When will the clinic face its highest scheduled demand?",
            "Booked visits", "", "visits", BusinessChartType.Line, points,
            data.Appointments.Count,
            peak.PrimaryValue == 0 ? "No appointment demand is recorded in the nine-week planning window."
                : $"The week of {peak.Label} carries the peak load at {peak.PrimaryValue:0} booked visits.",
            peak.PrimaryValue == 0 ? "Begin recording appointment dates to establish a capacity baseline."
                : $"Review front-desk and clinical coverage before the week of {peak.Label}; compare staffing with the {peak.PrimaryValue:0}-visit load.",
            "Appointments.Date and Appointments.Status");
    }

    private static BusinessIntelligenceChart AppointmentPipeline(ClinicData data)
    {
        string[] statuses = ["Scheduled", "Confirmed", "Checked in", "Pending", "Completed", "Cancelled"];
        var points = statuses.Select(status => new BusinessIntelligencePoint(status,
            data.Appointments.Count(x => string.Equals(x.Status, status, StringComparison.OrdinalIgnoreCase)),
            Detail: $"Appointments currently marked {status.ToLowerInvariant()}")).ToArray();
        var total = data.Appointments.Count;
        var completed = data.Appointments.Count(x => x.Status == "Completed");
        var cancelled = data.Appointments.Count(x => x.Status == "Cancelled");
        var completionRate = Percentage(completed, total);
        return new BusinessIntelligenceChart(
            "Appointment pipeline",
            "Current appointment distribution by operational status",
            "Where are visits accumulating or dropping out of the care journey?",
            "Appointments", "", "records", BusinessChartType.Column, points, total,
            total == 0 ? "No appointment pipeline is available yet."
                : $"Completed visits represent {completionRate:0}% of all records; {cancelled} {(cancelled == 1 ? "visit is" : "visits are")} cancelled.",
            total == 0 ? "Record appointment status changes so the team can measure throughput."
                : "Prioritize Pending and Scheduled records for confirmation, then investigate cancellation reasons outside this report when the count rises.",
            "Appointments.Status");
    }

    private static BusinessIntelligenceChart ServiceDemand(ClinicData data)
    {
        var points = data.Appointments
            .GroupBy(x => string.IsNullOrWhiteSpace(x.Service) ? "Unspecified" : x.Service)
            .Select(group => new BusinessIntelligencePoint(group.Key, group.Count(), Detail: $"{group.Count()} appointments"))
            .OrderByDescending(x => x.PrimaryValue).ThenBy(x => x.Label)
            .Take(6).ToArray();
        var leader = points.FirstOrDefault();
        var share = leader is null ? 0 : Percentage(leader.PrimaryValue, data.Appointments.Count);
        return new BusinessIntelligenceChart(
            "Service demand portfolio",
            "Top six services by booked appointment volume",
            "Which services drive the greatest share of clinic demand?",
            "Appointments", "", "bookings", BusinessChartType.HorizontalBar, points, data.Appointments.Count,
            leader is null ? "No service demand has been recorded."
                : $"{leader.Label} leads demand with {leader.PrimaryValue:0} bookings, or {share:0}% of appointment volume.",
            leader is null ? "Capture a service on every appointment to make demand planning possible."
                : $"Protect capacity and supplies for {leader.Label}; use the remaining service mix to balance veterinarian schedules.",
            "Appointments.Service");
    }

    private static BusinessIntelligenceChart InquiryBacklog(ClinicData data)
    {
        var points = data.Inquiries
            .GroupBy(x => string.IsNullOrWhiteSpace(x.Category) ? "Unspecified" : x.Category)
            .Select(group =>
            {
                var open = group.Count(x => !IsClosed(x.Status));
                var resolved = group.Count() - open;
                return new BusinessIntelligencePoint(group.Key, open, resolved, $"{open} open · {resolved} resolved");
            })
            .OrderByDescending(x => x.PrimaryValue).ThenByDescending(x => x.SecondaryValue)
            .Take(6).ToArray();
        var risk = points.FirstOrDefault();
        var totalOpen = points.Sum(x => x.PrimaryValue);
        return new BusinessIntelligenceChart(
            "Inquiry backlog by category",
            "Open versus resolved demand across the six largest inquiry themes",
            "Which client needs are creating the largest unresolved workload?",
            "Open", "Resolved", "inquiries", BusinessChartType.StackedHorizontal, points, data.Inquiries.Count,
            risk is null ? "No inquiry workload has been recorded."
                : $"{risk.Label} has the largest visible backlog at {risk.PrimaryValue:0} open inquiries; the displayed categories contain {totalOpen:0} open items.",
            risk is null ? "Classify inquiries consistently to establish a service-demand baseline."
                : $"Assign ownership and a response date to the oldest {risk.Label.ToLowerInvariant()} inquiries first.",
            "Inquiries.Category and Inquiries.Status");
    }

    private static BusinessIntelligenceChart ChannelEffectiveness(ClinicData data)
    {
        var points = data.Inquiries
            .GroupBy(x => string.IsNullOrWhiteSpace(x.Channel) ? "Unspecified" : x.Channel)
            .Select(group =>
            {
                var resolved = group.Count(x => IsClosed(x.Status));
                var rate = Percentage(resolved, group.Count());
                return new BusinessIntelligencePoint(group.Key, rate, Detail: $"{resolved} resolved of {group.Count()} received");
            })
            .OrderByDescending(x => x.PrimaryValue).ThenBy(x => x.Label)
            .ToArray();
        var best = points.FirstOrDefault();
        return new BusinessIntelligenceChart(
            "Inquiry channel effectiveness",
            "Resolution rate by client contact source",
            "Which intake channels are converting incoming questions into resolved outcomes?",
            "Resolution rate", "", "%", BusinessChartType.HorizontalBar, points, data.Inquiries.Count,
            best is null ? "No channel effectiveness data is available."
                : $"{best.Label} currently has the highest recorded resolution rate at {best.PrimaryValue:0}% ({best.Detail}).",
            best is null ? "Capture both inquiry channel and final status for every client contact."
                : $"Review the response practice used for {best.Label} and adapt it to lower-performing channels without assuming channel alone caused the result.",
            "Inquiries.Channel and Inquiries.Status");
    }

    private static BusinessIntelligenceChart CampaignReachAndClaims(ClinicData data)
    {
        var points = data.Promotions
            .Where(x => x.Reach > 0 || x.Claims > 0)
            .OrderByDescending(x => x.Reach)
            .ThenBy(x => x.Name)
            .Take(6)
            .Select(x => new BusinessIntelligencePoint(x.Name, Math.Max(0, x.Reach - x.Claims), x.Claims,
                $"{x.Reach} reached · {x.Claims} verified claims · {x.Status}"))
            .ToArray();
        var totalReach = data.Promotions.Sum(x => x.Reach);
        var totalClaims = data.Promotions.Sum(x => x.Claims);
        return new BusinessIntelligenceChart(
            "Campaign reach and claims",
            "Delivered reach split into unclaimed reach and verified claims",
            "How much recorded campaign reach has progressed to a verified claim?",
            "Unclaimed reach", "Verified claims", "clients", BusinessChartType.StackedHorizontal, points, data.Promotions.Count,
            totalReach == 0 ? "No campaign delivery has been recorded yet."
                : $"The promotion portfolio has reached {totalReach:N0} clients and produced {totalClaims:N0} verified claims.",
            totalReach == 0 ? "Prepare a consented audience and record each delivery before judging campaign performance."
                : "Review campaigns with high unclaimed reach first; test the offer, audience, and call to action before expanding delivery.",
            "Promotions.Reach, Promotions.Claims and Promotions.Status");
    }

    private static BusinessIntelligenceChart CampaignApprovalPipeline(ClinicData data)
    {
        string[] stages = ["Draft", "Needs review", "Approved", "Rejected"];
        var points = stages.Select(stage => new BusinessIntelligencePoint(stage,
            data.Promotions.Count(x => string.Equals(x.Approval, stage, StringComparison.OrdinalIgnoreCase)),
            Detail: $"Campaigns with {stage.ToLowerInvariant()} approval state")).ToArray();
        var waiting = data.Promotions.Count(x => x.Approval == "Needs review");
        return new BusinessIntelligenceChart(
            "Campaign approval pipeline",
            "Current campaign count by approval decision",
            "Where are promotional campaigns waiting in the governance workflow?",
            "Campaigns", "", "records", BusinessChartType.Column, points, data.Promotions.Count,
            data.Promotions.Count == 0 ? "No campaign approval records are available."
                : $"{waiting} campaign{(waiting == 1 ? " is" : "s are")} waiting for an administrator decision.",
            waiting == 0 ? "Keep the approval record complete before every campaign launch."
                : "Review the oldest submitted campaign first and record either approval or a clear change request.",
            "Promotions.Approval");
    }

    private static BusinessIntelligenceChart AudiencePerformance(ClinicData data)
    {
        var points = data.Promotions
            .GroupBy(x => string.IsNullOrWhiteSpace(x.Audience) ? "Unspecified" : x.Audience)
            .Select(group =>
            {
                var reach = group.Sum(x => x.Reach);
                var claims = group.Sum(x => x.Claims);
                return new BusinessIntelligencePoint(group.Key, Math.Max(0, reach - claims), claims,
                    $"{reach} reached · {claims} claims · {Percentage(claims, reach):0.0}% conversion");
            })
            .OrderByDescending(x => x.SecondaryValue)
            .ThenBy(x => x.Label)
            .Take(6)
            .ToArray();
        var leader = points.FirstOrDefault();
        return new BusinessIntelligenceChart(
            "Audience segment performance",
            "Reach and verified claims grouped by campaign audience",
            "Which consented audience segments are producing the most verified claims?",
            "Unclaimed reach", "Verified claims", "clients", BusinessChartType.StackedHorizontal, points, data.Promotions.Count,
            leader is null ? "No audience performance is available yet."
                : $"{leader.Label} has the highest recorded claim volume at {leader.SecondaryValue:0} claims ({leader.Detail}).",
            leader is null ? "Assign a defined audience to each campaign and record delivery results."
                : $"Use {leader.Label} as a benchmark, but compare offer and channel before attributing the result to audience alone.",
            "Promotions.Audience, Promotions.Reach and Promotions.Claims");
    }

    private static BusinessIntelligenceChart CampaignChannelEffectiveness(ClinicData data)
    {
        var points = data.Promotions
            .GroupBy(x => string.IsNullOrWhiteSpace(x.Channel) ? "Unspecified" : x.Channel)
            .Select(group =>
            {
                var reach = group.Sum(x => x.Reach);
                var claims = group.Sum(x => x.Claims);
                return new BusinessIntelligencePoint(group.Key, Percentage(claims, reach),
                    Detail: $"{claims} claims from {reach} reached across {group.Count()} campaigns");
            })
            .OrderByDescending(x => x.PrimaryValue)
            .ThenBy(x => x.Label)
            .ToArray();
        var leader = points.FirstOrDefault();
        return new BusinessIntelligenceChart(
            "Campaign channel effectiveness",
            "Portfolio conversion rate by campaign delivery channel",
            "Which delivery channels convert recorded reach into verified claims?",
            "Claim rate", "", "%", BusinessChartType.HorizontalBar, points, data.Promotions.Count,
            leader is null ? "No campaign channel performance is available."
                : $"{leader.Label} has the highest recorded claim rate at {leader.PrimaryValue:0.0}% ({leader.Detail}).",
            leader is null ? "Record a channel, reached count, and claims for every campaign."
                : $"Compare a similar offer and audience on another channel before making {leader.Label} the default.",
            "Promotions.Channel, Promotions.Reach and Promotions.Claims");
    }

    private static BusinessIntelligenceChart CampaignConversion(ClinicData data)
    {
        var points = data.Promotions
            .Where(x => x.Reach > 0 || x.Claims > 0)
            .Select(x => new BusinessIntelligencePoint(x.Name, x.Reach == 0 ? 0 : x.Claims / (double)x.Reach * 100,
                Detail: $"{x.Claims} claims from {x.Reach} reached · {x.Approval}"))
            .OrderByDescending(x => x.PrimaryValue).ThenBy(x => x.Label)
            .Take(6).ToArray();
        var leader = points.FirstOrDefault();
        return new BusinessIntelligenceChart(
            "Campaign conversion",
            "Claim rate for up to six promotional offers with recorded reach",
            "Which campaigns turn client reach into recorded claims most effectively?",
            "Claim rate", "", "%", BusinessChartType.HorizontalBar, points, data.Promotions.Count,
            leader is null ? "No promotion has both reach tracking and a measurable campaign result yet."
                : $"{leader.Label} leads recorded conversion at {leader.PrimaryValue:0.0}% ({leader.Detail}).",
            leader is null ? "Record audience reach and claims for each approved promotion before comparing performance."
                : $"Use {leader.Label} as a test benchmark, then compare audience, offer, and channel before increasing campaign spend or reach.",
            "Promotions.Reach, Promotions.Claims and Promotions.Approval");
    }

    private static BusinessIntelligenceChart ClientExperience(ClinicData data)
    {
        var points = data.Feedback
            .GroupBy(x => string.IsNullOrWhiteSpace(x.Category) ? "Unspecified" : x.Category)
            .Select(group =>
            {
                var average = group.Average(x => x.Rating);
                var open = group.Count(x => x.Status is "Open" or "Escalated");
                return new BusinessIntelligencePoint(group.Key, average / 5d * 100,
                    Detail: $"{average:0.0}/5 average · {group.Count()} cases · {open} open");
            })
            .OrderBy(x => x.PrimaryValue).ThenBy(x => x.Label)
            .Take(6).ToArray();
        var weakest = points.FirstOrDefault();
        return new BusinessIntelligenceChart(
            "Client experience by category",
            "Average feedback score normalized to a 100-point scale",
            "Which experience themes need the earliest operational improvement?",
            "Experience score", "", "%", BusinessChartType.HorizontalBar, points, data.Feedback.Count,
            weakest is null ? "No client experience ratings are available."
                : $"{weakest.Label} is the lowest-scoring recorded theme at {weakest.PrimaryValue:0}% ({weakest.Detail}).",
            weakest is null ? "Collect structured feedback after completed visits to establish an experience baseline."
                : $"Review open {weakest.Label.ToLowerInvariant()} cases, identify the repeated service issue, and assign one measurable improvement owner.",
            "Feedback.Category, Feedback.Rating and Feedback.Status");
    }

    private static bool IsClosed(string status) => status is "Resolved" or "Closed";

    private static DateTime StartOfWeek(DateTime date)
    {
        var offset = ((int)date.DayOfWeek + 6) % 7;
        return date.Date.AddDays(-offset);
    }

    private static double Percentage(double value, double total) => total <= 0 ? 0 : value / total * 100;
}
