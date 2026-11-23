using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Shapes;
using PawCare.Desktop.Models;
using PawCare.Desktop.Services;

namespace PawCare.Desktop.Views;

internal sealed record PromotionBiReportRow(
    string Campaign,
    string Code,
    string Audience,
    string Channel,
    DateTime StartDate,
    string Period,
    string Approval,
    string Status,
    int Reach,
    int Claims,
    string Conversion);

internal sealed record OperationalBiReportRow(string Area, string Metric, string Value, string Source);

public partial class MainWindow
{
    private UIElement BuildBusinessIntelligenceReportsPage()
    {
        var includesPromotions = _enabledFeatures.HasFlag(SubscriptionFeatures.Promotions);
        var charts = BusinessIntelligenceService.Build(Data, includesPromotions, DateTime.Today);
        var kpis = BusinessIntelligenceService.BuildKpis(Data, includesPromotions, DateTime.Today);
        var root = VerticalPage();
        root.Children.Add(PageHeader("Business intelligence",
            includesPromotions
                ? "Decision support for promotional performance, organized into KPI, dashboard, and reports."
                : "Branch decision support for operations, organized into KPI, dashboard, and reports."));

        var content = new ContentControl();
        var views = new UIElement[]
        {
            BuildBiKpiView(kpis, includesPromotions),
            BuildBiDashboardView(charts, includesPromotions),
            BuildBiReportsView(includesPromotions)
        };
        string[] labels = ["KPI", "Dashboard", "Reports"];
        var buttons = new List<Button>();
        var tabStrip = new StackPanel { Orientation = Orientation.Horizontal };
        void Activate(int selected)
        {
            content.Content = views[selected];
            for (var index = 0; index < buttons.Count; index++)
            {
                buttons[index].Background = index == selected ? Brush("GoldBrush") : Brushes.White;
                buttons[index].BorderBrush = index == selected ? Brush("GoldBrush") : Brush("LineBrush");
                buttons[index].Foreground = Brush("ForestDarkBrush");
            }
        }
        for (var index = 0; index < labels.Length; index++)
        {
            var selected = index;
            var button = new Button
            {
                Content = labels[index],
                Style = (Style)Application.Current.FindResource("OutlineButton"),
                MinWidth = 118,
                Margin = new Thickness(index == 0 ? 0 : 8, 0, 0, 0)
            };
            button.Click += (_, _) => Activate(selected);
            buttons.Add(button);
            tabStrip.Children.Add(button);
        }
        root.Children.Add(new Border
        {
            Background = Brush("SagePaleBrush"),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(8),
            Margin = new Thickness(0, 0, 0, 20),
            Child = tabStrip
        });
        root.Children.Add(content);
        Activate(0);
        return root;
    }

    private UIElement BuildBiKpiView(IReadOnlyList<BusinessKpi> kpis, bool includesPromotions)
    {
        var root = new StackPanel();
        root.Children.Add(SectionHeading("Key performance indicators",
            includesPromotions
                ? "Six measurable promotion outcomes with a formula, target, period, and status."
                : "Six measurable branch outcomes with a formula, target, period, and status."));
        root.Children.Add(new Border
        {
            Background = Brush("GoldSoftBrush"),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(16, 12, 16, 12),
            Margin = new Thickness(0, 0, 0, 16),
            Child = Text("Targets are explicit course baselines for evaluation. Green means the target is met; amber is near target; red needs action.", 10, Brush("ForestDarkBrush"))
        });

        var grid = new Grid();
        for (var column = 0; column < 3; column++)
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (var row = 0; row < 2; row++)
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        for (var index = 0; index < kpis.Count; index++)
        {
            var card = BuildBiKpiCard(kpis[index]);
            card.Margin = new Thickness(index % 3 == 0 ? 0 : 12, index < 3 ? 0 : 12, 0, 0);
            Grid.SetColumn(card, index % 3);
            Grid.SetRow(card, index / 3);
            grid.Children.Add(card);
        }
        root.Children.Add(grid);
        return root;
    }

