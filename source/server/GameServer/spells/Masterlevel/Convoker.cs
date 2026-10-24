using System;
using System.Collections;
using DOL.AI.Brain;
using DOL.Database;
using DOL.Events;
using DOL.GS;
using DOL.GS.Effects;
using DOL.GS.PacketHandler;
using DOL.GS.PlayerClass;

namespace DOL.GS.Spells
{
	//http://www.camelotherald.com/masterlevels/ma.php?ml=Convoker
	//no shared timer
	#region Convoker-1
	[SpellHandler(eSpellType.SummonWood)]
	public class SummonWoodSpellHandler : SummonItemSpellHandler
	{
		public SummonWoodSpellHandler(GameLiving caster, Spell spell, SpellLine line)
			: base(caster, spell, line)
		{
			DbItemTemplate template = GameServer.Database.FindObjectByKey<DbItemTemplate>("mysticwood_wooden_boards");
			if (template != null)
			{
                items.Add(GameInventoryItem.Create(template));
                foreach (DbInventoryItem item in items)
                {
                    if (item.IsStackable)
                    {
                        item.Count = 1;
                        item.Weight = item.Count * item.Weight;
                    }
                }
			}
		}
	}
	#endregion

	//no shared timer
	#region Convoker-2
	[SpellHandler(eSpellType.PrescienceNode)]
	public class PrescienceNodeSpellHandler : FontSpellHandler
	{
		// constructor
		public PrescienceNodeSpellHandler(GameLiving caster, Spell spell, SpellLine line)
			: base(caster, spell, line)
		{
			ApplyOnNPC = false;
			ApplyOnCombat = true;
			Friendly = false; // Prescience reveals enemies; its sub-spell targets ENEMY

			//Construct a new font.
			font = new GameFont();
			font.Model = 2584;
			font.Name = spell.Name;
			font.Realm = caster.Realm;
			font.X = caster.X;
			font.Y = caster.Y;
			font.Z = caster.Z;
			font.CurrentRegionID = caster.CurrentRegionID;
			font.Heading = caster.Heading;
			font.Owner = caster;

			// Construct the font spell
			dbs = new DbSpell();
			dbs.SpellID = spell.ID;
			dbs.Name = spell.Name;
			dbs.Icon = 7312;
			dbs.ClientEffect = 7312;
			dbs.Damage = spell.Damage;
			dbs.DamageType = (int)spell.DamageType;
			dbs.Target = eSpellTarget.ENEMY.ToString();
			dbs.Radius = 0;
			dbs.Type = eSpellType.Prescience.ToString();
			dbs.Value = spell.Value;
			dbs.Duration = spell.ResurrectHealth;
			dbs.Frequency = spell.ResurrectMana;
			dbs.Pulse = 0;
			dbs.PulsePower = 0;
			dbs.LifeDrainReturn = spell.LifeDrainReturn;
			dbs.Power = 0;
			dbs.CastTime = 0;
			dbs.Range = WorldMgr.VISIBILITY_DISTANCE;
			sRadius = 2000;
			s = new Spell(dbs, 50);
			sl = SkillBase.GetSpellLine(GlobalSpellsLines.Reserved_Spells);
			heal = ScriptMgr.CreateSpellHandler(m_caster, s, sl);
		}
	}
	[SpellHandler(eSpellType.Prescience)]
	public class PrescienceSpellHandler : SpellHandler
	{
		public override bool HasConflictingEffectWith(ISpellHandler compare)
		{
			return false;
		}

		public override bool HasPositiveEffect
		{
			get { return false; }
		}

		public override void OnEffectStart(GameSpellEffect effect)
		{
			base.OnEffectStart(effect);
		}

		public override int OnEffectExpires(GameSpellEffect effect, bool noMessages)
		{
			return base.OnEffectExpires(effect, noMessages);
		}

		public PrescienceSpellHandler(GameLiving caster, Spell spell, SpellLine line) : base(caster, spell, line) { }
	}
	#endregion

