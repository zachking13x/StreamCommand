using System.Globalization;
using System.Text;

namespace StreamCommand.Services;

/// <summary>
/// Pure price helpers. The Store only hands back localized display strings
/// ("$6.99", "6,99 €", "¥1,200"), so savings have to be derived from those.
/// </summary>
public static class PricingMath
{
    /// <summary>
    /// Pulls the numeric amount out of a Store-formatted price. A separator followed by
    /// exactly 1–2 trailing digits is the decimal point; any other separator is grouping.
    /// </summary>
    public static bool TryParseAmount(string? formatted, out decimal amount)
    {
        amount = 0;
        if (string.IsNullOrWhiteSpace(formatted)) return false;

        var kept = new StringBuilder();
        foreach (var c in formatted)
            if (char.IsAsciiDigit(c) || c is '.' or ',') kept.Append(c);

        var s = kept.ToString().Trim('.', ',');
        if (s.Length == 0 || !s.Any(char.IsAsciiDigit)) return false;

        int lastSep = s.LastIndexOfAny(['.', ',']);
        string normalized;
        if (lastSep >= 0 && s.Length - lastSep - 1 is 1 or 2)
        {
            var whole = s[..lastSep].Replace(".", "").Replace(",", "");
            normalized = $"{whole}.{s[(lastSep + 1)..]}";
        }
        else
        {
            normalized = s.Replace(".", "").Replace(",", "");
        }

        return decimal.TryParse(normalized, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out amount);
    }

    /// <summary>
    /// The price a buyer actually pays. For a subscription the first-period price can be a $0
    /// trial, so prefer the recurrence price. For a one-time add-on the Store still fills the
    /// recurrence price — with "$0.00" — so it must be ignored (this showed Lifetime as $0.00).
    /// </summary>
    public static string DisplayPrice(bool isSubscription, string formattedPrice, string? formattedRecurrencePrice)
        => isSubscription && !string.IsNullOrWhiteSpace(formattedRecurrencePrice)
            ? formattedRecurrencePrice
            : formattedPrice;

}
