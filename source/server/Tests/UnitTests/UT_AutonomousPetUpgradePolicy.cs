using System.Reflection;
using System.Runtime.CompilerServices;
using DOL.Database;
using DOL.GS;
using DOL.GS.Spells;
using NUnit.Framework;

namespace DOL.UnitTests;

[TestFixture, NonParallelizable]
public sealed class UT_AutonomousPetUpgradePolicy
{
    private PetTestLanguageScope _languageScope;
    private CompanionPolicyTestServerScope _serverScope;

    private sealed class Owner : GameBot
    {
        public Owner() : base((OfflineWorldBotRecord)null) { }
        public override byte Level { get; set; }
    }

    private sealed class Pet : GameSummonedPet
    {
        public Pet() : base((INpcTemplate)null) { }
        public override byte Level { get; set; }
    }

    private static bool IsMainPetUpgrade(GameBot owner, GameSummonedPet pet, Spell summon)
    {
        MethodInfo method = typeof(AutonomousPetSupport).GetMethod("IsMainPetUpgrade",
            BindingFlags.Static | BindingFlags.NonPublic)!;
        return (bool)method.Invoke(null, [owner, pet, summon])!;
    }

    [SetUp]
    public void SetUp()
    {
        _languageScope = new PetTestLanguageScope();
        _serverScope = new CompanionPolicyTestServerScope();
    }

    [TearDown]
    public void TearDown()
    {
        _serverScope.Dispose();
        _languageScope.Dispose();
    }

    [Test]
    public void SameSummonIsKeptUntilOwnerLevelsEnoughToImprovePet()
    {
        const int summonId = 987654;
        var owner = (Owner)RuntimeHelpers.GetUninitializedObject(typeof(Owner));
        owner.Level = 20;
        var pet = (Pet)RuntimeHelpers.GetUninitializedObject(typeof(Pet));
        pet.Level = 16;
        pet.SummonSpellID = summonId;
        pet.SummonOwnerLevel = 20;
        var summon = new Spell(new DbSpell
        {
            SpellID = summonId,
            Type = eSpellType.SummonCommander.ToString(),
            Target = "Pet",
            Damage = -88,
            Value = 44,
        }, 20);

        Assert.That(IsMainPetUpgrade(owner, pet, summon), Is.False,
            "The same spell at the same owner level cannot fix a lower-than-projected pet level.");

        owner.Level = 21;
        Assert.That(IsMainPetUpgrade(owner, pet, summon), Is.True,
            "A new owner level that raises the projected pet level can justify replacement.");

        pet.Level = 18;
        Assert.That(IsMainPetUpgrade(owner, pet, summon), Is.False,
            "Do not replace a pet when the new level would not improve it.");
    }
}
