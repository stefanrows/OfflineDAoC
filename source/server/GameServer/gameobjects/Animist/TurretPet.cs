using DOL.AI.Brain;
using DOL.Database;

namespace DOL.GS
{
    public class TurretPet : GameSummonedPet
    {
        // Requested server balance: all Animist shrooms, regardless of owner.
        // Other summoned pets and the owner's own health are unchanged.
        public const double HealthMultiplier = 1.33;
        public override double MaxHealthScalingFactor => base.MaxHealthScalingFactor * HealthMultiplier;

        public Spell TurretSpell;

        // The Animist shroom templates have no speed. Only the main shroom moves (it follows its owner),
        // using the speed of a typical summoned pet; field turrets stay where they were planted.
        public const short MainPetMaxSpeedBase = 200;

        public TurretPet(INpcTemplate template) : base(template) { }

        protected void MakeMobile()
        {
            if (MaxSpeedBase <= 0)
                MaxSpeedBase = MainPetMaxSpeedBase;
        }

        /// <summary>
        /// The Animist whose realm abilities a shroom inherits: its direct owner when that is a player or a companion bot.
        /// Not <see cref="ControlledMobBrain.GetPlayerOwner"/>, which hands a companion's shroom the bot's human owner.
        /// </summary>
        public static GameLiving GetRealmAbilityOwner(GameLiving living) =>
            living is TurretPet { Owner: GamePlayer or GameBot } turret ? turret.Owner : null;

        /// <summary>The owner's realm ability bonus (Wild Power, Mastery of Magery, ...) for a shroom, 0 for anything else.</summary>
        public static int GetInheritedAbilityBonus(GameLiving living, eProperty property) =>
            GetRealmAbilityOwner(living)?.AbilityBonus[property] ?? 0;

        protected override void BuildAmbientTexts()
        {
            base.BuildAmbientTexts();

            if (ambientTexts.Count>0)
            {
                foreach (DbMobXAmbientBehavior ambientText in ambientTexts)
                    ambientText.Chance /= 5;
            }
        }

        public override void StartAttack(GameObject attackTarget)
        {
            if (attackTarget == null)
                return;

            if (attackTarget is GameLiving livingTarget && !GameServer.ServerRules.IsAllowedToAttack(this, livingTarget, true))
                return;

            if (Brain is IControlledBrain brain)
            {
                if (brain.AggressionState is eAggressionState.Passive)
                    return;
            }

            TargetObject = attackTarget;

            if (Brain is TurretMainPetTankBrain)
                attackComponent.RequestStartAttack();
        }

        public override void StartInterruptTimer(int duration, AttackData.eAttackType attackType, GameLiving attacker)
        {
            // Don't interrupt turrets (1.90 EU).
            return;
        }

        public override void OnCastSpellLosCheckFail(GameObject target)
        {
            // GameNPC would walk up to a target it cannot see. The main shroom stays with its owner instead.
            if (this is TurretFnfPet)
                base.OnCastSpellLosCheckFail(target);
            else if (castingComponent.QueuedSpellHandler?.Target == target)
                castingComponent.ClearUpQueuedSpellHandler();

            // This is where FnF turrets with LoS check on aggro enabled clear their list of hidden targets.
            // This can create a delay between attacks in some cases because turrets only ask one target at a time.
            if (Brain is TurretFNFBrain fnfBrain)
                fnfBrain.RemoveFromAggroList(target as GameLiving);
        }
    }
}
