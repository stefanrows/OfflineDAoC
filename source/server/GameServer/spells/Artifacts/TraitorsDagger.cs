/*
 * DAWN OF LIGHT - The first free open source DAoC server emulator
 * 
 * This program is free software; you can redistribute it and/or
 * modify it under the terms of the GNU General Public License
 * as published by the Free Software Foundation; either version 2
 * of the License, or (at your option) any later version.
 * 
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
 * GNU General Public License for more details.
 * 
 * You should have received a copy of the GNU General Public License
 * along with this program; if not, write to the Free Software
 * Foundation, Inc., 59 Temple Place - Suite 330, Boston, MA  02111-1307, USA.
 *
 */

using System;
using DOL.AI.Brain;
using DOL.Database;
using DOL.Events;

namespace DOL.GS.Spells
{
	/// <summary>
	/// Traitor's Dagger shade form with an offensive proc: the wearer turns into a shade
	/// (GamePlayer.Shade) instead of a model change.
	/// </summary>
	[SpellHandler(eSpellType.TraitorsDaggerProc)]
	public class TraitorsDaggerProc : ArtifactMorphProcSpellHandler
	{
		public TraitorsDaggerProc(GameLiving caster, Spell spell, SpellLine line) : base(caster, spell, line) { }

		public override void OnArtifactEffectStart(ECSGameSpellEffect effect)
		{
			if (effect.Owner is GamePlayer player)
			{
				player.Shade(true);
				player.Out.SendUpdatePlayer();
			}
		}

		public override void OnArtifactEffectStop(ECSGameSpellEffect effect)
		{
			if (effect.Owner is GamePlayer player)
			{
				player.Shade(false);
				player.Out.SendUpdatePlayer();
			}
		}
	}

    [SpellHandler(eSpellType.DdtProcDd)]
    public class DdtProcDd:DirectDamageSpellHandler
    {
        public DdtProcDd(GameLiving caster,Spell spell,SpellLine line) : base(caster,spell,line) { }

        public override void OnDirectEffect(GameLiving target)
        {
            base.OnDirectEffect(target);
            Caster.ChangeHealth(Caster,eHealthChangeType.Spell,-Spell.ResurrectHealth);
        }
    }

    [SpellHandler(eSpellType.TraitorsDaggerSummon)]
    public class TraitorsDaggerSummon : SummonSpellHandler
    {
        private ISpellHandler _trap;

        public override void ApplyEffectOnTarget(GameLiving target)
        {
            //Set pet infos & Brain
            base.ApplyEffectOnTarget(target);
            ProcPetBrain petBrain = (ProcPetBrain) m_pet.Brain;
            petBrain.AddToAggroList(target);
        }

        protected override GameSummonedPet GetGamePet(INpcTemplate template) { return new TraitorDaggerPet(template); }
        protected override IControlledBrain GetPetBrain(GameLiving owner) { return new ProcPetBrain(owner); }
        protected override void SetBrainToOwner(IControlledBrain brain) { }
        // GameLivingEvent.AttackFinished is no longer raised for pets in our tree, so the
        // pet reports its own landed attacks (TraitorDaggerPet.OnAttackEnemy).
        protected override void AddHandlers()
        {
            if (m_pet is TraitorDaggerPet pet)
                pet.AttackLanded = OnPetAttack;
        }

        protected void OnPetAttack(AttackData ad)
        {
            if (ad?.Target == null || !ad.Target.IsAlive)
                return;

            if (ad.AttackResult is not eAttackResult.HitUnstyled and not eAttackResult.HitStyle)
                return;

            // Spirit procs lifetap when hitting ennemy
            _trap ??= MakeTrap();

            if (Util.Chance(50))
                _trap?.StartSpell(ad.Target);
        }

        private ISpellHandler MakeTrap()
        {
            DbSpell dbs = new DbSpell();
            dbs.Name = "Increased Essence Consumption";
            dbs.Icon = 11020;
            dbs.ClientEffect = 11020;
            dbs.DamageType = 10;
            dbs.Target = eSpellTarget.ENEMY.ToString();
            dbs.Radius = 0;
            dbs.Type = eSpellType.PetLifedrain.ToString();
            dbs.Damage = 70;
            dbs.LifeDrainReturn = 100;
            dbs.Value = -100;
            dbs.Duration = 0;
            dbs.Frequency = 0;
            dbs.Pulse = 0;
            dbs.PulsePower = 0;
            dbs.Power = 0;
            dbs.CastTime = 0;
            dbs.Range = 350;
            Spell s = new Spell(dbs, 50);
            SpellLine sl = SkillBase.GetSpellLine(GlobalSpellsLines.Reserved_Spells);
            return ScriptMgr.CreateSpellHandler(m_pet, s, sl);
        }

        public TraitorsDaggerSummon(GameLiving caster, Spell spell, SpellLine line)
            : base(caster, spell, line) { }
    }
}

namespace DOL.GS
{
    public class TraitorDaggerPet : GameSummonedPet
	{
		public override int MaxHealth
		{
			get { return Level * 15; }
		}
		public Action<AttackData> AttackLanded { get; set; }

		public override void OnAttackedByEnemy(AttackData ad) { }

		public override void OnAttackEnemy(AttackData ad)
		{
			base.OnAttackEnemy(ad);

			if (ad?.Attacker == this)
				AttackLanded?.Invoke(ad);
		}

		public TraitorDaggerPet(INpcTemplate npcTemplate) : base(npcTemplate) { }
	}
}
