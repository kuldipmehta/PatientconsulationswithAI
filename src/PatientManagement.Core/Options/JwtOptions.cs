namespace PatientManagement.Core.Options;

public class JwtOptions
{
    public const string SectionName = "Jwt";

    public string Issuer { get; set; } = "PatientManagement.Api";
    public string Audience { get; set; } = "PatientManagement.Client";
    public int ExpiryMinutes { get; set; } = 480;
    public string SigningKey { get; set; } = string.Empty;
}
