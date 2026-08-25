#if UNITY_INCLUDE_TESTS
using NUnit.Framework;

public sealed class DeclarativeRuleVocabularyTests
{
    [Test]
    public void RuntimeEffectOperation_IsAcceptedBySharedVocabulary()
    {
        Assert.That(EffectResolver.IsSupported("draw"), Is.True);
        Assert.That(DeclarativeRuleVocabulary.IsSupportedOperation("draw"), Is.True);
    }

    [Test]
    public void AttackReactionOperation_IsAcceptedBySharedVocabulary()
    {
        Assert.That(DeclarativeRuleVocabulary.IsSupportedTiming(ReactionRules.AttackReactionTiming), Is.True);
        Assert.That(DeclarativeRuleVocabulary.IsSupportedOperation(ReactionRules.BlockAttackOperation), Is.True);
    }

    [TestCase("subject")]
    [TestCase("in_hand")]
    [TestCase("in_play")]
    public void RuntimeScopes_AreAcceptedBySharedVocabulary(string scope)
    {
        Assert.That(DeclarativeRuleVocabulary.IsSupportedScope(scope), Is.True);
    }

    [Test]
    public void UnknownVocabulary_IsRejected()
    {
        Assert.That(DeclarativeRuleVocabulary.IsSupportedTiming("after_playing"), Is.False);
        Assert.That(DeclarativeRuleVocabulary.IsSupportedOperation("gian_card"), Is.False);
        Assert.That(DeclarativeRuleVocabulary.IsSupportedTarget("enemy"), Is.False);
        Assert.That(DeclarativeRuleVocabulary.IsSupportedResource("energy"), Is.False);
    }
}
#endif
