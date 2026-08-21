using System.Runtime.CompilerServices;
using UnityEngine;
using UnityEngine.UI;

public class GameHUDHandler : MonoBehaviour
{
    [SerializeField]
    private Button _endTurnButton;

    [SerializeField]
    private GameObject _myTurnPanel, _othersTurnPanel;

    private void Awake()
    {
        PlayerHandler.OnLocalTurnStarted += InitialiseHUD;
        _endTurnButton.onClick.AddListener(delegate {
            EndTurn();
        });
    }

    private void InitialiseHUD()
    {
        SetupHUD(true);
    }

    private void EndTurn()
    {
        PlayersTurnsHandler.Instance.FinishTurn();
        SetupHUD(false);
    }

    private void SetupHUD(bool isTurn)
    {
        _endTurnButton.interactable = isTurn;
        _myTurnPanel.SetActive(isTurn);
        _othersTurnPanel.SetActive(!isTurn);
    }

    private void OnDestroy()
    {
        PlayerHandler.OnLocalTurnStarted -= InitialiseHUD;
    }
}
