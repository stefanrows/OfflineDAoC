using NUnit.Framework;

namespace OfflineDaoc.Launcher;

[TestFixture]
public sealed class JoinFriendProfileTests
{
    private string _path = string.Empty;

    [SetUp]
    public void SetUp() =>
        _path = Path.Combine(Path.GetTempPath(), "offline-daoc-join-" + Guid.NewGuid().ToString("N"), "join-friend.json");

    [TearDown]
    public void TearDown()
    {
        string? directory = Path.GetDirectoryName(_path);
        if (directory != null && Directory.Exists(directory))
            Directory.Delete(directory, true);
    }

    [Test]
    public void RememberedPasswordRoundTripsWithoutPlaintextOnDisk()
    {
        JoinFriendProfile.Save(_path, "100.120.169.75", "offline", "Aaron", "S3cret!pw", rememberPassword: true);

        JoinFriendProfile loaded = JoinFriendProfile.Load(_path);
        Assert.That(loaded.HostAddress, Is.EqualTo("100.120.169.75"));
        Assert.That(loaded.HostAccount, Is.EqualTo("offline"));
        Assert.That(loaded.GuestAccount, Is.EqualTo("Aaron"));
        Assert.That(loaded.Password, Is.EqualTo("S3cret!pw"));
        Assert.That(File.ReadAllText(_path), Does.Not.Contain("S3cret!pw"));
    }

    [Test]
    public void DetailsAreKeptButPasswordIsForgottenWhenNotRemembered()
    {
        JoinFriendProfile.Save(_path, "100.120.169.75", "offline", "Aaron", "S3cret!pw", rememberPassword: true);
        JoinFriendProfile.Save(_path, "100.120.169.75", "offline", "Aaron", "S3cret!pw", rememberPassword: false);

        JoinFriendProfile loaded = JoinFriendProfile.Load(_path);
        Assert.That(loaded.GuestAccount, Is.EqualTo("Aaron"));
        Assert.That(loaded.Password, Is.Empty);
        Assert.That(loaded.RememberPassword, Is.False);
    }

    [Test]
    public void MissingOrDamagedFileLoadsEmptyProfile()
    {
        Assert.That(JoinFriendProfile.Load(_path).HostAddress, Is.Empty);

        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        File.WriteAllText(_path, "{ not json");
        Assert.That(JoinFriendProfile.Load(_path).GuestAccount, Is.Empty);

        File.WriteAllText(_path, "{\"HostAddress\":\"100.64.0.1\",\"ProtectedPassword\":\"not-base64!\"}");
        JoinFriendProfile loaded = JoinFriendProfile.Load(_path);
        Assert.That(loaded.HostAddress, Is.EqualTo("100.64.0.1"));
        Assert.That(loaded.Password, Is.Empty);
    }
}
