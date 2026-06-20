using MdvCore.Mdv;

namespace MdvCore.Tests;

/// <summary>
/// Edge-case coverage built on synthetic <see cref="MdvCartridge.CreateEmpty"/> cartridges so every
/// case is deterministic and independent of the binary fixtures.
/// </summary>
public class MdvCartridgeEdgeCaseTests
{
    // Distinct, multi-block content per file so reassembly and survivor identity are actually tested.
    private static byte[] Pattern(int seed, int length) =>
        Enumerable.Range(0, length).Select(j => (byte)((seed * 7 + j) % 256)).ToArray();

    private static MdvCartridge WithFiles(int count, int baseLength = 600)
    {
        var cart = MdvCartridge.CreateEmpty("EDGE");
        for (int i = 1; i <= count; i++)
            cart = cart.ImportFile($"FILE{i}", Pattern(i, baseLength + i * 200));
        return cart;
    }

    [Fact]
    public void Delete_middle_file_renumbers_and_keeps_every_survivor()
    {
        var cart = WithFiles(5);
        var expected = cart.Files
            .Where(f => f.Name != "FILE3")
            .ToDictionary(f => f.Name, f => cart.ReadFileData(f));

        var updated = cart.DeleteFile("FILE3");

        Assert.Null(updated.FindFile("FILE3"));
        Assert.Equal(4, updated.Files.Count);
        foreach (var (name, data) in expected)
            Assert.Equal(data, updated.ReadFileData(updated.FindFile(name)!));

        // Survives a full save/reload round-trip too.
        var reloaded = MdvCartridge.LoadMdv(updated.ToBytes());
        foreach (var (name, data) in expected)
            Assert.Equal(data, reloaded.ReadFileData(reloaded.FindFile(name)!));
    }

    [Fact]
    public void Delete_that_shrinks_the_directory_frees_a_sector()
    {
        // 8 files → directory is (8+1)*64 = 576 bytes = 2 sectors; 7 files → 512 bytes = 1 sector.
        var cart = WithFiles(8, baseLength: 100);
        int DirSectors(MdvCartridge c) => c.Sectors.Count(s => s.State == MdvSectorState.Used && s.FileNumber == 0);
        Assert.Equal(2, DirSectors(cart));

        var updated = cart.DeleteFile("FILE4");

        Assert.Equal(1, DirSectors(updated));
        Assert.Equal(7, updated.Files.Count);
        // All survivors remain readable.
        foreach (var f in updated.Files)
            Assert.Equal(f.DataLength, updated.ReadFileData(f).Length);
    }

    [Fact]
    public void Delete_the_only_file_yields_an_empty_reloadable_cartridge()
    {
        var cart = MdvCartridge.CreateEmpty("ONE").ImportFile("SOLO", Pattern(1, 800));

        var empty = cart.DeleteFile("SOLO");

        Assert.Empty(empty.Files);
        var reloaded = MdvCartridge.LoadMdv(empty.ToBytes());
        Assert.Empty(reloaded.Files);
        // Still usable afterwards.
        var again = reloaded.ImportFile("NEW", Pattern(2, 300));
        Assert.NotNull(again.FindFile("NEW"));
    }

    [Fact]
    public void Delete_then_import_reuses_freed_space_without_residual_bytes()
    {
        var old = Enumerable.Repeat((byte)0xFF, 2000).ToArray();
        var cart = MdvCartridge.CreateEmpty("REUSE").ImportFile("OLD", old);

        var fresh = Enumerable.Repeat((byte)0x00, 1000).ToArray();
        var updated = cart.DeleteFile("OLD").ImportFile("NEW", fresh);

        Assert.Null(updated.FindFile("OLD"));
        Assert.Equal(fresh, updated.ReadFileData(updated.FindFile("NEW")!));
    }

    [Fact]
    public void WouldFit_is_true_at_exact_capacity_and_import_succeeds()
    {
        var cart = MdvCartridge.CreateEmpty("FIT");
        // Directory takes 1 block; a single file of this length needs exactly the remaining sectors.
        // needed = 1 + ceil((L+64)/512); choose L so it equals AvailableSectors.
        long exact = 252L * 512 - 64; // → file needs 252 blocks, +1 directory = 253 = AvailableSectors

        bool fits = cart.WouldFit(exact, "BIG", overwriteExisting: false, out int needed, out int available);

        Assert.True(fits);
        Assert.Equal(available, needed);

        var imported = cart.ImportFile("BIG", new byte[exact]);
        Assert.Equal(exact, imported.ReadFileData(imported.FindFile("BIG")!).Length);
    }

