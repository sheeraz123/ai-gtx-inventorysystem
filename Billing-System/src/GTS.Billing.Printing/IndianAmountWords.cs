namespace GTS.Billing.Printing;

internal static class IndianAmountWords
{
    private static readonly string[] Small = ["Zero", "One", "Two", "Three", "Four", "Five", "Six", "Seven",
        "Eight", "Nine", "Ten", "Eleven", "Twelve", "Thirteen", "Fourteen", "Fifteen", "Sixteen", "Seventeen",
        "Eighteen", "Nineteen"];
    private static readonly string[] Tens = ["", "", "Twenty", "Thirty", "Forty", "Fifty", "Sixty", "Seventy", "Eighty", "Ninety"];

    internal static string Format(decimal value)
    {
        if (decimal.Round(value, 2) != value) return "Unavailable for amounts with precision below one paise";
        var absolute = Math.Abs(value);
        var rupees = decimal.Truncate(absolute);
        var paise = (int)((absolute - rupees) * 100);
        return (value < 0 ? "Minus " : "") + InWords(rupees) +
            (paise == 0 ? "" : " and " + InWords(paise) + " Paise") + " Only";
    }

    private static string InWords(decimal value)
    {
        foreach (var (scale, name) in new (int, string)[] { (10000000, "Crore"), (100000, "Lakh"), (1000, "Thousand"), (100, "Hundred") })
        {
            if (value < scale) continue;
            var rest = value % scale;
            return InWords((value - rest) / scale) + " " + name + (rest == 0 ? "" : " " + InWords(rest));
        }
        var number = (int)value;
        return number < 20 ? Small[number] : Tens[number / 10] + (number % 10 == 0 ? "" : " " + Small[number % 10]);
    }
}
