#if UNITY_INCLUDE_TESTS
using NUnit.Framework;

public sealed class CardDefinitionReferenceTests
{
    [Test]
    public void Format_TrimsAndBuildsQualifiedReference()
    {
        Assert.That(CardDefinitionReference.Format(" base ", " village "), Is.EqualTo("base:village"));
    }

    [Test]
    public void ParseQualified_RejectsMalformedReferences()
    {
        Assert.That(CardDefinitionReference.TryParseQualified("base:village", out string extensionId, out string cardId), Is.True);
        Assert.That(extensionId, Is.EqualTo("base"));
        Assert.That(cardId, Is.EqualTo("village"));
        Assert.That(CardDefinitionReference.TryParseQualified(":village", out _, out _), Is.False);
        Assert.That(CardDefinitionReference.TryParseQualified("base:", out _, out _), Is.False);
        Assert.That(CardDefinitionReference.TryParseQualified("base:village:extra", out _, out _), Is.False);
    }

    [Test]
    public void GetCardId_AcceptsQualifiedAndLocalIds()
    {
        Assert.That(CardDefinitionReference.TryGetCardId("intrigue:mascarade", out string qualified), Is.True);
        Assert.That(qualified, Is.EqualTo("mascarade"));
        Assert.That(CardDefinitionReference.TryGetCardId("mascarade", out string local), Is.True);
        Assert.That(local, Is.EqualTo("mascarade"));
    }

    [Test]
    public void Matches_QualifiedReferenceRequiresSameExtension()
    {
        Assert.That(CardDefinitionReference.Matches("base:village", "base:village"), Is.True);
        Assert.That(CardDefinitionReference.Matches("base:village", "intrigue:village"), Is.False);
    }

    [Test]
    public void Matches_LocalIdMatchesQualifiedReferenceByCardId()
    {
        Assert.That(CardDefinitionReference.Matches("village", "base:village"), Is.True);
        Assert.That(CardDefinitionReference.Matches("village", "base:forge"), Is.False);
    }
}
#endif
