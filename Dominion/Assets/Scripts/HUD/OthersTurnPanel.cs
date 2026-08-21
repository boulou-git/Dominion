using ExitGames.Client.Photon;
using Photon.Realtime;
using UnityEngine;

public class OthersTurnPanel : MonoBehaviour, IOnEventCallback
{
    public void OnEvent(EventData photonEvent)
    {
        Debug.LogError("Received event: " + photonEvent.Code);
    }
}
