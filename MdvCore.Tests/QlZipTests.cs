using MdvCore.Mdv;

namespace MdvCore.Tests;

public class QlZipTests
{
    [Fact]
    public void Extra_field_round_trips_type_and_data_space()
    {
        var entry = new MdvFileEntry(
            FileNumber: 1, Name: "GAME", TypeCode: 1, DataLength: 1000, DataSpace: 4096, BlockCount: 2,
            FileAccess: 0, ExtraInfo: 0, UpdateDate: 0, ReferenceDate: 0, BackupDate: 0);

        byte[] header = MdvCartridge.BuildQlFileHeader(entry);
        byte[] field = QlZip.BuildQlExtraField(header);

        var (typeCode, dataSpace) = QlZip.ReadQlExtraField(field);

        Assert.Equal((byte)1, typeCode);
        Assert.Equal(4096u, dataSpace);
    }

    [Fact]
    public void Reading_absent_or_unusable_extra_data_yields_defaults()
    {
        Assert.Equal(((byte)0, 0u), QlZip.ReadQlExtraField(null));
        Assert.Equal(((byte)0, 0u), QlZip.ReadQlExtraField(Array.Empty<byte>()));
        Assert.Equal(((byte)0, 0u), QlZip.ReadQlExtraField(new byte[] { 0x01, 0x02 })); // too short for a header
        // A well-formed but non-QDOS field (id 0x0001, size 0) is skipped → defaults.
        Assert.Equal(((byte)0, 0u), QlZip.ReadQlExtraField(new byte[] { 0x01, 0x00, 0x00, 0x00 }));
    }

    [Fact]
    public void Qdos_field_after_an_unrelated_field_is_still_found()
    {
        var entry = new MdvFileEntry(
            FileNumber: 1, Name: "EXEC", TypeCode: 1, DataLength: 10, DataSpace: 2048, BlockCount: 1,
            FileAccess: 0, ExtraInfo: 0, UpdateDate: 0, ReferenceDate: 0, BackupDate: 0);
        byte[] qdos = QlZip.BuildQlExtraField(MdvCartridge.BuildQlFileHeader(entry));

        // Prepend an unrelated 4-byte extra field (id 0x7875, size 0) before the QDOS one.
        var combined = new byte[] { 0x75, 0x78, 0x00, 0x00 }.Concat(qdos).ToArray();

        var (typeCode, dataSpace) = QlZip.ReadQlExtraField(combined);
        Assert.Equal((byte)1, typeCode);
        Assert.Equal(2048u, dataSpace);
    }
}
