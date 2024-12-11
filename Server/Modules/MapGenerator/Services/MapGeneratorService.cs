﻿using System.Diagnostics;
using Fracture.Server.Modules.MapGenerator.Models;
using Fracture.Server.Modules.NoiseGenerator.Models;
using Fracture.Server.Modules.NoiseGenerator.Services;

namespace Fracture.Server.Modules.MapGenerator.Services;

public class MapGeneratorService : IMapGeneratorService
{
    private readonly Random _rnd = new();
    public MapData MapData { get; private set; } = default!;
    private MapParameters MapParameters;
    private MapParametersService mapParametersService = new();

    public Task<MapData> GetMap()
    {
        MapData = GenerateMap();
        return Task.FromResult(MapData);
    }

    private MapData GenerateMap()
    {
        MapParameters = mapParametersService.ReadMapParametersFromJson(
            "Config/MapParameters/Normal.json"
        );
        var noiseParameters = MapParameters.NoiseParameters;
        noiseParameters.Seed = noiseParameters.UseRandomSeed
            ? _rnd.Next(-100000, 100000)
            : noiseParameters.Seed;
        int width = 64;
        int height = 64;
        bool useFalloff = true;

        var grid = new Node[width, height];

        var falloffMap = FalloffGenerator.GenerateCustom(width); // Choose falloff type here

        var heightMap = CustomPerlin.GenerateNoiseMap(
            width,
            noiseParameters.Seed,
            noiseParameters.Octaves,
            noiseParameters.Persistence,
            noiseParameters.Lacunarity,
            noiseParameters.Scale
        );

        var temperatureMap = CustomPerlin.GenerateNoiseMap(
            width,
            noiseParameters.Seed + 1,
            noiseParameters.Octaves,
            noiseParameters.Persistence,
            noiseParameters.Lacunarity,
            noiseParameters.Scale
        );

        var biomeCategories = MapParameters.BiomeCategories;
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            heightMap[x, y] = (float)
                Math.Clamp(
                    (1 - Math.Pow(1 - heightMap[x, y], noiseParameters.Sharpness))
                        + noiseParameters.Boost,
                    0,
                    1
                );
            Math.Clamp(
                (1 - Math.Pow(1 - temperatureMap[x, y], noiseParameters.Sharpness))
                    + noiseParameters.Boost,
                0,
                1
            );

            if (useFalloff)
            {
                heightMap[x, y] = Math.Clamp(
                    noiseParameters.FalloffType
                        ? CustomPerlin.Lerp(
                            heightMap[x, y],
                            falloffMap[x, y],
                            noiseParameters.Falloff
                        )
                        : Math.Clamp(
                            heightMap[x, y] - (1 - falloffMap[x, y]) * noiseParameters.Falloff,
                            0,
                            1
                        ),
                    0,
                    1
                );
                temperatureMap[x, y] = Math.Clamp(
                    noiseParameters.FalloffType
                        ? CustomPerlin.Lerp(
                            temperatureMap[x, y],
                            falloffMap[x, y],
                            noiseParameters.Falloff
                        )
                        : Math.Clamp(
                            temperatureMap[x, y] - (1 - falloffMap[x, y]) * noiseParameters.Falloff,
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

                // If no biome is found, log it it's really good if biome data are invalid its easy to find where
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