	//no shared timer
	#region Convoker-3
	[SpellHandler(eSpellType.PowerTrap)]
	public class PowerTrapSpellHandler : MineSpellHandler
	{
		// constructor
		public PowerTrapSpellHandler(GameLiving caster, Spell spell, SpellLine line)
			: base(caster, spell, line)
		{
			//Construct a new mine.
			mine = new(new BlankBrain());
			mine.Model = 2590;
			mine.Name = spell.Name;
			mine.Realm = caster.Realm;
			mine.X = caster.X;
			mine.Y = caster.Y;
			mine.Z = caster.Z;
			mine.CurrentRegionID = caster.CurrentRegionID;
			mine.Heading = caster.Heading;
			mine.Owner = caster;

			// Construct the mine spell
			dbs = new DbSpell();
			dbs.SpellID = spell.ID;
			dbs.Name = spell.Name;
			dbs.Icon = 7313;
			dbs.ClientEffect = 7313;
			dbs.Damage = spell.Damage;
			dbs.DamageType = (int)spell.DamageType;
			dbs.Target = eSpellTarget.ENEMY.ToString();
			dbs.Radius = 0;
			dbs.Type = eSpellType.PowerRend.ToString();
			dbs.Value = spell.Value;
			dbs.Duration = spell.ResurrectHealth;
			dbs.Frequency = spell.ResurrectMana;
			dbs.Pulse = 0;
			dbs.PulsePower = 0;
			dbs.LifeDrainReturn = spell.LifeDrainReturn;
			dbs.Power = 0;
			dbs.CastTime = 0;
			dbs.Range = WorldMgr.VISIBILITY_DISTANCE;
			sRadius = 350;
			s = new Spell(dbs, 1);
			sl = SkillBase.GetSpellLine(GlobalSpellsLines.Reserved_Spells);
			trap = ScriptMgr.CreateSpellHandler(m_caster, s, sl);
		}
	}
	#endregion

	//no shared timer
	#region Convoker-4
	[SpellHandler(eSpellType.SpeedWrapWard)]
	public class SpeedWrapWardSpellHandler : FontSpellHandler
	{
		// constructor
		public SpeedWrapWardSpellHandler(GameLiving caster, Spell spell, SpellLine line)
			: base(caster, spell, line)
		{
			ApplyOnCombat = true;
			Friendly = false;

			//Construct a new mine.
			font = new GameFont();
			font.Model = 2586;
			font.Name = spell.Name;
			font.Realm = caster.Realm;
			font.X = caster.X;
			font.Y = caster.Y;
			font.Z = caster.Z;
			font.CurrentRegionID = caster.CurrentRegionID;
			font.Heading = caster.Heading;
			font.Owner = caster;

			// Construct the mine spell
			dbs = new DbSpell();
			dbs.SpellID = spell.ID;
			dbs.Name = spell.Name;
			dbs.Icon = 7237;
			dbs.ClientEffect = 7237;
			dbs.Damage = spell.Damage;
			dbs.DamageType = (int)spell.DamageType;
			dbs.Target = eSpellTarget.ENEMY.ToString();
			dbs.Radius = 0;
			dbs.Type = eSpellType.SpeedWrap.ToString();
			dbs.Value = spell.Value;
			dbs.Duration = spell.ResurrectHealth;
			dbs.Frequency = spell.ResurrectMana;
			dbs.Pulse = 0;
			dbs.PulsePower = 0;
			dbs.LifeDrainReturn = spell.LifeDrainReturn;
			dbs.Power = 0;
			dbs.CastTime = 0;
			dbs.Range = WorldMgr.VISIBILITY_DISTANCE;
			sRadius = 1000;
			dbs.SpellGroup = 9;
			s = new Spell(dbs, 50);
			sl = SkillBase.GetSpellLine(GlobalSpellsLines.Reserved_Spells);
			heal = ScriptMgr.CreateSpellHandler(m_caster, s, sl);
		}
	}
	[SpellHandler(eSpellType.SpeedWrap)]
	public class SpeedWrapSpellHandler : SpellHandler
	{
		public override double CalculateSpellResistChance(GameLiving target)
		{
			return 0;
		}

