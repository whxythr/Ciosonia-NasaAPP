using System.Collections.Generic;
using NUnit.Framework;
using SpaceSim.Localization;

namespace SpaceSim.Tests
{
    /// <summary>Interface translation: lookups, kept punctuation, fallback to the source, table format.</summary>
    public class LocTests
    {
        [SetUp]
        public void SetUp()
        {
            Loc.SetTable(Language.English, new Dictionary<string, string> { { "Закрыть", "Close" }, { "м/с²", "m/s²" } });
            Loc.SetLanguage(Language.English);
        }

        [TearDown]
        public void TearDown()
        {
            Loc.SetTable(Language.English, new Dictionary<string, string>());
            Loc.SetLanguage(Language.Russian);
        }

        [Test]
        public void KeepsSpacesAndBracketsAroundTheText()
        {
            Assert.AreEqual("Close", Loc.T("Закрыть"));
            Assert.AreEqual("  · Close (", Loc.T("  · Закрыть ("));
            Assert.AreEqual(" m/s², ", Loc.T(" м/с², "));
        }

        [Test]
        public void UnknownOrEmptyTextFallsBackToTheSource()
        {
            Assert.AreEqual("Неизвестно", Loc.T("Неизвестно"));
            Assert.AreEqual(" · 12 ", Loc.T(" · 12 "));
            Assert.IsNull(Loc.T(null));
        }

        [Test]
        public void LanguageSwitchChangesTheResultAndTheVersion()
        {
            int version = Loc.Version;
            Loc.SetLanguage(Language.Russian);
            Assert.AreEqual("Закрыть", Loc.T("Закрыть"));
            Assert.Greater(Loc.Version, version);
        }

        [Test]
        public void OtherLetterCaseUsesTheEntryAndKeepsTheCase()
        {
            // Regression: «ЗАКРЫТЬ» (caps button) had no entry of its own and stayed Russian in English.
            Assert.AreEqual("CLOSE", Loc.T("ЗАКРЫТЬ"));
            Assert.AreEqual("CLOSE  →", Loc.T("ЗАКРЫТЬ  →"));
            Assert.AreEqual("Close", Loc.T("закрыть".Substring(0, 1).ToUpperInvariant() + "акрыть"));
        }

        [Test]
        public void SwitchingBackAndForthTranslatesAgain()
        {
            Assert.AreEqual("Close", Loc.T("Закрыть"));
            Loc.SetLanguage(Language.Russian);
            Assert.AreEqual("Закрыть", Loc.T("Закрыть"));
            Loc.SetLanguage(Language.English);
            Assert.AreEqual("Close", Loc.T("Закрыть"), "the cache of the previous language must not survive the switch");
        }

        [Test]
        public void ParseReadsTabSeparatedLines()
        {
            var table = Loc.Parse("а\tb\r\nбез таба\n\tпусто\nстрока\\n2\tline\\n2\n");
            Assert.AreEqual(2, table.Count);
            Assert.AreEqual("b", table["а"]);
            Assert.AreEqual("line\n2", table["строка\n2"]);
        }
    }
}
