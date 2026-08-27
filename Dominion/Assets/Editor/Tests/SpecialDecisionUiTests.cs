#if UNITY_INCLUDE_TESTS
using NUnit.Framework;

public sealed class SpecialDecisionUiTests
{
    [TestCase(0f, 11, 0)]
    [TestCase(0.24f, 11, 2)]
    [TestCase(0.25f, 11, 3)]
    [TestCase(0.5f, 11, 5)]
    [TestCase(1f, 11, 10)]
    public void DeckPercentage_IsRoundedToAValidInsertionPosition(float percentage, int choices, int expected)
    {
        Assert.AreEqual(expected, DeckPositionDecisionView.PositionFromPercentage(percentage, choices));
    }

    [Test]
    public void CardSearch_IsAccentInsensitiveAndNeverReturnsMoreThanFourSuggestions()
    {
        string[] labels = { "Éclaireur", "Écuries", "Évêque", "Émissaire", "Épée", "Village" };
        var matches = CardNameDecisionView.FindMatches("e", labels, CardNameDecisionView.MaximumVisibleSuggestions);

        Assert.AreEqual(4, matches.Count);
        Assert.That(matches, Does.Contain(0));
        Assert.That(matches, Does.Not.Contain(5));
    }
}
#endif
