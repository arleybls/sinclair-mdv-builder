using MdvCore.Mdv;

namespace MdvCore.Tests;

public class MdvCartridgeLoadTests
{
    private static string FixturesDir()
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null && !Directory.Exists(Path.Combine(dir, "fixtures")))
            dir = Directory.GetParent(dir)?.FullName;
        if (dir == null)
            throw new DirectoryNotFoundException("Could not locate the fixtures/ directory.");
        return Path.Combine(dir, "fixtures");
    }

    public static IEnumerable<object[]> MdvFixtures()
    {
        foreach (var path in Directory.EnumerateFiles(FixturesDir())
                     .Where(p => p.EndsWith(".mdv", StringComparison.OrdinalIgnoreCase)))
            yield return new object[] { Path.GetFileName(path) };
    }

    [Theory]
    [MemberData(nameof(MdvFixtures))]
    public void Loads_and_yields_consistent_structure(string fixture)
    {
        var cart = MdvCartridge.Load(Path.Combine(FixturesDir(), fixture));

        // The allocation map always describes exactly 255 sectors.
        Assert.Equal(MdvCartridge.SectorCount, cart.Sectors.Count);
        Assert.Equal(MdvCartridge.SectorCount,
            cart.UsedSectorCount + cart.FreeSectorCount + cart.DamagedSectorCount);

        // Every listed file is plausible.
        foreach (var file in cart.Files)
        {
            Assert.False(string.IsNullOrWhiteSpace(file.Name));
            Assert.True(file.DataLength >= 0);
            Assert.True(file.BlockCount >= 1);
            Assert.InRange(file.FileNumber, (byte)1, (byte)0xF7);
        }

        // File numbers are the directory order 1..N.
        Assert.Equal(
            Enumerable.Range(1, cart.Files.Count).Select(i => (byte)i),
            cart.Files.Select(f => f.FileNumber));
    }

    [Fact]
    public void Abacus_has_a_medium_name_and_at_least_one_file()
    {
        var cart = MdvCartridge.Load(Path.Combine(FixturesDir(), "ABACUS.MDV"));
        Assert.False(string.IsNullOrWhiteSpace(cart.MediumName));
        Assert.NotEmpty(cart.Files);
    }
}
