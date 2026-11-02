namespace DspSeedFinder.Data
{
    public enum EStarType
    {
        MainSeqStar = 0,
        GiantStar = 1,
        WhiteDwarf = 2,
        NeutronStar = 3,
        BlackHole = 4,
    }

    public enum ESpectrType
    {
        M = 0, K = 1, G = 2, F = 3, A = 4, B = 5, O = 6, X = 7,
    }

    public enum EPlanetType
    {
        None = 0, Vocano = 1, Ocean = 2, Desert = 3, Ice = 4, Gas = 5,
    }

    public enum EThemeDistribute
    {
        Default = 0, Birth = 1, Interstellar = 2, Rare = 3,
    }

    public enum EVeinType : byte
    {
        None = 0, Iron = 1, Copper = 2, Silicium = 3, Titanium = 4, Stone = 5,
        Coal = 6, Oil = 7, Fireice = 8, Diamond = 9, Fractal = 10,
        Crysrub = 11, Grat = 12, Bamboo = 13, Mag = 14, Max = 15,
    }

    [System.Flags]
    public enum EPlanetSingularity
    {
        None = 0,
        TidalLocked = 1,
        TidalLocked2 = 2,
        TidalLocked4 = 4,
        LaySide = 8,
        ClockwiseRotate = 0x10,
        MultipleSatellites = 0x20,
    }

    public enum EAstroType
    {
        Star = 0, Planet = 1, Gas = 2,
    }
}