    private static Border BuildBiKpiCard(BusinessKpi kpi)
    {
        var statusText = kpi.Status switch
        {
            BusinessKpiStatus.OnTrack => "On track",
            BusinessKpiStatus.Watch => "Watch",
            BusinessKpiStatus.NeedsAttention => "Needs action",
            _ => "No data"
        };
        var statusBackground = kpi.Status switch
        {
            BusinessKpiStatus.OnTrack => Brush("SageSoftBrush"),
            BusinessKpiStatus.Watch => Brush("GoldSoftBrush"),
            BusinessKpiStatus.NeedsAttention => new SolidColorBrush(Color.FromRgb(248, 233, 231)),
            _ => Brush("SagePaleBrush")
        };
        var statusForeground = kpi.Status == BusinessKpiStatus.NeedsAttention ? Brush("DangerBrush") : Brush("ForestDarkBrush");
        var top = new Grid();
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        top.Children.Add(Text(kpi.Name, 11, Brush("InkBrush"), FontWeights.SemiBold));
        var chip = new Border
        {
            Background = statusBackground,
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(8, 4, 8, 4),
            Child = Text(statusText, 8, statusForeground, FontWeights.SemiBold)
        };
        Grid.SetColumn(chip, 1);
        top.Children.Add(chip);

        var body = new StackPanel();
        body.Children.Add(top);
        body.Children.Add(new TextBlock
        {
            Text = kpi.DisplayValue,
            FontFamily = DisplayFont,
            FontSize = 29,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brush("ForestDarkBrush"),
            Margin = new Thickness(0, 15, 0, 11)
        });
        body.Children.Add(BiKpiDetail("Target", kpi.TargetDisplay));
        body.Children.Add(BiKpiDetail("Period", kpi.Period));
        body.Children.Add(BiKpiDetail("Formula", kpi.Formula));
        body.Children.Add(BiKpiDetail("Sample", kpi.SampleSize.ToString("N0")));
        var card = Panel(body, new Thickness(17));
        card.MinHeight = 220;
        return card;
    }

    private static TextBlock BiKpiDetail(string label, string value)
    {
        var text = new TextBlock { FontSize = 9, Foreground = Brush("MutedBrush"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) };
        text.Inlines.Add(new System.Windows.Documents.Run(label + ": ") { FontWeight = FontWeights.SemiBold, Foreground = Brush("ForestDarkBrush") });
        text.Inlines.Add(new System.Windows.Documents.Run(value));
        return text;
    }

    private UIElement BuildBiDashboardView(IReadOnlyList<BusinessIntelligenceChart> charts, bool includesPromotions)
    {
        var root = new StackPanel();
        var heading = SectionHeading("Six decision views",
            $"{(includesPromotions ? "Promotion" : "Operational")} data · {_session.TenantName} · generated {DateTime.Now:MMM d, yyyy h:mm tt}");
        heading.Margin = new Thickness(0, 4, 0, 12);
        root.Children.Add(heading);

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(18) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (var row = 0; row < 3; row++)
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        for (var index = 0; index < charts.Count; index++)
        {
            var card = BuildBiChartCard(charts[index]);
            card.Margin = new Thickness(0, 0, 0, 18);
            Grid.SetRow(card, index / 2);
            Grid.SetColumn(card, index % 2 == 0 ? 0 : 2);
            grid.Children.Add(card);
        }
        root.Children.Add(grid);
        return root;
    }

