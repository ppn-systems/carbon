// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Text;
using Carbon.Maps.Generators;
using Carbon.Maps.Landmarks;
using Carbon.Maps.Settlements;
using FluentAssertions;
using Xunit;

namespace Carbon.Maps.Tests;

/// <summary>
/// Villages are laid out from <c>structures.json</c> footprints (furniture sizes), so the server and every client must
/// generate them with the same catalog. Pinned before the catalog was passed through world generation.
/// </summary>
public sealed class VillageStructureCatalogTests
{
    private static readonly VillageBuildingRole[] s_roles =
        [VillageBuildingRole.House, VillageBuildingRole.Smithy, VillageBuildingRole.Store, VillageBuildingRole.Storehouse, VillageBuildingRole.Stall];

    [Fact]
    public void Generate_ShippedCatalog_BuildingsMatchThePinnedDigest()
    {
        StringBuilder text = new();
        foreach (VillageBuildingRole role in s_roles)
        {
            for (ulong seed = 0; seed < 200; seed++)
            {
                AppendTemplate(text, VillageBuildingGenerator.Generate(role, seed));
            }
        }

        Fnv(text).Should().Be(8561769121045331354UL, "village buildings generated from the shipped structures.json");
    }

    [Fact]
    public void Landmarks_ShippedCatalog_VillagePlotsMatchThePinnedDigest()
    {
        StringBuilder plots = VillagePlotText(new ProceduralWorldGenerator(42UL));

        plots.Length.Should().BeGreaterThan(0, "seed 42 has villages within four sites of the origin");
        Fnv(plots).Should().Be(15806800225155006993UL, "village plots generated from the shipped structures.json");
    }

    internal static StringBuilder VillagePlotText(ProceduralWorldGenerator generator)
    {
        StringBuilder text = new();
        for (int sy = -4; sy <= 4; sy++)
        {
            for (int sx = -4; sx <= 4; sx++)
            {
                PointOfInterest poi = generator.Landmarks.GetSite(sx, sy);
                if (poi.Type is not (PointOfInterestType.RiverVillage or PointOfInterestType.DesertVillage or
                    PointOfInterestType.SnowVillage or PointOfInterestType.SwampVillage))
                {
                    continue;
                }

                foreach (VillagePlot plot in generator.Landmarks.GetLayout(sx, sy).Plots)
                {
                    text.Append(plot.Role).Append('@').Append(plot.OriginX).Append(',').Append(plot.OriginY)
                        .Append(' ').Append(plot.Width).Append('x').Append(plot.Height).Append(" t").Append(plot.Turns).Append(' ');
                    AppendTemplate(text, plot.Template);
                }
            }
        }

        return text;
    }

    internal static ulong Fnv(StringBuilder text)
    {
        ulong hash = 14695981039346656037UL;
        foreach (char c in text.ToString())
        {
            hash = (hash ^ c) * 1099511628211UL;
        }

        return hash;
    }

    private static void AppendTemplate(StringBuilder text, VillageBuildingTemplate template)
    {
        text.Append(template.Id).Append(':');
        foreach (VillageFurniture f in template.Furniture)
        {
            text.Append(f.Type).Append('/').Append(f.X).Append(',').Append(f.Y).Append('/').Append(f.Rotation).Append(';');
        }

        text.Append('\n');
    }
}
