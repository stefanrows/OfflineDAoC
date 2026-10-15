using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;
using System.Linq;
using NUnit.Framework;

namespace DOL.GS.Tests;

// Task 11 probe, read-only on the installed data: every realm frontier region has an open BG porter with a
// medallion merchant in reach, and a group member boarding there lands at its LEADER's landing.
[TestFixture, NonParallelizable]
public class UT_OpenBattlegroundOwnPorter
{
    private static SQLiteConnection OpenInstalled()
    {
        string root = Environment.GetEnvironmentVariable("OFFLINE_DAOC_ROOT");
        string path = root == null ? null : Path.Combine(root, "runtime", "data", "opendaoc.sqlite3.db");
        if (path == null || !File.Exists(path)) Assert.Ignore("installed data not available");
        var db = new SQLiteConnection($"Data Source={path};Read Only=True;Pooling=False;");
        db.Open();
        return db;
    }

    [Test]
    public void EveryFrontierRegionHasAPorterWithAMedallionMerchantInReach()
    {
        using SQLiteConnection db = OpenInstalled();
        var merchants = new List<(int Region, int X, int Y)>();
        var porters = new List<(int Region, int X, int Y)>();
        using (var q = db.CreateCommand())
        {
            q.CommandText = "select Region,X,Y from Mob where ItemsListTemplateID in " +
                $"(select ItemListID from MerchantItem where ItemTemplateID='{AutonomousBattlegroundParticipation.BattlegroundMedallionId}')";
            using var r = q.ExecuteReader();
            while (r.Read()) merchants.Add((r.GetInt32(0), r.GetInt32(1), r.GetInt32(2)));
        }
        using (var q = db.CreateCommand())
        {
            q.CommandText = "select Region,X,Y from Mob where ClassType='DOL.GS.Scripts.OFTeleporter' and Region in (1,100,200)";
            using var r = q.ExecuteReader();
            while (r.Read()) porters.Add((r.GetInt32(0), r.GetInt32(1), r.GetInt32(2)));
        }
        foreach (int region in new[] { 1, 100, 200 })
            Assert.That(porters.Any(p => p.Region == region && merchants.Any(m => m.Region == region &&
                    Math.Sqrt(Math.Pow(m.X - p.X, 2) + Math.Pow(m.Y - p.Y, 2)) <= AutonomousBattlegroundParticipation.MerchantReach)),
                Is.True, $"region {region} has a source porter");
    }

    [Test]
    public void AMemberLandsAtItsLeadersLandingNotItsOwnRealmLanding()
    {
        using SQLiteConnection db = OpenInstalled();
        var landingRealms = new List<eRealm>();
        using (var q = db.CreateCommand())
        {
            // GetLandingRealms: portal keeps (original realm 1..3, no current owner) of region 252 in keep id order.
            q.CommandText = "select Realm,OriginalRealm from Keep where Region=252 and KeepType=0 and Name like '%Portal Keep' order by KeepID";
            using var r = q.ExecuteReader();
            while (r.Read())
            {
                int realm = r.GetInt32(0);
                landingRealms.Add((eRealm)(realm is >= 1 and <= 3 ? realm : r.GetInt32(1)));
            }
        }
        Assert.That(landingRealms, Is.EquivalentTo(new[] { eRealm.Albion, eRealm.Midgard, eRealm.Hibernia }), "one landing per realm");
        foreach (eRealm leader in landingRealms)
            foreach (eRealm member in landingRealms.Where(realm => realm != leader))
            {
                // GetLanding passes the leader's realm; the member's own realm is never consulted.
                int landing = BattlegroundCampaignPolicy.ChooseLanding(leader, null, landingRealms, 0);
                int ownRealm = BattlegroundCampaignPolicy.ChooseLanding(member, null, landingRealms, 0);
                Assert.That(landingRealms[landing], Is.EqualTo(leader), $"{member} member of a {leader} leader");
                Assert.That(landing, Is.Not.EqualTo(ownRealm));
            }
    }
}
