using System;

namespace CustomDemonstrators.Api;

// Which of the force respawn buttons ran
public enum ForceApplyKind
{
    Demonstrators,
    Garages,
}

public static class ForceApplyEvents
{
    public static event Action<ForceApplyKind>? Applied;

    internal static void RaiseApplied(ForceApplyKind kind)
    {
        var handlers = Applied;
        if (handlers == null) return;

        foreach (var handler in handlers.GetInvocationList())
        {
            try
            {
                ((Action<ForceApplyKind>)handler)(kind);
            }
            catch (Exception ex)
            {
                Main.Logger.LogException(
                    $"A {nameof(ForceApplyEvents)}.{nameof(Applied)} listener threw "
                    + $"({handler.Method.DeclaringType?.Assembly.GetName().Name}"
                    + $".{handler.Method.DeclaringType?.Name}.{handler.Method.Name}):", ex);
            }
        }
    }
}
