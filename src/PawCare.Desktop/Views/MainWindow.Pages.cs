using System.Collections;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using PawCare.Desktop.Controls;
using PawCare.Desktop.Models;

namespace PawCare.Desktop.Views;

public partial class MainWindow
{
    private UIElement BuildDashboardPage()
    {
        if (_session.Role == UserRole.Veterinarian) return BuildVeterinarianDashboard();
        if (_session.Role == UserRole.Admin) return BuildAdminDashboard();
        if (_session.Role == UserRole.SuperAdmin) return BuildSuperAdminDashboard();

        var root = VerticalPage();
        var date = new Border
        {
            Background = Brushes.White,
            BorderBrush = Brush("LineBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(9),
            Padding = new Thickness(13, 10, 13, 10),
            Child = Text(DateTime.Today.ToString("dddd, MMMM d"), 10, Brush("MutedBrush"))
        };

        if (_session.Role == UserRole.Receptionist)
        {
            root.Children.Add(PageHeader("Front desk workspace", "Start the next task, manage today’s visits, and clear client follow-ups from one place.",
                date,
                PageIncluded("clients") ? ActionButton("Add client", (_, _) => OpenClientDialog(), false) : new StackPanel(),
                PageIncluded("appointments") ? ActionButton("Book appointment", (_, _) => OpenAppointmentDialog()) : new StackPanel()));
            root.Children.Add(BuildDashboardHero());
            root.Children.Add(BuildQuickStartPanel());
            var todayAppointments = Data.Appointments.Count(x => x.Date.Date == DateTime.Today);
            var openInquiries = Data.Inquiries.Count(x => x.Status is not "Resolved" and not "Closed");
            var followUpsDue = Data.Inquiries.Count(x => x.FollowUpDate?.Date <= DateTime.Today && x.Status is not "Resolved" and not "Closed")
                + Data.Feedback.Count(x => x.FollowUpDate?.Date <= DateTime.Today && x.Status != "Closed")
                + (PageIncluded("reminders") ? Data.Reminders.Count(x => x.DueDate.Date <= DateTime.Today && x.Status != "Sent") : 0);
            var openCases = Data.Feedback.Count(x => x.Status is not "Reviewed" and not "Closed");
            root.Children.Add(Metrics(
                MetricCard("calendar", todayAppointments.ToString(), "Appointments today", "Today’s schedule", Brush("SageSoftBrush")),
                MetricCard("message", openInquiries.ToString(), "Open inquiries", "Live workload", new SolidColorBrush(Color.FromRgb(232, 242, 244))),
                MetricCard("reminder", followUpsDue.ToString(), "Follow-ups due", followUpsDue > 0 ? "Needs attention" : "All clear", Brush("GoldSoftBrush")),
                MetricCard("feedback", openCases.ToString(), "Open client cases", "Complaints & feedback", new SolidColorBrush(Color.FromRgb(248, 233, 231)))));

            var columns = new Grid();
            columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.55, GridUnitType.Star) });
            columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
            columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.85, GridUnitType.Star) });
            var left = new StackPanel();
            left.Children.Add(BuildTodayAppointmentsPanel());
            left.Children.Add(BuildActivityPanel());
            columns.Children.Add(left);
            var right = new StackPanel();
            right.Children.Add(BuildFrontDeskActionCenter());
            right.Children.Add(BuildCarePulsePanel());
            Grid.SetColumn(right, 2);
            columns.Children.Add(right);
            root.Children.Add(columns);
        }
        else
        {
            root.Children.Add(PageHeader("Operations overview", "Monitor client experience, appointment flow, and team follow-through.", date,
                PageIncluded("reports") ? ActionButton("Open reports", (_, _) => Navigate("reports")) : ActionButton("Review feedback", (_, _) => Navigate("feedback"))));
            root.Children.Add(BuildDashboardHero());
            var escalations = Data.Feedback.Count(x => x.Status is "Open" or "Escalated");
            var experienceMetric = PageIncluded("satisfaction")
                ? MetricCard("heart", $"{(Data.Feedback.Count == 0 ? 0 : Data.Feedback.Average(x => x.Rating)):0.0}/5", "Client satisfaction", "Collected responses", Brush("GoldSoftBrush"))
                : MetricCard("message", Data.Inquiries.Count(x => x.Status is not "Resolved" and not "Closed").ToString(), "Open inquiries", "Current workload", Brush("GoldSoftBrush"));
            root.Children.Add(Metrics(
                MetricCard("calendar", Data.Appointments.Count(x => x.Date >= DateTime.Today).ToString(), "Upcoming appointments", "Current schedule", Brush("SageSoftBrush")),
                experienceMetric,
                MetricCard("feedback", escalations.ToString(), "Open escalations", "Manager review", new SolidColorBrush(Color.FromRgb(248, 233, 231))),
                MetricCard("message", Data.Inquiries.Count(x => x.FollowUpDate?.Date <= DateTime.Today && x.Status is not "Resolved" and not "Closed").ToString(), "Inquiry follow-ups", "Due or overdue", new SolidColorBrush(Color.FromRgb(232, 242, 244)))));

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.4, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.9, GridUnitType.Star) });
            var left = new StackPanel();
            left.Children.Add(BuildTodayAppointmentsPanel());
            left.Children.Add(BuildVisitVolumePanel());
            grid.Children.Add(left);
            var right = new StackPanel();
            right.Children.Add(BuildManagerActionCenter());
            right.Children.Add(BuildTeamTargetsPanel());
            Grid.SetColumn(right, 2);
            grid.Children.Add(right);
            root.Children.Add(grid);
        }
        return root;
    }

    private UIElement BuildClientsPage()
    {
        var root = VerticalPage();
        var table = Table(Data.Clients,
            Column("CLIENT", nameof(ClientRecord.ClientName), 170), Column("PATIENT", nameof(ClientRecord.Patient), 160),
            Column("PHONE", nameof(ClientRecord.Phone), 130), Column("EMAIL", nameof(ClientRecord.Email), 200),
            Column("LOCATION", nameof(ClientRecord.Address), 140), Column("CONSENT", nameof(ClientRecord.ConsentLabel), 100));
        void OpenSelected()
        {
            if (table.SelectedItem is not ClientRecord record) { ShowSelectionRequired("client profile"); return; }
            if (_session.Role is UserRole.Receptionist or UserRole.Admin) OpenClientDialog(record);
            else OpenClientRecordModal(record);
        }
        table.MouseDoubleClick += (_, _) => OpenSelected();
        var actions = new List<UIElement> { ActionButton(_session.Role is UserRole.Receptionist or UserRole.Admin ? "Edit selected" : "View selected", (_, _) => OpenSelected(), false) };
        if (_session.Role == UserRole.Manager)
            actions.Add(ActionButton("Contact selected", (_, _) => OpenClientContactModal(table.SelectedItem as ClientRecord), false));
        if (_session.Role is UserRole.Receptionist or UserRole.Admin)
        {
            actions.Add(ActionButton("Archive selected", (_, _) => ArchiveSelected(Data.Clients, table.SelectedItem as ClientRecord, "clients", "client profile"), false));
            actions.Add(ActionButton("Add client", (_, _) => OpenClientDialog()));
        }
        var title = _session.Role == UserRole.Veterinarian ? "Patient directory" : _session.Role == UserRole.Manager ? "Client & patient directory" : "Client profiles";
        var subtitle = _session.Role == UserRole.Veterinarian
            ? "Review patient demographics, owner contacts, visit history, and recent clinical context."
            : _session.Role == UserRole.Manager
                ? "Find client contact information and patient context before operational follow-through."
                : "Keep owner contact details and patient information connected.";
        root.Children.Add(PageHeader(title, subtitle, actions.ToArray()));
        root.Children.Add(Metrics(
            MetricCard("clients", Data.Clients.Count.ToString(), "Client profiles", "Active CRM records", Brush("SageSoftBrush")),
            MetricCard("message", Data.Clients.Count(x => !string.IsNullOrWhiteSpace(x.Phone) && !string.IsNullOrWhiteSpace(x.Email)).ToString(), "Complete contacts", "Phone and email", new SolidColorBrush(Color.FromRgb(232, 242, 244))),
            MetricCard("message", Data.Clients.Count(x => !string.IsNullOrWhiteSpace(x.Email)).ToString(), "Email contacts", "Available for visit details", Brush("GoldSoftBrush")),
            MetricCard("calendar", Data.Clients.Count(x => x.LastVisit?.Date >= DateTime.Today.AddDays(-30)).ToString(), "Seen in 30 days", "Recent relationships", Brush("SageSoftBrush"))));
        var search = new TextBox { ToolTip = "Search client, patient, phone or email" };
        var view = ResetView(Data.Clients);
        search.TextChanged += (_, _) => { var term = search.Text.Trim(); view.Filter = item => item is ClientRecord c && (string.IsNullOrEmpty(term) || Contains(c.ClientName, term) || Contains(c.PetName, term) || Contains(c.Phone, term) || Contains(c.Email, term)); };
        root.Children.Add(Toolbar(search));
        root.Children.Add(Panel(table, new Thickness(0)));
        return root;
    }

    private UIElement BuildAppointmentsPage()
    {
        var root = VerticalPage();
        var appointments = _session.Role == UserRole.Veterinarian
            ? Data.Appointments.Where(x => Same(x.Veterinarian, _session.Name)).ToArray()
            : Data.Appointments.ToArray();
        var table = Table(appointments,
            Column("DATE & TIME", nameof(AppointmentRecord.Visit), 145), Column("CLIENT & PATIENT", nameof(AppointmentRecord.ClientPatient), 180),
            Column("SERVICE", nameof(AppointmentRecord.Service), 190), Column("VETERINARIAN", nameof(AppointmentRecord.Veterinarian), 160),
            Column("STATUS", nameof(AppointmentRecord.Status), 100), Column("NOTES", nameof(AppointmentRecord.Notes), 240));
        table.MouseDoubleClick += (_, _) => { if (table.SelectedItem is AppointmentRecord record) ReviewAppointment(record); };
        var actions = new List<UIElement> { ActionButton("Review selected", (_, _) => { if (table.SelectedItem is AppointmentRecord record) ReviewAppointment(record); else ShowSelectionRequired("appointment"); }, false) };
        if (_session.Role == UserRole.Receptionist)
        {
            actions.Add(ActionButton("Email reminder", (_, _) => OpenAppointmentEmail(table.SelectedItem as AppointmentRecord), false));
            actions.Add(ActionButton("＋ Book appointment", (_, _) => OpenAppointmentDialog()));
        }
        else if (_session.Role == UserRole.Admin)
            actions.Add(ActionButton("＋ Book appointment", (_, _) => OpenAppointmentDialog()));
        if (_session.Role is UserRole.Receptionist or UserRole.Admin)
        {
            actions.Add(ActionButton("Archive selected", (_, _) => ArchiveSelected(Data.Appointments, table.SelectedItem as AppointmentRecord, "appointments", "appointment"), false));
            actions.Add(ActionButton("Archived appointments", (_, _) => OpenArchivedAppointments(), false));
        }
        root.Children.Add(PageHeader(_session.Role == UserRole.Manager ? "Customer appointments" : "Appointments",
            _session.Role == UserRole.Manager ? "Monitor visit volume, service demand, and completion status." : "Book visits and keep every appointment detail accurate.", actions.ToArray()));
        if (_session.Role == UserRole.Receptionist)
        {
            root.Children.Add(Metrics(
                MetricCard("calendar", Data.Appointments.Count(x => x.Date.Date == DateTime.Today).ToString(), "Visits today", "Current schedule", Brush("SageSoftBrush")),
                MetricCard("check", Data.Appointments.Count(x => x.Date.Date == DateTime.Today && x.Status == "Checked in").ToString(), "Checked in", "Ready for care", new SolidColorBrush(Color.FromRgb(232, 242, 244))),
                MetricCard("calendar", Data.Appointments.Count(x => x.Date.Date >= DateTime.Today && x.Status == "Confirmed").ToString(), "Confirmed", "Upcoming visits", Brush("GoldSoftBrush")),
                MetricCard("reminder", Data.Appointments.Count(x => x.Date.Date >= DateTime.Today && x.Status is "Scheduled" or "Pending").ToString(), "Need confirmation", "Follow-up queue", new SolidColorBrush(Color.FromRgb(248, 233, 231)))));
        }
        else if (_session.Role == UserRole.Veterinarian)
        {
            var appointmentIdsWithRecords = Data.Consultations.Where(x => Same(x.Veterinarian, _session.Name)).Select(x => x.AppointmentId).ToHashSet(StringComparer.OrdinalIgnoreCase);
            root.Children.Add(Metrics(
                MetricCard("calendar", appointments.Count(x => x.Date.Date == DateTime.Today).ToString(), "Assigned today", "Clinical schedule", Brush("SageSoftBrush")),
                MetricCard("check", appointments.Count(x => x.Date.Date == DateTime.Today && x.Status == "Checked in").ToString(), "Checked in", "Ready for assessment", new SolidColorBrush(Color.FromRgb(232, 242, 244))),
                MetricCard("records", appointments.Count(x => x.Date.Date <= DateTime.Today && !appointmentIdsWithRecords.Contains(x.Id)).ToString(), "Records to start", "Clinical documentation", Brush("GoldSoftBrush")),
                MetricCard("medical", appointments.Count(x => x.Status == "Completed").ToString(), "Completed visits", "Current schedule", new SolidColorBrush(Color.FromRgb(232, 242, 244)))));
        }
        var search = new TextBox { ToolTip = "Search client, patient, service or veterinarian" };
        var filter = ChoiceFilter(["All statuses", "Scheduled", "Confirmed", "Checked in", "Pending", "Completed", "Cancelled"]);
        var view = ResetView(appointments);
        void Apply() { var term = search.Text.Trim(); var status = filter.SelectedItem?.ToString(); view.Filter = item => item is AppointmentRecord a && (string.IsNullOrEmpty(term) || Contains(a.ClientName, term) || Contains(a.PetName, term) || Contains(a.Service, term) || Contains(a.Veterinarian, term)) && (status == "All statuses" || a.Status == status); }
        search.TextChanged += (_, _) => Apply(); filter.SelectionChanged += (_, _) => Apply();
        root.Children.Add(Toolbar(search, filter));
        root.Children.Add(Panel(table, new Thickness(0)));
        return root;
    }

    private UIElement BuildInquiriesPage()
    {
        var root = VerticalPage();
        var open = Data.Inquiries.Count(x => x.Status is not "Resolved" and not "Closed");
        var due = Data.Inquiries.Count(x => x.FollowUpDate?.Date <= DateTime.Today && x.Status is not "Resolved" and not "Closed");
        var leads = Data.Inquiries.Count(x => x.Category == "New client");
        var resolvedPercent = Data.Inquiries.Count == 0 ? 0 : (Data.Inquiries.Count - open) * 100 / Data.Inquiries.Count;
        var table = Table(Data.Inquiries,
            Column("SUBJECT", nameof(InquiryRecord.Subject), 225), Column("CLIENT & CONTACT", nameof(InquiryRecord.ClientContact), 205),
            Column("SOURCE", nameof(InquiryRecord.Source), 145), Column("CATEGORY", nameof(InquiryRecord.Category), 125),
            Column("PRIORITY", nameof(InquiryRecord.Priority), 80), Column("FOLLOW-UP", nameof(InquiryRecord.FollowUp), 100),
            Column("STATUS", nameof(InquiryRecord.Status), 90), Column("OWNER", nameof(InquiryRecord.Owner), 110),
            Column("LAST CONTACT", nameof(InquiryRecord.LastContact), 155), Column("OUTCOME", nameof(InquiryRecord.LastContactOutcome), 180));
        void OpenSelected()
        {
            if (table.SelectedItem is not InquiryRecord record) { ShowSelectionRequired("inquiry"); return; }
            if (_session.Role == UserRole.Receptionist) OpenInquiryDialog(record);
            else OpenManagerInquiryModal(record);
        }
        table.MouseDoubleClick += (_, _) => OpenSelected();
        var actions = new List<UIElement> { ActionButton("Review selected", (_, _) => OpenSelected(), false) };
        if (_session.Role == UserRole.Manager)
            actions.Add(ActionButton("Contact client", (_, _) => OpenInquiryContactModal(table.SelectedItem as InquiryRecord), false));
        if (_session.Role == UserRole.Receptionist)
        {
            actions.Add(ActionButton("Log inquiry", (_, _) => OpenInquiryDialog()));
            actions.Add(ActionButton("Archive selected", (_, _) => ArchiveSelected(Data.Inquiries, table.SelectedItem as InquiryRecord, "inquiries", "inquiry"), false));
        }
        root.Children.Add(PageHeader(_session.Role == UserRole.Manager ? "Inquiry follow-ups" : "Client inquiries",
            _session.Role == UserRole.Manager
                ? "Review priority conversations, confirm ownership, and keep promised client responses on schedule."
                : "Capture every conversation as structured CRM data and make follow-through visible.", actions.ToArray()));
        root.Children.Add(Metrics(
            MetricCard("message", open.ToString(), "Open conversations", "Live workload", Brush("SageSoftBrush")),
            MetricCard("reminder", due.ToString(), "Follow-ups due", due > 0 ? "Includes overdue" : "All clear", Brush("GoldSoftBrush")),
            MetricCard("clients", leads.ToString(), "New client leads", "Captured inquiries", new SolidColorBrush(Color.FromRgb(232, 242, 244))),
            MetricCard("check", $"{resolvedPercent}%", "Resolution rate", "Current records", Brush("SageSoftBrush"))));
        var search = new TextBox { ToolTip = "Search inquiry, client, pet, contact or subject" };
        var filter = ChoiceFilter(["All statuses", "New", "Open", "Follow-up", "Resolved", "Closed"]);
        var view = ResetView(Data.Inquiries);
        void Apply() { var term = search.Text.Trim(); var status = filter.SelectedItem?.ToString(); view.Filter = item => item is InquiryRecord q && (string.IsNullOrEmpty(term) || Contains(q.Subject, term) || Contains(q.ClientName, term) || Contains(q.PetName, term) || Contains(q.Contact, term) || Contains(q.Category, term)) && (status == "All statuses" || q.Status == status); }
        search.TextChanged += (_, _) => Apply(); filter.SelectionChanged += (_, _) => Apply();
        root.Children.Add(Toolbar(search, filter));
        root.Children.Add(Panel(table, new Thickness(0)));
        return root;
    }

    private UIElement BuildRemindersPage()
    {
        var root = VerticalPage();
        var table = Table(Data.Reminders,
            Column("TYPE", nameof(ReminderRecord.Type), 185), Column("CLIENT & PATIENT", nameof(ReminderRecord.ClientPatient), 190),
            Column("CHANNEL", nameof(ReminderRecord.Channel), 100), Column("DUE", nameof(ReminderRecord.DueDate), 120),
            Column("STATUS", nameof(ReminderRecord.Status), 100), Column("MESSAGE", nameof(ReminderRecord.Message), 360));
        table.MouseDoubleClick += (_, _) => { if (table.SelectedItem is ReminderRecord record) OpenReminderDialog(record); };
        root.Children.Add(PageHeader("Reminders & follow-ups", "Schedule thoughtful touchpoints across the client care journey.",
            ActionButton("Record sent", (_, _) => { if (table.SelectedItem is ReminderRecord r) { if (MessageBox.Show(this, "Confirm that you sent this reminder through the selected channel.", "Record delivery", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes) { r.Status = "Sent"; PersistAndRefresh("reminders", "♢", "Reminder sent", $"{r.Type} · {r.ClientName}"); } } else ShowSelectionRequired("reminder"); }, false),
            ActionButton("Email reminder", (_, _) => OpenReminderEmail(table.SelectedItem as ReminderRecord), false),
            ActionButton("Archive selected", (_, _) => ArchiveSelected(Data.Reminders, table.SelectedItem as ReminderRecord, "reminders", "reminder"), false),
            ActionButton("＋ New reminder", (_, _) => OpenReminderDialog())));
        root.Children.Add(Metrics(
            MetricCard("reminder", Data.Reminders.Count(x => x.DueDate.Date <= DateTime.Today && x.Status != "Sent").ToString(), "Due now", "Requires follow-through", Brush("GoldSoftBrush")),
            MetricCard("calendar", Data.Reminders.Count(x => x.Status == "Scheduled").ToString(), "Scheduled", "Upcoming messages", Brush("SageSoftBrush")),
            MetricCard("check", Data.Reminders.Count(x => x.Status == "Sent").ToString(), "Completed", "Recorded as sent", new SolidColorBrush(Color.FromRgb(232, 242, 244))),
            MetricCard("message", Data.Reminders.Count(x => x.Channel == "Email").ToString(), "Email follow-ups", "Selected channel", Brush("SageSoftBrush"))));
        var search = new TextBox { ToolTip = "Search client, patient or reminder type" };
        var filter = ChoiceFilter(["All statuses", "Pending", "Scheduled", "Sent"]);
        var view = ResetView(Data.Reminders);
        void Apply() { var term = search.Text.Trim(); var status = filter.SelectedItem?.ToString(); view.Filter = item => item is ReminderRecord r && (string.IsNullOrEmpty(term) || Contains(r.ClientName, term) || Contains(r.PetName, term) || Contains(r.Type, term)) && (status == "All statuses" || r.Status == status); }
        search.TextChanged += (_, _) => Apply(); filter.SelectionChanged += (_, _) => Apply();
        root.Children.Add(Toolbar(search, filter));
        root.Children.Add(Panel(table, new Thickness(0)));
        return root;
    }

    private UIElement BuildFeedbackIntakePage()
    {
        var root = VerticalPage();
        var openComplaints = Data.Feedback.Count(x => x.CaseType == "Complaint" && x.Status is not "Reviewed" and not "Closed");
        var urgent = Data.Feedback.Count(x => x.Priority == "Urgent" && x.Status != "Closed");
        var dueToday = Data.Feedback.Count(x => x.FollowUpDate?.Date <= DateTime.Today && x.Status != "Closed");
        var average = Data.Feedback.Count == 0 ? 0 : Data.Feedback.Average(x => x.Rating);
        var table = Table(Data.Feedback,
            Column("CASE", nameof(FeedbackRecord.CaseType), 90), Column("CLIENT & CONTACT", nameof(FeedbackRecord.ClientContact), 205),
            Column("PATIENT", nameof(FeedbackRecord.PetName), 90), Column("SOURCE", nameof(FeedbackRecord.Channel), 90),
            Column("CATEGORY", nameof(FeedbackRecord.Category), 110), Column("PRIORITY", nameof(FeedbackRecord.Priority), 80),
            Column("RATING", nameof(FeedbackRecord.Stars), 105), Column("FOLLOW-UP", nameof(FeedbackRecord.FollowUp), 100),
            Column("STATUS", nameof(FeedbackRecord.Status), 90), Column("OWNER", nameof(FeedbackRecord.Owner), 110),
            Column("CLIENT NARRATIVE", nameof(FeedbackRecord.Comment), 330));
        table.MouseDoubleClick += (_, _) => { if (table.SelectedItem is FeedbackRecord record) OpenFeedbackDialog(record); };
        root.Children.Add(PageHeader("Complaints & feedback intake", "Collect the complete client experience record and route cases for accountable follow-up.",
            ActionButton("Open selected", (_, _) => { if (table.SelectedItem is FeedbackRecord record) OpenFeedbackDialog(record); else ShowSelectionRequired("client case"); }, false),
            ActionButton("Email client", (_, _) => OpenFeedbackEmail(table.SelectedItem as FeedbackRecord), false),
            ActionButton("Archive selected", (_, _) => ArchiveSelected(Data.Feedback, table.SelectedItem as FeedbackRecord, "feedback-intake", "client case"), false),
            ActionButton("＋ Record case", (_, _) => OpenFeedbackDialog())));
        root.Children.Add(Metrics(
            MetricCard("feedback", openComplaints.ToString(), "Open complaints", "Requires follow-through", new SolidColorBrush(Color.FromRgb(248, 233, 231))),
            MetricCard("feedback", urgent.ToString(), "Urgent cases", urgent > 0 ? "Escalate today" : "All clear", Brush("GoldSoftBrush")),
            MetricCard("reminder", dueToday.ToString(), "Follow-ups due", "Includes overdue", new SolidColorBrush(Color.FromRgb(232, 242, 244))),
            MetricCard("heart", $"{average:0.0}/5", "Average rating", "All collected cases", Brush("SageSoftBrush"))));
        var search = new TextBox { ToolTip = "Search case, client, contact, category or narrative" };
        var filter = ChoiceFilter(["All cases", "Complaint", "Feedback", "Compliment"]);
        var view = ResetView(Data.Feedback);
        void Apply()
        {
            var term = search.Text.Trim();
            var type = filter.SelectedItem?.ToString();
            view.Filter = item => item is FeedbackRecord f
                && (string.IsNullOrEmpty(term) || Contains(f.ClientName, term) || Contains(f.Contact, term) || Contains(f.PetName, term) || Contains(f.Category, term) || Contains(f.Comment, term) || Contains(f.Owner, term))
                && (type == "All cases" || f.CaseType == type);
        }
        search.TextChanged += (_, _) => Apply(); filter.SelectionChanged += (_, _) => Apply();
        root.Children.Add(Toolbar(search, filter));
        root.Children.Add(Panel(table, new Thickness(0)));
        return root;
    }

    private UIElement BuildPromotionsPage()
    {
        var root = VerticalPage();
        var table = Table(Data.Promotions,
            Column("CAMPAIGN", nameof(PromotionRecord.Name), 180), Column("CODE", nameof(PromotionRecord.Code), 105),
            Column("OFFER", nameof(PromotionRecord.OfferValue), 100), Column("AUDIENCE", nameof(PromotionRecord.Audience), 180),
            Column("CHANNEL", nameof(PromotionRecord.Channel), 110), Column("SCHEDULE", nameof(PromotionRecord.Schedule), 145),
            Column("STATUS", nameof(PromotionRecord.Status), 90), Column("REVIEW", nameof(PromotionRecord.ReviewStatus), 160),
            Column("PERFORMANCE", nameof(PromotionRecord.Performance), 190));
        table.MouseDoubleClick += (_, _) => { if (table.SelectedItem is PromotionRecord record) OpenPromotionDialog(record); };
        root.Children.Add(PageHeader("Campaigns & promotional offers", "Create a draft, submit it for review, deliver only to consented clients, and measure verified results.",
            ActionButton("Edit / review", (_, _) => { if (table.SelectedItem is PromotionRecord record) OpenPromotionDialog(record); else ShowSelectionRequired("promotional offer"); }, false),
            ActionButton("Preview audience", (_, _) => PreviewPromotionAudience(table.SelectedItem as PromotionRecord), false),
            ActionButton("Submit for approval", (_, _) => SubmitPromotion(table.SelectedItem as PromotionRecord), false),
            _session.Role == UserRole.Admin ? ActionButton("Approve", (_, _) => DecidePromotion(table.SelectedItem as PromotionRecord, true), false) : new StackPanel(),
            _session.Role == UserRole.Admin ? ActionButton("Request changes", (_, _) => DecidePromotion(table.SelectedItem as PromotionRecord, false), false) : new StackPanel(),
            ActionButton("Start campaign", (_, _) => StartPromotion(table.SelectedItem as PromotionRecord), false),
            ActionButton("Prepare audience", (_, _) => OpenCampaignDelivery(table.SelectedItem as PromotionRecord), false),
            ActionButton("Email one client", (_, _) => OpenPromotionEmail(table.SelectedItem as PromotionRecord), false),
            ActionButton("Record external reach", (_, _) => RecordPromotionMetric(table.SelectedItem as PromotionRecord, false), false),
            ActionButton("Record claim", (_, _) => RecordPromotionMetric(table.SelectedItem as PromotionRecord, true), false),
            ActionButton("Complete", (_, _) => CompletePromotion(table.SelectedItem as PromotionRecord), false),
            ActionButton("Archive selected", (_, _) => ArchiveSelected(Data.Promotions, table.SelectedItem as PromotionRecord, "promotions", "promotional offer"), false),
            ActionButton("Create campaign", (_, _) => OpenPromotionDialog())));
        var totalReach = Data.Promotions.Sum(x => x.Reach);
        var totalClaims = Data.Promotions.Sum(x => x.Claims);
        var conversion = totalReach == 0 ? 0 : (int)Math.Round(totalClaims * 100d / totalReach);
        root.Children.Add(Metrics(
            MetricCard("promotion", Data.Promotions.Count(x => x.Status == "Active" && x.Approval == "Approved").ToString(), "Active campaigns", "Ready for delivery", Brush("SageSoftBrush")),
            MetricCard("audit", Data.Promotions.Count(x => x.Approval == "Needs review").ToString(), "Waiting for review", "Admin decision needed", Brush("GoldSoftBrush")),
            MetricCard("clients", totalReach.ToString(), "Clients reached", $"{totalClaims} verified claims", new SolidColorBrush(Color.FromRgb(232, 242, 244))),
            MetricCard("reports", $"{conversion}%", "Campaign conversion", "Claims divided by reach", Brush("SageSoftBrush"))));
        var search = new TextBox { ToolTip = "Search campaign, offer code, audience, creator or reviewer" };
        var filter = ChoiceFilter(["All statuses", "Active", "Scheduled", "Draft", "Completed"]);
        var reviewFilter = ChoiceFilter(["All review states", "Draft", "Needs review", "Approved", "Rejected"]);
        var view = ResetView(Data.Promotions);
        void Apply() { var term = search.Text.Trim(); var status = filter.SelectedItem?.ToString(); var review = reviewFilter.SelectedItem?.ToString(); view.Filter = item => item is PromotionRecord p && (string.IsNullOrEmpty(term) || Contains(p.Name, term) || Contains(p.Code, term) || Contains(p.Audience, term) || Contains(p.CreatedBy, term) || Contains(p.ApprovedBy, term)) && (status == "All statuses" || p.Status == status) && (review == "All review states" || p.Approval == review); }
        search.TextChanged += (_, _) => Apply(); filter.SelectionChanged += (_, _) => Apply(); reviewFilter.SelectionChanged += (_, _) => Apply();
        root.Children.Add(Toolbar(search, filter, reviewFilter));
        root.Children.Add(Panel(table, new Thickness(0)));
        return root;
    }

    private UIElement BuildSatisfactionPage()
    {
        var root = VerticalPage();
        var average = Data.Feedback.Count == 0 ? 0 : Data.Feedback.Average(x => x.Rating);
        var positive = Data.Feedback.Count(x => x.Rating >= 4);
        var needsAttention = Data.Feedback.Count(x => x.Rating <= 3 && x.Status is not "Reviewed" and not "Closed");
        var followUps = Data.Feedback.Count(x => x.ConsentToFollowUp && x.Status is not "Reviewed" and not "Closed");
        root.Children.Add(PageHeader("Client satisfaction", "Understand experience patterns, then move directly to the cases that need follow-through.",
            ActionButton("Review feedback queue", (_, _) => Navigate("feedback"))));
        root.Children.Add(BuildSatisfactionSummary(average, positive, needsAttention));
        root.Children.Add(Metrics(
            MetricCard("heart", $"{average:0.0}/5", "Average rating", "Collected responses", Brush("GoldSoftBrush")),
            MetricCard("message", Data.Feedback.Count.ToString(), "Responses collected", "Stored cases", Brush("SageSoftBrush")),
            MetricCard("feedback", needsAttention.ToString(), "Cases needing attention", needsAttention > 0 ? "Manager review" : "Queue is clear", new SolidColorBrush(Color.FromRgb(248, 233, 231))),
            MetricCard("reminder", followUps.ToString(), "Consented follow-ups", "Open client commitments", new SolidColorBrush(Color.FromRgb(232, 242, 244)))));

        var columns = new Grid();
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.05, GridUnitType.Star) });
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.95, GridUnitType.Star) });
        columns.Children.Add(BuildRatingDistributionPanel());
        var actionPanel = BuildSatisfactionActionPanel();
        Grid.SetColumn(actionPanel, 2);
        columns.Children.Add(actionPanel);
        root.Children.Add(columns);
        root.Children.Add(BuildExperienceThemesPanel());
        return root;
    }

    private UIElement BuildFeedbackPage()
    {
        var root = VerticalPage();
        var urgent = Data.Feedback.Count(x => x.Priority == "Urgent" && x.Status != "Closed");
        var due = Data.Feedback.Count(x => x.FollowUpDate?.Date <= DateTime.Today && x.Status is not "Reviewed" and not "Closed");
        var unassigned = Data.Feedback.Count(x => x.Status is "Open" or "Escalated" && string.IsNullOrWhiteSpace(x.Owner));
        var contacted = Data.Feedback.Count(x => x.LastContactUtc is not null);
        var table = Table(Data.Feedback,
            Column("DATE", nameof(FeedbackRecord.Date), 105), Column("CASE", nameof(FeedbackRecord.CaseType), 90),
            Column("CLIENT & PATIENT", nameof(FeedbackRecord.ClientPatient), 185), Column("RATING", nameof(FeedbackRecord.Stars), 105),
            Column("CATEGORY", nameof(FeedbackRecord.Category), 110), Column("PRIORITY", nameof(FeedbackRecord.Priority), 80),
            Column("NARRATIVE", nameof(FeedbackRecord.Comment), 330), Column("STATUS", nameof(FeedbackRecord.Status), 90),
            Column("ESCALATION", nameof(FeedbackRecord.Escalation), 115), Column("OWNER", nameof(FeedbackRecord.Owner), 110),
            Column("LAST CONTACT", nameof(FeedbackRecord.LastContact), 155), Column("OUTCOME", nameof(FeedbackRecord.LastContactOutcome), 180));
        table.MouseDoubleClick += (_, _) => { if (table.SelectedItem is FeedbackRecord record) OpenFeedbackDialog(record); };
        root.Children.Add(PageHeader("Feedback & escalations", "Review client concerns, assign follow-through, and close the loop.",
            ActionButton("Review selected", (_, _) => { if (table.SelectedItem is FeedbackRecord record) OpenFeedbackDialog(record); else ShowSelectionRequired("client case"); }, false),
            ActionButton("Contact client", (_, _) => OpenFeedbackContactModal(table.SelectedItem as FeedbackRecord), false),
            ActionButton("Archive selected", (_, _) => ArchiveSelected(Data.Feedback, table.SelectedItem as FeedbackRecord, "feedback", "client case"), false),
            ActionButton("＋ Record feedback", (_, _) => OpenFeedbackDialog())));
        root.Children.Add(Metrics(
            MetricCard("feedback", urgent.ToString(), "Urgent escalations", urgent > 0 ? "Act today" : "Queue is clear", new SolidColorBrush(Color.FromRgb(248, 233, 231))),
            MetricCard("reminder", due.ToString(), "Follow-ups due", "Today or overdue", Brush("GoldSoftBrush")),
            MetricCard("staff", unassigned.ToString(), "Unassigned cases", unassigned > 0 ? "Assign an owner" : "Ownership complete", new SolidColorBrush(Color.FromRgb(232, 242, 244))),
            MetricCard("message", contacted.ToString(), "Cases contacted", "Recorded client touchpoints", Brush("SageSoftBrush"))));
        var search = new TextBox { ToolTip = "Search client, patient, category, owner or comment" };
        var filter = ChoiceFilter(["All statuses", "Open", "Escalated", "Reviewed", "Closed"]);
        var priorityFilter = ChoiceFilter(["All priorities", "Urgent", "High", "Medium", "Low"]);
        var view = ResetView(Data.Feedback);
        void Apply()
        {
            var term = search.Text.Trim();
            var status = filter.SelectedItem?.ToString();
            var priority = priorityFilter.SelectedItem?.ToString();
            view.Filter = item => item is FeedbackRecord f
                && (string.IsNullOrEmpty(term) || Contains(f.CaseType, term) || Contains(f.ClientName, term) || Contains(f.PetName, term) || Contains(f.Category, term) || Contains(f.Priority, term) || Contains(f.Comment, term) || Contains(f.Owner, term))
                && (status == "All statuses" || f.Status == status)
                && (priority == "All priorities" || f.Priority == priority);
        }
        search.TextChanged += (_, _) => Apply(); filter.SelectionChanged += (_, _) => Apply(); priorityFilter.SelectionChanged += (_, _) => Apply();
        root.Children.Add(Toolbar(search, filter, priorityFilter));
        root.Children.Add(Panel(table, new Thickness(0)));
        return root;
    }

    private UIElement BuildReportsPage()
    {
        return BuildBusinessIntelligenceReportsPage();
    }

    private Border BuildTodayAppointmentsPanel()
    {
        var panel = new StackPanel();
        var todaysAppointments = Data.Appointments.Where(x => x.Date.Date == DateTime.Today).OrderBy(x => x.Time).ToArray();
        panel.Children.Add(SectionHeading("Today’s schedule", $"{todaysAppointments.Length} {(todaysAppointments.Length == 1 ? "visit" : "visits")} · double-click a row in Appointments to review details"));
        foreach (var item in todaysAppointments.Take(5))
            panel.Children.Add(InfoRow(item.Visit.Split('·').Last().Trim(), item.ClientPatient, $"{item.Service} · {item.Veterinarian}", item.Status));
        if (todaysAppointments.Length == 0)
            panel.Children.Add(EmptyState("calendar", "No visits scheduled today", "Use Book appointment to add the next client visit."));
        var openSchedule = ActionButton(todaysAppointments.Length > 5 ? $"View all {todaysAppointments.Length} appointments" : "Open appointment schedule", (_, _) => Navigate("appointments"), false);
        openSchedule.Margin = new Thickness(0, 14, 0, 0);
        panel.Children.Add(openSchedule);
        var border = Panel(panel);
        border.Margin = new Thickness(0, 0, 0, 16);
        return border;
    }

    private Border BuildDashboardHero()
    {
        var receptionist = _session.Role == UserRole.Receptionist;
        var openInquiries = Data.Inquiries.Count(x => x.Status is not "Resolved" and not "Closed");
        var followUps = Data.Inquiries.Count(x => x.FollowUpDate?.Date <= DateTime.Today && x.Status is not "Resolved" and not "Closed")
            + Data.Feedback.Count(x => x.FollowUpDate?.Date <= DateTime.Today && x.Status != "Closed")
            + (PageIncluded("reminders") ? Data.Reminders.Count(x => x.DueDate.Date <= DateTime.Today && x.Status != "Sent") : 0);
        var urgentCases = Data.Feedback.Count(x => x.Priority == "Urgent" && x.Status != "Closed");
        var nextAppointment = Data.Appointments
            .Where(x => x.Date.Date == DateTime.Today && x.Status is not "Completed" and not "Cancelled")
            .OrderBy(x => x.Time)
            .FirstOrDefault();

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.55, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.7, GridUnitType.Star) });

        var message = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        message.Children.Add(new TextBlock
        {
            Text = receptionist ? "Today’s care coordination" : "Clinic performance at a glance",
            FontFamily = DisplayFont,
            FontSize = 21,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brushes.White,
            Margin = new Thickness(0, 0, 0, 7)
        });
        message.Children.Add(Text(
            receptionist
                ? "Keep appointments, inquiries, complaints, feedback, and follow-ups connected from the first conversation."
                : "Monitor visit flow and client experience, then move directly to the work that needs attention.",
            11, new SolidColorBrush(Color.FromRgb(199, 210, 203))));
        var chips = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 17, 0, 0) };
        var visitsToday = Data.Appointments.Count(x => x.Date.Date == DateTime.Today);
        chips.Children.Add(HeroChip($"{visitsToday} {(visitsToday == 1 ? "visit" : "visits")} today"));
        chips.Children.Add(HeroChip($"{openInquiries} open inquiries"));
        chips.Children.Add(HeroChip(receptionist ? $"{followUps} follow-ups" : $"{urgentCases} urgent cases", urgentCases > 0 && !receptionist));
        message.Children.Add(chips);
        grid.Children.Add(message);

        var separator = new Border { Width = 1, Background = new SolidColorBrush(Color.FromArgb(42, 255, 255, 255)), Margin = new Thickness(24, 0, 24, 0) };
        Grid.SetColumn(separator, 1);
        grid.Children.Add(separator);

        var priority = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        priority.Children.Add(Text("Next priority", 10, Brush("GoldBrush"), FontWeights.SemiBold));
        if (receptionist && nextAppointment is not null)
        {
            priority.Children.Add(new TextBlock { Text = nextAppointment.PetName, FontFamily = DisplayFont, FontSize = 20, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White, Margin = new Thickness(0, 7, 0, 2) });
            priority.Children.Add(Text($"{nextAppointment.Service} · {nextAppointment.Visit.Split('·').Last().Trim()}", 10, new SolidColorBrush(Color.FromRgb(199, 210, 203))));
            priority.Children.Add(Text(nextAppointment.ClientName, 10, Brushes.White, FontWeights.SemiBold));
        }
        else
        {
            priority.Children.Add(new TextBlock { Text = urgentCases > 0 ? $"{urgentCases} case{(urgentCases == 1 ? "" : "s")}" : "All clear", FontFamily = DisplayFont, FontSize = 20, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White, Margin = new Thickness(0, 7, 0, 2) });
            priority.Children.Add(Text(urgentCases > 0 ? "Urgent experience review" : "No urgent escalations", 10, new SolidColorBrush(Color.FromRgb(199, 210, 203))));
            priority.Children.Add(Text("Open feedback queue", 10, Brushes.White, FontWeights.SemiBold));
        }
        Grid.SetColumn(priority, 2);
        grid.Children.Add(priority);

        return new Border
        {
            Background = Brush("ForestBrush"),
            CornerRadius = new CornerRadius(16),
            Padding = new Thickness(25, 22, 25, 22),
            Margin = new Thickness(0, 0, 0, 18),
            Child = grid
        };
    }

    private static Border HeroChip(string value, bool urgent = false)
    {
        return new Border
        {
            Background = urgent ? Brush("GoldBrush") : new SolidColorBrush(Color.FromArgb(24, 255, 255, 255)),
            BorderBrush = urgent ? Brush("GoldBrush") : new SolidColorBrush(Color.FromArgb(36, 255, 255, 255)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(9),
            Padding = new Thickness(10, 6, 10, 6),
            Margin = new Thickness(0, 0, 8, 0),
            Child = Text(value, 9, urgent ? Brush("ForestDarkBrush") : Brushes.White, FontWeights.SemiBold)
        };
    }

    private Border BuildFrontDeskActionCenter()
    {
        var openInquiries = Data.Inquiries.Count(x => x.Status is not "Resolved" and not "Closed");
        var openCases = Data.Feedback.Count(x => x.CaseType is "Complaint" or "Feedback" && x.Status is not "Reviewed" and not "Closed");
        var followUps = Data.Reminders.Count(x => x.Status != "Sent");

        var panel = new StackPanel();
        panel.Children.Add(SectionHeading("Front desk action center", "Open each queue without searching the menu"));
        if (PageIncluded("inquiries")) panel.Children.Add(ActionQueueButton("message", "Client inquiries", "Review and respond to open conversations", openInquiries, (_, _) => Navigate("inquiries")));
        if (PageIncluded("feedback-intake")) panel.Children.Add(ActionQueueButton("feedback", "Complaints & feedback", "Capture details and maintain follow-through", openCases, (_, _) => Navigate("feedback-intake"), openCases > 0));
        if (PageIncluded("reminders")) panel.Children.Add(ActionQueueButton("reminder", "Reminders & follow-ups", "Complete scheduled client commitments", followUps, (_, _) => Navigate("reminders")));
        if (PageIncluded("promotions")) panel.Children.Add(ActionQueueButton("promotion", "Promotional offers", "Manage consent-aware campaigns", Data.Promotions.Count(x => x.Status == "Active"), (_, _) => Navigate("promotions")));
        var border = Panel(panel, new Thickness(18, 18, 18, 12));
        border.Margin = new Thickness(0, 0, 0, 16);
        return border;
    }

    private Border BuildManagerActionCenter()
    {
        var escalations = Data.Feedback.Count(x => x.Status is "Open" or "Escalated");
        var unreviewed = Data.Feedback.Count(x => x.Status != "Reviewed" && x.Status != "Closed");
        var upcoming = Data.Appointments.Count(x => x.Date >= DateTime.Today && x.Status != "Completed");
        var inquiryFollowUps = Data.Inquiries.Count(x => x.FollowUpDate?.Date <= DateTime.Today && x.Status is not "Resolved" and not "Closed");

        var panel = new StackPanel();
        panel.Children.Add(SectionHeading("Management action center", "Move from client context to accountable follow-through"));
        if (PageIncluded("clients")) panel.Children.Add(ActionQueueButton("clients", "Client & patient directory", "Check contact and patient context", Data.Clients.Count, (_, _) => Navigate("clients")));
        if (PageIncluded("appointments")) panel.Children.Add(ActionQueueButton("calendar", "Appointment operations", "Review the upcoming visit pipeline", upcoming, (_, _) => Navigate("appointments")));
        if (PageIncluded("inquiries")) panel.Children.Add(ActionQueueButton("message", "Inquiry follow-ups", "Review due and overdue responses", inquiryFollowUps, (_, _) => Navigate("inquiries"), inquiryFollowUps > 0));
        if (PageIncluded("feedback")) panel.Children.Add(ActionQueueButton("feedback", "Feedback & escalations", "Assign ownership and close the loop", escalations, (_, _) => Navigate("feedback"), escalations > 0));
        if (PageIncluded("satisfaction")) panel.Children.Add(ActionQueueButton("heart", "Client satisfaction", "Review collected experience signals", unreviewed, (_, _) => Navigate("satisfaction")));
        if (PageIncluded("reports")) panel.Children.Add(ActionQueueButton("reports", "Reports", "Turn CRM activity into decisions", Data.Feedback.Count, (_, _) => Navigate("reports")));
        var border = Panel(panel, new Thickness(18, 18, 18, 12));
        border.Margin = new Thickness(0, 0, 0, 16);
        return border;
    }

    private static Button ActionQueueButton(string iconKey, string title, string subtitle, int count, RoutedEventHandler click, bool attention = false)
    {
        var content = new Grid();
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var icon = new Border
        {
            Width = 34,
            Height = 34,
            CornerRadius = new CornerRadius(9),
            Background = attention ? Brush("GoldSoftBrush") : Brush("SagePaleBrush"),
            Child = CreateIcon(iconKey, Brush("ForestBrush"), 16)
        };
        content.Children.Add(icon);

        var copy = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 10, 0) };
        copy.Children.Add(Text(title, 10.5, Brush("InkBrush"), FontWeights.SemiBold));
        copy.Children.Add(Text(subtitle, 8.5, Brush("MutedBrush")));
        Grid.SetColumn(copy, 1);
        content.Children.Add(copy);

        var badge = new Border
        {
            MinWidth = 30,
            Background = attention ? Brush("GoldBrush") : Brush("SageSoftBrush"),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(8, 4, 8, 4),
            VerticalAlignment = VerticalAlignment.Center,
            Child = Text(count.ToString(), 9, Brush("ForestDarkBrush"), FontWeights.SemiBold)
        };
        ((TextBlock)badge.Child).HorizontalAlignment = HorizontalAlignment.Center;
        Grid.SetColumn(badge, 2);
        content.Children.Add(badge);

        var button = new Button
        {
            Content = content,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(4, 9, 4, 9),
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Cursor = System.Windows.Input.Cursors.Hand,
            FocusVisualStyle = (Style)Application.Current.FindResource("PawFocusVisual"),
            Template = RoundedButtonTemplate(10)
        };
        button.MouseEnter += (_, _) => button.Background = Brush("SagePaleBrush");
        button.MouseLeave += (_, _) => button.Background = Brushes.Transparent;
        button.Click += click;
        return button;
    }

    private Border BuildQuickStartPanel()
    {
        var panel = new StackPanel();
        panel.Children.Add(SectionHeading("Start a front desk task", "Create a record directly; use the navigation for queues, search, and review."));
        var grid = new UniformGrid { Columns = 3 };
        if (PageIncluded("appointments")) grid.Children.Add(QuickButton("calendar", "Book appointment", "Schedule a client visit", (_, _) => OpenAppointmentDialog()));
        if (PageIncluded("clients")) grid.Children.Add(QuickButton("clients", "Add client & patient", "Create a connected profile", (_, _) => OpenClientDialog()));
        if (PageIncluded("inquiries")) grid.Children.Add(QuickButton("message", "Log inquiry", "Capture a conversation", (_, _) => OpenInquiryDialog()));
        if (PageIncluded("feedback-intake")) grid.Children.Add(QuickButton("feedback", "Record feedback", "Create a client experience case", (_, _) => OpenFeedbackDialog()));
        if (PageIncluded("reminders")) grid.Children.Add(QuickButton("reminder", "Schedule follow-up", "Keep a client commitment", (_, _) => OpenReminderDialog()));
        if (PageIncluded("promotions")) grid.Children.Add(QuickButton("promotion", "Create offer", "Build a consent-aware campaign", (_, _) => OpenPromotionDialog()));
        panel.Children.Add(grid);
        var border = Panel(panel, new Thickness(18, 18, 10, 10));
        border.Margin = new Thickness(0, 0, 0, 18);
        return border;
    }

    private Border BuildActivityPanel()
    {
        var panel = new StackPanel();
        panel.Children.Add(SectionHeading("Recent activity", "Latest updates across the clinic"));
        foreach (var item in Data.Activities.Take(5)) panel.Children.Add(InfoRow(item.Time, item.Title, item.Detail, "Updated"));
        var border = Panel(panel); border.Margin = new Thickness(0, 0, 0, 16); return border;
    }

    private Border BuildCarePulsePanel()
    {
        var panel = new StackPanel();
        panel.Children.Add(SectionHeading("Record quality", "Calculated from the CRM data shown in each module"));
        panel.Children.Add(ProgressRow("Inquiries resolved", Percentage(Data.Inquiries.Count(x => x.Status is "Resolved" or "Closed"), Data.Inquiries.Count), 100, false, true));
        if (PageIncluded("reminders"))
            panel.Children.Add(ProgressRow("Reminders completed", Percentage(Data.Reminders.Count(x => x.Status == "Sent"), Data.Reminders.Count), 100, true, true));
        else
            panel.Children.Add(ProgressRow("Cases resolved", Percentage(Data.Feedback.Count(x => x.Status == "Closed"), Data.Feedback.Count), 100, true, true));
        panel.Children.Add(ProgressRow("Contact profiles complete", Percentage(Data.Clients.Count(x => !string.IsNullOrWhiteSpace(x.Phone) && !string.IsNullOrWhiteSpace(x.Email)), Data.Clients.Count), 100, false, true));
        return Panel(panel);
    }

    private Border BuildVisitVolumePanel() => ReportPanel("Upcoming visit volume", "Appointments grouped by scheduled date", Data.Appointments
        .Where(x => x.Date >= DateTime.Today)
        .GroupBy(x => x.Date.Date)
        .OrderBy(x => x.Key)
        .Select(x => (x.Key.ToString("ddd, MMM d"), x.Count()))
        .ToArray());

    private Border BuildTeamTargetsPanel() => PercentReportPanel("Operational follow-through", "Calculated from current clinic records", new[]
    {
        ("Appointments completed", Percentage(Data.Appointments.Count(x => x.Status == "Completed"), Data.Appointments.Count)),
        ("Inquiries resolved", Percentage(Data.Inquiries.Count(x => x.Status is "Resolved" or "Closed"), Data.Inquiries.Count)),
        ("Feedback reviewed", Percentage(Data.Feedback.Count(x => x.Status is "Reviewed" or "Closed"), Data.Feedback.Count))
    });

    private Border BuildExperienceThemesPanel() => PercentReportPanel("Experience by category", "Average rating from each collected feedback theme", Data.Feedback
        .GroupBy(x => x.Category)
        .OrderBy(x => x.Key)
        .Select(x => (x.Key, (int)Math.Round(x.Average(item => item.Rating) / 5d * 100)))
        .ToArray());

    private Border BuildSatisfactionSummary(double average, int positive, int needsAttention)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(190) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var score = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        score.Children.Add(new TextBlock { Text = $"{average:0.0}", FontFamily = DisplayFont, FontSize = 38, FontWeight = FontWeights.SemiBold, Foreground = Brush("GoldBrush") });
        score.Children.Add(Text("Average rating out of 5", 10, Brushes.White, FontWeights.SemiBold));
        score.Children.Add(Text($"Based on {Data.Feedback.Count} collected response{(Data.Feedback.Count == 1 ? "" : "s")}", 9, new SolidColorBrush(Color.FromRgb(199, 210, 203))));
        grid.Children.Add(score);

        var divider = new Border { Width = 1, Background = new SolidColorBrush(Color.FromArgb(45, 255, 255, 255)), Margin = new Thickness(22, 0, 22, 0) };
        Grid.SetColumn(divider, 1);
        grid.Children.Add(divider);

        var strongest = Data.Feedback.GroupBy(x => x.Category).OrderByDescending(x => x.Average(item => item.Rating)).FirstOrDefault();
        var weakest = Data.Feedback.GroupBy(x => x.Category).OrderBy(x => x.Average(item => item.Rating)).FirstOrDefault();
        var brief = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        brief.Children.Add(Text($"{positive} of {Data.Feedback.Count} responses are rated 4–5 stars", 14, Brushes.White, FontWeights.SemiBold));
        brief.Children.Add(Text(strongest is null ? "Collect more feedback to identify strengths." : $"Strongest signal: {strongest.Key} ({strongest.Average(x => x.Rating):0.0}/5)", 10, new SolidColorBrush(Color.FromRgb(199, 210, 203))));
        brief.Children.Add(Text(weakest is null ? "No improvement theme available yet." : $"Improvement opportunity: {weakest.Key} ({weakest.Average(x => x.Rating):0.0}/5)", 10, new SolidColorBrush(Color.FromRgb(199, 210, 203))));
        var attention = Text(needsAttention > 0 ? $"{needsAttention} case{(needsAttention == 1 ? "" : "s")} still need manager follow-through." : "No low-rating cases are waiting for review.", 10, needsAttention > 0 ? Brush("GoldBrush") : Brushes.White, FontWeights.SemiBold);
        attention.Margin = new Thickness(0, 8, 0, 0);
        brief.Children.Add(attention);
        Grid.SetColumn(brief, 2);
        grid.Children.Add(brief);

        return new Border { Background = Brush("ForestBrush"), CornerRadius = new CornerRadius(16), Padding = new Thickness(24, 21, 24, 21), Margin = new Thickness(0, 0, 0, 18), Child = grid };
    }

    private Border BuildRatingDistributionPanel()
    {
        var panel = new StackPanel();
        panel.Children.Add(SectionHeading("Rating distribution", "Every response in the current dataset"));
        for (var rating = 5; rating >= 1; rating--)
        {
            var count = Data.Feedback.Count(x => x.Rating == rating);
            panel.Children.Add(ProgressRow($"{rating} star{(rating == 1 ? "" : "s")}", count, Math.Max(1, Data.Feedback.Count), rating >= 4));
        }
        var border = Panel(panel);
        border.Margin = new Thickness(0, 0, 0, 16);
        return border;
    }

    private Border BuildSatisfactionActionPanel()
    {
        var panel = new StackPanel();
        panel.Children.Add(SectionHeading("Needs manager attention", "Low ratings and open experience cases"));
        var cases = Data.Feedback
            .Where(x => x.Rating <= 3 || x.Status is "Open" or "Escalated")
            .OrderBy(x => x.Rating)
            .ThenByDescending(x => x.Priority == "Urgent")
            .Take(5)
            .ToArray();
        foreach (var item in cases)
            panel.Children.Add(InfoRow($"{item.Rating}/5", $"{item.ClientName} · {item.PetName}", $"{item.Category} · {item.Comment}", item.Status));
        if (cases.Length == 0)
            panel.Children.Add(Text("No low-rating or open cases are waiting for review.", 10, Brush("MutedBrush")));
        var button = ActionButton("Open feedback & escalations", (_, _) => Navigate("feedback"), false);
        button.Margin = new Thickness(0, 14, 0, 0);
        panel.Children.Add(button);
        var border = Panel(panel);
        border.Margin = new Thickness(0, 0, 0, 16);
        return border;
    }

    private Border BuildManagementBrief(int openInquiries, int openExperience, double average)
    {
        var panel = new StackPanel();
        panel.Children.Add(SectionHeading("Management brief", "The current records point to these priorities"));
        var topInquiry = Data.Inquiries.GroupBy(x => x.Category).OrderByDescending(x => x.Count()).FirstOrDefault();
        var lowestTheme = Data.Feedback.GroupBy(x => x.Category).OrderBy(x => x.Average(item => item.Rating)).FirstOrDefault();
        var upcoming = Data.Appointments.Count(x => x.Date >= DateTime.Today && x.Status != "Completed");
        panel.Children.Add(InfoRow("Operations", "Upcoming visit pipeline", $"{upcoming} appointment{(upcoming == 1 ? "" : "s")} remain on the current schedule.", upcoming > 0 ? "Monitor" : "Clear"));
        panel.Children.Add(InfoRow("CRM", topInquiry is null ? "No inquiry pattern yet" : $"Top inquiry theme: {topInquiry.Key}", $"{openInquiries} open conversation{(openInquiries == 1 ? "" : "s")} require follow-through.", openInquiries > 0 ? "Review" : "Clear"));
        panel.Children.Add(InfoRow("Experience", lowestTheme is null ? "No experience pattern yet" : $"Lowest-rated theme: {lowestTheme.Key}", $"Average satisfaction is {average:0.0}/5 with {openExperience} open experience case{(openExperience == 1 ? "" : "s")}.", openExperience > 0 ? "Action" : "Stable"));
        var border = Panel(panel);
        border.Margin = new Thickness(0, 0, 0, 18);
        return border;
    }

    private Border BuildCampaignDecisionPanel()
    {
        var panel = new StackPanel();
        panel.Children.Add(SectionHeading("Campaign performance", "Recorded reach, claims, and conversion by promotional offer"));
        foreach (var promotion in Data.Promotions.OrderByDescending(x => x.Claims))
            panel.Children.Add(InfoRow(promotion.Code, promotion.Name, $"{promotion.Audience} · {promotion.Reach} reached · {promotion.Claims} claims · {promotion.Conversion}% conversion", promotion.Status));
        var border = Panel(panel);
        border.Margin = new Thickness(0, 0, 0, 16);
        return border;
    }

    private Border BuildExperienceSignalsPanel()
    {
        var panel = new StackPanel();
        panel.Children.Add(SectionHeading("Experience signals", "What needs manager attention"));
        foreach (var item in Data.Feedback.Where(x => x.Status is "Open" or "Escalated")) panel.Children.Add(InfoRow(item.Priority, $"{item.Category} · {item.ClientName}", item.Comment, item.Status));
        if (!Data.Feedback.Any(x => x.Status is "Open" or "Escalated")) panel.Children.Add(Text("No open feedback.", 11, Brush("MutedBrush")));
        var border = Panel(panel); border.Margin = new Thickness(0, 0, 0, 16); return border;
    }

    private static StackPanel VerticalPage() => new();

    private static ComboBox ChoiceFilter(IEnumerable<string> values)
    {
        var combo = new ComboBox { ItemsSource = values, SelectedIndex = 0 };
        return combo;
    }

    private static ICollectionView ResetView(IEnumerable source)
    {
        var view = CollectionViewSource.GetDefaultView(source);
        view.Filter = null;
        return view;
    }

    private static StackPanel SectionHeading(string title, string subtitle)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
        panel.Children.Add(new TextBlock { Text = title, FontFamily = DisplayFont, FontSize = 16, FontWeight = FontWeights.SemiBold, Foreground = Brush("ForestDarkBrush") });
        var description = Text(subtitle, 9, Brush("MutedBrush"));
        description.Margin = new Thickness(0, 3, 0, 0);
        panel.Children.Add(description);
        return panel;
    }

    private static Border InfoRow(string lead, string title, string detail, string status)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(66) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var leadText = Text(lead, 10, Brush("ForestBrush"), FontWeights.SemiBold); leadText.VerticalAlignment = VerticalAlignment.Center; grid.Children.Add(leadText);
        var copy = new StackPanel(); copy.Children.Add(Text(title, 11, Brush("InkBrush"), FontWeights.SemiBold)); copy.Children.Add(Text(detail, 9, Brush("MutedBrush"))); Grid.SetColumn(copy, 1); grid.Children.Add(copy);
        var statusText = new Border { Background = Brush("SagePaleBrush"), CornerRadius = new CornerRadius(8), Padding = new Thickness(8, 4, 8, 4), Child = Text(status, 8, Brush("ForestBrush"), FontWeights.SemiBold), VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(statusText, 2); grid.Children.Add(statusText);
        return new Border { BorderBrush = new SolidColorBrush(Color.FromRgb(237, 240, 237)), BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(2, 11, 2, 11), Child = grid };
    }

    private static Border EmptyState(string iconKey, string title, string detail)
    {
        var panel = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
        panel.Children.Add(new Border
        {
            Width = 42,
            Height = 42,
            Background = Brush("SagePaleBrush"),
            CornerRadius = new CornerRadius(12),
            Child = CreateIcon(iconKey, Brush("SageBrush"), 18),
            HorizontalAlignment = HorizontalAlignment.Center
        });
        var heading = Text(title, 11, Brush("InkBrush"), FontWeights.SemiBold);
        heading.HorizontalAlignment = HorizontalAlignment.Center;
        heading.Margin = new Thickness(0, 9, 0, 3);
        panel.Children.Add(heading);
        var description = Text(detail, 9, Brush("MutedBrush"));
        description.HorizontalAlignment = HorizontalAlignment.Center;
        panel.Children.Add(description);
        return new Border { Padding = new Thickness(16, 24, 16, 22), Child = panel };
    }

    private void ShowSelectionRequired(string recordType) =>
        ShowWorkflowNotice("Select a record", $"Select a {recordType} from the table first, then choose the action again.");

    private static Button QuickButton(string iconKey, string title, string subtitle, RoutedEventHandler click)
    {
        var content = new Grid();
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(38) });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        content.Children.Add(CreateIcon(iconKey, Brush("ForestBrush"), 17));
        var copy = new StackPanel { Margin = new Thickness(7, 0, 0, 0) };
        copy.Children.Add(Text(title, 10, Brush("InkBrush"), FontWeights.SemiBold));
        copy.Children.Add(Text(subtitle, 8.5, Brush("MutedBrush")));
        Grid.SetColumn(copy, 1);
        content.Children.Add(copy);
        var button = new Button
        {
            Content = content,
            Background = Brush("CreamBrush"),
            BorderBrush = Brush("LineBrush"),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(11),
            Margin = new Thickness(0, 0, 8, 8),
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Cursor = System.Windows.Input.Cursors.Hand,
            FocusVisualStyle = (Style)Application.Current.FindResource("PawFocusVisual"),
            Template = RoundedButtonTemplate(10)
        };
        button.MouseEnter += (_, _) => button.Background = Brush("SagePaleBrush");
        button.MouseLeave += (_, _) => button.Background = Brush("CreamBrush");
        button.Click += click;
        return button;
    }

    private static Grid ProgressRow(string label, int value, int maximum, bool gold = false, bool showPercent = false)
    {
        var grid = new Grid { Margin = new Thickness(0, 5, 0, 7) };
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var meta = new Grid(); meta.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); meta.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); meta.Children.Add(Text(label, 9, Brush("InkBrush"))); var valueText = Text(showPercent ? $"{Percentage(value, maximum)}%" : value.ToString(), 9, Brush("MutedBrush")); Grid.SetColumn(valueText, 1); meta.Children.Add(valueText); grid.Children.Add(meta);
        var progress = new ProgressBar { Minimum = 0, Maximum = maximum, Value = value, Height = 6, BorderThickness = new Thickness(0), Background = new SolidColorBrush(Color.FromRgb(237, 240, 236)), Foreground = gold ? Brush("GoldBrush") : Brush("SageBrush"), Margin = new Thickness(0, 6, 0, 0) }; Grid.SetRow(progress, 1); grid.Children.Add(progress); return grid;
    }

    private static Border ReportPanel(string title, string subtitle, IEnumerable<(string Label, int Value)> rows)
    {
        var panel = new StackPanel(); panel.Children.Add(SectionHeading(title, subtitle));
        var array = rows.ToArray(); var max = Math.Max(1, array.Select(x => x.Value).DefaultIfEmpty(1).Max());
        foreach (var row in array) panel.Children.Add(ProgressRow(row.Label, row.Value, max));
        var border = Panel(panel); border.Margin = new Thickness(0, 0, 0, 16); return border;
    }

    private static Border PercentReportPanel(string title, string subtitle, IEnumerable<(string Label, int Value)> rows)
    {
        var panel = new StackPanel();
        panel.Children.Add(SectionHeading(title, subtitle));
        var array = rows.ToArray();
        foreach (var row in array)
            panel.Children.Add(ProgressRow(row.Label, Math.Clamp(row.Value, 0, 100), 100, row.Value >= 80, true));
        if (array.Length == 0)
            panel.Children.Add(Text("No records are available for this breakdown yet.", 10, Brush("MutedBrush")));
        var border = Panel(panel);
        border.Margin = new Thickness(0, 0, 0, 16);
        return border;
    }

    private static int Percentage(int value, int total) => total <= 0 ? 0 : (int)Math.Round(value / (double)total * 100);

    private void ResetDemoData()
    {
        if (_session.Role != UserRole.Admin) return;
        if (MessageBox.Show(this, "Reset all locally stored clinic records to the original PawCare demo dataset?", "Reset demo data", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        _store.Reset();
        Navigate("dashboard");
    }

    private void ReviewAppointment(AppointmentRecord record)
    {
        if (_session.Role is UserRole.Receptionist or UserRole.Admin)
        {
            OpenAppointmentDialog(record);
            return;
        }

        var client = Data.Clients.FirstOrDefault(x => x.Id == record.ClientId)
            ?? Data.Clients.FirstOrDefault(x => Same(x.ClientName, record.ClientName) && Same(x.PetName, record.PetName));
        var consultation = Data.Consultations.FirstOrDefault(x => x.AppointmentId == record.Id && Same(x.Veterinarian, _session.Name));
        var veterinarian = _session.Role == UserRole.Veterinarian;
        var dialog = new RecordDialog(this, veterinarian ? "Assigned visit" : "Appointment review",
            veterinarian ? "Review the visit context before opening its protected clinical record." : "Review schedule, service, client, and patient details without changing the front-desk record.",
            [
                new("visit", "Date and time", FieldKind.ReadOnly, record.Visit),
                new("status", "Appointment status", FieldKind.ReadOnly, record.Status),
                new("client", "Client / owner", FieldKind.ReadOnly, record.ClientName),
                new("patient", "Patient", FieldKind.ReadOnly, record.PetName),
                new("service", "Service", FieldKind.ReadOnly, record.Service),
                new("vet", "Veterinarian", FieldKind.ReadOnly, record.Veterinarian),
                new("phone", "Client phone", FieldKind.ReadOnly, EmptyLabel(client?.Phone)),
                new("email", "Client email", FieldKind.ReadOnly, EmptyLabel(client?.Email)),
                new("notes", "Visit notes", FieldKind.ReadOnlyMultiline, string.IsNullOrWhiteSpace(record.Notes) ? "No visit notes recorded." : record.Notes, SpanColumns: true),
                new("clinical", "Clinical record", FieldKind.ReadOnly, consultation is null ? "Not started" : consultation.Status, SpanColumns: true)
            ], veterinarian ? (consultation is null ? "Start clinical record" : "Open clinical record") : "Close", showCancel: veterinarian);
        if (dialog.ShowModal() == true && veterinarian) OpenConsultationDialog(consultation);
    }

    private void OpenArchivedAppointments()
    {
        var archived = _store.GetArchivedAppointments();
        if (archived.Count == 0)
        {
            ShowWorkflowNotice("No archived appointments", "Archived appointments will appear here and can be restored to the active schedule.");
            return;
        }

        var labels = archived.ToDictionary(
            x => $"{x.Date:MMM d, yyyy} · {x.Time} · {x.PetName} · {x.Service} · {x.Id}",
            x => x,
            StringComparer.Ordinal);
        var dialog = new RecordDialog(this, "Archived appointments",
            "Choose an appointment to return to the active schedule. Its original details and audit history will be preserved.",
            [
                new("appointment", "Appointment", FieldKind.Choice, labels.Keys.First(), labels.Keys.ToArray(),
                    Help: $"{archived.Count} archived {(archived.Count == 1 ? "appointment" : "appointments")} in this branch.", SpanColumns: true),
                new("result", "Restore behavior", FieldKind.ReadOnly,
                    "Returns the record to Appointments and queues a restore event for cloud synchronization.", SpanColumns: true)
            ], "Restore appointment");
        if (dialog.ShowModal() != true) return;

        var record = labels[dialog.Text("appointment")];
        Data.Appointments.Add(record);
        PersistAndRefresh("appointments", "calendar", "Appointment restored", $"{record.PetName} · {record.Service}");
    }

    private void ArchiveSelected<T>(System.Collections.ObjectModel.ObservableCollection<T> collection, T? record, string page, string label) where T : class
    {
        if (record is null) { ShowSelectionRequired(label); return; }
        if (MessageBox.Show(this, $"Archive this {label}? The record will be hidden from the current queue and retained in SQL history.", "Archive record", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        collection.Remove(record);
        PersistAndRefresh(page, "records", $"{label} archived", label);
    }
}
