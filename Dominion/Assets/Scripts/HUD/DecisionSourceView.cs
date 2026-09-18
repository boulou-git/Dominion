using UnityEngine;
using UnityEngine.UI;

/// <summary>All sizing, aspect handling and typography are authored in the prefab.</summary>
public sealed class DecisionSourceView : MonoBehaviour
{
    public void Bind(GameStateSnapshot state, PendingDecisionSnapshot decision)
    {
        int id = DecisionPresentation.SourceId(decision);
        CardInstance card = NetworkGameState.FindCardInstance(state, id);
        Image art = transform.Find("Artwork").GetComponent<Image>();
        Text name = transform.Find("Name").GetComponent<Text>();
        Text rules = transform.Find("Rules").GetComponent<Text>();
        Text context = transform.Find("Context").GetComponent<Text>();
        art.sprite = null;
        name.text = "EFFET EN COURS";
        rules.text = "";
        context.text = "";
        if (card != null && RoomGameSetup.TryResolveCard(card.DefinitionId, out ExtensionPackageData extension, out ExtensionCardData definition))
        {
            art.sprite = ExtensionVisualLoader.LoadCardArtwork(extension, definition);
            name.text = definition.name;
            rules.text = definition.text;
        }
        art.enabled = art.sprite != null;
        if (decision.SourceCardInstanceId > 0 && decision.SourceCardInstanceId != id)
        {
            CardInstance cause = NetworkGameState.FindCardInstance(state, decision.SourceCardInstanceId);
            if (cause != null && RoomGameSetup.TryResolveCard(cause.DefinitionId, out _, out ExtensionCardData causeDefinition))
                context.text = "En réponse à : " + causeDefinition.name;
        }
    }
}
