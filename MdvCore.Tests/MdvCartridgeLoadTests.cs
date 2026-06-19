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

        // File numbers follow directory order: strictly ascending and distinct. Gaps are allowed
        // — a cartridge that has had files deleted keeps the surviving slot numbers (e.g. MDV1.MDV
        // lists #1 and #4), so they are not necessarily a contiguous 1..N.
        var numbers = cart.Files.Select(f => (int)f.FileNumber).ToList();
        Assert.Equal(numbers.OrderBy(n => n).ToList(), numbers);
        Assert.Equal(numbers.Count, numbers.Distinct().Count());
    }

    [Fact]
    public void Abacus_has_a_medium_name_and_at_least_one_file()
    {
        var cart = MdvCartridge.Load(Path.Combine(FixturesDir(), "ABACUS.MDV"));
        Assert.False(string.IsNullOrWhiteSpace(cart.MediumName));
        Assert.NotEmpty(cart.Files);
    }

    [Theory]
    [MemberData(nameof(MdvFixtures))]
    public void ReadFileData_returns_the_declared_length_for_every_file(string fixture)
    {
        var cart = MdvCartridge.Load(Path.Combine(FixturesDir(), fixture));

        foreach (var file in cart.Files)
        {
            var data = cart.ReadFileData(file);
            Assert.Equal(file.DataLength, data.Length);
        }
    }

    [Theory]
    [MemberData(nameof(MdvFixtures))]
    public void Save_writes_the_image_back_byte_for_byte(string fixture)
    {
        var source = Path.Combine(FixturesDir(), fixture);
        var cart = MdvCartridge.Load(source);

        var temp = Path.Combine(Path.GetTempPath(), $"mdvtest_{Guid.NewGuid():N}.mdv");
        try
        {
            cart.Save(temp);
            Assert.Equal(File.ReadAllBytes(source), File.ReadAllBytes(temp));
        }
        finally
        {
            if (File.Exists(temp))
                File.Delete(temp);
        }
    }
}
