﻿using Fracture.Server.Modules.MapGenerator.Models;
using Fracture.Server.Modules.NoiseGenerator.Models;
using Fracture.Server.Modules.NoiseGenerator.Services;

namespace Fracture.Server.Modules.MapGenerator.Services;

public class MapGeneratorService : IMapGeneratorService
{
    private readonly float _boost = 0.3f; // Flat boost to heightmap. Adds, then clamps
    private readonly float _falloff = 0.3f; // How much the falloff map affects the heightmap
    private readonly bool _falloffType = true; // true = lerp, false = subtract
    private readonly float _lacunarity = 2f; // How fast the frequency increases for each octave
    private readonly int _octaves = 3; // Number of octaves

    private readonly float _persistence = 0.5f; // How much consecutive octaves contribute to the noise
    private readonly float _scale = 2.9f; // Scale of the noise, bigger scale = more zoomed in
    private readonly float _sharpness = 1f; // How "sharp" heightmap is. Just a power function

    private readonly Random _rnd = new();

    private int _seed;

    public MapData MapData { get; private set; } = default!;

    public Task<MapData> GetMap(NoiseParameters noiseParameters)
    {
        MapData = GenerateMap(noiseParameters);
        return Task.FromResult(MapData);
    }

    private MapData GenerateMap(NoiseParameters noiseParameters)
    {
        var width = 64;
        var height = 64;
        var useFalloff = true;
        _seed = noiseParameters.UseRandomSeed ? _rnd.Next(int.MaxValue) : noiseParameters.Seed;

        var grid = new Node[width, height];

        var falloffMap = FalloffGenerator.GenerateEuclideanSquared(width); // Choose falloff type here

        var heightMap = CustomPerlin.GenerateNoiseMap(
            width,
            _seed,
            _octaves,
            _persistence,
            _lacunarity,
            _scale
        );

        var temperatureMap = CustomPerlin.GenerateNoiseMap(
            width,
            _seed + 1,
            _octaves,
            _persistence,
            _lacunarity,
            _scale
        );

        var biomeCategories = BiomeFactory.GetBiomes();
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            heightMap[x, y] = (float)
                Math.Clamp(Math.Pow(heightMap[x, y], _sharpness) + _boost, 0, 1);
            Math.Clamp(Math.Pow(temperatureMap[x, y], _sharpness) + _boost, 0, 1);

            if (useFalloff)
            {
                heightMap[x, y] = Math.Clamp(
                    _falloffType
                        ? CustomPerlin.Lerp(heightMap[x, y], falloffMap[x, y], _falloff)
                        : Math.Clamp(heightMap[x, y] - (1 - falloffMap[x, y]) * _falloff, 0, 1),
                    0,
                    1
                );
                temperatureMap[x, y] = Math.Clamp(
                    _falloffType
                        ? CustomPerlin.Lerp(temperatureMap[x, y], falloffMap[x, y], _falloff)
                        : Math.Clamp(
                            temperatureMap[x, y] - (1 - falloffMap[x, y]) * _falloff,
                            0,
                            1
                        ),
                    0,
                    1
                );
            }

            var biomeCategory = biomeCategories.FirstOrDefault(b =>
                heightMap[x, y] >= b.MinHeight && heightMap[x, y] < b.MaxHeight
            );

            // If no biome category is found, log it
            if (biomeCategory == null)
            {
                Console.WriteLine(
                    $"No biome category found for height {heightMap[x, y]} at ({x}, {y})."
                );
            }

            Biome biome = null;
            if (biomeCategory != null)
            {
                biome = biomeCategory.Biomes.FirstOrDefault(sb =>
                    temperatureMap[x, y] >= sb.MinTemperature
                    && temperatureMap[x, y] < sb.MaxTemperature
                )!;

                // If no biome is found, log it
                if (biome == null)
                {
                    Console.WriteLine(
                        $"No biome found for temperature {temperatureMap[x, y]} at ({x}, {y}) within category {biomeCategory.TerrainType}"
                    );
                }
            }

            grid[x, y] = new Node(x, y, biome)
            {
                NoiseValue = heightMap[x, y],
                Walkable =
                    biomeCategory != null
                    && !(
                        biomeCategory.TerrainType
                        is TerrainType.DeepOcean
                            or TerrainType.ShallowWater
                    ),
            };
        }

        return new MapData(grid);
    }
}