		public override void OnEffectStart(GameSpellEffect effect)
		{
			base.OnEffectStart(effect);
			if (effect.Owner is GamePlayer)
				((GamePlayer)effect.Owner).Out.SendUpdateMaxSpeed();
		}
		public override int OnEffectExpires(GameSpellEffect effect, bool noMessages)
		{
			if (effect.Owner is GamePlayer)
				((GamePlayer)effect.Owner).Out.SendUpdateMaxSpeed();
			return base.OnEffectExpires(effect, noMessages);
		}
		public SpeedWrapSpellHandler(GameLiving caster, Spell spell, SpellLine line) : base(caster, spell, line) { }
	}
	#endregion

	//shared timer 1
	#region Convoker-5
	[SpellHandler(eSpellType.SummonWarcrystal)]
	public class SummonWarcrystalSpellHandler : SummonItemSpellHandler
	{
		public SummonWarcrystalSpellHandler(GameLiving caster, Spell spell, SpellLine line)
			: base(caster, spell, line)
		{
			string ammo = string.Empty;
			switch (Util.Random(1, 2))
			{
				case 1:
					ammo = "mystic_ammo_heat";
					break;
				case 2:
					ammo = "mystic_ammo_cold";
					break;
			}
			DbItemTemplate template = GameServer.Database.FindObjectByKey<DbItemTemplate>(ammo);
			if (template != null)
			{
                items.Add(GameInventoryItem.Create(template));
                foreach (DbInventoryItem item in items)
                {
                    if (item.IsStackable)
                    {
                        item.Count = 1;
                        item.Weight = item.Count * item.Weight;
                    }
                }

			}
		}
	}
	#endregion

	//shared timer 1
	#region Convoker-6
	[SpellHandler(eSpellType.Battlewarder)]
	public class BattlewarderSpellHandler : SpellHandler
	{
		/// <summary>
		/// Execute battle warder summon spell
		/// </summary>
		/// <param name="target"></param>
		public override void FinishSpellCast(GameLiving target)
		{
			m_caster.Mana -= PowerCost(target);
			base.FinishSpellCast(target);
		}
		public override bool HasConflictingEffectWith(ISpellHandler compare)
		{
			return false;
		}

		// The legacy OnEffectStart(GameSpellEffect) never ran, so no warder was ever
		// planted: SpellHandler builds an ECS effect. BattlewarderECSEffect plants the
		// warder and dismisses it when the caster moves, attacks or casts.
		public override ECSGameSpellEffect CreateECSEffect(in ECSGameEffectInitParams initParams)
		{
			return ECSGameEffectFactory.Create(initParams, static (in i) => new BattlewarderECSEffect(i));
		}

		public override bool CheckBeginCast(GameLiving selectedTarget)
		{
			if (!base.CheckBeginCast(selectedTarget)) return false;
			if (!InReach(m_caster, Spell))
			{
				MessageToCaster("Your area target is out of range.  Set a closer ground position.", eChatType.CT_SpellResisted);
				return false;
			}
			return true;

		}
		/// <summary>
		/// A ground target is set, in view and within the spell's range (2,300). The range was
		/// never checked, so a warder could be planted anywhere the client still saw.
		/// </summary>
		public static bool InReach(GameLiving caster, Spell spell) =>
			caster.GroundTarget.IsValid && caster.GroundTargetInView &&
			(spell.Range <= 0 || caster.IsWithinRadius(caster.GroundTarget, spell.Range));

		public BattlewarderSpellHandler(GameLiving caster, Spell spell, SpellLine line)
			: base(caster, spell, line) { }
	}

	/// <summary>
	/// Battlewarder: a warder at the caster's ground target that cannot move and
	/// fights for the caster while the caster holds still.
	/// </summary>
	public class BattlewarderECSEffect : ECSGameSpellEffect
	{
		public const string WarderFallen = "fallen";

		private GameNPC _warder;
		private MovementWatch _watch;

		public BattlewarderECSEffect(in ECSGameEffectInitParams initParams) : base(initParams) { }

		public GameNPC Warder => _warder;
		public MovementWatch Watch => _watch;