    private UIElement BuildBiReportsView(bool includesPromotions)
    {
        var root = new StackPanel();
        root.Children.Add(PageHeader("Detailed reports",
            includesPromotions
                ? "Filter campaign-level records, then export the complete KPI and dashboard evidence as CSV or print."
                : "Review branch summaries, then export the complete KPI and dashboard evidence as CSV or print.",
            ActionButton("Export detailed CSV", (_, _) => ExportReport(), false),
            ActionButton("Print BI report", (_, _) => PrintReport(), false)));
        if (!includesPromotions)
        {
            var kpis = BusinessIntelligenceService.BuildKpis(Data, false, DateTime.Today);
            var rows = kpis.Select(x => new OperationalBiReportRow("Operations", x.Name, x.DisplayValue, x.Formula)).ToArray();
            root.Children.Add(Table(rows,
                Column("AREA", nameof(OperationalBiReportRow.Area), 110),
                Column("METRIC", nameof(OperationalBiReportRow.Metric), 200),
                Column("VALUE", nameof(OperationalBiReportRow.Value), 120),
                Column("SOURCE / FORMULA", nameof(OperationalBiReportRow.Source), 360)));
            return root;
        }

        var rowsSource = Data.Promotions
            .OrderByDescending(x => x.StartDate)
            .Select(x => new PromotionBiReportRow(x.Name, x.Code, x.Audience, x.Channel, x.StartDate, x.Schedule,
                x.Approval, x.Status, x.Reach, x.Claims, x.Reach == 0 ? "0%" : $"{x.Claims / (double)x.Reach * 100:0.0}%"))
            .ToArray();
        var view = CollectionViewSource.GetDefaultView(rowsSource);
        var search = new TextBox { ToolTip = "Search campaign, code, audience, or channel", Width = 300, Style = (Style)Application.Current.FindResource("FieldTextBox") };
        var statuses = new[] { "All statuses" }.Concat(rowsSource.Select(x => x.Status).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct().OrderBy(x => x)).ToArray();
        var status = new ComboBox { ItemsSource = statuses, SelectedIndex = 0, Width = 155, Style = (Style)Application.Current.FindResource("FieldComboBox") };
        string[] periods = ["All periods", "Current month", "Last 30 days", "Last 90 days"];
        var period = new ComboBox { ItemsSource = periods, SelectedIndex = 0, Width = 155, Style = (Style)Application.Current.FindResource("FieldComboBox") };
        void ApplyFilter()
        {
            var term = search.Text.Trim();
            view.Filter = item => item is PromotionBiReportRow row
                && (string.IsNullOrWhiteSpace(term) || row.Campaign.Contains(term, StringComparison.OrdinalIgnoreCase)
                    || row.Code.Contains(term, StringComparison.OrdinalIgnoreCase)
                    || row.Audience.Contains(term, StringComparison.OrdinalIgnoreCase)
                    || row.Channel.Contains(term, StringComparison.OrdinalIgnoreCase))
                && (status.SelectedIndex <= 0 || row.Status == status.SelectedItem?.ToString())
                && (period.SelectedIndex switch
                {
                    1 => row.StartDate.Year == DateTime.Today.Year && row.StartDate.Month == DateTime.Today.Month,
                    2 => row.StartDate.Date >= DateTime.Today.AddDays(-30),
                    3 => row.StartDate.Date >= DateTime.Today.AddDays(-90),
                    _ => true
                });
        }
        search.TextChanged += (_, _) => ApplyFilter();
        status.SelectionChanged += (_, _) => ApplyFilter();
        period.SelectionChanged += (_, _) => ApplyFilter();

        var filters = new WrapPanel { Margin = new Thickness(0, 0, 0, 14) };
        filters.Children.Add(search);
        status.Margin = new Thickness(10, 0, 0, 0);
        period.Margin = new Thickness(10, 0, 0, 0);
        filters.Children.Add(status);
        filters.Children.Add(period);
        root.Children.Add(filters);
        root.Children.Add(Table(view,
            Column("CAMPAIGN", nameof(PromotionBiReportRow.Campaign), 190),
            Column("CODE", nameof(PromotionBiReportRow.Code), 90),
            Column("AUDIENCE", nameof(PromotionBiReportRow.Audience), 175),
            Column("CHANNEL", nameof(PromotionBiReportRow.Channel), 105),
            Column("PERIOD", nameof(PromotionBiReportRow.Period), 150),
            Column("APPROVAL", nameof(PromotionBiReportRow.Approval), 105),
            Column("STATUS", nameof(PromotionBiReportRow.Status), 90),
            Column("REACH", nameof(PromotionBiReportRow.Reach), 75),
            Column("CLAIMS", nameof(PromotionBiReportRow.Claims), 75),
            Column("CONVERSION", nameof(PromotionBiReportRow.Conversion), 100)));
        return root;
    }

