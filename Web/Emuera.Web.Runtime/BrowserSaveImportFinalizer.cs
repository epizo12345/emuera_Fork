namespace MinorShift.Emuera.Web.Runtime;

public enum SaveImportFinalizationResult
{
    CleanupCompleted,
    CleanupFailed,
    LockReleaseFailed,
    TitleHandoffStarted
}

public static class BrowserSaveImportFinalizer
{
    public static async Task<SaveImportFinalizationResult> FinishAsync(
        bool overwriteDeclined,
        bool saveManagerMode,
        Func<Task<bool>> cleanup,
        Func<Task> beginTitleHandoff,
        Func<Task<bool>> releaseStoreLock,
        Func<Task> navigateToTitle,
        Func<Task> startRuntimeTitle)
    {
        if (!await cleanup())
            return SaveImportFinalizationResult.CleanupFailed;
        if (!overwriteDeclined)
            return SaveImportFinalizationResult.CleanupCompleted;

        await beginTitleHandoff();
        if (saveManagerMode)
        {
            if (!await releaseStoreLock())
                return SaveImportFinalizationResult.LockReleaseFailed;
            await navigateToTitle();
        }
        else
            await startRuntimeTitle();

        return SaveImportFinalizationResult.TitleHandoffStarted;
    }
}