		/// <summary>Why the hold asked to end (a MovementWatch reason or "fallen"), null while held.</summary>
		public string EndReason { get; private set; }

		/// <summary>Puts the warder into the world; tests replace it.</summary>
		protected virtual bool PlantWarder(GameNPC warder)
		{
			return warder.AddToWorld();
		}

		public override void OnStartEffect()
		{
			base.OnStartEffect();

			// Players and bots alike (TOA_PLAN WP2.6); only players read the messages.
			if (SpellHandler?.Caster is not GameLiving caster || !caster.IsAlive)
				return;

			if (!caster.GroundTarget.IsValid || !caster.GroundTargetInView)
			{
				MasterLevelPlacement.TellCaster(caster, "Your area target is out of range.  Set a closer ground position.", eChatType.CT_SpellResisted);
				End();
				return;
			}

			_warder = new BattleWarderNpc
			{
				CurrentRegion = caster.CurrentRegion,
				Heading = (ushort)((caster.Heading + 2048) % 4096),
				Level = 70,
				Realm = caster.Realm,
				Name = "Battle Warder",
				Model = 993,
				MaxSpeedBase = 0,
				GuildName = string.Empty,
				Size = 50,
				X = caster.GroundTarget.X,
				Y = caster.GroundTarget.Y,
				Z = caster.GroundTarget.Z,
			};
			_warder.AddBrain(new MLBrain());

			if (!PlantWarder(_warder))
			{
				_warder = null;
				MasterLevelPlacement.TellCaster(caster, "Your area target is out of range.  Set a closer ground position.", eChatType.CT_SpellResisted);
				End();
				return;
			}

			GameEventMgr.AddHandler(_warder, GameLivingEvent.Dying, new DOLEventHandler(WarderFell));

			_watch = new MovementWatch(caster, why =>
			{
				if (why != MovementWatch.Gone)
					MasterLevelPlacement.TellCaster(caster, "Your concentration fades", eChatType.CT_SpellExpires);

				EndReason ??= why;
				End();
			},
			onMove: true, onAttack: true, onCast: true, ownCast: SpellHandler);
			_watch.Start();
		}

		public override void OnStopEffect()
		{
			base.OnStopEffect();

			_watch?.Stop();
			_watch = null;

			GameNPC warder = _warder;
			_warder = null;

			if (warder != null)
			{
				GameEventMgr.RemoveHandler(warder, GameLivingEvent.Dying, new DOLEventHandler(WarderFell));
				warder.Delete();
			}
		}

		// Dying is still raised, so the warder's death can be listened for.
		private void WarderFell(DOLEvent e, object sender, EventArgs args)
		{
			MasterLevelPlacement.TellCaster(SpellHandler?.Caster, "Your Battle Warder has fallen!", eChatType.CT_SpellExpires);

			EndReason ??= WarderFallen;
			End();
		}
	}

	/// <summary>
	/// The Battle Warder body. It exists only while its effect holds: it never
	/// respawns (a plain realm GameNPC would come back ownerless after 5-10
	/// minutes, aggressive and without an effect to remove it), and killing it
	/// pays no experience, realm points or loot.
	/// </summary>
	public class BattleWarderNpc : GameNPC
	{
		public override int RespawnInterval
		{
			get => -1;
			set { }
		}

		public override RewardEligibility RewardStatus => RewardEligibility.DeniedInvalid;

		public override bool CanDropLoot => false;

		public override void StartRespawn() { }
	}
	#endregion

