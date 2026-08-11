using NUnit.Framework;
using Photon.Pun;
using Photon.Realtime;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;

public class GameInitialiser : MonoBehaviour
{
    [SerializeField]
    private GameObject _playerPrefab;

    [SerializeField]
    private PlayersTurnsHandler _turnsHandler;

    private void Start()
    {
        PhotonView photonView = PhotonView.Get(this);

        if (PhotonNetwork.IsMasterClient)
        {
            foreach (Player player in PhotonNetwork.CurrentRoom.Players.Values)
            {
                photonView.RPC("SpawnPlayer", RpcTarget.All, player);
            }

            // Setting up the game
            photonView.RPC("SetupRealmCards", RpcTarget.All);
            photonView.RPC("SetupDeckAndDraw", RpcTarget.All);
            photonView.RPC("SetupAllSharedDecks", RpcTarget.All);
            photonView.RPC("DrawFirstHand", RpcTarget.All);
        }

        _turnsHandler.Initialise();
    }

    [PunRPC]
    private void SpawnPlayer(Player thisPlayer)
    {
        Debug.Log("Received my player " + thisPlayer.NickName);
    }

    [PunRPC]
    private void SetupRealmCards()
    {
        Debug.Log("Creation de 10 paquets aleatoires de cartes royaumes");
    }

    [PunRPC]
    private void SetupDeckAndDraw()
    {
        Debug.Log("Distribution de 3 cartes domaines et 7 cartes cuivre");
    }

    [PunRPC]
    private void SetupAllSharedDecks()
    {
        Debug.Log("Mise en place des decks de cuivre, argent et or selon le nombre de joueurs");
        Debug.Log("Mise en place des decks de domaines, duches et provinces selon le nombre de joueurs");
        Debug.Log("Mise en place du deck de maledictions selon le nombre de joueurs");
    }

    [PunRPC]
    private void DrawFirstHand()
    {
        Debug.Log("Je pioche mes cinq premieres cartes");
    }
}
