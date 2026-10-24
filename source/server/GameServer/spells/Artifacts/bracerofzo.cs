using System;
using DOL.AI.Brain;
using DOL.GS.PacketHandler;

namespace DOL.GS.Spells
{
    /// <summary>
    /// Zo' Arkat summoning
    /// </summary>
    [SpellHandler(eSpellType.ZoSummon)]
    public class BracerOfZo : SpellHandler, IArtifactEffectHandler
    {
		private static readonly Logging.Logger log = Logging.LoggerManager.Create(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);

		public override bool IsUnPurgeAble { get { return true; } }
		
		protected ZoarkatPet[] deamons = new ZoarkatPet[3];
        
		public override ECSGameSpellEffect CreateECSEffect(in ECSGameEffectInitParams initParams)
		{
			return ArtifactSpellEffect.Create(initParams);
		}

		public void OnArtifactEffectStart(ECSGameSpellEffect effect)
		{
			if (Caster.TargetObject is not GameLiving target) return;
			if (Caster is not GamePlayer player) return;

            INpcTemplate template = NpcTemplateMgr.GetTemplate(Spell.LifeDrainReturn);
            if (template == null)
			{
				String errorMessage = String.Format("NPC template {0} is missing, spell ID = {1}", Spell.LifeDrainReturn, Spell.ID);
				if (log.IsWarnEnabled) log.Warn(errorMessage);
				if (player.Client?.Account?.PrivLevel > 1) MessageToCaster(errorMessage, eChatType.CT_System);
				return;
			}

            Point2D spawnPoint = Caster.GetPointFromHeading( Caster.Heading, 64 );
            for (int i = 0; i < deamons.Length; i++)
            {
                deamons[i] = new ZoarkatPet(template);
                deamons[i].SetOwnBrain(new ProcPetBrain(player));
                deamons[i].X = spawnPoint.X + Util.Random(20,40) - Util.Random(20,40);
                deamons[i].Y = spawnPoint.Y + Util.Random(20,40) - Util.Random(20,40);
                deamons[i].Z = Caster.Z;
                deamons[i].CurrentRegion = Caster.CurrentRegion;
                deamons[i].Heading = (ushort)((Caster.Heading + 2048) % 4096);
                deamons[i].Realm = Caster.Realm;
                deamons[i].Level = 36;
                deamons[i].Flags |= GameNPC.eFlags.FLYING;
                deamons[i].AddToWorld();
				(deamons[i].Brain as IOldAggressiveBrain)?.AddToAggroList(target);
				(deamons[i].Brain as ProcPetBrain)?.Think();
            }
		}

        public void OnArtifactEffectStop(ECSGameSpellEffect effect)
        {
            for (int i = 0; i < deamons.Length; i++)
            {
            	if (deamons[i] != null)
            	{
            		deamons[i].Health = 0;
            		deamons[i].Delete();
            		deamons[i] = null;
            	}
            }
        }

        public override double CalculateSpellResistChance(GameLiving target) { return 0; }
        public BracerOfZo(GameLiving caster, Spell spell, SpellLine line) : base(caster, spell, line) { }
    }

    [SpellHandler(eSpellType.Bedazzlement)]
    public class ZoDebuffSpellHandler : DualStatDebuff, IArtifactEffectHandler
    {
		public override eProperty Property1 { get { return eProperty.FumbleChance; } }
		public override eProperty Property2 { get { return eProperty.SpellFumbleChance; } }

		public override void ApplyEffectOnTarget(GameLiving target)
		{
			base.ApplyEffectOnTarget(target);
			target.StartInterruptTimer(target.SpellInterruptDuration, AttackData.eAttackType.Spell, Caster);
		}
		
        public ZoDebuffSpellHandler(GameLiving caster, Spell spell, SpellLine line) : base(caster, spell, line) { }

        // Bridged artifact effects share the eEffect.Unknown bucket; the stock debuff base
        // conflicts with everything there. Only another spell of the same type conflicts.
        public override bool HasConflictingEffectWith(ISpellHandler compare)
        {
            return ArtifactSpells.ConflictsWith(this, compare);
        }

        public override ECSGameSpellEffect CreateECSEffect(in ECSGameEffectInitParams initParams)
        {
            return ArtifactSpellEffect.Create(initParams);
        }

        public virtual void OnArtifactEffectStart(ECSGameSpellEffect effect)
        {
            ArtifactSpellEffect.ApplyProperties(this, effect.Owner, (int) (Spell.Value * effect.Effectiveness), false);
            ArtifactSpells.RefreshStats(effect.Owner, resists: true);
        }

        public virtual void OnArtifactEffectStop(ECSGameSpellEffect effect)
        {
            ArtifactSpellEffect.ApplyProperties(this, effect.Owner, (int) (Spell.Value * effect.Effectiveness), true);
            ArtifactSpells.RefreshStats(effect.Owner, resists: true);
        }
    }
}

namespace DOL.GS
{
    public class ZoarkatPet : GameSummonedPet
	{
		public override int MaxHealth
        {
            get { return Level*10; }
        }
		public override void OnAttackedByEnemy(AttackData ad) { }
		public ZoarkatPet(INpcTemplate npcTemplate) : base(npcTemplate) { }
	}
}
