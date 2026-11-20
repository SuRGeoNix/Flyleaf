namespace Flyleaf.Controls.WPF;

internal static class FlyleafDockWidths
{
    internal static (double Left, double Right) Fit(double budget, double left, double leftMinimum, double right, double rightMinimum)
    {
        budget = Math.Max(0, budget);
        if (left + right <= budget) return (left, right);
        double minimum = leftMinimum + rightMinimum;
        if (budget <= minimum)
            return minimum == 0 ? (0, 0) : (budget * leftMinimum / minimum, budget * rightMinimum / minimum);
        double extra = left + right - minimum;
        double scale = extra > 0 ? (budget - minimum) / extra : 0;
        return (leftMinimum + (left - leftMinimum) * scale, rightMinimum + (right - rightMinimum) * scale);
    }
}
