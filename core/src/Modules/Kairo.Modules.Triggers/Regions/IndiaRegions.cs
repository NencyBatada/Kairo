namespace Kairo.Modules.Triggers.Regions;

internal static class RegionKinds
{
    public const string Country = "country";
    public const string State = "state";
    public const string City = "city";
}

internal sealed record Region(string Id, string Name, string Kind, string? StateId = null, double? Latitude = null, double? Longitude = null)
{
    public bool HasForecast => Latitude is not null && Longitude is not null;
}

internal static class IndiaRegions
{
    public const string CountryId = "india";

    public static readonly IReadOnlyList<Region> All =
    [
        new(CountryId, "India", RegionKinds.Country),
        State("andhra-pradesh", "Andhra Pradesh"),
        State("arunachal-pradesh", "Arunachal Pradesh"),
        State("assam", "Assam"),
        State("bihar", "Bihar"),
        State("chhattisgarh", "Chhattisgarh"),
        State("goa", "Goa"),
        State("gujarat", "Gujarat"),
        State("haryana", "Haryana"),
        State("himachal-pradesh", "Himachal Pradesh"),
        State("jharkhand", "Jharkhand"),
        State("karnataka", "Karnataka"),
        State("kerala", "Kerala"),
        State("madhya-pradesh", "Madhya Pradesh"),
        State("maharashtra", "Maharashtra"),
        State("manipur", "Manipur"),
        State("meghalaya", "Meghalaya"),
        State("mizoram", "Mizoram"),
        State("nagaland", "Nagaland"),
        State("odisha", "Odisha"),
        State("punjab", "Punjab"),
        State("rajasthan", "Rajasthan"),
        State("sikkim", "Sikkim"),
        State("tamil-nadu", "Tamil Nadu"),
        State("telangana", "Telangana"),
        State("tripura", "Tripura"),
        State("uttar-pradesh", "Uttar Pradesh"),
        State("uttarakhand", "Uttarakhand"),
        State("west-bengal", "West Bengal"),
        State("andaman-and-nicobar-islands", "Andaman and Nicobar Islands"),
        State("chandigarh", "Chandigarh"),
        State("dadra-and-nagar-haveli-and-daman-and-diu", "Dadra and Nagar Haveli and Daman and Diu"),
        State("delhi", "Delhi"),
        State("jammu-and-kashmir", "Jammu and Kashmir"),
        State("ladakh", "Ladakh"),
        State("lakshadweep", "Lakshadweep"),
        State("puducherry", "Puducherry"),
        City("mumbai", "Mumbai", "maharashtra", 19.0760, 72.8777),
        City("pune", "Pune", "maharashtra", 18.5204, 73.8567),
        City("nagpur", "Nagpur", "maharashtra", 21.1458, 79.0882),
        City("nashik", "Nashik", "maharashtra", 19.9975, 73.7898),
        City("new-delhi", "New Delhi", "delhi", 28.6139, 77.2090),
        City("noida", "Noida", "uttar-pradesh", 28.5355, 77.3910),
        City("gurugram", "Gurugram", "haryana", 28.4595, 77.0266),
        City("bengaluru", "Bengaluru", "karnataka", 12.9716, 77.5946),
        City("mysuru", "Mysuru", "karnataka", 12.2958, 76.6394),
        City("mangaluru", "Mangaluru", "karnataka", 12.9141, 74.8560),
        City("hyderabad", "Hyderabad", "telangana", 17.3850, 78.4867),
        City("chennai", "Chennai", "tamil-nadu", 13.0827, 80.2707),
        City("coimbatore", "Coimbatore", "tamil-nadu", 11.0168, 76.9558),
        City("madurai", "Madurai", "tamil-nadu", 9.9252, 78.1198),
        City("kolkata", "Kolkata", "west-bengal", 22.5726, 88.3639),
        City("ahmedabad", "Ahmedabad", "gujarat", 23.0225, 72.5714),
        City("surat", "Surat", "gujarat", 21.1702, 72.8311),
        City("vadodara", "Vadodara", "gujarat", 22.3072, 73.1812),
        City("jaipur", "Jaipur", "rajasthan", 26.9124, 75.7873),
        City("jodhpur", "Jodhpur", "rajasthan", 26.2389, 73.0243),
        City("lucknow", "Lucknow", "uttar-pradesh", 26.8467, 80.9462),
        City("kanpur", "Kanpur", "uttar-pradesh", 26.4499, 80.3319),
        City("agra", "Agra", "uttar-pradesh", 27.1767, 78.0081),
        City("varanasi", "Varanasi", "uttar-pradesh", 25.3176, 82.9739),
        City("indore", "Indore", "madhya-pradesh", 22.7196, 75.8577),
        City("bhopal", "Bhopal", "madhya-pradesh", 23.2599, 77.4126),
        City("patna", "Patna", "bihar", 25.5941, 85.1376),
        City("ranchi", "Ranchi", "jharkhand", 23.3441, 85.3096),
        City("raipur", "Raipur", "chhattisgarh", 21.2514, 81.6296),
        City("bhubaneswar", "Bhubaneswar", "odisha", 20.2961, 85.8245),
        City("guwahati", "Guwahati", "assam", 26.1445, 91.7362),
        City("visakhapatnam", "Visakhapatnam", "andhra-pradesh", 17.6868, 83.2185),
        City("vijayawada", "Vijayawada", "andhra-pradesh", 16.5062, 80.6480),
        City("kochi", "Kochi", "kerala", 9.9312, 76.2673),
        City("thiruvananthapuram", "Thiruvananthapuram", "kerala", 8.5241, 76.9366),
        City("kozhikode", "Kozhikode", "kerala", 11.2588, 75.7804),
        City("ludhiana", "Ludhiana", "punjab", 30.9010, 75.8573),
        City("amritsar", "Amritsar", "punjab", 31.6340, 74.8723),
        City("dehradun", "Dehradun", "uttarakhand", 30.3165, 78.0322),
        City("shimla", "Shimla", "himachal-pradesh", 31.1048, 77.1734),
        City("srinagar", "Srinagar", "jammu-and-kashmir", 34.0837, 74.7973),
        City("panaji", "Panaji", "goa", 15.4909, 73.8278),
    ];

    private static readonly Dictionary<string, Region> ById = All.ToDictionary(region => region.Id, StringComparer.Ordinal);

    public static Region? Find(string id) => ById.GetValueOrDefault(id);

    public static string NameOf(string id) => Find(id)?.Name ?? id;

    private static Region State(string id, string name) => new(id, name, RegionKinds.State);

    private static Region City(string id, string name, string stateId, double latitude, double longitude) =>
        new(id, name, RegionKinds.City, stateId, latitude, longitude);
}
