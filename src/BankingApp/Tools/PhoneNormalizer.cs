using System.Text.RegularExpressions;

namespace BankingApp.Tools;

/// <summary>
/// US phone-number normalizer — participant code moved into the consolidated
/// BankingApp. Pure function; also exposed as an MCP tool.
/// </summary>
public sealed partial class PhoneNormalizer
{
    /// <summary>Normalizes a US phone number to E.164-ish form: +1XXXXXXXXXX.</summary>
    public string NormalizePhone(string phone)
    {
        var digits = new string(phone.Where(char.IsDigit).ToArray());
        if (digits.Length == 10)
        {
            return "+1" + digits;
        }

        if (digits.Length == 11 && digits.StartsWith('1'))
        {
            return "+" + digits;
        }

        return "INVALID: expected a 10-digit US phone number";
    }

    [GeneratedRegex(@"[^\d+]")]
    private static partial Regex NonNumericChars();
}