using System.IO;
using ProjectAbyss.Core.Net;
using ProjectAbyss.Core.Profile;
using Xunit;

namespace ProjectAbyss.Core.Tests
{
    public class ProfileTests
    {
        [Fact]
        public void UnlocksPersistImmediatelyAndOnlyOnce()
        {
            using var dir = new TempDir();
            var store = new ProfileStore(dir.Path);
            store.LoadOrCreate(TestData.Data, "Tester");
            Assert.Contains("warrior", store.Profile.UnlockedClasses);
            Assert.True(store.Unlock("diff:hard:run1", UnlockKind.Difficulty, "hard"));
            Assert.False(store.Unlock("diff:hard:run1", UnlockKind.Difficulty, "hard"));
            var reloaded = new ProfileStore(dir.Path);
            reloaded.LoadOrCreate(TestData.Data, "x");
            Assert.Contains("hard", reloaded.Profile.UnlockedDifficulties);
            Assert.Equal("Tester", reloaded.Profile.DisplayName);
        }

        [Fact]
        public void DeveloperProfilesNeverUnlock()
        {
            using var dir = new TempDir();
            var store = new ProfileStore(dir.Path);
            store.LoadOrCreate(TestData.Data, "Dev").IsDeveloperProfile = true;
            Assert.False(store.Unlock("ach:1", UnlockKind.Achievement, "first_blood"));
            Assert.Empty(store.Profile.Achievements);
        }

        [Fact]
        public void ExportAndDeleteCoverAllLocalData()
        {
            using var dir = new TempDir();
            var store = new ProfileStore(dir.Path);
            store.LoadOrCreate(TestData.Data, "Tester");
            using (ParticipantIdentity.LoadOrCreate(Path.Combine(dir.Path, "identity.key")))
            {
            }
            Assert.Contains("Tester", store.ExportJson());
            store.DeleteAll();
            Assert.Empty(Directory.GetFiles(dir.Path));
        }

        [Fact]
        public void UnreadableProfileIsSetAsideNotOverwritten()
        {
            using var dir = new TempDir();
            File.WriteAllText(Path.Combine(dir.Path, "profile.json"), "garbage");
            var store = new ProfileStore(dir.Path);
            store.LoadOrCreate(TestData.Data, "New");
            Assert.Single(Directory.GetFiles(dir.Path, "profile.json.unreadable-*"));
        }

        [Fact]
        public void IdentitySignaturesVerifyAndIdsAreBoundToKeys()
        {
            using var dir = new TempDir();
            string keyPath = Path.Combine(dir.Path, "identity.key");
            using var a = ParticipantIdentity.LoadOrCreate(keyPath);
            using var a2 = ParticipantIdentity.LoadOrCreate(keyPath);
            using var b = ParticipantIdentity.Create();
            Assert.Equal(a.ParticipantId, a2.ParticipantId);
            Assert.NotEqual(a.ParticipantId, b.ParticipantId);
            byte[] nonce = { 1, 2, 3, 4, 5 };
            byte[] sig = a.Sign(nonce);
            Assert.True(ParticipantIdentity.Verify(a.PublicKey, nonce, sig));
            Assert.False(ParticipantIdentity.Verify(b.PublicKey, nonce, sig));
            Assert.False(ParticipantIdentity.Verify(a.PublicKey, new byte[] { 9 }, sig));
            Assert.Equal(a.ParticipantId, ParticipantIdentity.IdFromPublicKey(a.PublicKey));
        }
    }
}
