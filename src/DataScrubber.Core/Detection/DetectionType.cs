namespace DataScrubber.Detection;

/// <summary>
///     Categorises a detected entity. The enum is part of the public API; new
///     values are appended, never reordered or removed.
/// </summary>
public enum DetectionType
{
    /// <summary>An email address.</summary>
    Email,

    /// <summary>A dotted-quad IPv4 address.</summary>
    IPv4,

    /// <summary>A standard-form IPv6 address, optionally with <c>::</c> compression.</summary>
    IPv6,

    /// <summary>A URL with an explicit scheme.</summary>
    Url,

    /// <summary>A telephone number (NANP or E.164).</summary>
    Phone,

    /// <summary>A 13–19 digit credit-card number that passes Luhn validation.</summary>
    CreditCard,

    /// <summary>A vendor-specific or high-entropy API key, secret, or token.</summary>
    ApiKey,

    /// <summary>The right-hand side of a password-style assignment.</summary>
    Password,

    /// <summary>The username segment of a POSIX or Windows user-home path.</summary>
    UserPath,

    /// <summary>A hardware MAC address.</summary>
    MacAddress,

    /// <summary>A person's name produced by the NER pipeline.</summary>
    Person,

    /// <summary>An organisation name produced by the NER pipeline.</summary>
    Organization,

    /// <summary>A location name produced by the NER pipeline.</summary>
    Location,
}

/// <summary>
///     Extension helpers for <see cref="DetectionType"/>.
/// </summary>
public static class DetectionTypeExtensions
{
    /// <summary>
    ///     Returns the upper snake-case tag name used inside <c>[…]</c> placeholders
    ///     emitted by the replacer. <see cref="DetectionType.Email"/> becomes
    ///     <c>EMAIL</c>; <see cref="DetectionType.CreditCard"/> becomes
    ///     <c>CREDIT_CARD</c>; <see cref="DetectionType.MacAddress"/> becomes
    ///     <c>MAC_ADDRESS</c>.
    /// </summary>
    /// <param name="type">The detection type.</param>
    /// <returns>The upper snake-case tag name.</returns>
    public static string ToTagName(this DetectionType type) => type switch
    {
        DetectionType.Email => "EMAIL",
        DetectionType.IPv4 => "IPV4",
        DetectionType.IPv6 => "IPV6",
        DetectionType.Url => "URL",
        DetectionType.Phone => "PHONE",
        DetectionType.CreditCard => "CREDIT_CARD",
        DetectionType.ApiKey => "API_KEY",
        DetectionType.Password => "PASSWORD",
        DetectionType.UserPath => "USER_PATH",
        DetectionType.MacAddress => "MAC_ADDRESS",
        DetectionType.Person => "PERSON",
        DetectionType.Organization => "ORGANIZATION",
        DetectionType.Location => "LOCATION",
        _ => type.ToString().ToUpperInvariant(),
    };
}
