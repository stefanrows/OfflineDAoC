using System.Linq;
using System.Text.RegularExpressions;
using DOL.GS;
using DOL.GS.Commands;
using NUnit.Framework;

namespace DOL.UnitTests
{
    [TestFixture]
    public class UT_TemporaryGroupSpawnMenu
    {
        [TestCase(eRealm.Albion)]
        [TestCase(eRealm.Midgard)]
        [TestCase(eRealm.Hibernia)]
        public void MenuContainsEveryClassFromEveryRealm(eRealm realm)
        {
            string[] expected = TemporaryGroupClassCatalog.All()
                .Select(entry => $"{entry.Realm}: {entry.CharacterClass}")
                .ToArray();
            string[] links = Regex.Matches(TemporaryGroupSpawnMenu.BuildMenuText(realm), @"\[([^\]]+)\]")
                .Select(match => match.Groups[1].Value)
                .ToArray();

            Assert.That(links, Is.EquivalentTo(expected));
            Assert.That(links, Has.Length.EqualTo(expected.Length));
        }

        [TestCase(eRealm.Albion)]
        [TestCase(eRealm.Midgard)]
        [TestCase(eRealm.Hibernia)]
        public void EveryClickableLabelResolvesToItsClass(eRealm realm)
        {
            foreach ((eRealm expectedRealm, eCharacterClass expected, _) in TemporaryGroupClassCatalog.All())
            {
                string label = $"{expectedRealm}: {expected}";
                Assert.That(TemporaryGroupClassCatalog.TryResolve(realm, label, out eRealm actualRealm, out eCharacterClass actual),
                    Is.True);
                Assert.That(actualRealm, Is.EqualTo(expectedRealm));
                Assert.That(actual, Is.EqualTo(expected));
            }
        }

        [Test]
        public void BareClassNameUsesThePlayerRealm()
        {
            Assert.That(TemporaryGroupClassCatalog.TryResolve(eRealm.Midgard, "Healer", out eRealm realm, out eCharacterClass characterClass),
                Is.True);
            Assert.That(realm, Is.EqualTo(eRealm.Midgard));
            Assert.That(characterClass, Is.EqualTo(eCharacterClass.Healer));
        }
    }
}
