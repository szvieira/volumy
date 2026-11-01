namespace Volumy.Services;

/// <summary>
/// Wraps entry points (event handlers, timer ticks, tray commands, dispatcher callbacks) so a
/// bug in one feature logs to crash.log instead of closing the whole app.
/// </summary>
internal static class Guard
{
    public static void Run(string where, Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            CrashLog.Write(where, ex);
        }
    }

    public static Action Wrap(string where, Action action) => () => Run(where, action);

    public static Action<T> Wrap<T>(string where, Action<T> action) => value => Run(where, () => action(value));
}
