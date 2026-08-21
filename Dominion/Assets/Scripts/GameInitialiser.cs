using Photon.Pun;
using UnityEngine;

public class GameInitialiser : MonoBehaviour
{
    [SerializeField]
    private GameObject _playerPrefab;

    [SerializeField]
    private PlayersTurnsHandler _turnsHandler;

    private void Start()
    {
        // Every client rebuilds its local view from the authoritative room snapshot.
        NetworkGameState.HydrateFromRoom(true);

        // Setup must run exactly once for the whole match. Reconnecting or becoming the
        // new Master Client must never recreate piles/decks or restart the game.
        if (PhotonNetwork.IsMasterClient &&
            NetworkGameState.IsStarted &&
            NetworkGameState.State != null &&
            !NetworkGameState.State.IsInitialised)
        {
            SetupGameAuthoritatively();
        }

        _turnsHandler.Initialise();
    }

    private void SetupGameAuthoritatively()
    {
        // These setup steps will progressively move into a pure GameSetup/GameEngine layer.
        SetupRealmCards();
        SetupDeckAndDraw();
        SetupAllSharedDecks();
        DrawFirstHand();

        NetworkGameState.MarkInitialised();
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
