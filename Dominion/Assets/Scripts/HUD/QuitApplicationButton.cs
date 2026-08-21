using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Simple reusable quit button. In the Unity Editor it stops Play Mode;
/// in a build it closes the application.
/// </summary>
[RequireComponent(typeof(Button))]
public sealed class QuitApplicationButton : MonoBehaviour
{
    private Button _button;

    private void Awake()
    {
        _button = GetComponent<Button>();
        _button.onClick.RemoveListener(QuitApplication);
        _button.onClick.AddListener(QuitApplication);
    }

    private void OnDestroy()
    {
        if (_button != null)
            _button.onClick.RemoveListener(QuitApplication);
    }

    public void QuitApplication()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }
}
