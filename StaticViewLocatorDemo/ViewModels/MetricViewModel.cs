namespace StaticViewLocatorDemo.ViewModels;

public class MetricViewModel : ViewModelBase
{
    public MetricViewModel(string name, string unit, double value, string description)
    {
        Name = name;
        Unit = unit;
        Value = value;
        Description = description;
    }

    public string Name { get; }

    public string Unit { get; }

    public double Value { get; }

    public string Description { get; }

    public override string Title => Name;
}
