using System.IO;
using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using Microsoft.Win32;
using PawCare.Desktop.Models;
using PawCare.Desktop.Services;

namespace PawCare.Desktop.Views;

public partial class MainWindow
{
    private IReadOnlyList<(string Metric, string Value)> ReportRows()
    {
        var average = Data.Feedback.Count == 0 ? 0 : Data.Feedback.Average(x => x.Rating);
        return
        [
            ("Organization", _session.TenantName),
            ("Report date", DateTime.Now.ToString("yyyy-MM-dd HH:mm")),
            ("Upcoming appointments", Data.Appointments.Count(x => x.Date >= DateTime.Today && x.Status != "Cancelled").ToString()),
            ("Completed appointments", Data.Appointments.Count(x => x.Status == "Completed").ToString()),
            ("Open inquiries", Data.Inquiries.Count(x => x.Status is not "Resolved" and not "Closed").ToString()),
            ("Open complaints and feedback", Data.Feedback.Count(x => x.Status is "Open" or "Escalated").ToString()),
            ("Average satisfaction", $"{average:0.0}/5"),
            ("Active promotions", Data.Promotions.Count(x => x.Status == "Active" && x.Approval == "Approved").ToString())
        ];
    }

    private void ExportReport()
    {
        if (!RequireSubscribed("reports")) return;
        var dialog = new SaveFileDialog { Title = "Export business intelligence report", Filter = "CSV file (*.csv)|*.csv", FileName = $"PawCare-business-intelligence-{DateTime.Today:yyyy-MM-dd}.csv" };
        if (dialog.ShowDialog(this) != true) return;
        static string Csv(string value) => $"\"{value.Replace("\"", "\"\"")}\"";
        var charts = BusinessIntelligenceService.Build(Data,
            _enabledFeatures.HasFlag(SubscriptionFeatures.Promotions), DateTime.Today);
        var kpis = BusinessIntelligenceService.BuildKpis(Data,
            _enabledFeatures.HasFlag(SubscriptionFeatures.Promotions), DateTime.Today);
        var lines = new List<string>
        {
            "RecordType,Chart,Dimension,PrimaryMetric,PrimaryValue,SecondaryMetric,SecondaryValue,Unit,Detail"
        };
        lines.AddRange(ReportRows().Select(row =>
            $"Summary,,{Csv(row.Metric)},{Csv("Value")},{Csv(row.Value)},,,,"));
        lines.AddRange(kpis.Select(kpi =>
            $"KPI,,{Csv(kpi.Name)},{Csv("Actual")},{Csv(kpi.DisplayValue)},{Csv("Target")},{Csv(kpi.TargetDisplay)},,{Csv($"{kpi.Status} · {kpi.Period} · {kpi.Formula} · n={kpi.SampleSize}")}"));
        if (_enabledFeatures.HasFlag(SubscriptionFeatures.Promotions))
        {
            lines.AddRange(Data.Promotions.OrderByDescending(x => x.StartDate).Select(promotion => string.Join(",",
                "Campaign",
                Csv("Campaign performance"),
                Csv(promotion.Name),
                Csv("Reach"),
                promotion.Reach.ToString(CultureInfo.InvariantCulture),
                Csv("Claims"),
                promotion.Claims.ToString(CultureInfo.InvariantCulture),
                Csv("clients"),
                Csv($"{promotion.Code} · {promotion.Audience} · {promotion.Channel} · {promotion.Schedule} · {promotion.Approval} · {promotion.Status}"))));
        }
        foreach (var chart in charts)
        {
            lines.Add($"Metadata,{Csv(chart.Title)},{Csv("Business question")},,,,{Csv("")},,{Csv(chart.BusinessQuestion)}");
            lines.Add($"Metadata,{Csv(chart.Title)},{Csv("Finding")},,,,{Csv("")},,{Csv(chart.Finding)}");
            lines.Add($"Metadata,{Csv(chart.Title)},{Csv("Recommended action")},,,,{Csv("")},,{Csv(chart.RecommendedAction)}");
            lines.Add($"Metadata,{Csv(chart.Title)},{Csv("Source")},,,,{Csv("")},,{Csv(chart.Source)}");
            lines.AddRange(chart.Points.Select(point => string.Join(",",
                "Data",
                Csv(chart.Title),
                Csv(point.Label),
                Csv(chart.PrimaryMetric),
                point.PrimaryValue.ToString("0.##", CultureInfo.InvariantCulture),
                Csv(chart.SecondaryMetric),
                point.SecondaryValue.ToString("0.##", CultureInfo.InvariantCulture),
                Csv(chart.Unit),
                Csv(point.Detail))));
        }
        File.WriteAllText(dialog.FileName, string.Join(Environment.NewLine, lines), new UTF8Encoding(true));
        MessageBox.Show(this, "The detailed BI report was exported successfully with all six graph datasets, findings, actions, and sources.", "PawCare business intelligence", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void PrintReport()
    {
        if (!RequireSubscribed("reports")) return;
        var dialog = new PrintDialog();
        if (dialog.ShowDialog() != true) return;
        var charts = BusinessIntelligenceService.Build(Data,
            _enabledFeatures.HasFlag(SubscriptionFeatures.Promotions), DateTime.Today);
        var kpis = BusinessIntelligenceService.BuildKpis(Data,
            _enabledFeatures.HasFlag(SubscriptionFeatures.Promotions), DateTime.Today);
        var document = new FlowDocument { PagePadding = new Thickness(45), FontSize = 10, FontFamily = (FontFamily)Application.Current.FindResource("UiFont") };
        document.Blocks.Add(new Paragraph(new Run("PawCare business intelligence report")) { FontSize = 20, FontWeight = FontWeights.Bold, Foreground = Brush("ForestDarkBrush") });
        document.Blocks.Add(new Paragraph(new Run($"{_session.TenantName} · generated {DateTime.Now:MMM d, yyyy h:mm tt} · operational data only")) { Foreground = Brush("MutedBrush") });
        foreach (var row in ReportRows())
            document.Blocks.Add(new Paragraph(new Run($"{row.Metric}: ") { FontWeight = FontWeights.SemiBold }) { Inlines = { new Run(row.Value) }, Margin = new Thickness(0, 2, 0, 2) });
        document.Blocks.Add(new Paragraph(new Run("Key performance indicators")) { FontSize = 15, FontWeight = FontWeights.SemiBold, Foreground = Brush("ForestDarkBrush"), Margin = new Thickness(0, 18, 0, 5) });
        foreach (var kpi in kpis)
            document.Blocks.Add(new Paragraph(new Run($"{kpi.Name}: {kpi.DisplayValue} · target {kpi.TargetDisplay} · {kpi.Status} · {kpi.Period} · {kpi.Formula} · n={kpi.SampleSize}"))
                { Margin = new Thickness(0, 2, 0, 2), Foreground = Brush("InkBrush") });
        foreach (var chart in charts)
        {
            document.Blocks.Add(new Paragraph(new Run(chart.Title)) { FontSize = 15, FontWeight = FontWeights.SemiBold, Foreground = Brush("ForestDarkBrush"), Margin = new Thickness(0, 18, 0, 3), BreakPageBefore = false });
            document.Blocks.Add(new Paragraph(new Run(chart.BusinessQuestion)) { FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 4) });
            document.Blocks.Add(new Paragraph(new Run($"Finding: {chart.Finding}")) { Margin = new Thickness(0, 0, 0, 3) });
            document.Blocks.Add(new Paragraph(new Run($"Recommended action: {chart.RecommendedAction}")) { Margin = new Thickness(0, 0, 0, 6) });
            foreach (var point in chart.Points)
            {
                var secondary = string.IsNullOrWhiteSpace(chart.SecondaryMetric) ? "" : $" · {chart.SecondaryMetric}: {point.SecondaryValue:0.##}";
                document.Blocks.Add(new Paragraph(new Run($"{point.Label} — {chart.PrimaryMetric}: {point.PrimaryValue:0.##}{secondary} {chart.Unit} · {point.Detail}"))
                    { Margin = new Thickness(12, 1, 0, 1), Foreground = Brush("InkBrush") });
            }
            document.Blocks.Add(new Paragraph(new Run($"Source: {chart.Source} · n={chart.SampleSize:N0}")) { FontSize = 8, Foreground = Brush("MutedBrush"), Margin = new Thickness(0, 4, 0, 0) });
        }
        document.PageWidth = dialog.PrintableAreaWidth;
        document.PageHeight = dialog.PrintableAreaHeight;
        dialog.PrintDocument(((IDocumentPaginatorSource)document).DocumentPaginator, "PawCare business intelligence report");
    }
}
