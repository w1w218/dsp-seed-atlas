using System;
using DspSeedFinder.MathLf;

namespace DspSeedFinder.Data
{
    /// <summary>游戏开局描述（仅 P0 结构生成所需字段；蜂巢/战斗暂以默认值）。</summary>
    public class GameDesc
    {
        public int galaxyAlgo = 20200403;
        public int galaxySeed;
        public int starCount = 64;
        public bool isRareResource;       // “稀有无”选项（gasCoef 0.8）
        public bool isInfiniteResource;
        public float resourceMultiplier = 1f;
        public float oilAmountMultiplier = 1f;
        public int starNameLCID = 2052;   // 中文（影响命名，不影响结构）
        public int[] savedThemeIds = null!;   // 主题集（null = 全部主题）

        // 蜂巢（战斗）：P0 用默认值使 hive 计算趋于 0，不影响星系/行星结构
        public float maxDensity = 1f;
        public float initialColonize = 0f;
    }

    /// <summary>银河（星系）。</summary>
    public class GalaxyData
    {
        public int seed;
        public int starCount;
        public StarData[] stars = new StarData[0];
        public int birthPlanetId;
        public int birthStarId;
        public int habitableCount;
        public AstroData[] astrosData = new AstroData[0];
    }

    public class AstroData
    {
        public int id;
        public EAstroType type;
        public int parentId;
        public VectorLF3 uPos;
        public VectorLF3 uPosNext;
        public Quaternion uRot = Quaternion.identity;
        public Quaternion uRotNext = Quaternion.identity;
        public float uRadius;
    }

    /// <summary>恒星。</summary>
    public class StarData
    {
        public GalaxyData galaxy = null!;
        public int seed;
        public int index;
        public int id;
        public string name = "";
        public VectorLF3 position;
        public VectorLF3 uPosition;
        public float mass = 1f;
        public float lifetime = 50f;
        public float age;
        public EStarType type;
        public float temperature = 8500f;
        public ESpectrType spectr;
        public float classFactor;
        public float color;
        public float luminosity = 1f;
        public float radius = 1f;
        public float acdiskRadius;
        public float habitableRadius = 1f;
        public float lightBalanceRadius = 1f;
        public float dysonRadius = 10f;
        public float orbitScaler = 1f;
        public float asterBelt1OrbitIndex;
        public float asterBelt2OrbitIndex;
        public float asterBelt1Radius;
        public float asterBelt2Radius;
        public int planetCount;
        public float level;
        public float resourceCoef = 1f;
        public PlanetData[] planets = new PlanetData[0];

        public int astroId => id * 100;
        public float physicsRadius => radius * 1200f;
        public bool epicHive => type == EStarType.NeutronStar || type == EStarType.BlackHole;
    }

    /// <summary>行星。</summary>
    public class PlanetData
    {
        public GalaxyData galaxy = null!;
        public StarData star = null!;
        public int seed;
        public int infoSeed;
        public int id;
        public int index;
        public int orbitAround;
        public int number;
        public int orbitIndex;
        public string name = "";
        public float orbitRadius = 1f;
        public float orbitInclination;
        public float orbitLongitude;
        public double orbitalPeriod = 3600.0;
        public float orbitPhase;
        public float obliquity;
        public double rotationPeriod = 480.0;
        public float rotationPhase;
        public float radius = 200f;
        public float scale = 1f;
        public float sunDistance;
        public float habitableBias;
        public float temperatureBias;
        public float ionHeight;
        public float windStrength;
        public float luminosity;
        public float landPercent;
        public double mod_x;
        public double mod_y;
        public float waterHeight;
        public int waterItemId;
        public bool levelized;
        public int iceFlag;
        public EPlanetType type;
        public EPlanetSingularity singularity;
        public int theme;
        public int algoId;
        public int style;
        public PlanetData orbitAroundPlanet = null!;
        public Quaternion runtimeOrbitRotation;
        public Quaternion runtimeSystemRotation;
        public int[] gasItems = new int[0];
        public float[] gasSpeeds = new float[0];
        public int precision;
        public int segment;
        // P1 地形数据
        public PlanetRawData data = null!;

        // 矿脉生成所需（GenerateVeins 完整移植）
        public Vector3 birthPoint;                // 出生星偏置向量
        public Vector3 birthResourcePoint0;       // 出生星铁特殊点
        public Vector3 birthResourcePoint1;       // 出生星铜特殊点
        public Vector3 veinBiasVector;            // 非出生星偏置向量（写入）
        public int[] veinCounts = new int[15];    // GenerateVeins 结果：每矿种组数
        public long[] veinAmounts = new long[15]; // GenerateVeins 结果：每矿种总储量

        public float realRadius => radius * scale;
    }
}
