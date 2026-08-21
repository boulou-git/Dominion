using System;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Adds the Action -> Buy behaviour to the shared next-phase button.
/// BuyPhaseGameplayController keeps ownership of Buy/Cleanup so its cleanup animation
/// remains unchanged.
/// </summary>
public sealed class PhaseAdvanceButtonBinding : MonoBehaviour
{
    private Button _button;

    private void Start()
    {
        Bind();
    }

    private void OnDestroy()
    {
        if (_button != null)
            _button.onClick.RemoveListener(OnNextPhaseClicked);
    }

    private void Bind()
    {
        Transform buttonTransform = FindDeepChild(transform, "NextPhaseButton");
        if (buttonTransform == null)
        {
            Debug.LogWarning("NextPhaseButton not found; Action -> Buy binding unavailable.");
            return;
        }

        _button = buttonTransform.GetComponent<Button>();
        if (_button == null)
        {
            Debug.LogWarning("NextPhaseButton has no Button component.");
            return;
        }

        _button.onClick.RemoveListener(OnNextPhaseClicked);
        _button.onClick.AddListener(OnNextPhaseClicked);
    }

    private void OnNextPhaseClicked()
    {
        GameStateSnapshot state = NetworkGameState.State;
        if (state == null || state.IsPaused || state.PendingChoice != null)
            return;

        // Buy/Cleanup are intentionally ignored here: BuyPhaseGameplayController
        // handles those phases and plays the cleanup animation before advancing.
        if (!string.Equals(state.Phase, NetworkGameState.ActionPhase, StringComparison.Ordinal))
            return;

        if (PlayersTurnsHandler.Instance != null)
            PlayersTurnsHandler.Instance.AdvancePhase();
    }

    private static Transform FindDeepChild(Transform parent, string childName)
    {
        if (parent == null)
            return null;

        for (int i = 0; i < parent.childCount; i++)
        {
            Transform child = parent.GetChild(i);
            if (string.Equals(child.name, childName, StringComparison.Ordinal))
                return child;

            Transform nested = FindDeepChild(child, childName);
            if (nested != null)
                return nested;
        }

        return null;
    }
}
