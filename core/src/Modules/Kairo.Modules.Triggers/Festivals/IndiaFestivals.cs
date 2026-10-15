namespace Kairo.Modules.Triggers.Festivals;

internal sealed record SeedFestival(string Name, DateOnly StartsOn, DateOnly EndsOn, IReadOnlyList<string> Regions);

internal static class IndiaFestivals
{
    private static readonly string[] North = ["delhi", "haryana", "punjab", "rajasthan", "uttar-pradesh", "madhya-pradesh", "uttarakhand", "himachal-pradesh"];
    private static readonly string[] East = ["west-bengal", "assam", "tripura", "odisha", "bihar", "jharkhand"];
    private static readonly string[] Chhath = ["bihar", "jharkhand", "uttar-pradesh", "delhi"];
    private static readonly string[] Ganesh = ["maharashtra", "goa", "karnataka", "telangana", "andhra-pradesh"];
    private static readonly string[] Lohri = ["punjab", "haryana", "himachal-pradesh", "delhi", "jammu-and-kashmir"];

    public static readonly IReadOnlyList<SeedFestival> All =
    [
        Event("Navratri", 2026, 10, 11, 2026, 10, 19),
        Event("Durga Puja", 2026, 10, 17, 2026, 10, 20, East),
        Day("Dussehra", 2026, 10, 20),
        Day("Karwa Chauth", 2026, 10, 29, North),
        Day("Dhanteras", 2026, 11, 6),
        Day("Diwali", 2026, 11, 8),
        Day("Bhai Dooj", 2026, 11, 11),
        Day("Chhath Puja", 2026, 11, 15, Chhath),
        Day("Guru Nanak Jayanti", 2026, 11, 24),
        Day("Black Friday", 2026, 11, 27),
        Day("Christmas", 2026, 12, 25),
        Event("New Year", 2026, 12, 31, 2027, 1, 1),
        Day("Lohri", 2027, 1, 14, Lohri),
        Day("Makar Sankranti", 2027, 1, 15),
        Day("Pongal", 2027, 1, 15, ["tamil-nadu", "puducherry"]),
        Day("Magh Bihu", 2027, 1, 15, ["assam"]),
        Day("Republic Day", 2027, 1, 26),
        Day("Valentine's Day", 2027, 2, 14),
        Day("Maha Shivratri", 2027, 3, 6),
        Day("Eid al-Fitr", 2027, 3, 10),
        Day("Holi", 2027, 3, 22),
        Day("Ugadi and Gudi Padwa", 2027, 4, 8, ["maharashtra", "karnataka", "andhra-pradesh", "telangana", "goa"]),
        Day("Baisakhi", 2027, 4, 14, ["punjab", "haryana"]),
        Day("Bohag Bihu", 2027, 4, 14, ["assam"]),
        Day("Vishu", 2027, 4, 15, ["kerala"]),
        Day("Mother's Day", 2027, 5, 9),
        Day("Eid al-Adha", 2027, 5, 17),
        Day("Independence Day", 2027, 8, 15),
        Day("Raksha Bandhan", 2027, 8, 17),
        Day("Janmashtami", 2027, 8, 25),
        Day("Ganesh Chaturthi", 2027, 9, 4, Ganesh),
        Day("Onam", 2027, 9, 12, ["kerala"]),
        Day("Dussehra", 2027, 10, 10),
        Day("Diwali", 2027, 10, 29),
        Day("Guru Nanak Jayanti", 2027, 11, 14),
        Day("Black Friday", 2027, 11, 26),
        Day("Christmas", 2027, 12, 25),
    ];

    private static SeedFestival Day(string name, int year, int month, int day, IReadOnlyList<string>? regions = null) =>
        new(name, new DateOnly(year, month, day), new DateOnly(year, month, day), regions ?? []);

    private static SeedFestival Event(string name, int year, int month, int day, int endYear, int endMonth, int endDay, IReadOnlyList<string>? regions = null) =>
        new(name, new DateOnly(year, month, day), new DateOnly(endYear, endMonth, endDay), regions ?? []);
}
