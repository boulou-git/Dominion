using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Keeps the player's visual hand order locally without sending a network command on every drag.
/// When cleanup is requested, the order is projected onto the current authoritative hand and
/// sent once with the phase-advance command.
/// </summary>
public static class LocalHandOrderTracker
{
    private static readonly List<int> VisualOrder = new List<int>();

    public static void CaptureFromParent(Transform parent)
    {
        if (parent == null)
            return;

        VisualOrder.Clear();
        for (int i = 0; i < parent.childCount; i++)
        {
            HandCardMotion motion = parent.GetChild(i).GetComponent<HandCardMotion>();
            if (motion != null && motion.InstanceId > 0)
                VisualOrder.Add(motion.InstanceId);
        }
    }

    public static int[] ResolveForAuthoritativeHand(List<int> authoritativeHand)
    {
        if (authoritativeHand == null || authoritativeHand.Count == 0)
            return new int[0];

        List<int> resolved = new List<int>(authoritativeHand.Count);
        HashSet<int> authoritative = new HashSet<int>(authoritativeHand);

        foreach (int instanceId in VisualOrder)
        {
            if (authoritative.Contains(instanceId) && !resolved.Contains(instanceId))
                resolved.Add(instanceId);
        }

        foreach (int instanceId in authoritativeHand)
        {
            if (!resolved.Contains(instanceId))
                resolved.Add(instanceId);
        }

        return resolved.ToArray();
    }

    public static void Clear()
    {
        VisualOrder.Clear();
    }
}