	//no shared timer
	#region Convoker-7
	[SpellHandler(eSpellType.DissonanceTrap)]
	public class DissonanceTrapSpellHandler : MineSpellHandler
	{
		// constructor
		public DissonanceTrapSpellHandler(GameLiving caster, Spell spell, SpellLine line)
			: base(caster, spell, line)
		{
			//Construct a new mine.
			mine = new(new BlankBrain());
			mine.Model = 2588;
			mine.Name = spell.Name;
			mine.Realm = caster.Realm;
			mine.X = caster.X;
			mine.Y = caster.Y;
			mine.Z = caster.Z;
			mine.CurrentRegionID = caster.CurrentRegionID;
			mine.Heading = caster.Heading;
			mine.Owner = caster;

			// Construct the mine spell
			dbs = new DbSpell();
			dbs.SpellID = spell.ID;
			dbs.Name = spell.Name;
			dbs.Icon = 7255;
			dbs.ClientEffect = 7255;
			dbs.Damage = spell.Damage;
			dbs.DamageType = (int)spell.DamageType;
			dbs.Target = eSpellTarget.ENEMY.ToString();
			dbs.Radius = 0;
			dbs.Type = eSpellType.DirectDamage.ToString();
			dbs.Value = spell.Value;
			dbs.Duration = spell.ResurrectHealth;
			dbs.Frequency = spell.ResurrectMana;
			dbs.Pulse = 0;
			dbs.PulsePower = 0;
			dbs.LifeDrainReturn = spell.LifeDrainReturn;
			dbs.Power = 0;
			dbs.CastTime = 0;
			dbs.Range = WorldMgr.VISIBILITY_DISTANCE;
			sRadius = 350;
			s = new Spell(dbs, 1);
			sl = SkillBase.GetSpellLine(GlobalSpellsLines.Reserved_Spells);
			trap = ScriptMgr.CreateSpellHandler(m_caster, s, sl);
		}
	}
	#endregion

	//no shared timer
	#region Convoker-8
	[SpellHandler(eSpellType.BrittleGuard)]
	public class BrittleGuardSpellHandler : MasterlevelHandling
	{
		private static readonly Logging.Logger log = Logging.LoggerManager.Create(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);

		GameNPC summoned = null;
		GameSpellEffect beffect = null;
		public BrittleGuardSpellHandler(GameLiving caster, Spell spell, SpellLine line)
			: base(caster, spell, line)
		{

		}

		/// <summary>
		/// called after normal spell cast is completed and effect has to be started
		/// </summary>
		public override void FinishSpellCast(GameLiving target)
		{
			m_caster.Mana -= PowerCost(target);
			base.FinishSpellCast(target);
		}

		public override void ApplyEffectOnTarget(GameLiving target)
		{
			GamePlayer player = Caster as GamePlayer;
			if (player == null)
			{
				return;
			}

			INpcTemplate template = NpcTemplateMgr.GetTemplate(Spell.LifeDrainReturn);
			if (template == null)
			{
				if (log.IsWarnEnabled)
					log.WarnFormat("NPC template {0} not found! Spell: {1}", Spell.LifeDrainReturn, Spell.ToString());
				MessageToCaster("NPC template " + Spell.LifeDrainReturn + " not found!", eChatType.CT_System);
				return;
			}

			Point2D summonloc;
			beffect = CreateSpellEffect(target, CasterEffectiveness);
			{
				summonloc = target.GetPointFromHeading( target.Heading, 64 );

				BrittleBrain controlledBrain = new BrittleBrain(player);
				controlledBrain.IsMainPet = false;
				summoned = new GameNPC(template);
				summoned.SetOwnBrain(controlledBrain);
				summoned.X = summonloc.X;
				summoned.Y = summonloc.Y;
				summoned.Z = target.Z;
				summoned.CurrentRegion = target.CurrentRegion;
				summoned.Heading = (ushort)((target.Heading + 2048) % 4096);
				summoned.Realm = target.Realm;
				summoned.Level = 1;
				summoned.Size = 10;
				summoned.AddToWorld();
				controlledBrain.AggressionState = eAggressionState.Passive;
				GameEventMgr.AddHandler(summoned, GameLivingEvent.Dying, new DOLEventHandler(GuardDie));
				beffect.Start(Caster);
			}
		}
		private void GuardDie(DOLEvent e, object sender, EventArgs args)
		{
			GameNPC bguard = sender as GameNPC;
			if(bguard==summoned)
			{
				GameEventMgr.RemoveHandler(summoned, GameLivingEvent.Dying, new DOLEventHandler(GuardDie));
				beffect.Cancel(false);
			}
		}
		/// <summary>
		/// When an applied effect expires.
		/// Duration spells only.
		/// </summary>
		/// <param name="effect">The expired effect</param>
		/// <param name="noMessages">true, when no messages should be sent to player and surrounding</param>
		/// <returns>immunity duration in milliseconds</returns>
		public override int OnEffectExpires(GameSpellEffect effect, bool noMessages)
		{
			if(summoned!=null)
			{
				summoned.Health = 0; // to send proper remove packet
				summoned.Delete();
			}
			return base.OnEffectExpires(effect, noMessages);
		}
	}
	#endregion

