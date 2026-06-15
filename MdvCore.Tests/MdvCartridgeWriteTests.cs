using MdvCore.Mdv;

namespace MdvCore.Tests;

public class MdvCartridgeWriteTests
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

    private static MdvCartridge Abacus() => MdvCartridge.Load(Path.Combine(FixturesDir(), "ABACUS.MDV"));

    [Fact]
    public void ImportFile_adds_a_readable_file_and_preserves_the_others()
    {
        var cart = Abacus();
        var originals = cart.Files.ToDictionary(f => f.Name, f => cart.ReadFileData(f));

        var content = Enumerable.Range(0, 1500).Select(i => (byte)(i % 251)).ToArray();
        var updated = cart.ImportFile("HELLO_dat", content);

        // Produces a valid native image and re-loads cleanly.
        Assert.Equal(MdvCartridge.ImageSize, updated.ToBytes().Length);

        // The new file is present with exactly the imported bytes.
        var added = updated.FindFile("HELLO_dat");
        Assert.NotNull(added);
        Assert.Equal(content, updated.ReadFileData(added!));

        // Every original file survives with identical content.
        foreach (var (name, data) in originals)
        {
            var still = updated.FindFile(name);
            Assert.NotNull(still);
            Assert.Equal(data, updated.ReadFileData(still!));
        }

        Assert.Equal(cart.Files.Count + 1, updated.Files.Count);
    }

    [Fact]
    public void ImportFile_overwrite_replaces_content_without_adding_a_file()
    {
        var cart = Abacus();
        var target = cart.Files.First();
        var content = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };

        var updated = cart.ImportFile(target.Name, content, overwrite: true);

        Assert.Equal(cart.Files.Count, updated.Files.Count);
        Assert.Equal(content, updated.ReadFileData(updated.FindFile(target.Name)!));
    }

    [Fact]
    public void DeleteFile_removes_the_file_and_preserves_the_others()
    {
        var cart = Abacus();
        Assert.True(cart.Files.Count >= 1);

        var target = cart.Files.First();
        var survivors = cart.Files.Skip(1).ToDictionary(f => f.Name, f => cart.ReadFileData(f));

        var updated = cart.DeleteFile(target.Name);

        Assert.Equal(MdvCartridge.ImageSize, updated.ToBytes().Length);
        Assert.Null(updated.FindFile(target.Name));
        Assert.Equal(cart.Files.Count - 1, updated.Files.Count);

        foreach (var (name, data) in survivors)
        {
            var still = updated.FindFile(name);
            Assert.NotNull(still);
            Assert.Equal(data, updated.ReadFileData(still!));
        }
    }

    [Fact]
    public void Importing_a_copy_under_a_new_name_keeps_both_files_identical()
    {
        var cart = Abacus();
        var original = cart.Files.First();
        var content = cart.ReadFileData(original);

        var updated = cart.ImportFile(original.Name + "_copy", content, original.TypeCode, original.DataSpace);

        var copy = updated.FindFile(original.Name + "_copy");
        Assert.NotNull(copy);
        Assert.Equal(content, updated.ReadFileData(copy!));
        Assert.Equal(original.TypeCode, copy!.TypeCode);
        Assert.Equal(cart.Files.Count + 1, updated.Files.Count);
        // The source file is untouched.
        Assert.Equal(content, updated.ReadFileData(updated.FindFile(original.Name)!));
    }

    [Fact]
    public void CreateEmpty_produces_a_blank_loadable_cartridge()
    {
        var cart = MdvCartridge.CreateEmpty("BLANK", mediumId: 0x1234);

        Assert.Equal(MdvCartridge.ImageSize, cart.ToBytes().Length);
        Assert.Equal("BLANK", cart.MediumName);
        Assert.Equal(0x1234, cart.MediumId);
        Assert.Empty(cart.Files);
        Assert.Equal(MdvCartridge.SectorCount, cart.Sectors.Count);

        // A file can be imported into the fresh cartridge and read back.
        var content = new byte[] { 10, 20, 30, 40 };
        var updated = cart.ImportFile("HELLO_dat", content);
        Assert.Equal(content, updated.ReadFileData(updated.FindFile("HELLO_dat")!));
    }

    [Fact]
    public void Renaming_changes_the_name_and_keeps_the_content()
    {
        var cart = Abacus();
        var target = cart.Files.First();
        var content = cart.ReadFileData(target);

        var updated = cart.RenameFile(target.Name, "GAMES_" + target.Name);

        Assert.Null(updated.FindFile(target.Name));
        var moved = updated.FindFile("GAMES_" + target.Name);
        Assert.NotNull(moved);
        Assert.Equal(content, updated.ReadFileData(moved!));
        Assert.Equal(cart.Files.Count, updated.Files.Count);
    }

    [Fact]
    public void SetFileType_changes_the_type_and_keeps_the_content()
    {
        var cart = Abacus();
        var target = cart.Files.First();
        var content = cart.ReadFileData(target);
        byte newType = (byte)(target.IsExecutable ? 0 : 1);

        var updated = cart.SetFileType(target.Name, newType, 0);

        var changed = updated.FindFile(target.Name);
        Assert.NotNull(changed);
        Assert.Equal(newType, changed!.TypeCode);
        Assert.Equal(content, updated.ReadFileData(changed));
    }

    [Fact]
    public void WouldFit_rejects_a_file_larger_than_the_free_space()
    {
        var cart = Abacus();
        long huge = (long)MdvCartridge.SectorCount * 512; // far more than any cartridge holds

        bool fits = cart.WouldFit(huge, "BIG_dat", overwriteExisting: false, out int needed, out int available);

        Assert.False(fits);
        Assert.True(needed > available);
    }
}
