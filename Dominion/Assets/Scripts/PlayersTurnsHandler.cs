using NUnit.Framework;
using Photon.Pun;
using Photon.Realtime;
using System.Collections.Generic;
using UnityEngine;

public class PlayersTurnsHandler : MonoBehaviour
{
    public static PlayersTurnsHandler Instance;

    [SerializeField]
    private PlayerHandler _playerHandler;

    public List<Player> PlayersTurnOrder;

    private int _currentPlayerIndex;

    public void Initialise()
    {
        Instance = this;

        if (PhotonNetwork.IsMasterClient)
        {
            PlayersTurnOrder = new List<Player>(PhotonNetwork.CurrentRoom.Players.Values);
            _currentPlayerIndex = -1;
            PhotonView.Get(this).RPC("StartNewTurn", RpcTarget.MasterClient);
        }
    }

    [PunRPC]
    private void StartNewTurn()
    {
        _currentPlayerIndex++;
        if (_currentPlayerIndex == PlayersTurnOrder.Count) 
            _currentPlayerIndex = 0;
        PhotonView.Get(this).RPC("StartTurn", RpcTarget.All, PlayersTurnOrder[_currentPlayerIndex].NickName);
    }

    [PunRPC]
    private void StartTurn(string playerName)
    {
        if(PhotonNetwork.LocalPlayer.NickName == playerName)
        {
            _playerHandler.BeginTurn();
        }
    }

    public void FinishTurn()
    {
        PhotonView.Get(this).RPC("StartNewTurn", RpcTarget.MasterClient);
    }
}
