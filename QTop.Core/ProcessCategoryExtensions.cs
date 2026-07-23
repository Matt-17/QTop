namespace QTop.Core;

public static class ProcessCategoryExtensions
{
    public static string ToDisplayName(this ProcessCategory category) => category switch
    {
        ProcessCategory.App => "Apps",
        ProcessCategory.Background => "Background",
        ProcessCategory.WindowsSystem => "Windows/System",
        ProcessCategory.Service => "Services",
        _ => "Unknown"
    };

    public static int ToSortOrder(this ProcessCategory category) => category switch
    {
        ProcessCategory.App => 0,
        ProcessCategory.Background => 1,
        ProcessCategory.Service => 2,
        ProcessCategory.WindowsSystem => 3,
        _ => 4
    };
}
