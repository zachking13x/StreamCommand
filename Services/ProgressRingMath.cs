namespace StreamCommand.Services;

/// <summary>
/// Dash maths for an Ellipse used as a progress ring. WPF measures StrokeDashArray in
/// multiples of StrokeThickness, not pixels — the old dashboard passed pixel lengths, so the
/// ring drew full at any progress above ~20%.
/// </summary>
public static class ProgressRingMath
{
    /// <summary>(filled, gap) in stroke-thickness units for an ellipse of the given outer diameter.</summary>
    public static (double Filled, double Gap) Dashes(double fraction, double diameter, double thickness)
    {
        // The stroke is centred on the shape's inset edge: the path runs at diameter − thickness.
        var circumference = Math.PI * (diameter - thickness) / thickness;
        var filled = circumference * Math.Clamp(fraction, 0, 1);
        return (filled, circumference - filled + 0.001);
    }
}
