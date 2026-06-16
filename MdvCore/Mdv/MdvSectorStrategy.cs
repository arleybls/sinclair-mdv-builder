namespace MdvCore.Mdv;

/// <summary>How file blocks are placed across sectors when an image is written.</summary>
public enum MdvSectorStrategy
{
    /// <summary>Fill consecutive sectors (step back by one each block).</summary>
    Sequential,

    /// <summary>Spread blocks out by stepping back 13 sectors each time.</summary>
    Spaced,

    /// <summary>Scatter blocks to random free sectors.</summary>
    Random,
}
