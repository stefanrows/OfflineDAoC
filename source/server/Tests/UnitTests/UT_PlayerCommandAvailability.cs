using System;
using System.Linq;
using DOL.GS;
using DOL.GS.Commands;
using NUnit.Framework;

namespace DOL.UnitTests
{
    [TestFixture]
    public class UT_PlayerCommandAvailability
    {
        private static string[] CommandsOn(Type handlerType) => handlerType
            .GetCustomAttributes(typeof(CmdAttribute), false)
            .Cast<CmdAttribute>()
            .SelectMany(attribute => new[] { attribute.Cmd }
                .Concat(attribute.Aliases ?? Array.Empty<string>()))
            .ToArray();

        [Test]
        public void RemovedAutomationCommandsAreNotRegistered()
        {
            Assert.That(CommandsOn(typeof(BotCommand)), Is.Empty);
            Assert.That(CommandsOn(typeof(TravelCommandHandler)), Is.Empty);
            Assert.That(AppDomain.CurrentDomain.GetAssemblies()
                .SelectMany(assembly =>
                {
                    try { return assembly.GetTypes(); }
                    catch { return Array.Empty<Type>(); }
                })
                .SelectMany(CommandsOn), Does.Not.Contain("&go"));
        }

        [Test]
        public void NoCommandHandlerRegistersADuplicateCommandKey()
        {
            var duplicates = AppDomain.CurrentDomain.GetAssemblies()
                .SelectMany(assembly =>
                {
                    try { return assembly.GetTypes(); }
                    catch { return Array.Empty<Type>(); }
                })
                .Where(type => type.IsClass && typeof(ICommandHandler).IsAssignableFrom(type))
                .SelectMany(type => CommandsOn(type).Select(key => new { type, key }))
                .GroupBy(entry => entry.key)
                .Where(group => group.Select(entry => entry.type).Distinct().Count() > 1)
                .Select(group => $"{group.Key}: {string.Join(", ", group.Select(entry => entry.type.Name).Distinct())}")
                .ToArray();

            Assert.That(duplicates, Is.Empty,
                "Command keys registered by more than one handler (LoadCommands silently drops the losing alias): "
                + string.Join("; ", duplicates));
        }

        [TestCase(typeof(TeleportToExchangeCommandHandler), "&tc")]
        [TestCase(typeof(MobsCommandHandler), "&mobs")]
        [TestCase(typeof(TeleportToBotCommandHandler), "&tele")]
        [TestCase(typeof(SpawnTemporaryGroupBotCommandHandler), "&spawn")]
        public void RetainedPlayerCommandsRemainRegistered(Type handlerType, string command)
        {
            Assert.That(CommandsOn(handlerType), Does.Contain(command));
        }

        [Test]
        public void LauncherTeleportUsesAnAccountScopedOwnerCommandAndSharedTeleportService()
        {
            var command = new OfflineBotCommandRecord
            {
                BotId = 42,
                CommandType = AutonomousPopulationController.TeleportToBotCommandType,
                RequestedByAccount = "offline",
            };

            Assert.Multiple(() =>
            {
                Assert.That(command.CommandType, Is.EqualTo("TeleportToBot"));
                Assert.That(command.RequestedByAccount, Is.EqualTo("offline"));
                Assert.That(PlayerBotTeleport.TryTeleport(null, null, out string message), Is.False);
                Assert.That(message, Does.Contain("No logged-in player"));
            });
        }
    }
}
