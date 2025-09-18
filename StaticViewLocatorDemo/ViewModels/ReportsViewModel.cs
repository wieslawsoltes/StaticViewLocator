using System;
using System.Collections.Generic;

namespace StaticViewLocatorDemo.ViewModels;

public class ReportsViewModel : ViewModelBase
{
    public ReportsViewModel()
    {
        Reports = new List<ReportViewModel>
        {
            new("Usage", "Daily active user counts split by platform.", DateTime.Now.AddMinutes(-32), "Up to date"),
            new("Infrastructure", "Server health with CPU and memory trends.", DateTime.Now.AddHours(-3), "Requires review"),
            new("Commerce", "Payments accepted and refunds processed in the last week.", DateTime.Now.AddDays(-1), "Regenerating"),
        };
    }

    public override string Title => "Reports";

    public IReadOnlyList<ReportViewModel> Reports { get; }
}