	//no shared timer
	#region Convoker-9
	[SpellHandler(eSpellType.SummonMastery)]
	public class Convoker9Handler : MasterlevelHandling
		//public class Convoker9Handler : MasterlevelBuffHandling
	{
		private GameNPC m_living;
		private GamePlayer m_player;

		//public override eProperty Property1 { get { return eProperty.MeleeDamage; } }

		public override void ApplyEffectOnTarget(GameLiving target)
		{
			foreach (JuggernautEffect jg in target.EffectList.GetAllOfType<JuggernautEffect>())
			{
				if (jg != null)
				{
					MessageToCaster("Your Pet already has an ability of this type active", eChatType.CT_SpellResisted);
					return;
				}
			}

			// Add byNefa 04.02.2011 13:35
			// Check if Necro try to use ML9 Convoker at own Pet
			if  (m_player != null && m_player.CharacterClass is ClassDisciple)
			{
				NecromancerPet necroPet = target as NecromancerPet;
				if (necroPet == null || necroPet.Owner == m_player)
				{ // Caster is a Nekro and his Target is his Own Pet
					MessageToCaster("You cant use this ability on your own Pet", eChatType.CT_SpellResisted);
					return;
				}
			}
			base.ApplyEffectOnTarget(target);
		}

		public override void OnEffectStart(GameSpellEffect effect)
		{
			m_living = m_player.ControlledBrain.Body;
			m_living.Level += 20;
			m_living.BaseBuffBonusCategory[eProperty.MeleeDamage] += 275;
			m_living.BaseBuffBonusCategory[eProperty.ArmorAbsorption] += 75;
			m_living.Size += 40;
			base.OnEffectStart(effect);
		}

		public override int OnEffectExpires(GameSpellEffect effect, bool noMessages)
		{
			m_living.Level -= 20;
			m_living.BaseBuffBonusCategory[eProperty.MeleeDamage] -= 275;
			m_living.BaseBuffBonusCategory[eProperty.ArmorAbsorption] -= 75;
			m_living.Size -= 40;
			return base.OnEffectExpires(effect, noMessages);
		}

		public Convoker9Handler(GameLiving caster, Spell spell, SpellLine line) : base(caster, spell, line)
		{
			m_player = caster as GamePlayer;
		}
	}
	#endregion


	//no shared timer
	#region Convoker-10
	[SpellHandler(eSpellType.SummonTitan)]
	public class Convoker10SpellHandler : MasterlevelHandling
	{
		private static readonly Logging.Logger log = Logging.LoggerManager.Create(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);

		private int x, y, z;

		public Convoker10SpellHandler(GameLiving caster, Spell spell, SpellLine line) : base(caster, spell, line) { }

		public override bool HasPositiveEffect => true;

		public override bool CheckBeginCast(GameLiving selectedTarget)
		{
			if(!CheckCastLocation())
				return false;
			return base.CheckBeginCast(selectedTarget);
		}

		/// <summary>
		/// called after normal spell cast is completed and effect has to be started
		/// </summary>
		public override void FinishSpellCast(GameLiving target)
		{
			m_caster.Mana -= PowerCost(target);
			base.FinishSpellCast(target);
		}

