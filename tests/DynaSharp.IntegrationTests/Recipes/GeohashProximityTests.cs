using System.Text;
using DynaSharp.IntegrationTests.Backends;
using DynaSharp.IntegrationTests.Commands;
using DynaSharp.Mapping;

namespace DynaSharp.IntegrationTests.Recipes;

/// <summary>
/// <see href="../../../docs/patterns.md">Geohash proximity</see>: a place goes into the partition of its grid cell, and "near here" reads the cell of a point and the eight around it. The grid breaks at the edges of the map: longitude must wrap at the
/// antimeridian, where neighbours share no prefix, and near a pole a cell is as narrow as a street, so the eight neighbours do not reach around it.
/// </summary>
[MethodDataSource(typeof(BackendSelection), nameof(BackendSelection.Selected))]
public sealed class GeohashProximityTests(Backend backend)
{
    private const string Base32 = "0123456789bcdefghjkmnpqrstuvwxyz";
    private const int Precision = 4;

    // Four characters are 20 bits, 10 for each axis.
    private const double CellHeight = 180.0 / 1024;
    private const double CellWidth = 360.0 / 1024;

    [Test]
    public async Task NearAsync_APlaceAcrossACellBorder_IsFoundAndAFarOneIsNot()
    {
        // Arrange (8.7890625 is a border between two cells)
        await using var sandbox = await CommandSandbox.StartAsync(backend);
        var places = sandbox.Shop.Of<Place>();
        await PutAsync(places, "near", 47.4, 8.80);
        await PutAsync(places, "far", 40.7, -74.0);

        // Act
        var found = await NearAsync(places, 47.4, 8.78);

        // Assert
        await Assert.That((found.Single().Name, Encode(47.4, 8.78) == Encode(47.4, 8.80))).IsEqualTo(("near", false));
    }

    [Test]
    public async Task NearAsync_AcrossTheAntimeridian_FindsThePlaceBecauseLongitudeWraps()
    {
        // Arrange (a few kilometres apart, and their hashes have no prefix in common)
        await using var sandbox = await CommandSandbox.StartAsync(backend);
        var places = sandbox.Shop.Of<Place>();
        await PutAsync(places, "east of the line", 0.1, -179.99);

        // Act
        var found = await NearAsync(places, 0.1, 179.99);

        // Assert
        await Assert.That((found.Single().Name, Encode(0.1, 179.99)[0] == Encode(0.1, -179.99)[0])).IsEqualTo(("east of the line", false));
    }

    [Test]
    public async Task NearAsync_AcrossThePole_MissesAPlaceThatIsClose()
    {
        // Arrange (2 km apart over the pole, and half a world apart in the grid: what is near a pole needs another index)
        await using var sandbox = await CommandSandbox.StartAsync(backend);
        var places = sandbox.Shop.Of<Place>();
        await PutAsync(places, "over the pole", 89.99, 180.0 - 0.01);

        // Act
        var found = await NearAsync(places, 89.99, 0.0);

        // Assert
        await Assert.That(found).IsEmpty();
    }

    private static async Task PutAsync(Table<Place> places, string name, double latitude, double longitude) =>
        await places.Put(new Place { Cell = Encode(latitude, longitude), Name = name }).SendAsync();

    private static async Task<List<Place>> NearAsync(Table<Place> places, double latitude, double longitude)
    {
        // At the pole, cells above collapse into one, and a fan-out takes each partition once.
        var cells = new HashSet<string>();
        for (var row = -1; row <= 1; row++)
        {
            for (var column = -1; column <= 1; column++)
            {
                var wrapped = ((longitude + (column * CellWidth) + 540) % 360) - 180;
                cells.Add(Encode(Math.Clamp(latitude + (row * CellHeight), -90, 90), wrapped));
            }
        }

        return await places.Query(cells.Select(Place.Keys.Pk.Of)).ItemsAsync().ToListAsync();
    }

    // Bits of longitude and latitude in turn, each halving the range, five to a character. Input: 57.64911, 10.40744. Output: u4pr.
    private static string Encode(double latitude, double longitude)
    {
        var (south, north, west, east) = (-90.0, 90.0, -180.0, 180.0);
        var hash = new StringBuilder(Precision);
        var character = 0;
        for (var bit = 0; bit < Precision * 5; bit++)
        {
            var upper = bit % 2 == 0 ? Halve(longitude, ref west, ref east) : Halve(latitude, ref south, ref north);
            character = (character * 2) + (upper ? 1 : 0);
            if (bit % 5 == 4)
            {
                hash.Append(Base32[character]);
                character = 0;
            }
        }

        return hash.ToString();

        static bool Halve(double value, ref double low, ref double high)
        {
            var middle = (low + high) / 2;
            var upper = value >= middle;
            (low, high) = upper ? (middle, high) : (low, middle);
            return upper;
        }
    }
}

[DynamoDbEntity]
[Discriminator("PLACE")]
[PartitionKey("CELL#{Cell}")]
[SortKey("{Name}")]
internal sealed partial record Place
{
    public required string Cell { get; init; }

    public required string Name { get; init; }
}
