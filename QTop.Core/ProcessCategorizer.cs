namespace QTop.Core;

public static class ProcessCategorizer
{
    public static ProcessCategory Categorize(ProcessClassificationInput input)
    {
        if (input.IsService)
            return ProcessCategory.Service;

        if (input.HasVisibleMainWindow)
            return ProcessCategory.App;

        if (input.IsSessionZero ||
            input.IsWindowsOwned ||
            input.IsKnownSystemProcess ||
            input.DetailsDenied)
            return ProcessCategory.WindowsSystem;

        return ProcessCategory.Background;
    }
}
