using DOL.GS.Keeps;
using DOL.GS.PacketHandler;
using DOL.Language;

namespace DOL.GS.Commands
{
    [CmdAttribute("&assist", ePrivLevel.Player, "Assist your target", "/assist [playerName]")]
    public class AssistCommandHandler : AbstractCommandHandler, ICommandHandler
    {
        public void OnCommand(GameClient client, string[] args)
        {
            // if (IsSpammingCommand(client.Player, "assist"))
            //     return;

            if (args.Length > 1)
            {
                if (args[1].Equals(client.Player.Name, System.StringComparison.OrdinalIgnoreCase))
                {
                    // We cannot assist our target when it has no target.
                    if (!HasTarget(client, client.Player))
                        return;

                    YouAssist(client, client.Player.Name, client.Player.TargetObject);
                    return;
                }

                GamePlayer assistPlayer = null;

                foreach (GamePlayer plr in client.Player.GetPlayersInRadius(2048))
                {
                    if (!plr.Name.Equals(args[1], System.StringComparison.CurrentCultureIgnoreCase))
                        continue;

                    assistPlayer = plr;
                    break;
                }

                if (assistPlayer == null && GameServer.Instance.Configuration.ServerType is EGameServerType.GST_PvP)
                {
                    foreach (GameNPC npc in client.Player.GetNPCsInRadius(2048))
                    {
                        if (!npc.Name.Equals(args[1], System.StringComparison.CurrentCultureIgnoreCase))
                            continue;

                        if (!GameServer.ServerRules.IsSameRealm(client.Player, npc, true))
                        {
                            NoValidTarget(client, npc);
                            return;
                        }

                        if (!HasTarget(client, npc))
                            return;

                        YouAssist(client, npc.Name, npc.TargetObject);
                        return;
                    }
                }

                if (assistPlayer != null)
                {
                    // Each server type handles the assist command on it's own way.
                    switch(GameServer.Instance.Configuration.ServerType)
                    {
                        case EGameServerType.GST_Normal:
                        {
                            // We cannot assist players of an enemy realm.
                            if (!SameRealm(client, assistPlayer, false))
                                return;

                            // We cannot assist our target when it has no target.
                            if (!HasTarget(client, assistPlayer))
                                return;

                            YouAssist(client, assistPlayer.Name, assistPlayer.TargetObject);
                            return;
                        }
                        case EGameServerType.GST_PvE:
                        {
                            // We cannot assist our target when it has no target.
                            if (!HasTarget(client, assistPlayer))
                                return;

                            YouAssist(client, assistPlayer.Name, assistPlayer.TargetObject);
                            return;
                        }
                        case EGameServerType.GST_PvP:
                        {
                            HandlePvpAssist(client, assistPlayer);
                            return;
                        }
                    }
                }

                client.Out.SendMessage(LanguageMgr.GetTranslation(client.Account.Language, "Scripts.Players.Assist.MemberNotFound"), eChatType.CT_System, eChatLoc.CL_SystemWindow);
                return;
            }

            if (client.Player.TargetObject != null)
            {
                if (client.Player.TargetObject == client.Player)
                {
                    YouAssist(client, client.Player.Name, client.Player.TargetObject);
                    return;
                }

                if (client.Player.TargetObject is GameNPC or GamePlayer)
                {
                    if (client.Player.TargetObject is GameMovingObject)
                    {
                        NoValidTarget(client, client.Player.TargetObject as GameLiving);
                        return;
                    }

                    // Each server type handles the assist command on it's own way.
                    switch(GameServer.Instance.Configuration.ServerType)
                    {
                        case EGameServerType.GST_Normal:
                        {
                            GameLiving targetLiving = (GameLiving) client.Player.TargetObject;

                            //We cannot assist npc's or players of an enemy realm.
                            if (!SameRealm(client, targetLiving, false))
                                return;

                            //We cannot assist our target when it has no target.
                            if (!HasTarget(client, targetLiving))
                                return;

                            YouAssist(client, client.Player.TargetObject.GetName(0, true), targetLiving.TargetObject);
                            return;
                        }
                        case EGameServerType.GST_PvE:
                        {
                            if (client.Player.TargetObject is GamePlayer)
                            {
                                // We cannot assist our target when it has no target.
                                if (!HasTarget(client, client.Player.TargetObject as GameLiving))
                                    return;

                                YouAssist(client, client.Player.TargetObject.Name, (client.Player.TargetObject as GameLiving).TargetObject);
                                return;
                            }
                            else if (client.Player.TargetObject is GameNPC)
                            {
                                if (!SameRealm(client, client.Player.TargetObject as GameNPC, true))
                                    return;
                                else
                                {
                                    // We cannot assist our target when it has no target.
                                    if (!HasTarget(client, client.Player.TargetObject as GameNPC))
                                        return;

                                    YouAssist(client, client.Player.TargetObject.GetName(0, true), (client.Player.TargetObject as GameLiving).TargetObject);
                                    return;
                                }
                            }

                            break;
                        }
                        case EGameServerType.GST_PvP:
                        {
                            GameLiving targetLiving = client.Player.TargetObject as GameLiving;
                            HandlePvpAssist(client, targetLiving);
                            return;

                        }
                    }
                }
            }

            client.Out.SendMessage(LanguageMgr.GetTranslation(client.Account.Language, "Scripts.Players.Assist.SelectMember"), eChatType.CT_System, eChatLoc.CL_SystemWindow);
            return;
        }