    private Border BuildBiChartCard(BusinessIntelligenceChart chart)
    {
        var content = new StackPanel();
        content.Children.Add(new TextBlock
        {
            Text = chart.Title,
            FontFamily = DisplayFont,
            FontSize = 16,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brush("ForestDarkBrush"),
            TextWrapping = TextWrapping.Wrap
        });
        var subtitle = Text(chart.Subtitle, 10, Brush("MutedBrush"));
        subtitle.Margin = new Thickness(0, 3, 0, 0);
        content.Children.Add(subtitle);
        var question = Text(chart.BusinessQuestion, 11, Brush("InkBrush"), FontWeights.SemiBold);
        question.Margin = new Thickness(0, 12, 0, 4);
        content.Children.Add(question);
        var source = Text($"n={chart.SampleSize:N0} · Source: {chart.Source}", 9, Brush("MutedBrush"));
        source.Margin = new Thickness(0, 0, 0, 14);
        content.Children.Add(source);
        content.Children.Add(BuildBiChartVisual(chart));

        var interpretation = new StackPanel();
        interpretation.Children.Add(BiNarrative("Finding", chart.Finding));
        var action = BiNarrative("Recommended action", chart.RecommendedAction);
        action.Margin = new Thickness(0, 9, 0, 0);
        interpretation.Children.Add(action);
        content.Children.Add(new Border
        {
            Background = Brush("SagePaleBrush"),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(12),
            Margin = new Thickness(0, 16, 0, 0),
            Child = interpretation
        });
        var panel = Panel(content, new Thickness(18));
        panel.MinHeight = 410;
        return panel;
    }

    private static TextBlock BiNarrative(string label, string value)
    {
        var text = new TextBlock { FontSize = 10, Foreground = Brush("InkBrush"), TextWrapping = TextWrapping.Wrap, LineHeight = 16 };
        text.Inlines.Add(new System.Windows.Documents.Run(label + ": ") { FontWeight = FontWeights.SemiBold, Foreground = Brush("ForestDarkBrush") });
        text.Inlines.Add(new System.Windows.Documents.Run(value));
        return text;
    }

    private static UIElement BuildBiChartVisual(BusinessIntelligenceChart chart)
    {
        if (chart.Points.Count == 0)
        {
            return new Border
            {
                Height = 180,
                Background = new SolidColorBrush(Color.FromRgb(248, 249, 247)),
                CornerRadius = new CornerRadius(10),
                Child = new TextBlock
                {
                    Text = "Not enough recorded data to draw this graph yet.",
                    Foreground = Brush("MutedBrush"),
                    FontSize = 11,
                    TextWrapping = TextWrapping.Wrap,
                    TextAlignment = TextAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(24)
                }
            };
        }
        return chart.ChartType switch
        {
            BusinessChartType.Line => BuildBiLineChart(chart),
            BusinessChartType.Column => BuildBiColumnChart(chart),
            BusinessChartType.StackedHorizontal => BuildBiStackedChart(chart),
            _ => BuildBiHorizontalChart(chart)
        };
    }

