using System.Collections.Generic;

namespace StaticViewLocatorDemo.ViewModels;

public class DashboardViewModel : ViewModelBase
{
    public DashboardViewModel()
    {
        Metrics = new List<MetricViewModel>
        {
            new("Active Users", "people", 1280, "Users currently signed in across all platforms."),
            new("Conversion", "%", 3.7, "Percentage of visitors completing the onboarding journey."),
            new("Support Tickets", "open", 5, "Items waiting in the support queue."),
            new("Build Duration", "min", 7.4, "Average CI build time over the last 24 hours."),
        };
    }

    public override string Title => "Dashboard";

    public string Summary => "A snapshot of the most important application metrics.";

    public IReadOnlyList<MetricViewModel> Metrics { get; }
}