		/// <summary>
		/// Summons the Crystal Titan for a player or a bot (TOA_PLAN WP2.6). It used to start a
		/// legacy GameSpellEffect whose timer never ran, so the titan never left; it now lives
		/// for the spell's duration (5 minutes) on a CrystalTitanECSEffect, which removes it.
		/// The titan is the caster's pet (TitanBrain), so its kills pay the caster as any pet's.
		/// </summary>
		public override void ApplyEffectOnTarget(GameLiving target)
		{
			if (Caster == null || !Caster.IsAlive)
				return;

			INpcTemplate template = NpcTemplateMgr.GetTemplate(Spell.LifeDrainReturn);
			if (template == null)
			{
				if (log.IsWarnEnabled)
					log.WarnFormat("NPC template {0} not found! Spell: {1}", Spell.LifeDrainReturn, Spell.ToString());
				MessageToCaster("NPC template " + Spell.LifeDrainReturn + " not found!", eChatType.CT_System);
				return;
			}

			TitanBrain controlledBrain = new TitanBrain(Caster);
			controlledBrain.IsMainPet = false;
			controlledBrain.WalkState = eWalkState.Stay;
			GameNPC summoned = new GameNPC(template);
			summoned.SetOwnBrain(controlledBrain);
			//Suncheck:
			//	Is needed, else it can cause error (i.e. /cast-command)
			if (x == 0 || y == 0)
				CheckCastLocation();
			summoned.X = x;
			summoned.Y = y;
			summoned.Z = z;
			summoned.CurrentRegion = Caster.CurrentRegion;
			summoned.Heading = (ushort)((Caster.Heading + 2048) % 4096);
			summoned.Realm = Caster.Realm;
			summoned.Size = 10;
			summoned.Level = 100;
			summoned.Flags |= GameNPC.eFlags.PEACE;

			if (!summoned.AddToWorld())
				return;

			controlledBrain.AggressionState = eAggressionState.Aggressive;
			ECSGameEffectFactory.Create(CrystalTitanECSEffect.ParamsFor(summoned, this, CasterEffectiveness),
				static (in ECSGameEffectInitParams i) => new CrystalTitanECSEffect(i));
		}

		private bool CheckCastLocation()
		{
			x = Caster.X;
			y = Caster.Y;
			z = Caster.Z;
			if (Spell.Target == eSpellTarget.AREA)
			{
				if (!Caster.GroundTarget.IsValid)
				{
					MessageToCaster("You must set a groundtarget!", eChatType.CT_SpellResisted);
					return false;
				}

				if (!Caster.GroundTargetInView)
				{
					MessageToCaster("Your area target is not in view.", eChatType.CT_SpellResisted);
					return false;
				}

				x = Caster.GroundTarget.X;
				y = Caster.GroundTarget.Y;
				z = Caster.GroundTarget.Z;
			}
			return true;
		}

		public override double CalculateSpellResistChance(GameLiving target)
		{
			return 0;
		}
	}

	/// <summary>
	/// The Crystal Titan's life: it grows from size 10 to 60 in 2-second steps and stays
	/// peaceful until fully grown, then fights; when the effect ends (duration, death) it goes.
	/// </summary>
	public class CrystalTitanECSEffect : ECSGameSpellEffect
	{
		public const int GrowInterval = 2000;
		public const int FullSize = 60;

		private ECSGameTimer _growTimer;

		public CrystalTitanECSEffect(in ECSGameEffectInitParams initParams) : base(initParams)
		{
			EffectType = eEffect.Unknown;
		}

		/// <summary>The titan's effect lasts the summoning spell's duration (5 minutes in the data).</summary>
		public static ECSGameEffectInitParams ParamsFor(GameNPC titan, ISpellHandler handler, double effectiveness) =>
			new(titan, handler.Spell.Duration, effectiveness, handler);

		public override void OnStartEffect()
		{
			base.OnStartEffect();
			_growTimer = new ECSGameTimer(Owner, Grow, GrowInterval);
		}

		/// <summary>One growth step; false once the titan is fully grown and fighting.</summary>
		public bool GrowStep()
		{
			if (Owner is not GameNPC titan || titan.ObjectState == GameObject.eObjectState.Deleted)
				return false;

			if (titan.Size < FullSize)
			{
				titan.Size = (byte)Math.Min(FullSize, titan.Size + 10);

				if (titan.Size < FullSize)
					return true;
			}

			titan.Flags &= ~GameNPC.eFlags.PEACE;
			return false;
		}

