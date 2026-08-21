using System;

/// <summary>
/// Runtime identity of one physical card in a match.
/// The definition contains static content; this object only identifies the copy and its owner.
/// </summary>
[Serializable]
public sealed class CardInstance
{
    public int InstanceId;
    public string DefinitionId;
    public string OwnerPlayerId;

    public CardInstance(int instanceId, string definitionId, string ownerPlayerId)
    {
        InstanceId = instanceId;
        DefinitionId = definitionId;
        OwnerPlayerId = ownerPlayerId;
    }
}
