using System.Runtime.CompilerServices;
using UnityEngine;
using UnityEngine.UI;

public class GameHUDHandler : MonoBehaviour
{
    [SerializeField]
    private Button _endTurnButton;

    [SerializeField]
    private GameObject _myTurnPanel, _othersTurnPanel;
    private bool _isLocalTurn;

    private void Awake()
    {
        PlayerHandler.OnLocalTurnStarted += InitialiseHUD;
        NetworkGameState.StateChanged += HandleStateChanged;
        _endTurnButton.onClick.AddListener(delegate {
            EndTurn();
        });
    }

    private void InitialiseHUD()
    {
        _isLocalTurn = true;
        SetupHUD(true);
    }

    private void EndTurn()
    {
        if (PendingDecisionInputLock.IsActive(NetworkGameState.State))
            return;
        PlayersTurnsHandler.Instance.FinishTurn();
        _isLocalTurn = false;
        SetupHUD(false);
    }

    private void HandleStateChanged(GameStateSnapshot state)
    {
        if (_endTurnButton != null)
            _endTurnButton.interactable = _isLocalTurn && !PendingDecisionInputLock.IsActive(state);
    }

    private void SetupHUD(bool isTurn)
    {
        _endTurnButton.interactable = isTurn && !PendingDecisionInputLock.IsActive(NetworkGameState.State);
        _myTurnPanel.SetActive(isTurn);
        _othersTurnPanel.SetActive(!isTurn);
    }

    private void OnDestroy()
    {
        PlayerHandler.OnLocalTurnStarted -= InitialiseHUD;
        NetworkGameState.StateChanged -= HandleStateChanged;
    }
}
