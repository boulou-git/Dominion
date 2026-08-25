using UnityEngine;

/// <summary>
/// Scene-level game initialiser. The authoritative match, starter decks and opening
/// hands are already created by NetworkGameState before the Game scene is opened.
/// This component only hydrates the local snapshot and starts turn presentation.
/// </summary>
public class GameInitialiser : MonoBehaviour
{
    [SerializeField]
    private GameObject _playerPrefab;

    [SerializeField]
    private PlayersTurnsHandler _turnsHandler;

    private void Start()
    {
        NetworkGameState.HydrateFromRoom(true);

        if (_turnsHandler != null)
            _turnsHandler.Initialise();
    }
}