    [Fact]
    public void Importing_one_sector_too_much_is_rejected_and_throws()
    {
        var cart = MdvCartridge.CreateEmpty("OVER");
        long overByOne = 252L * 512 - 64 + 1; // pushes the file to 253 blocks → 254 total > 253 free

        Assert.False(cart.WouldFit(overByOne, "TOOBIG", overwriteExisting: false, out int needed, out int available));
        Assert.True(needed > available);
        Assert.Throws<MdvInsufficientSpaceException>(() => cart.ImportFile("TOOBIG", new byte[overByOne]));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(511)]
    [InlineData(512)]
    [InlineData(513)]
    [InlineData(1023)]
    [InlineData(1024)]
    [InlineData(1025)]
    public void Files_at_block_boundaries_round_trip_exactly(int length)
    {
        var content = Pattern(9, length);
        var cart = MdvCartridge.CreateEmpty("BND").ImportFile("DATA", content);

        var file = cart.FindFile("DATA");
        Assert.NotNull(file);
        Assert.Equal(length, file!.DataLength);
        Assert.Equal(content, cart.ReadFileData(file));

        var reloaded = MdvCartridge.LoadMdv(cart.ToBytes());
        Assert.Equal(content, reloaded.ReadFileData(reloaded.FindFile("DATA")!));
    }

    [Fact]
    public void Zero_byte_file_imports_and_reads_back_empty()
    {
        var cart = MdvCartridge.CreateEmpty("ZERO").ImportFile("EMPTY", Array.Empty<byte>());

        var file = cart.FindFile("EMPTY");
        Assert.NotNull(file);
        Assert.Equal(0, file!.DataLength);
        Assert.Empty(cart.ReadFileData(file));

        var reloaded = MdvCartridge.LoadMdv(cart.ToBytes());
        Assert.Empty(reloaded.ReadFileData(reloaded.FindFile("EMPTY")!));
    }

    [Fact]
    public void Damaged_sector_is_preserved_and_never_allocated()
    {
        var cart = MdvCartridge.CreateEmpty("DMG"); // CreateEmpty marks sector 254 damaged
        Assert.Equal(MdvSectorState.Damaged, cart.Sectors[254].State);

        var afterImport = cart.ImportFile("A", Pattern(1, 1500));
        Assert.Equal(MdvSectorState.Damaged, afterImport.Sectors[254].State);

        var afterDelete = afterImport.DeleteFile("A");
        Assert.Equal(MdvSectorState.Damaged, afterDelete.Sectors[254].State);

        var reloaded = MdvCartridge.LoadMdv(afterImport.ToBytes());
        Assert.Equal(MdvSectorState.Damaged, reloaded.Sectors[254].State);
    }

    [Fact]
    public void Random_strategy_preserves_content_and_is_deterministic_under_a_seed()
    {
        var content = Pattern(3, 4000);

        var previousStrategy = MdvCartridge.AllocationStrategy;
        var previousSeed = MdvCartridge.AllocationSeed;
        try
        {
            MdvCartridge.AllocationStrategy = MdvSectorStrategy.Random;
            MdvCartridge.AllocationSeed = 12345;
            // Pin the medium id: CreateEmpty randomises it otherwise, which alone would differ the bytes.
            var a = MdvCartridge.CreateEmpty("RND", mediumId: 0x1111).ImportFile("DATA", content);
            var b = MdvCartridge.CreateEmpty("RND", mediumId: 0x1111).ImportFile("DATA", content);

            Assert.Equal(content, a.ReadFileData(a.FindFile("DATA")!));
            // Same seed → byte-identical image.
            Assert.Equal(a.ToBytes(), b.ToBytes());

            MdvCartridge.AllocationStrategy = MdvSectorStrategy.Sequential;
            var seq = MdvCartridge.CreateEmpty("RND", mediumId: 0x1111).ImportFile("DATA", content);
            int[] Used(MdvCartridge c) =>
                c.Sectors.Where(s => s.State == MdvSectorState.Used).Select(s => s.Index).OrderBy(i => i).ToArray();
            Assert.NotEqual(Used(seq), Used(a));
        }
        finally
        {
            MdvCartridge.AllocationStrategy = previousStrategy;
            MdvCartridge.AllocationSeed = previousSeed;
        }
    }

    [Theory]
    [InlineData("ABC.DEF", "ABC_DEF")]
    [InlineData("no_dots", "no_dots")]
    [InlineData("a.b.c", "a_b_c")]
    public void CleanFileName_replaces_dots(string input, string expected) =>
        Assert.Equal(expected, MdvCartridge.CleanFileName(input));

    [Fact]
    public void CleanFileName_handles_null_and_truncates_to_36()
    {
        Assert.Equal(string.Empty, MdvCartridge.CleanFileName(null!));
        Assert.Equal(36, MdvCartridge.CleanFileName(new string('x', 50)).Length);
    }

