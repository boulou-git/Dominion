#if UNITY_INCLUDE_TESTS
using NUnit.Framework;

public sealed class ExtensionCatalogValidationTests
{
    [Test]
    public void ValidDeclarativePackage_PassesValidation()
    {
        ExtensionPackageData package = CreatePackage("play", "draw", "self");
        package.cards[0].abilities[0].effects[0].amount = 1;

        bool valid = ExtensionCatalog.TryValidatePackage(package, out string error);

        Assert.That(valid, Is.True, error);
        Assert.That(error, Is.Empty);
    }

    [Test]
    public void UnknownOperation_IsRejectedAtPackageValidation()
    {
        ExtensionPackageData package = CreatePackage("play", "gian_card", "self");

        bool valid = ExtensionCatalog.TryValidatePackage(package, out string error);

        Assert.That(valid, Is.False);
        StringAssert.Contains("unsupported operation", error);
    }

    [Test]
    public void UnknownTiming_IsRejectedAtPackageValidation()
    {
        ExtensionPackageData package = CreatePackage("after_playing", "draw", "self");

        bool valid = ExtensionCatalog.TryValidatePackage(package, out string error);

        Assert.That(valid, Is.False);
        StringAssert.Contains("unsupported timing", error);
    }

    [Test]
    public void InvalidZone_IsRejectedAtPackageValidation()
    {
        ExtensionPackageData package = CreatePackage("play", "choose_cards", "self");
        package.cards[0].abilities[0].effects[0].zone = "graveyard";

        bool valid = ExtensionCatalog.TryValidatePackage(package, out string error);

        Assert.That(valid, Is.False);
        StringAssert.Contains("unsupported zone", error);
    }

    [Test]
    public void AttackReaction_BlockAttack_IsAccepted()
    {
        ExtensionPackageData package = CreatePackage(ReactionRules.AttackReactionTiming, ReactionRules.BlockAttackOperation, "self");
        package.cards[0].abilities[0].scope = "in_hand";

        bool valid = ExtensionCatalog.TryValidatePackage(package, out string error);

        Assert.That(valid, Is.True, error);
    }

    [Test]
    public void BlockAttackOutsideReactionTiming_IsRejected()
    {
        ExtensionPackageData package = CreatePackage("play", ReactionRules.BlockAttackOperation, "self");

        bool valid = ExtensionCatalog.TryValidatePackage(package, out string error);

        Assert.That(valid, Is.False);
        StringAssert.Contains("outside attack_reaction", error);
    }

    [Test]
    public void ContradictorySelectionConditions_AreRejected()
    {
        ExtensionPackageData package = CreatePackage("play", "draw", "self");
        package.cards[0].abilities[0].effects[0].requiresLastSelection = true;
        package.cards[0].abilities[0].effects[0].requiresNoLastSelection = true;

        bool valid = ExtensionCatalog.TryValidatePackage(package, out string error);

        Assert.That(valid, Is.False);
        StringAssert.Contains("cannot require both", error);
    }

    private static ExtensionPackageData CreatePackage(string timing, string operation, string target)
    {
        ExtensionPackageData package = new ExtensionPackageData
        {
            id = "test",
            name = "Test",
            version = 1,
            schemaVersion = ExtensionCatalog.SupportedSchemaVersion
        };

        ExtensionCardData card = new ExtensionCardData
        {
            id = "carte_test",
            name = "Carte test",
            cost = 2
        };
        CardAbilityData ability = new CardAbilityData
        {
            when = timing
        };
        ability.effects.Add(new CardEffectData
        {
            op = operation,
            target = target
        });
        card.abilities.Add(ability);
        package.cards.Add(card);
        return package;
    }
}
#endif
