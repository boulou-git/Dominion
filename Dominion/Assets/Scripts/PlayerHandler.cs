using Photon.Pun;
using UnityEngine;

public class PlayerHandler : MonoBehaviour
{
    public string Pseudo {  get; private set; }

    public delegate void LocalTurnStarted();
    public static event LocalTurnStarted OnLocalTurnStarted;

    [PunRPC]
    public void SendPseudo(string pseudo)
    {
        Debug.LogError("Received " + pseudo);
        Pseudo = pseudo;
    }

    public void BeginTurn()
    {
        Debug.LogError("Begin Turn");
        OnLocalTurnStarted?.Invoke();
    }
}