    [Fact]
    public void GetSectorData_is_bounds_checked()
    {
        var cart = MdvCartridge.CreateEmpty("GSD");
        Assert.Null(cart.GetSectorData(-1));
        Assert.Null(cart.GetSectorData(256));
        Assert.NotNull(cart.GetSectorData(0)); // sector 0 (the map) is always present
    }

    [Fact]
    public void FindFile_is_case_insensitive()
    {
        var cart = MdvCartridge.CreateEmpty("CASE").ImportFile("Hello", Pattern(1, 200));
        Assert.NotNull(cart.FindFile("HELLO"));
        Assert.NotNull(cart.FindFile("hello"));
        Assert.Null(cart.FindFile("nope"));
    }

    [Fact]
    public void Duplicate_names_use_first_match_semantics()
    {
        // Documents current behavior: import without overwrite can create a same-named second file,
        // and FindFile / DeleteFile then act on the first match only.
        var a = Pattern(1, 300);
        var b = Pattern(2, 300);
        var cart = MdvCartridge.CreateEmpty("DUP").ImportFile("DUP", a).ImportFile("DUP", b);

        Assert.Equal(2, cart.Files.Count(f => f.Name == "DUP"));
        Assert.Equal(a, cart.ReadFileData(cart.FindFile("DUP")!)); // first match

        var afterDelete = cart.DeleteFile("DUP");
        Assert.Equal(1, afterDelete.Files.Count(f => f.Name == "DUP"));
        Assert.Equal(b, afterDelete.ReadFileData(afterDelete.FindFile("DUP")!)); // the survivor
    }

    [Fact]
    public void Rename_and_set_type_on_a_missing_file_are_no_ops()
    {
        var cart = MdvCartridge.CreateEmpty("NOP").ImportFile("REAL", Pattern(1, 200));
        Assert.Same(cart, cart.RenameFile("GHOST", "WHATEVER"));
        Assert.Same(cart, cart.SetFileType("GHOST", 1, 0));
    }

    [Theory]
    [InlineData(100)]                          // too short
    [InlineData(MdvCartridge.ImageSize + 1)]   // too long
    public void Load_rejects_a_wrongly_sized_image(int size) =>
        Assert.Throws<InvalidDataException>(() => MdvCartridge.LoadMdv(new byte[size]));

    [Fact]
    public void Load_rejects_an_image_with_no_sector_zero()
    {
        // Correct length but all zero → no sector has the 0xFF valid flag → sector 0 missing.
        Assert.Throws<InvalidDataException>(() => MdvCartridge.LoadMdv(new byte[MdvCartridge.ImageSize]));
    }

    [Fact]
    public void Minerva_workaround_damages_one_sector_without_mutating_the_original()
    {
        var cart = MdvCartridge.CreateEmpty("MIN", mediumId: 0x2222).ImportFile("F", Pattern(1, 1500));
        Assert.True(cart.VerifyChecksums()); // a freshly built image is "perfect"

        byte[] minerva = cart.ToMinervaCompatibleBytes();
        Assert.Equal(MdvCartridge.ImageSize, minerva.Length);
        Assert.NotEqual(cart.ToBytes(), minerva);                 // it changed something
        Assert.True(cart.VerifyChecksums());                      // ...but not the original

        var reloaded = MdvCartridge.LoadMdv(minerva);
        Assert.False(reloaded.VerifyChecksums());                 // now a sector fails verify
        Assert.NotNull(reloaded.FirstChecksumError());
        // Exactly sector 13 (by logical sector number) is the one that fails — what the map shows.
        Assert.Equal(new[] { 13 }, reloaded.SectorsFailingVerify().OrderBy(n => n).ToArray());
        // The damaged sector is #13, so its data no longer reads back intact, but the rest is fine:
        // the other file's content is untouched.
        Assert.Equal(cart.ReadFileData(cart.FindFile("F")!), reloaded.ReadFileData(reloaded.FindFile("F")!));
    }

    [Fact]
    public void VerifyChecksums_passes_for_a_freshly_built_image_and_fails_when_corrupted()
    {
        var cart = MdvCartridge.CreateEmpty("CHK").ImportFile("F", Pattern(1, 1500));
        Assert.True(cart.VerifyChecksums());
        Assert.Null(cart.FirstChecksumError());

        // Flip a byte in sector 0's data payload (on disk at offset 52) and reload.
        var bytes = cart.ToBytes();
        bytes[52] ^= 0xFF;
        var corrupt = MdvCartridge.LoadMdv(bytes);
        Assert.False(corrupt.VerifyChecksums());
        Assert.NotNull(corrupt.FirstChecksumError());
    }
}
