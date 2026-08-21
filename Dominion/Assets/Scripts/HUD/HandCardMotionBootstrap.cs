using System;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Attaches HandCardMotion to cards created dynamically inside LocalHand/Cards.
/// This keeps the motion behaviour independent from the temporary GameLayout builder.
/// </summary>
public sealed class HandCardMotionBootstrap : MonoBehaviour
{
    private const string RootName = "DominionHandCardMotionBootstrap";
    private Transform _handCardsRoot;
    private float _nextScan;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Register()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (!string.Equals(scene.name, "Game", StringComparison.Ordinal))
            return;

        if (GameObject.Find(RootName) != null)
            return;

        GameObject root = new GameObject(RootName, typeof(HandCardMotionBootstrap));
        SceneManager.MoveGameObjectToScene(root, scene);
    }

    private void Update()
    {
        if (Time.unscaledTime < _nextScan)
            return;

        _nextScan = Time.unscaledTime + 0.20f;
        ResolveHandRoot();
        AttachToCurrentCards();
    }

    private void ResolveHandRoot()
    {
        if (_handCardsRoot != null)
            return;

        GameObject gameUi = GameObject.Find("DominionGameUI");
        if (gameUi == null)
            return;

        Transform localHand = FindDeepChild(gameUi.transform, "LocalHand");
        if (localHand == null)
            return;

        _handCardsRoot = FindDirectChild(localHand, "Cards");
    }

    private void AttachToCurrentCards()
    {
        if (_handCardsRoot == null)
            return;

        for (int i = 0; i < _handCardsRoot.childCount; i++)
        {
            GameObject child = _handCardsRoot.GetChild(i).gameObject;
            if (child == null || child.GetComponent<RectTransform>() == null)
                continue;

            // Ignore the empty-hand explanatory text.
            if (child.GetComponent<UnityEngine.UI.Text>() != null)
                continue;

            if (child.GetComponent<HandCardMotion>() == null)
                child.AddComponent<HandCardMotion>();
        }
    }

    private static Transform FindDirectChild(Transform parent, string childName)
    {
        if (parent == null)
            return null;

        for (int i = 0; i < parent.childCount; i++)
        {
            Transform child = parent.GetChild(i);
            if (string.Equals(child.name, childName, StringComparison.Ordinal))
                return child;
        }

        return null;
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
