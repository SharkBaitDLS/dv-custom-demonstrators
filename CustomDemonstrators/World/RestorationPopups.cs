using System.Collections;
using System.Collections.Generic;
using DV.Utils;
using UnityEngine;

namespace CustomDemonstrators.World;

// Manages a queue for notification popups so that locomotives that share a license both pop up
// in sequence when their license is bought.
internal static class RestorationPopups
{
    private static readonly Queue<string> Pending = new();

    private static string? showing;

    private static int capturing;

    internal static void BeginCapture() => capturing++;

    internal static void EndCapture()
    {
        if (capturing > 0) capturing--;
    }

    internal static void Reset()
    {
        Pending.Clear();
        showing = null;
        capturing = 0;
    }

    internal static bool ShouldShow(string message, bool manualDismiss)
    {
        if (capturing == 0 || !manualDismiss)
        {
            Pending.Clear();
            showing = null;
            return true;
        }

        if (showing == null)
        {
            showing = message;
            return true;
        }

        // Steps that aren't locomotive specific queue identical messages simultaneously, and we want
        // to still dedupe those instead of being annoying and popping up a shitload at once.
        if (message != showing && !Pending.Contains(message)) Pending.Enqueue(message);
        return false;
    }

    internal static void OnDismissed()
    {
        if (showing == null) return;

        showing = null;
        if (Pending.Count > 0) SingletonBehaviour<CoroutineManager>.Instance.Run(ShowNext());
    }

    private static IEnumerator ShowNext()
    {
        yield return null;

        if (Pending.Count == 0) yield break;

        var helper = SingletonBehaviour<TutorialHelper>.Instance;
        if (helper == null)
        {
            Reset();
            yield break;
        }

        string message = Pending.Dequeue();
        BeginCapture();
        try
        {
            helper.ShowTutorialFloatie(message, null, Vector3.zero, localize: false, targetIsUI: false,
                TutorialHelper.SoundType.Acknowledge, manualDismiss: true);
        }
        finally
        {
            EndCapture();
        }
    }
}
