using Photon.Pun;
using Photon.Realtime;
using UnityEngine;

public class GameInitialiser : MonoBehaviour
{
    [SerializeField]
    private GameObject _playerPrefab;

    [SerializeField]
    private PlayersTurnsHandler _turnsHandler;

    [SerializeField]
    private NetworkGameState _networkGameState;

    private void Start()
    {
        if (_networkGameState == null)
            _networkGameState = FindFirstObjectByType<NetworkGameState>();

        if (PhotonNetwork.IsMasterClient)
        {
            SetupGameAuthoritatively();
        }
        else
        {
            // A client never builds its own game state. It asks the Master Client for the truth.
            _networkGameState?.RequestFullState();
        }

        _turnsHandler.Initialise();
    }

    private void SetupGameAuthoritatively()
    {
        if (_networkGameState == null)
        {
            Debug.LogError("NetworkGameState is missing from the game scene.");
            return;
        }

        _networkGameState.InitialiseAuthoritativeState();

        // These setup steps will progressively move into a pure GameSetup/GameEngine layer.
        SetupRealmCards();
        SetupDeckAndDraw();
        SetupAllSharedDecks();
        DrawFirstHand();
    }

    private void SetupRealmCards()
    {
        Debug.Log("Creation de 10 paquets aleatoires de cartes royaumes");
    }

    private void SetupDeckAndDraw()
    {
        Debug.Log("Distribution de 3 cartes domaines et 7 cartes cuivre");
    }

    private void SetupAllSharedDecks()
    {
        Debug.Log("Mise en place des decks de cuivre, argent et or selon le nombre de joueurs");
        Debug.Log("Mise en place des decks de domaines, duches et provinces selon le nombre de joueurs");
        Debug.Log("Mise en place du deck de maledictions selon le nombre de joueurs");
    }

    private void DrawFirstHand()
    {
        Debug.Log("Je pioche les cinq premieres cartes de chaque joueur");
    }
}
