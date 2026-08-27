using UnityEngine;

public sealed partial class EndGameFlowController
{
    private void ClearStage()
    {
        if (_scoreRoutine != null)
        {
            StopCoroutine(_scoreRoutine);
            _scoreRoutine = null;
        }
        _activeCardAnimations = 0;
        _animatedRows.Clear();
        _rankingButtons.Clear();

        if (_stageRoot != null)
        {
            Destroy(_stageRoot.gameObject);
            _stageRoot = null;
        }
    }

    private static void ClearChildren(RectTransform parent)
    {
        if (parent == null) return;
        for (int i = parent.childCount - 1; i >= 0; i--)
            Destroy(parent.GetChild(i).gameObject);
    }

    private static string Sanitize(string value)
    {
        if (string.IsNullOrEmpty(value)) return "card";
        return value.Replace(':', '_').Replace('/', '_').Replace(' ', '_');
    }
}
