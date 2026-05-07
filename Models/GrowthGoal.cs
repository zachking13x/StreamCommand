namespace StreamCommand.Models;

public class GrowthGoal
{
    public string Label { get; set; } = "";
    public string Category { get; set; } = "";
    public double Current { get; set; }
    public double Target { get; set; }
    public string Unit { get; set; } = "";

    public double Percent => Target > 0 ? System.Math.Min(100, Current / Target * 100) : 0;
    public string PercentDisplay => $"{Percent:F0}%";
    public string CurrentDisplay => Current.ToString("N0");
    public string TargetDisplay => Target.ToString("N0");
    public bool IsComplete => Current >= Target;

}