		private int Grow(ECSGameTimer timer) => GrowStep() ? GrowInterval : 0;

		public override void OnStopEffect()
		{
			base.OnStopEffect();
			_growTimer?.Stop();
			_growTimer = null;

			if (Owner != null && Owner.ObjectState != GameObject.eObjectState.Deleted)
			{
				Owner.Health = 0; // to send proper remove packet
				Owner.Delete();
			}
		}
	}
	#endregion
}

#region BrittleBrain
namespace DOL.AI.Brain
{
    public class BrittleBrain : ControlledMobBrain
	{
		public BrittleBrain(GameLiving owner)
			: base(owner)
		{
			if (owner == null)
				throw new ArgumentNullException("owner");
		}

		public override void FollowOwner()
		{
			if (Body.IsAttacking)
				Disengage();

			Body.Follow(Owner, MIN_OWNER_FOLLOW_DIST, MAX_OWNER_FOLLOW_DIST);
		}
	}
}
#endregion

#region Titanbrain

namespace DOL.AI.Brain
{
    public class TitanBrain : ControlledMobBrain
	{
		private GameLiving m_target;

		public TitanBrain(GameLiving owner)
			: base(owner)
		{
		}

		public GameLiving Target
		{
			get { return m_target; }
			set { m_target = value; }
		}

		#region AI

		private IList FindTarget()
		{
			ArrayList list = new ArrayList();

			// Players and bots (companions, world bots) alike.
			foreach (GameLiving living in DOL.GS.Spells.MasterlevelHandling.PlayersAndBotsAround(Body, (ushort)Body.attackComponent.AttackRange))
			{
				if (living.IsAlive && GameServer.ServerRules.IsAllowedToAttack(Body, living, true))
					list.Add(living);
			}
			return list;
		}

		public override void Think()
		{
			if (Body.TargetObject is GameNPC)
				Body.TargetObject = null;

			if (Body.attackComponent.AttackState)
				return;

			IList enemies = new ArrayList();
			if (Target == null)
				enemies = FindTarget();
			else if (!Body.IsWithinRadius(Target, Body.attackComponent.AttackRange))
				enemies = FindTarget();
			else if (!Target.IsAlive)
				enemies = FindTarget();
			if (enemies.Count > 0 && Target == null)
			{
				//pick a random target...
				int targetnum = Util.Random(0, enemies.Count - 1);

				//Choose a random target.
				Target = enemies[targetnum] as GameLiving;
			}
			else if (enemies.Count < 1)
			{
				WalkState = eWalkState.Stay;
				enemies = FindTarget();
			}

			if (Target != null)
			{
				if (!Target.IsAlive)
				{
					Target = null;
				}
				else if (Body.IsWithinRadius(Target, Body.attackComponent.AttackRange))
				{
					Body.TargetObject = Target;
					Goto(Target);
					Body.StartAttack(Target);
				}
				else
				{
					Target = null;
				}
			}
		}
		#endregion
	}
}
#endregion

#region MLBrain
public class MLBrain : GuardBrain
{
	public MLBrain() : base() { }

	public override int AggroRange
	{
		get { return 400; }
	}
	protected override void CheckNpcAggro()
	{
		//Check if we are already attacking, return if yes
		if (Body.attackComponent.AttackState)
			return;

		foreach (GameNPC npc in Body.GetNPCsInRadius((ushort)AggroRange))
		{
			if (AggroList.ContainsKey(npc))
				continue; // add only new npcs
			if ((npc.Flags & GameNPC.eFlags.FLYING) != 0)
				continue; // let's not try to attack flying mobs
			if (!GameServer.ServerRules.IsAllowedToAttack(Body, npc, true))
				continue;
			if (!npc.IsWithinRadius(Body, AggroRange))
				continue;

			// Pets, guards and bots: a warder fights the enemy realm's companions and world bots too.
			if (!(npc.Brain is IControlledBrain || npc is GameGuard || npc is GameBot))
				continue;

			AddToAggroList(npc);
			return;
		}
	}
}
#endregion