    private static UIElement BuildBiLineChart(BusinessIntelligenceChart chart)
    {
        const double width = 560;
        const double height = 190;
        const double left = 34;
        const double right = 12;
        const double top = 18;
        const double bottom = 34;
        var canvas = new Canvas { Width = width, Height = height, Background = Brushes.Transparent };
        var plotWidth = width - left - right;
        var plotHeight = height - top - bottom;
        var maximum = Math.Max(1, chart.Points.Max(x => x.PrimaryValue));
        for (var line = 0; line <= 3; line++)
        {
            var y = top + plotHeight * line / 3d;
            canvas.Children.Add(new Line { X1 = left, X2 = width - right, Y1 = y, Y2 = y, Stroke = new SolidColorBrush(Color.FromRgb(229, 234, 230)), StrokeThickness = 1 });
        }
        var polyline = new Polyline { Stroke = Brush("ForestBrush"), StrokeThickness = 3, StrokeLineJoin = PenLineJoin.Round };
        var area = new Polygon { Fill = new SolidColorBrush(Color.FromArgb(34, 127, 139, 108)) };
        area.Points.Add(new Point(left, top + plotHeight));
        for (var index = 0; index < chart.Points.Count; index++)
        {
            var point = chart.Points[index];
            var x = left + (chart.Points.Count == 1 ? plotWidth / 2 : plotWidth * index / (chart.Points.Count - 1d));
            var y = top + plotHeight - point.PrimaryValue / maximum * plotHeight;
            polyline.Points.Add(new Point(x, y));
            area.Points.Add(new Point(x, y));
            var dot = new Ellipse { Width = 9, Height = 9, Fill = Brush("GoldBrush"), Stroke = Brush("ForestDarkBrush"), StrokeThickness = 1.5, ToolTip = $"{point.Label}: {point.PrimaryValue:0} {chart.Unit}" };
            Canvas.SetLeft(dot, x - 4.5); Canvas.SetTop(dot, y - 4.5); canvas.Children.Add(dot);
            var value = new TextBlock { Text = point.PrimaryValue.ToString("0"), FontSize = 9, Foreground = Brush("ForestDarkBrush"), FontWeight = FontWeights.SemiBold };
            Canvas.SetLeft(value, x - 7); Canvas.SetTop(value, Math.Max(0, y - 18)); canvas.Children.Add(value);
            var label = new TextBlock { Text = point.Label, FontSize = 8, Foreground = Brush("MutedBrush"), Width = 55, TextAlignment = TextAlignment.Center };
            Canvas.SetLeft(label, x - 27.5); Canvas.SetTop(label, height - 25); canvas.Children.Add(label);
        }
        area.Points.Add(new Point(width - right, top + plotHeight));
        canvas.Children.Insert(4, area);
        canvas.Children.Add(polyline);
        return new Viewbox { Height = 190, Stretch = Stretch.Fill, Child = canvas };
    }

