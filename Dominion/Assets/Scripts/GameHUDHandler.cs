using UnityEngine;
using UnityEngine.UI;

public class GameHUDHandler : MonoBehaviour
{
    [SerializeField]
    private Button _endTurnButton;

    private void Awake()
    {
        PlayerHandler.OnLocalTurnStarted += InitialiseHUD;
        _endTurnButton.onClick.AddListener(delegate { 
            PlayersTurnsHandler.Instance.FinishTurn();
            _endTurnButton.interactable = false;
        });
    }

    private void InitialiseHUD()
    {
        Debug.Log("Coucou");
        _endTurnButton.interactable = true;
    }

    private void OnDestroy()
    {
        PlayerHandler.OnLocalTurnStarted -= InitialiseHUD;
    }
}
