namespace Kairo.Modules.Opportunities;

internal sealed class OpportunityOptions(IConfiguration configuration)
{
    public const string DefaultCountryCode = "IN";
    public const string DefaultLanguage = "1000";
    public const int RefreshFromHour = 5;
    public const int CutOff = 40;
    public const double MinGrowth = 0.2;
    public const long MinSearches = 100;
    public const int MaxSeeds = 40;
    public const int MaxCandidates = 200;
    public const int MaxShown = 12;
    public const int MaxKept = 20;
    public const int MaxProducts = 12;
    public const int ExpireAfterMissed = 3;

    public string CountryCode => configuration["Kairo:Opportunities:CountryCode"] is { Length: 2 } code ? code.ToUpperInvariant() : DefaultCountryCode;

    public string Language => configuration["Kairo:Opportunities:Language"] is { Length: > 0 } language ? language : DefaultLanguage;

    public string AdsApiVersion => configuration["Kairo:Google:AdsApiVersion"] is { Length: > 0 } version ? version : "v25";
}
