using System.Runtime.ExceptionServices;

namespace SnapData;

internal static class CleanupErrors
{
    internal const string DataKey = "SnapData.CleanupExceptions";

    internal static Exception Add(Exception? primary, Exception cleanup)
    {
        if (primary is null) return cleanup;
        if (ReferenceEquals(primary, cleanup)) return primary;
        if (primary.Data[DataKey] is not List<Exception> errors)
        {
            errors = [];
            primary.Data[DataKey] = errors;
        }
        errors.Add(cleanup);
        return primary;
    }

    internal static void ThrowIfAny(Exception? error)
    {
        if (error is not null) ExceptionDispatchInfo.Capture(error).Throw();
    }
}
