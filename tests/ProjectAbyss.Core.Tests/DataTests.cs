using System.Collections.Generic;
using System.IO;
using System.Linq;
using ProjectAbyss.Core.Data;
using Xunit;

namespace ProjectAbyss.Core.Tests
{
    public class DataTests
    {
        [Fact]
        public void ShippedDataLoadsAndValidates()
        {
            var data = TestData.Data;
            Assert.Contains("warrior", data.Classes.Keys);
            Assert.Equal(3, data.Classes["warrior"].WeaponTypes.Count);
            Assert.Equal(64, data.ContentHash.Length);
            Assert.True(data.Bosses["forest_warden"].Phases.Count >= 3);
        }

        [Fact]
        public void ValidatorRejectsBrokenReferences()
        {
            var dir = Path.Combine(System.AppContext.BaseDirectory, "data");
            var files = Directory.GetFiles(dir, "*.json").ToDictionary(f => Path.GetFileName(f), File.ReadAllText);
            files["classes.json"] = files["classes.json"].Replace("\"warrior_mace\"", "\"no_such_weapon\"");
            var ex = Assert.Throws<GameDataException>(() => GameData.LoadFromJson(files));
            Assert.Contains("no_such_weapon", ex.Message);
        }

        [Fact]
        public void ContentHashChangesWithContent()
        {
            var dir = Path.Combine(System.AppContext.BaseDirectory, "data");
            var files = Directory.GetFiles(dir, "*.json").ToDictionary(f => Path.GetFileName(f), File.ReadAllText);
            var a = GameData.LoadFromJson(files).ContentHash;
            files["balance.json"] = files["balance.json"].Replace("\"charges\": 2", "\"charges\": 3");
            var b = GameData.LoadFromJson(files).ContentHash;
            Assert.NotEqual(a, b);
        }

        [Fact]
        public void FullUpgradeCostMatchesDesignBaseline()
        {
            Assert.Equal(5230, TestData.Data.Balance.Economy.UpgradeGoldCost.Sum());
            Assert.Equal(10, TestData.Data.Balance.Economy.UpgradeGoldCost.Count);
        }
    }
}
