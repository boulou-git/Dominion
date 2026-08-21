using ExitGames.Client.Photon;
using Photon.Realtime;
using UnityEngine;

public class MyTurnHandler : MonoBehaviour, IOnEventCallback
{
    public int ActionsCount { get; private set; }
    public int MoneyCount { get; private set; }

    public void OnEvent(EventData photonEvent)
    {
        ActionsCount--;
    }
}
