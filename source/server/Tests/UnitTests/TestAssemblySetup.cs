using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using DOL.GS;
using DOL.GS.ServerProperties;
using DOL.Language;
using DOL.Logging;
using DOL.UnitTests;
using NUnit.Framework;

/// <summary>
/// Runs once for the whole test assembly (no namespace on purpose). GameObject
/// and SkillBase have static constructors that need a database, language
/// strings and logging; a test touching them first without those used to
/// poison the types for every later test (bug 38: 18 vs 84 failures depending
/// on order). Initialize them once, correctly, before any test runs.
/// </summary>
[SetUpFixture]
public sealed class TestAssemblySetup
{
    [OneTimeSetUp]
    public void InitializeSharedStatics()
    {
        LoggerManager.InitializeWithExplicitLibrary(null, LogLibrary.None);
        Properties.SERV_LANGUAGE ??= "EN";
        Properties.DB_LANGUAGE ??= "EN";
        PropertyInfo translations = typeof(LanguageMgr).GetProperty(nameof(LanguageMgr.Translations));
        if (translations?.GetValue(null) == null)
            translations?.SetValue(null, Activator.CreateInstance(translations.PropertyType));
        using (new EpicTestServerScope())
        {
            RuntimeHelpers.RunClassConstructor(typeof(GameObject).TypeHandle);
            RuntimeHelpers.RunClassConstructor(typeof(SkillBase).TypeHandle);
        }
    }
}
