using ExitGames.Client.Photon;
using Photon.Pun;
using Photon.Realtime;
using UnityEngine;

public class ActionsTriggerer : MonoBehaviour
{
    public const byte OnActionTriggered = 1;

    public void TriggerAction(GameAction action)
    {
        object[] content = new object[] { PhotonNetwork.LocalPlayer, action }; 
        RaiseEventOptions raiseEventOptions = new RaiseEventOptions { Receivers = ReceiverGroup.Others }; // You would have to set the Receivers to All in order to receive this event on the local client as well
        PhotonNetwork.RaiseEvent(OnActionTriggered, content, raiseEventOptions, SendOptions.SendReliable);
    }
}
