#if UNITY_INCLUDE_TESTS
using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

public sealed class DecisionWorkspaceTests
{
    [Test]
    public void ArtifactListener_IsPresentedInsteadOfTheOriginalAction()
    {
        var decision = new PendingDecisionSnapshot { SourceCardInstanceId = 12, ListenerCardInstanceId = 34 };
        Assert.AreEqual(34, DecisionPresentation.SourceId(decision));
        decision.ListenerCardInstanceId = 0;
        Assert.AreEqual(12, DecisionPresentation.SourceId(decision));
    }

    [TestCase("trash_selected", "ÉCART")]
    [TestCase("discard_selected", "DÉFAUSSE")]
    [TestCase("move_selected", "DESSUS DU DECK")]
    [TestCase("play_selected", "SÉLECTION")]
    public void Destination_UsesTheNextEffect_NotThePrompt(string operation, string expected)
    {
        var source = new ExtensionCardData { abilities = new List<CardAbilityData> {
            new CardAbilityData { effects = new List<CardEffectData> {
                new CardEffectData { op = "choose_cards" },
                new CardEffectData { op = "remember_selected_card_cost" },
                new CardEffectData { op = operation, destinationZone = "deck" }
            } }
        } };
        var choice = new PendingDecisionSnapshot { Operation = "choose_cards", AbilityIndex = 0, EffectIndex = 0, Prompt = "Texte sans importance" };
        Assert.AreEqual(expected, DecisionPresentation.Destination(choice, source));
    }

    [Test]
    public void OptionalChoice_AllowsPassingButNotExceedingTheMaximum()
    {
        var choice = new PendingDecisionSnapshot { MinSelections = 1, MaxSelections = 2, AllowPass = true };
        Assert.IsTrue(DecisionPresentation.IsValid(choice, 0));
        Assert.IsTrue(DecisionPresentation.IsValid(choice, 2));
        Assert.IsFalse(DecisionPresentation.IsValid(choice, 3));
        choice.AllowPass = false;
        Assert.IsFalse(DecisionPresentation.IsValid(choice, 0));
    }

    [Test]
    public void SingleCardDraft_CanBeReplacedAndReturnedWithoutSubmitting()
    {
        var selected = new HashSet<int> { 1 };
        Assert.IsTrue(PendingDecisionSelectionRules.Toggle(selected, 2, 1));
        CollectionAssert.AreEquivalent(new[] { 2 }, selected);
        Assert.IsTrue(PendingDecisionSelectionRules.Toggle(selected, 2, 1));
        Assert.IsEmpty(selected);
    }

    [Test]
    public void MultiCardDraft_RejectsOverflowWithoutChangingTheDraft()
    {
        var selected = new HashSet<int> { 1, 2 };
        Assert.IsFalse(PendingDecisionSelectionRules.Toggle(selected, 3, 2));
        CollectionAssert.AreEquivalent(new[] { 1, 2 }, selected);
    }

    [Test]
    public void Workspace_HasPrefabAuthoredControlsAndScrollableDropZones()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/UI/DecisionWorkspace.prefab");
        Assert.NotNull(prefab);
        Assert.NotNull(prefab.GetComponent<DecisionWorkspaceView>());
        var serialized = new SerializedObject(prefab.GetComponent<DecisionWorkspaceView>());
        Assert.NotNull(serialized.FindProperty("_cardPrefab").objectReferenceValue);
        Assert.NotNull(serialized.FindProperty("_optionPrefab").objectReferenceValue);
        Assert.NotNull(prefab.transform.Find("Panel/Source").GetComponent<DecisionSourceView>());
        foreach (string zone in new[] { "Available", "Chosen", "Options", "OptionsOnly" })
        {
            Transform scroll = prefab.transform.Find("Panel/" + zone + "/Scroll");
            Assert.NotNull(scroll.GetComponent<ScrollRect>());
            Assert.NotNull(scroll.Find("Viewport").GetComponent<Mask>());
            Assert.NotNull(scroll.Find("Viewport/Content").GetComponent<GridLayoutGroup>());
        }
        Assert.NotNull(prefab.transform.Find("Panel/Available").GetComponent<DecisionDropZone>());
        Assert.NotNull(prefab.transform.Find("Panel/Chosen").GetComponent<DecisionDropZone>());
        Assert.NotNull(prefab.transform.Find("Panel/Confirm").GetComponent<Button>());
        Assert.NotNull(prefab.transform.Find("Panel/Reset").GetComponent<Button>());
        Assert.NotNull(prefab.transform.Find("DragLayer"));
    }

    [Test]
    public void WorkspaceCard_HasAuthoredDragAndSelectionComponents()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/UI/DecisionWorkspaceCard.prefab");
        Assert.NotNull(prefab.GetComponent<DecisionDragCard>());
        Assert.NotNull(prefab.GetComponent<CanvasGroup>());
        Assert.IsTrue(prefab.transform.Find("Artwork").GetComponent<Image>().preserveAspect);
        Assert.NotNull(prefab.transform.Find("Selected"));
    }
}
#endif
