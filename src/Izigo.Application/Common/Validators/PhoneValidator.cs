using System.Text.RegularExpressions;

namespace Izigo.Application.Common.Validators;

public static class PhoneValidator
{
    // E.164: leading +, country code (1-3 digits), subscriber (min 4 digits), total 7–15 digits after +
    private static readonly Regex E164 = new(@"^\+[1-9]\d{6,14}$", RegexOptions.Compiled);

    /// <summary>Throws ArgumentException if the phone is not valid E.164.</summary>
    public static void Validate(string? phone, string fieldName = "phone")
    {
        if (string.IsNullOrWhiteSpace(phone) || !E164.IsMatch(phone))
            throw new ArgumentException(
                $"VALIDATION_ERROR: {fieldName} must be in E.164 format (e.g. +2250700000000).");
    }

    public static bool IsValid(string? phone) =>
        !string.IsNullOrWhiteSpace(phone) && E164.IsMatch(phone);
}
