using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using DOL.GS.PacketHandler;
using DOL.Language;

namespace DOL.GS
{
    /// <summary>
    /// The Arbiter who puts adventurers on the Master Level path (TOA_PLAN WP2.2, adapted from
    /// gaheris GaherisArbiter). Speaking to him enrols the speaker and every group member within
    /// 2000 units (level 40 and higher), names the eight disciplines while none is chosen, takes
    /// a whispered discipline name, and reports Master Level, experience and discipline. The
    /// plain <see cref="Arbiter"/> keeps its lore-only behaviour for rows that still name it.
    /// Everything goes out as one popup: the client shows only one popup per tick.
    /// </summary>
    public class MasterLevelArbiter : Arbiter
    {
        protected override bool SpeaksWelcome(GamePlayer player) => false;

        public override bool Interact(GamePlayer player)
        {
            bool newcomer = !player.MLGranted;

            if (!base.Interact(player))
                return false;

            var text = new StringBuilder();

            if (newcomer)
                text.Append(Welcome(player)).Append("\n\n");

            text.Append(Conversation(player));
            SayTo(player, eChatLoc.CL_PopupWindow, text.ToString());
            return true;
        }

        /// <summary>Enrolment, standing and the discipline list; everything but the lore greeting.</summary>
        public string Conversation(GamePlayer player)
        {
            var text = new StringBuilder();
            List<GamePlayer> enrolled = MasterLevelProgression.EnrolCompany(player, this);
            List<GamePlayer> mates = enrolled.Where(member => member != player).ToList();

            if (!player.MLGranted && player.Level < MasterLevelProgression.ENROL_MIN_LEVEL)
                text.Append("The trials are not for you yet. Come back when you have seen your ")
                    .Append(MasterLevelProgression.ENROL_MIN_LEVEL).Append("th season.\n\n");

            if (mates.Count > 0)
                text.Append(enrolled.Contains(player) ? "You and your company begin the trials: " : "Your company begins the trials: ")
                    .Append(string.Join(", ", mates.Select(mate => mate.Name))).Append(".\n\n");
            else if (enrolled.Contains(player))
                text.Append("You have begun the trials.\n\n");

            // A full bar (older saves, GM experience) is settled here as well as in the field.
            MasterLevelProgression.AddExperience(player, 0);

            text.Append(MasterLevelProgression.Describe(player));
            text.Append(PathList(player));
            return text.ToString();
        }

        /// <summary>The disciplines, offered only while the player is enrolled and has none.</summary>
        public static string PathList(GamePlayer player)
        {
            if (!player.MLGranted || MasterLevelProgression.PathOf(player) != null)
                return string.Empty;

            HashSet<string> open = MasterLevelProgression.CareerPaths(player.CharacterClass.ID);

            if (open.Count == 0)
                return "\n\nNo discipline is open to your calling yet.";

            var text = new StringBuilder("\n\nName a discipline and its arts open to you as you rise:");

            foreach ((string key, string what) in MasterLevelProgression.Paths)
            {
                if (open.Contains(key))
                    text.Append("\n  [").Append(key).Append("]: ").Append(what);
            }

            return text.ToString();
        }

        public override bool WhisperReceive(GameLiving source, string text)
        {
            if (source is GamePlayer player && MasterLevelProgression.PathIndex(text) >= 0 &&
                player.IsWithinRadius(this, WorldMgr.INTERACT_DISTANCE))
            {
                TurnTo(player, 5000);
                SayTo(player, eChatLoc.CL_PopupWindow, Answer(player, text));
                return true;
            }

            return base.WhisperReceive(source, text);
        }

        /// <summary>What the Arbiter says to a whispered discipline name.</summary>
        public static string Answer(GamePlayer player, string said)
        {
            MasterLevelChoice result = MasterLevelProgression.Choose(player, said);
            string wanted = MasterLevelProgression.Paths[Math.Max(0, MasterLevelProgression.PathIndex(said))].Key;

            return result switch
            {
                MasterLevelChoice.NotEnrolled => player.Level < MasterLevelProgression.ENROL_MIN_LEVEL
                    ? "Begin the trials first, and they are not for you before your " + MasterLevelProgression.ENROL_MIN_LEVEL + "th season."
                    : "Begin the trials first: speak to me and I will put you on the path.",
                MasterLevelChoice.NotInCareer => "That discipline is closed to your calling.",
                MasterLevelChoice.AlreadyWalked => "You already walk the path of the " + wanted + ".",
                MasterLevelChoice.OtherPathTaken => "You walk the path of the " + MasterLevelProgression.PathOf(player) +
                    ". Only a Star of Destiny from Demyphon frees that choice.",
                MasterLevelChoice.Chosen => "So be it. You are of the " + wanted + " now.\n\n" + MasterLevelProgression.Describe(player),
                _ => string.Empty,
            };
        }

        private string Welcome(GamePlayer player)
        {
            string language = player.Client?.Account?.Language;
            string realm = GlobalConstants.RealmToName((eRealm)Realm);
            return LanguageMgr.GetTranslation(language, $"{realm}.Arbiter.Interact.Welcome", player.CharacterClass.Name);
        }
    }
}

namespace DOL.GS.Scripts
{
    /// <summary>
    /// The class name gaheris sql/35 writes into the three Arbiter mob rows. Kept so those rows
    /// resolve without rewriting the imported SQL; the behaviour is <see cref="MasterLevelArbiter"/>.
    /// </summary>
    public class GaherisArbiter : MasterLevelArbiter
    {
    }
}