        private static bool HasTarget(GameClient client, GameLiving livingToCheck)
        {
            if (livingToCheck.TargetObject != null)
                return true;

            // We cannot assist our target when it has no target.
            client.Out.SendMessage(LanguageMgr.GetTranslation(client.Account.Language, "Scripts.Players.Assist.DoesntHaveTarget", livingToCheck.GetName(0, true)), eChatType.CT_System, eChatLoc.CL_SystemWindow);
            return false;
        }

        private static void HandlePvpAssist(GameClient client, GameLiving target)
        {
            // Camlann assistance follows the same group, guild, battlegroup,
            // and companion identity used by the server rules. Realm and chat
            // alliance are not substitutes for being allied.
            if (!GameServer.ServerRules.IsSameRealm(client.Player, target, true))
            {
                NoValidTarget(client, target);
                return;
            }

            if (!HasTarget(client, target))
                return;

            YouAssist(client, target.GetName(0, false), target.TargetObject);
        }

        private static void NoValidTarget(GameClient client, GameLiving livingToAssist)
        {
            // Original live text: {0} is not a member of your realm!
            // The original text sounds stupid if we use it for rams or other things: The battle ram is not a member of your realm!
            // But the text is also used for rams that are a member of our realm, so we don't use it.
            client.Out.SendMessage(LanguageMgr.GetTranslation(client.Account.Language, "Scripts.Players.Assist.NotValid", livingToAssist.GetName(0, true)), eChatType.CT_System, eChatLoc.CL_SystemWindow);
            return;
        }

        private static bool SameRealm(GameClient client, GameLiving livingToCheck, bool usePvEPvPRule)
        {
            if (usePvEPvPRule)
            {
                if (livingToCheck.Realm != 0)
                    return true;
            }
            else
            {
                if (livingToCheck.Realm == client.Player.Realm)
                    return true;
            }

            // We cannot assist livings of an enemy realm.
            client.Out.SendMessage(LanguageMgr.GetTranslation(client.Account.Language, "Scripts.Players.Assist.NoRealmMember", livingToCheck.GetName(0, true)), eChatType.CT_System, eChatLoc.CL_SystemWindow);
            return false;
        }

        private static void YouAssist(GameClient client, string targetName, GameObject assistTarget)
        {
            client.Out.SendMessage(LanguageMgr.GetTranslation(client.Account.Language, "Scripts.Players.Assist.YouAssist", targetName), eChatType.CT_System, eChatLoc.CL_SystemWindow);
            client.Out.SendChangeTarget(assistTarget);
            return;
        }
    }
}
