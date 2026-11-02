using DspSeedFinder.Data;

namespace DspSeedFinder.Gen
{
    /// <summary>行星建模管线（PlanetModelingManager.ModelingPlanetMain 简化版，P1：地形+陆海比；P2：矿脉）。</summary>
    public static class PlanetModelingManager
    {
        /// <summary>按 algoId 选算法（0~14 全量，验证版移植，与 DspFindSeed-3.1 一致）。</summary>
        public static PlanetAlgorithm Algorithm(PlanetData planet)
        {
            PlanetAlgorithm pa = planet.algoId switch
            {
                0 => new PlanetAlgorithm0(),
                1 => new PlanetAlgorithm1(),
                2 => new PlanetAlgorithm2(),
                3 => new PlanetAlgorithm3(),
                4 => new PlanetAlgorithm4(),
                5 => new PlanetAlgorithm5(),
                6 => new PlanetAlgorithm6(),
                7 => new PlanetAlgorithm7(),
                8 => new PlanetAlgorithm8(),
                9 => new PlanetAlgorithm9(),
                10 => new PlanetAlgorithm10(),
                11 => new PlanetAlgorithm11(),
                12 => new PlanetAlgorithm12(),
                13 => new PlanetAlgorithm13(),
                14 => new PlanetAlgorithm14(),
                _ => new PlanetAlgorithm11(),
            };
            pa.Reset(planet.seed, planet);
            return pa;
        }

        /// <summary>行星建模：高度场 + 陆海比 + 矿脉（等价 ModelingPlanetMain 全段）。</summary>
        public static void ModelingPlanet(PlanetData planet)
        {
            if (planet.data == null)
            {
                var data = new PlanetRawData(planet.precision);
                data.CalcVerts();
                planet.data = data;
            }
            var algo = Algorithm(planet);
            algo.GenerateTerrain(planet.mod_x, planet.mod_y);
            algo.CalcWaterPercent();   // → planet.landPercent
        }

        /// <summary>建模 + 矿脉生成，写 planet.veinCounts/veinAmounts。</summary>
        public static void ModelingPlanetWithVeins(PlanetData planet)
        {
            ModelingPlanet(planet);
            Algorithm(planet).GenerateVeins();
        }
    }
}