    private static UIElement BuildBiColumnChart(BusinessIntelligenceChart chart)
    {
        var maximum = Math.Max(1, chart.Points.Max(x => x.PrimaryValue));
        var grid = new UniformGrid { Columns = chart.Points.Count, Height = 190 };
        foreach (var point in chart.Points)
        {
            var column = new Grid { Margin = new Thickness(4, 0, 4, 0) };
            column.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            column.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var barStack = new StackPanel { VerticalAlignment = VerticalAlignment.Bottom };
            barStack.Children.Add(new TextBlock { Text = point.PrimaryValue.ToString("0"), FontSize = 9, Foreground = Brush("ForestDarkBrush"), FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 4) });
            barStack.Children.Add(new Border
            {
                Height = Math.Max(4, point.PrimaryValue / maximum * 120),
                Background = Brush("SageBrush"),
                CornerRadius = new CornerRadius(5, 5, 1, 1),
                ToolTip = $"{point.Label}: {point.PrimaryValue:0} {chart.Unit}"
            });
            column.Children.Add(barStack);
            var label = new TextBlock { Text = point.Label, FontSize = 8, Foreground = Brush("MutedBrush"), TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, Margin = new Thickness(0, 7, 0, 0), MaxHeight = 34 };
            Grid.SetRow(label, 1); column.Children.Add(label); grid.Children.Add(column);
        }
        return grid;
    }

    private static UIElement BuildBiHorizontalChart(BusinessIntelligenceChart chart)
    {
        var panel = new StackPanel();
        var maximum = chart.Unit == "%" ? 100 : Math.Max(1, chart.Points.Max(x => x.PrimaryValue));
        foreach (var point in chart.Points)
        {
            var row = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
            var meta = new Grid();
            meta.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            meta.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            meta.Children.Add(new TextBlock { Text = point.Label, FontSize = 9, Foreground = Brush("InkBrush"), TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = point.Label });
            var value = new TextBlock { Text = chart.Unit == "%" ? $"{point.PrimaryValue:0.#}%" : $"{point.PrimaryValue:0}", FontSize = 9, FontWeight = FontWeights.SemiBold, Foreground = Brush("ForestDarkBrush") };
            Grid.SetColumn(value, 1); meta.Children.Add(value); row.Children.Add(meta);
            var bar = new Grid { Height = 9, Margin = new Thickness(0, 5, 0, 0), ToolTip = point.Detail };
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(0.001, point.PrimaryValue), GridUnitType.Star) });
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(0.001, maximum - point.PrimaryValue), GridUnitType.Star) });
            bar.Children.Add(new Border { Background = Brush("SageBrush"), CornerRadius = new CornerRadius(5) });
            var track = new Border { Background = new SolidColorBrush(Color.FromRgb(237, 240, 236)), CornerRadius = new CornerRadius(5) };
            Grid.SetColumn(track, 1); bar.Children.Add(track); row.Children.Add(bar);
            if (!string.IsNullOrWhiteSpace(point.Detail))
            {
                var detail = Text(point.Detail, 8, Brush("MutedBrush")); detail.Margin = new Thickness(0, 3, 0, 0); row.Children.Add(detail);
            }
            panel.Children.Add(row);
        }
        return panel;
    }

    private static UIElement BuildBiStackedChart(BusinessIntelligenceChart chart)
    {
        var panel = new StackPanel();
        var legend = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
        legend.Children.Add(BiLegend(Brush("SageBrush"), chart.PrimaryMetric));
        legend.Children.Add(BiLegend(Brush("GoldBrush"), chart.SecondaryMetric));
        panel.Children.Add(legend);
        foreach (var point in chart.Points)
        {
            var row = new StackPanel { Margin = new Thickness(0, 0, 0, 11) };
            row.Children.Add(new TextBlock { Text = point.Label, FontSize = 9, Foreground = Brush("InkBrush"), TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = point.Label });
            var total = Math.Max(1, point.PrimaryValue + point.SecondaryValue);
            var bar = new Grid { Height = 10, Margin = new Thickness(0, 5, 0, 0), ToolTip = point.Detail };
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(0.001, point.PrimaryValue), GridUnitType.Star) });
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(0.001, point.SecondaryValue), GridUnitType.Star) });
            bar.Children.Add(new Border { Background = Brush("SageBrush"), CornerRadius = point.SecondaryValue == 0 ? new CornerRadius(5) : new CornerRadius(5, 0, 0, 5) });
            var resolved = new Border { Background = Brush("GoldBrush"), CornerRadius = point.PrimaryValue == 0 ? new CornerRadius(5) : new CornerRadius(0, 5, 5, 0) };
            Grid.SetColumn(resolved, 1); bar.Children.Add(resolved); row.Children.Add(bar);
            var detail = Text($"{point.PrimaryValue:0} {chart.PrimaryMetric.ToLowerInvariant()} · {point.SecondaryValue:0} {chart.SecondaryMetric.ToLowerInvariant()} · {total:0} total", 8, Brush("MutedBrush"));
            detail.Margin = new Thickness(0, 3, 0, 0); row.Children.Add(detail); panel.Children.Add(row);
        }
        return panel;
    }

    private static Border BiLegend(Brush color, string label) => new()
    {
        Margin = new Thickness(0, 0, 14, 0),
        Child = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Children =
            {
                new Border { Width = 9, Height = 9, Background = color, CornerRadius = new CornerRadius(3), Margin = new Thickness(0, 1, 5, 0) },
                new TextBlock { Text = label, FontSize = 8, Foreground = Brush("MutedBrush") }
            }
        }
    };
}
