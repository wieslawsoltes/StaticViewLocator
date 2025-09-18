using System;

namespace StaticViewLocatorDemo.ViewModels;

public class ReportViewModel : ViewModelBase
{
    public ReportViewModel(string title, string summary, DateTime lastGenerated, string status)
    {
        Title = title;
        Summary = summary;
        LastGenerated = lastGenerated;
        Status = status;
    }

    public override string Title { get; }

    public string Summary { get; }

    public DateTime LastGenerated { get; }

    public string Status { get; }
}
