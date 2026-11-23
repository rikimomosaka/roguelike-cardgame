using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

if (args.Length is not (3 or 4))
{
    Console.Error.WriteLine(
        "Usage: CardFrameAtlasTool <input-atlas.png> <output-root> <plaque-atlas.png> [type-frame-atlas.png]");
    return 2;
}

var inputPath = Path.GetFullPath(args[0]);
var outputRoot = Path.GetFullPath(args[1]);
var plaqueAtlasPath = Path.GetFullPath(args[2]);
var typeFrameAtlasPath = args.Length == 4 ? Path.GetFullPath(args[3]) : null;
if (!File.Exists(inputPath))
{
    Console.Error.WriteLine($"Atlas not found: {inputPath}");
    return 2;
}
if (!File.Exists(plaqueAtlasPath))
{
    Console.Error.WriteLine($"Plaque atlas not found: {plaqueAtlasPath}");
    return 2;
}
if (typeFrameAtlasPath is not null && !File.Exists(typeFrameAtlasPath))
{
    Console.Error.WriteLine($"Type-frame atlas not found: {typeFrameAtlasPath}");
    return 2;
}

try
{
    var summary = CardFrameAtlasProcessor.Process(
        inputPath,
        outputRoot,
        plaqueAtlasPath,
        typeFrameAtlasPath);
    Console.WriteLine(summary);
    return 0;
}
catch (Exception exception)
{
    Console.Error.WriteLine(exception);
    return 1;
}

internal static class CardFrameAtlasProcessor
{
    private const int AtlasColumns = 5;
    private const int AtlasRows = 4;
    private const int CardWidth = 560;
    private const int CardHeight = 784;
    private const int CostBaseSize = 128;
    private const int MinimumComponentPixels = 8;
    private const int TinyNoisePixels = 19;
    private const int PlaqueWidth = 302;
    private const int PlaqueHeight = 66;
    private const int ProtectedTitleBandBottom = 94;
    private const int RulesPanelTop = 500;
    // The generated affiliation artwork aperture is surrounded by a 16 px legacy rim,
    // while its bright horizontal top highlight reaches 25 px above the aperture. Keep
    // the original side/bottom cleanup width and extend only upward so the outer rails
    // remain byte-for-byte untouched outside the shape-derived mask.
    private const int LegacyArtworkFrameCleanupHorizontalDilation = 16;
    private const int LegacyArtworkFrameCleanupUpwardDilation = 25;
    private const int LegacyArtworkFrameCleanupDownwardDilation = 16;
    private const double MaximumArtworkBoundaryMeanPixelRgbDelta = 5.0;
    // The mask compares only the transparent aperture; connected corner ornaments remain
    // visible in the type-frame PNG but are intentionally excluded from this ratio. Two
    // percent is enough to reject the previous near-identical 0.53% pair while preserving
    // the shared central focus rectangle required by every illustration.
    private const double MinimumMaskPairDifferenceRatio = 0.02;
    private static readonly Color TransparentPixel = Color.FromArgb(0, 0, 0, 0);
    private static readonly Color LegacyArtworkBackingColor = Color.FromArgb(255, 8, 10, 13);
    // This fully opaque interior of every generated title band contains its affiliation
    // colour and subtle material pattern, but no outer ornaments or runtime title text.
    private static readonly Rectangle ArtworkBackingSampleBounds =
        Rectangle.FromLTRB(100, 42, 460, 94);
    private static readonly Rectangle ArtworkBoundaryInspectionBounds =
        Rectangle.FromLTRB(120, 80, 440, 141);
    private static readonly Rectangle PlaqueBounds = new(129, 426, PlaqueWidth, PlaqueHeight);
    private static readonly Rectangle OldPlaqueCrownRemovalBounds = Rectangle.FromLTRB(120, 370, 440, 426);
    private static readonly Rectangle OldPlaqueRemovalBounds = Rectangle.FromLTRB(129, 426, 431, 494);
    private static readonly string[] PlaqueNames =
        ["attack", "skill", "power", "unit", "status", "curse"];

    private enum AssetKind
    {
        Affiliation,
        Type,
        Cost,
        Empty,
    }

    private sealed record AtlasSlot(
        int Row,
        int Column,
        AssetKind Kind,
        string Name,
        string Description);

    // Position-addressed cells keep generated speckles from reordering the asset mapping.
    private static readonly AtlasSlot[] Slots =
    [
        new(0, 0, AssetKind.Affiliation, "striker", "affiliation striker"),
        new(0, 1, AssetKind.Affiliation, "guardian", "affiliation guardian"),
        new(0, 2, AssetKind.Affiliation, "tactician", "affiliation tactician"),
        new(0, 3, AssetKind.Affiliation, "enhancer", "affiliation enhancer"),
        new(0, 4, AssetKind.Affiliation, "universal", "affiliation universal"),

        new(1, 0, AssetKind.Affiliation, "curse", "affiliation curse"),
        new(1, 1, AssetKind.Type, "attack", "type ATTACK"),
        new(1, 2, AssetKind.Type, "skill", "type SKILL"),
        new(1, 3, AssetKind.Type, "power", "type POWER"),
        // Unit is the stable internal type/filename; the generated plaque visibly says SUMMON.
        new(1, 4, AssetKind.Type, "unit", "type SUMMON"),

        new(2, 0, AssetKind.Type, "status", "type STATUS"),
        new(2, 1, AssetKind.Type, "curse", "type CURSE"),
        new(2, 2, AssetKind.Cost, "striker", "cost striker"),
        new(2, 3, AssetKind.Cost, "guardian", "cost guardian"),
        new(2, 4, AssetKind.Cost, "tactician", "cost tactician"),

        new(3, 0, AssetKind.Cost, "enhancer", "cost enhancer"),
        new(3, 1, AssetKind.Cost, "universal", "cost universal"),
        new(3, 2, AssetKind.Cost, "curse", "cost curse"),
        new(3, 3, AssetKind.Empty, "empty-1", "reserved empty cell"),
        new(3, 4, AssetKind.Empty, "empty-2", "reserved empty cell"),
    ];

    private sealed class Component
    {
        public int Left { get; set; } = int.MaxValue;
        public int Top { get; set; } = int.MaxValue;
        public int Right { get; set; } = int.MinValue;
        public int Bottom { get; set; } = int.MinValue;
        public int Count { get; set; }
        public List<int> Pixels { get; } = [];
        public double CenterX => (Left + Right) / 2.0;
        public double CenterY => (Top + Bottom) / 2.0;

        public bool Contains(double x, double y, int padding) =>
            x >= Left - padding && x <= Right + padding &&
            y >= Top - padding && y <= Bottom + padding;
    }

    private sealed class PlaqueSet : IDisposable
    {
        public required Dictionary<string, Bitmap> Plaques { get; init; }
        public required byte[] CanonicalAlpha { get; init; }
        public required Rectangle CanonicalAlphaBounds { get; init; }
        public required int LabelBaseline { get; init; }
        public required string CleanedAtlasPath { get; init; }

        public Bitmap this[string name] => Plaques[name];

        public void Dispose()
        {
            foreach (var plaque in Plaques.Values)
            {
                plaque.Dispose();
            }
        }
    }

    private sealed class TypeFrameSet : IDisposable
    {
        public required Dictionary<string, Bitmap> Frames { get; init; }
        public required Color Matte { get; init; }
        public required string CleanedAtlasPath { get; init; }

        public Bitmap this[string name] => Frames[name];

        public void Dispose()
        {
            foreach (var frame in Frames.Values)
            {
                frame.Dispose();
            }
        }
    }

    private sealed record MaskDifferenceSummary(
        string MinimumPair,
        double MinimumRatio,
        string MaximumPair,
        double MaximumRatio);

    private sealed record AffiliationCleanupContract(
        string Name,
        int Width,
        int Height,
        int[] NormalizedInputArgb,
        int[] ExpectedBackingArgb,
        bool[] CleanupMask,
        Rectangle CleanupBounds,
        int ReplacedLegacyPixels);

    private sealed record AffiliationCleanupSummary(
        int CleanupPixels,
        int ReplacedLegacyPixels,
        int MinimumBackingColorCount,
        double MaximumBoundaryDelta,
        string MaximumBoundaryLocation);

    public static string Process(
        string inputPath,
        string outputRoot,
        string plaqueAtlasPath,
        string? typeFrameAtlasPath = null)
    {
        ValidateSlotManifest();
        var finalOutputRoot = Path.GetFullPath(outputRoot);
        Directory.CreateDirectory(finalOutputRoot);
        var stagingRoot = Path.GetFullPath(Path.Combine(
            finalOutputRoot,
            $".card-frame-stage-{Guid.NewGuid():N}"));
        EnsureContainedPath(finalOutputRoot, stagingRoot, "staging directory");
        Directory.CreateDirectory(stagingRoot);

        try
        {
            var affiliationDirectory = Path.Combine(stagingRoot, "affiliations");
            var typeDirectory = Path.Combine(stagingRoot, "types");
            var maskDirectory = Path.Combine(stagingRoot, "masks");
            var costDirectory = Path.Combine(stagingRoot, "costs");
            Directory.CreateDirectory(affiliationDirectory);
            Directory.CreateDirectory(typeDirectory);
            Directory.CreateDirectory(maskDirectory);
            Directory.CreateDirectory(costDirectory);
            var affiliationCleanupContracts = new List<AffiliationCleanupContract>();

            using var source = new Bitmap(inputPath);
            ValidateAtlasDimensions(source);
            using var cleaned = RemoveFlatChromaMatte(source, out var matte);
            using var plaqueSet = LoadPlaqueSet(plaqueAtlasPath);
            using var typeFrameSet = typeFrameAtlasPath is null
                ? null
                : LoadTypeFrameSet(typeFrameAtlasPath);
            var rowBoundaries = FindRowBoundaries(cleaned);
            var columnBoundaries = FindColumnBoundaries(cleaned);
            var cleanedAtlasPath = Path.Combine(
                Path.GetDirectoryName(inputPath) ?? finalOutputRoot,
                Path.GetFileNameWithoutExtension(inputPath) + "-clean.png");
            cleaned.Save(cleanedAtlasPath, ImageFormat.Png);

            var writtenPaths = new List<string>();
            foreach (var slot in Slots)
            {
                using var cell = CropAtlasCell(
                    cleaned,
                    slot.Row,
                    slot.Column,
                    rowBoundaries,
                    columnBoundaries);
                RemoveIsolatedNoise(cell, matte);

                if (slot.Kind == AssetKind.Empty)
                {
                    ValidateEmptyCell(cell, slot);
                    continue;
                }

                using var content = slot.Kind == AssetKind.Type && typeFrameSet is not null
                    ? new Bitmap(typeFrameSet[slot.Name])
                    : CropStableContent(cell, slot);
                var slotMatte = slot.Kind == AssetKind.Type && typeFrameSet is not null
                    ? typeFrameSet.Matte
                    : matte;
                switch (slot.Kind)
                {
                    case AssetKind.Affiliation:
                    {
                        using var transparentFrame = NormalizeAffiliationFrame(content, CardWidth, CardHeight);
                        RemoveResidualLowAlphaMatteSpill(transparentFrame);
                        RemoveIsolatedNoise(transparentFrame, slotMatte);
                        RemoveSmallOpaqueComponents(transparentFrame);
                        using var normalized = RemoveLegacyArtworkFrameAndAddOpaqueBacking(
                            transparentFrame,
                            slot,
                            out var cleanupMask,
                            out var cleanupBounds,
                            out var expectedBackingArgb,
                            out var replacedLegacyPixels);
                        affiliationCleanupContracts.Add(new AffiliationCleanupContract(
                            slot.Name,
                            transparentFrame.Width,
                            transparentFrame.Height,
                            ReadArgb(transparentFrame),
                            expectedBackingArgb,
                            cleanupMask,
                            cleanupBounds,
                            replacedLegacyPixels));
                        var path = Path.Combine(affiliationDirectory, slot.Name + ".png");
                        ValidateOutput(normalized, slot, slotMatte, minimumOpaquePixels: 5_000);
                        normalized.Save(path, ImageFormat.Png);
                        writtenPaths.Add(path);
                        break;
                    }
                    case AssetKind.Type:
                    {
                        using var normalized = PlaceTypeFrame(content, CardWidth, CardHeight);
                        var typePath = Path.Combine(typeDirectory, slot.Name + ".png");
                        RemoveResidualLowAlphaMatteSpill(normalized);
                        RemoveIsolatedNoise(normalized, slotMatte);
                        RemoveSmallOpaqueComponents(normalized, PlaqueBounds);
                        ReplaceTypePlaque(normalized, plaqueSet[slot.Name]);
                        RemoveSmallOpaqueComponents(normalized, PlaqueBounds);
                        ValidateCompositedPlaque(normalized, plaqueSet, slot);
                        ValidateOutput(normalized, slot, slotMatte, minimumOpaquePixels: 1_000);
                        normalized.Save(typePath, ImageFormat.Png);
                        writtenPaths.Add(typePath);

                        using var mask = CreateLargestEnclosedTransparencyMask(normalized);
                        var maskPath = Path.Combine(maskDirectory, slot.Name + ".png");
                        ValidateMask(mask, slot);
                        mask.Save(maskPath, ImageFormat.Png);
                        writtenPaths.Add(maskPath);
                        break;
                    }
                    case AssetKind.Cost:
                    {
                        using var normalized = NormalizeCostBase(content, CostBaseSize);
                        var path = Path.Combine(costDirectory, slot.Name + ".png");
                        RemoveResidualLowAlphaMatteSpill(normalized);
                        RemoveIsolatedNoise(normalized, slotMatte);
                        RemoveSmallOpaqueComponents(normalized);
                        ValidateOutput(normalized, slot, slotMatte, minimumOpaquePixels: 500);
                        normalized.Save(path, ImageFormat.Png);
                        writtenPaths.Add(path);
                        break;
                    }
                    default:
                        throw new ArgumentOutOfRangeException(nameof(slot.Kind));
                }
            }

            var maskDifference = ValidateTypeMaskDistinctness(
                maskDirectory,
                enforceMinimum: typeFrameSet is not null);
            ValidateWrittenOutputs(writtenPaths);
            var cleanupSummary = ValidateAffiliationArtworkCleanup(
                affiliationDirectory,
                affiliationCleanupContracts);
            var artworkCoveragePixels = ValidateAffiliationArtworkBackingCoverage(
                affiliationDirectory,
                maskDirectory);
            CommitStagedOutputs(writtenPaths, stagingRoot, finalOutputRoot);
            return $"atlas={cleanedAtlasPath}; matte=#{matte.R:X2}{matte.G:X2}{matte.B:X2}; " +
                   $"grid={AtlasColumns}x{AtlasRows}; rowCuts={string.Join('/', rowBoundaries)}; " +
                   $"columnCuts={string.Join('/', columnBoundaries)}; " +
                   $"plaques={plaqueSet.CleanedAtlasPath}; typeFrames={typeFrameSet?.CleanedAtlasPath ?? "main-atlas"}; " +
                   $"plaqueBounds={PlaqueBounds.Left},{PlaqueBounds.Top}..{PlaqueBounds.Right - 1},{PlaqueBounds.Bottom - 1}; labelBaseline={plaqueSet.LabelBaseline}; " +
                   $"maskDifference={maskDifference.MinimumPair}:{maskDifference.MinimumRatio:P2}..{maskDifference.MaximumPair}:{maskDifference.MaximumRatio:P2}; " +
                   $"affiliationArtworkBacking=name-band-mirror/{cleanupSummary.MinimumBackingColorCount}+-colors; " +
                   $"artworkBoundaryDelta={cleanupSummary.MaximumBoundaryDelta:F2}@{cleanupSummary.MaximumBoundaryLocation}; " +
                   $"legacyArtworkCleanup={cleanupSummary.CleanupPixels}px/{cleanupSummary.ReplacedLegacyPixels}px-replaced; " +
                   $"opaqueArtworkCoverage={artworkCoveragePixels}px/6-affiliations; " +
                   $"outputs={writtenPaths.Count}; committed=24 " +
                   "(affiliations=6, types=6, masks=6, costs=6)";
        }
        finally
        {
            DeleteStagingDirectory(stagingRoot, finalOutputRoot);
        }
    }

    private static void ValidateSlotManifest()
    {
        if (Slots.Length != AtlasColumns * AtlasRows)
        {
            throw new InvalidOperationException(
                $"Slot manifest must contain {AtlasColumns * AtlasRows} entries, but contains {Slots.Length}.");
        }

        var duplicateCoordinate = Slots
            .GroupBy(slot => (slot.Row, slot.Column))
            .FirstOrDefault(group => group.Count() != 1);
        if (duplicateCoordinate is not null)
        {
            throw new InvalidOperationException($"Atlas slot {duplicateCoordinate.Key} is duplicated.");
        }

        foreach (var (kind, expectedCount) in new[]
                 {
                     (AssetKind.Affiliation, 6),
                     (AssetKind.Type, 6),
                     (AssetKind.Cost, 6),
                     (AssetKind.Empty, 2),
                 })
        {
            var actualCount = Slots.Count(slot => slot.Kind == kind);
            if (actualCount != expectedCount)
            {
                throw new InvalidOperationException(
                    $"Expected {expectedCount} {kind} slots, but found {actualCount}.");
            }
        }

        var duplicateOutput = Slots
            .Where(slot => slot.Kind != AssetKind.Empty)
            .GroupBy(slot => (slot.Kind, slot.Name), StringTupleComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicateOutput is not null)
        {
            throw new InvalidOperationException(
                $"Duplicate output mapping for {duplicateOutput.Key.Kind}/{duplicateOutput.Key.Name}.");
        }
    }

    private static void ValidateAtlasDimensions(Bitmap source)
    {
        if (source.Width < AtlasColumns * 96 || source.Height < AtlasRows * 96)
        {
            throw new InvalidOperationException(
                $"Atlas is too small ({source.Width}x{source.Height}). " +
                $"A {AtlasColumns}x{AtlasRows} atlas needs cells of at least 96x96 pixels.");
        }
    }

    private static Bitmap RemoveFlatChromaMatte(Bitmap source, out Color matte)
    {
        matte = EstimateMatteFromCorners(source);
        ValidateMatte(matte);
        var matteColor = matte;

        var width = source.Width;
        var height = source.Height;
        var result = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(result))
        {
            graphics.CompositingMode = CompositingMode.SourceCopy;
            graphics.DrawImageUnscaled(source, 0, 0);
        }

        var connected = new bool[width * height];
        var edgeDepth = new byte[width * height];
        var localMatteArgb = new int[width * height];
        Array.Fill(edgeDepth, byte.MaxValue);
        var queue = new Queue<int>();

        void Seed(int x, int y)
        {
            var index = y * width + x;
            if (connected[index])
            {
                return;
            }

            var color = source.GetPixel(x, y);
            if (color.A == 0 || IsMatteCore(color, matteColor))
            {
                connected[index] = true;
                edgeDepth[index] = 0;
                localMatteArgb[index] = color.A == 0 ? matteColor.ToArgb() : color.ToArgb();
                queue.Enqueue(index);
            }
        }

        for (var x = 0; x < width; x++)
        {
            Seed(x, 0);
            Seed(x, height - 1);
        }

        for (var y = 1; y < height - 1; y++)
        {
            Seed(0, y);
            Seed(width - 1, y);
        }

        if (queue.Count == 0)
        {
            throw new InvalidOperationException(
                "The atlas border does not contain the flat chroma matte. " +
                "Export it on one continuous opaque #00FFFF background with generous gutters.");
        }

        var directions = new[] { (-1, 0), (1, 0), (0, -1), (0, 1) };

        void FloodConnectedMatte()
        {
            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                var currentX = current % width;
                var currentY = current / width;
                var currentDepth = edgeDepth[current];

                foreach (var (dx, dy) in directions)
                {
                    var nextX = currentX + dx;
                    var nextY = currentY + dy;
                    if (nextX < 0 || nextY < 0 || nextX >= width || nextY >= height)
                    {
                        continue;
                    }

                    var next = nextY * width + nextX;
                    if (connected[next])
                    {
                        continue;
                    }

                    var color = source.GetPixel(nextX, nextY);
                    var isCore = color.A == 0 || IsMatteCore(color, matteColor);
                    var keyAmount = ComputeChromaKeyAmount(color, matteColor);
                    var nextDepth = isCore ? 0 : currentDepth + 1;

                    // Follow blended matte only a few pixels into a contour. This removes
                    // antialias spill without treating legitimate navy/green as background.
                    if (!isCore && (currentDepth >= 4 || keyAmount < 0.08))
                    {
                        continue;
                    }

                    connected[next] = true;
                    edgeDepth[next] = (byte)Math.Min(nextDepth, byte.MaxValue);
                    var exactLocalMatte = color.A > 0 && ColorDistance(color, matteColor) <= 72;
                    localMatteArgb[next] = exactLocalMatte
                        ? color.ToArgb()
                        : localMatteArgb[current] != 0
                            ? localMatteArgb[current]
                            : matteColor.ToArgb();
                    queue.Enqueue(next);
                }
            }
        }

        FloodConnectedMatte();
        SeedLargeEnclosedMatteRegions(
            source,
            matteColor,
            connected,
            edgeDepth,
            localMatteArgb,
            queue);
        FloodConnectedMatte();

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var index = y * width + x;
                if (!connected[index])
                {
                    continue;
                }

                var sourceColor = source.GetPixel(x, y);
                var localMatte = localMatteArgb[index] == 0
                    ? matteColor
                    : Color.FromArgb(localMatteArgb[index]);
                if (sourceColor.A == 0)
                {
                    result.SetPixel(x, y, TransparentPixel);
                    continue;
                }

                var distance = ColorDistance(sourceColor, localMatte);
                var distanceKey = Clamp01(1.0 - distance / 150.0);
                var keyAmount = Math.Max(ComputeChromaKeyAmount(sourceColor, localMatte), distanceKey);
                if (keyAmount >= 0.985)
                {
                    result.SetPixel(x, y, TransparentPixel);
                    continue;
                }

                var alphaFraction = 1.0 - keyAmount;
                var alpha = ClampByte(sourceColor.A * alphaFraction);
                // Generated contact sheets often contain barely-visible one-pixel matte
                // remnants. At card scale they read as white/cyan dust, so discard the
                // lowest-opacity tail and keep the stronger antialias coverage only.
                if (alpha <= 64)
                {
                    result.SetPixel(x, y, TransparentPixel);
                    continue;
                }

                // Undo C = alpha*F + (1-alpha)*matte, removing cyan edge spill.
                var red = UnmixChannel(sourceColor.R, localMatte.R, keyAmount, alphaFraction);
                var green = UnmixChannel(sourceColor.G, localMatte.G, keyAmount, alphaFraction);
                var blue = UnmixChannel(sourceColor.B, localMatte.B, keyAmount, alphaFraction);
                var residualGreen = green >= Math.Max(red, blue) + 18;
                var residualBlue = blue >= Math.Max(red, green) + 24;
                var residualCyan = Math.Min(green, blue) >= red + 22;
                if (alpha <= 160 && (residualGreen || residualBlue || residualCyan))
                {
                    result.SetPixel(x, y, TransparentPixel);
                    continue;
                }

                result.SetPixel(x, y, Color.FromArgb(alpha, red, green, blue));
            }
        }

        RemoveResidualLowAlphaMatteSpill(result);

        return result;
    }

    private static void RemoveResidualLowAlphaMatteSpill(Bitmap bitmap)
    {
        // Some assembled atlases already contain antialiased source pixels. They are not
        // reachable through the opaque matte flood, but cyan compositing commonly leaves a
        // green-only residue once the blue component has been removed. Restrict this cleanup
        // to translucent pixels so opaque class colors (notably Tactician green) stay intact.
        for (var y = 0; y < bitmap.Height; y++)
        {
            for (var x = 0; x < bitmap.Width; x++)
            {
                var color = bitmap.GetPixel(x, y);
                if (color.A == 0 || color.A > 200)
                {
                    continue;
                }

                var residualGreen = color.G >= Math.Max(color.R, color.B) + 18;
                var residualCyan = Math.Min(color.G, color.B) >= color.R + 22;
                if (residualGreen || residualCyan)
                {
                    bitmap.SetPixel(x, y, TransparentPixel);
                }
            }
        }
    }

    private static Color EstimateMatteFromCorners(Bitmap source)
    {
        const int patchSize = 11;
        var colors = new List<Color>(patchSize * patchSize * 4);
        var origins = new[]
        {
            (0, 0),
            (Math.Max(0, source.Width - patchSize), 0),
            (0, Math.Max(0, source.Height - patchSize)),
            (Math.Max(0, source.Width - patchSize), Math.Max(0, source.Height - patchSize)),
        };

        foreach (var (originX, originY) in origins)
        {
            for (var y = originY; y < Math.Min(source.Height, originY + patchSize); y++)
            {
                for (var x = originX; x < Math.Min(source.Width, originX + patchSize); x++)
                {
                    var color = source.GetPixel(x, y);
                    if (color.A > 0)
                    {
                        colors.Add(color);
                    }
                }
            }
        }

        if (colors.Count == 0)
        {
            return Color.FromArgb(0, 255, 255);
        }

        static byte Median(IEnumerable<byte> values)
        {
            var ordered = values.OrderBy(value => value).ToArray();
            return ordered[ordered.Length / 2];
        }

        return Color.FromArgb(
            Median(colors.Select(color => color.R)),
            Median(colors.Select(color => color.G)),
            Median(colors.Select(color => color.B)));
    }

    private static void SeedLargeEnclosedMatteRegions(
        Bitmap source,
        Color matte,
        bool[] connected,
        byte[] edgeDepth,
        int[] localMatteArgb,
        Queue<int> outputQueue)
    {
        // Closed frame apertures are intentionally filled by the same flat matte but cannot
        // be reached from the atlas edge. Remove only sizeable, uniform matte wells; tiny
        // cyan details are left alone and isolated non-matte whites/silvers are never keyed.
        const int minimumRegionPixels = 64;
        var width = source.Width;
        var height = source.Height;
        var visited = new bool[width * height];
        var regionQueue = new Queue<int>();
        var directions = new[] { (-1, 0), (1, 0), (0, -1), (0, 1) };

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var start = y * width + x;
                if (visited[start] || connected[start])
                {
                    visited[start] = true;
                    continue;
                }

                visited[start] = true;
                var startColor = source.GetPixel(x, y);
                if (startColor.A > 0 && !IsMatteCore(startColor, matte))
                {
                    continue;
                }

                var region = new List<int>();
                regionQueue.Enqueue(start);
                while (regionQueue.Count > 0)
                {
                    var current = regionQueue.Dequeue();
                    region.Add(current);
                    var currentX = current % width;
                    var currentY = current / width;
                    foreach (var (dx, dy) in directions)
                    {
                        var nextX = currentX + dx;
                        var nextY = currentY + dy;
                        if (nextX < 0 || nextY < 0 || nextX >= width || nextY >= height)
                        {
                            continue;
                        }

                        var next = nextY * width + nextX;
                        if (visited[next] || connected[next])
                        {
                            visited[next] = true;
                            continue;
                        }

                        var color = source.GetPixel(nextX, nextY);
                        if (color.A == 0 || IsMatteCore(color, matte))
                        {
                            visited[next] = true;
                            regionQueue.Enqueue(next);
                        }
                    }
                }

                if (region.Count < minimumRegionPixels)
                {
                    continue;
                }

                foreach (var pixel in region)
                {
                    connected[pixel] = true;
                    edgeDepth[pixel] = 0;
                    var color = source.GetPixel(pixel % width, pixel / width);
                    localMatteArgb[pixel] = color.A > 0 && ColorDistance(color, matte) <= 72
                        ? color.ToArgb()
                        : matte.ToArgb();
                    outputQueue.Enqueue(pixel);
                }
            }
        }
    }

    private static void ValidateMatte(Color matte)
    {
        var expected = Color.FromArgb(0, 255, 255);
        if (ColorDistance(matte, expected) > 120 ||
            matte.R > 120 || matte.G < 150 || matte.B < 150 || Math.Abs(matte.G - matte.B) > 70)
        {
            throw new InvalidOperationException(
                $"Corner matte #{matte.R:X2}{matte.G:X2}{matte.B:X2} is not a safe cyan chroma matte. " +
                "Regenerate on one uniform opaque #00FFFF background; do not use a checkerboard.");
        }
    }

    private static bool IsMatteCore(Color color, Color matte) =>
        ColorDistance(color, matte) <= 72 || ComputeChromaKeyAmount(color, matte) >= 0.82;

    private static double ComputeChromaKeyAmount(Color color, Color matte)
    {
        if (color.A == 0)
        {
            return 1.0;
        }

        var greenRange = Math.Max(1, matte.G - matte.R);
        var blueRange = Math.Max(1, matte.B - matte.R);
        var greenExcess = (color.G - color.R) / (double)greenRange;
        var blueExcess = (color.B - color.R) / (double)blueRange;
        return Clamp01(Math.Min(greenExcess, blueExcess));
    }

    private static double ColorDistance(Color left, Color right)
    {
        var red = left.R - right.R;
        var green = left.G - right.G;
        var blue = left.B - right.B;
        return Math.Sqrt(red * red + green * green + blue * blue);
    }

    private static byte UnmixChannel(byte color, byte matte, double keyAmount, double alphaFraction) =>
        ClampByte((color - keyAmount * matte) / Math.Max(0.001, alphaFraction));

    private static byte ClampByte(double value) => (byte)Math.Clamp((int)Math.Round(value), 0, 255);

    private static double Clamp01(double value) => Math.Clamp(value, 0.0, 1.0);

    private sealed record LabelGeometry(int Baseline, double CenterX, Rectangle Bounds);

    private static TypeFrameSet LoadTypeFrameSet(string typeFrameAtlasPath)
    {
        using var source = new Bitmap(typeFrameAtlasPath);
        if (source.Width < 3 * 96 || source.Height < 2 * 96)
        {
            throw new InvalidOperationException(
                $"Type-frame atlas is too small ({source.Width}x{source.Height}); expected a 3x2 contact sheet.");
        }

        using var cleaned = RemoveFlatChromaMatte(source, out var matte);
        var cleanedPath = Path.Combine(
            Path.GetDirectoryName(typeFrameAtlasPath) ?? ".",
            Path.GetFileNameWithoutExtension(typeFrameAtlasPath) + "-clean.png");
        cleaned.Save(cleanedPath, ImageFormat.Png);

        var frames = new Dictionary<string, Bitmap>(StringComparer.OrdinalIgnoreCase);
        try
        {
            for (var index = 0; index < PlaqueNames.Length; index++)
            {
                var row = index / 3;
                var column = index % 3;
                var name = PlaqueNames[index];
                using var cell = CropThreeByTwoCell(cleaned, row, column);
                RemoveIsolatedNoise(cell, matte);
                var slot = new AtlasSlot(row, column, AssetKind.Type, name, $"type override {name}");
                var content = CropStableContent(cell, slot);
                frames.Add(name, content);
            }

            return new TypeFrameSet
            {
                Frames = frames,
                Matte = matte,
                CleanedAtlasPath = cleanedPath,
            };
        }
        catch
        {
            foreach (var frame in frames.Values)
            {
                frame.Dispose();
            }

            throw;
        }
    }

    private static PlaqueSet LoadPlaqueSet(string plaqueAtlasPath)
    {
        using var source = new Bitmap(plaqueAtlasPath);
        if (source.Width < 3 * 96 || source.Height < 2 * 96)
        {
            throw new InvalidOperationException(
                $"Plaque atlas is too small ({source.Width}x{source.Height}); expected a 3x2 contact sheet.");
        }

        using var cleaned = RemoveFlatChromaMatte(source, out var matte);
        var cleanedPath = Path.Combine(
            Path.GetDirectoryName(plaqueAtlasPath) ?? ".",
            Path.GetFileNameWithoutExtension(plaqueAtlasPath) + "-clean.png");
        cleaned.Save(cleanedPath, ImageFormat.Png);

        var normalizedPlaques = new Dictionary<string, Bitmap>(StringComparer.OrdinalIgnoreCase);
        try
        {
            for (var index = 0; index < PlaqueNames.Length; index++)
            {
                var row = index / 3;
                var column = index % 3;
                var name = PlaqueNames[index];
                using var cell = CropThreeByTwoCell(cleaned, row, column);
                RemoveIsolatedNoise(cell, matte);
                var slot = new AtlasSlot(row, column, AssetKind.Type, name, $"plaque {name}");
                using var content = CropStableContent(cell, slot);
                using var tight = TrimOpaqueBounds(content, 12);
                var normalized = ResizeExact(tight, PlaqueWidth, PlaqueHeight);
                RemoveResidualLowAlphaMatteSpill(normalized);
                RemoveIsolatedNoise(normalized, matte);
                normalizedPlaques.Add(name, normalized);
            }

            var skill = normalizedPlaques["skill"];
            var canonicalAlpha = ReadAlpha(skill);
            var canonicalBounds = GetAlphaBounds(skill, 0);
            var skillLabel = DetectLabelGeometry(skill, "skill");
            using var canonicalRgb = new Bitmap(skill);

            foreach (var name in PlaqueNames)
            {
                var sourcePlaque = normalizedPlaques[name];
                var label = DetectLabelGeometry(sourcePlaque, name);
                var shiftX = (int)Math.Round(skillLabel.CenterX - label.CenterX);
                var shiftY = skillLabel.Baseline - label.Baseline;
                if (Math.Abs(shiftX) > 3 || Math.Abs(shiftY) > 3)
                {
                    throw new InvalidOperationException(
                        $"Plaque {name} label alignment differs excessively from SKILL " +
                        $"(shift {shiftX},{shiftY}). Regenerate with a shared center/baseline.");
                }

                var conformed = ApplyCanonicalAlpha(
                    sourcePlaque,
                    canonicalRgb,
                    canonicalAlpha,
                    shiftX,
                    shiftY);
                sourcePlaque.Dispose();
                normalizedPlaques[name] = conformed;
            }

            ValidatePlaqueSet(normalizedPlaques, canonicalAlpha, canonicalBounds, skillLabel.Baseline);
            return new PlaqueSet
            {
                Plaques = normalizedPlaques,
                CanonicalAlpha = canonicalAlpha,
                CanonicalAlphaBounds = canonicalBounds,
                LabelBaseline = skillLabel.Baseline,
                CleanedAtlasPath = cleanedPath,
            };
        }
        catch
        {
            foreach (var plaque in normalizedPlaques.Values)
            {
                plaque.Dispose();
            }

            throw;
        }
    }

    private static Bitmap CropThreeByTwoCell(Bitmap source, int row, int column)
    {
        var left = column * source.Width / 3;
        var right = (column + 1) * source.Width / 3;
        var top = row * source.Height / 2;
        var bottom = (row + 1) * source.Height / 2;
        return CropRectangle(source, new Rectangle(left, top, right - left, bottom - top));
    }

    private static Bitmap TrimOpaqueBounds(Bitmap source, byte alphaThreshold)
    {
        var bounds = GetAlphaBounds(source, alphaThreshold);
        if (bounds.IsEmpty)
        {
            throw new InvalidOperationException("Cannot trim an image with no visible pixels.");
        }

        return CropRectangle(source, bounds);
    }

    private static byte[] ReadAlpha(Bitmap bitmap)
    {
        var alpha = new byte[bitmap.Width * bitmap.Height];
        for (var y = 0; y < bitmap.Height; y++)
        {
            for (var x = 0; x < bitmap.Width; x++)
            {
                alpha[y * bitmap.Width + x] = bitmap.GetPixel(x, y).A;
            }
        }

        return alpha;
    }

    private static int[] ReadArgb(Bitmap bitmap)
    {
        var argb = new int[bitmap.Width * bitmap.Height];
        for (var y = 0; y < bitmap.Height; y++)
        {
            for (var x = 0; x < bitmap.Width; x++)
            {
                argb[y * bitmap.Width + x] = bitmap.GetPixel(x, y).ToArgb();
            }
        }

        return argb;
    }

    private static Rectangle GetAlphaBounds(Bitmap bitmap, byte alphaThreshold)
    {
        var left = bitmap.Width;
        var top = bitmap.Height;
        var right = -1;
        var bottom = -1;
        for (var y = 0; y < bitmap.Height; y++)
        {
            for (var x = 0; x < bitmap.Width; x++)
            {
                if (bitmap.GetPixel(x, y).A <= alphaThreshold)
                {
                    continue;
                }

                left = Math.Min(left, x);
                top = Math.Min(top, y);
                right = Math.Max(right, x);
                bottom = Math.Max(bottom, y);
            }
        }

        return right < left
            ? Rectangle.Empty
            : Rectangle.FromLTRB(left, top, right + 1, bottom + 1);
    }

    private static LabelGeometry DetectLabelGeometry(Bitmap plaque, string name)
    {
        var left = plaque.Width;
        var top = plaque.Height;
        var right = -1;
        var bottom = -1;
        const int roiLeft = 24;
        const int roiRight = PlaqueWidth - 24;
        const int roiTop = 13;
        const int roiBottom = PlaqueHeight - 12;

        for (var y = roiTop; y < roiBottom; y++)
        {
            for (var x = roiLeft; x < roiRight; x++)
            {
                var color = plaque.GetPixel(x, y);
                var maximum = Math.Max(color.R, Math.Max(color.G, color.B));
                var brightness = color.R + color.G + color.B;
                if (color.A < 96 || maximum < 145 || brightness < 310)
                {
                    continue;
                }

                left = Math.Min(left, x);
                top = Math.Min(top, y);
                right = Math.Max(right, x);
                bottom = Math.Max(bottom, y);
            }
        }

        if (right < left)
        {
            throw new InvalidOperationException($"Could not detect the visible label in plaque {name}.");
        }

        var bounds = Rectangle.FromLTRB(left, top, right + 1, bottom + 1);
        return new LabelGeometry(bottom, (left + right) / 2.0, bounds);
    }

    private static Bitmap ApplyCanonicalAlpha(
        Bitmap source,
        Bitmap canonical,
        IReadOnlyList<byte> canonicalAlpha,
        int shiftX,
        int shiftY)
    {
        var result = new Bitmap(PlaqueWidth, PlaqueHeight, PixelFormat.Format32bppArgb);
        for (var y = 0; y < PlaqueHeight; y++)
        {
            for (var x = 0; x < PlaqueWidth; x++)
            {
                var alpha = canonicalAlpha[y * PlaqueWidth + x];
                if (alpha == 0)
                {
                    result.SetPixel(x, y, TransparentPixel);
                    continue;
                }

                var sourceX = x - shiftX;
                var sourceY = y - shiftY;
                var color = sourceX >= 0 && sourceY >= 0 &&
                            sourceX < source.Width && sourceY < source.Height
                    ? source.GetPixel(sourceX, sourceY)
                    : TransparentPixel;
                if (color.A == 0)
                {
                    color = canonical.GetPixel(x, y);
                }

                var residualGreen = color.G >= Math.Max(color.R, color.B) + 18;
                var residualCyan = Math.Min(color.G, color.B) >= color.R + 22;
                if (alpha <= 200 && (residualGreen || residualCyan))
                {
                    // Keep the canonical silhouette and neutralize only matte-polluted RGB.
                    // Falling back to canonical RGB is insufficient when the SKILL source
                    // carries the same subpixel spill at that coordinate.
                    var neutral = color.R;
                    color = Color.FromArgb(color.A, neutral, neutral, neutral);
                }

                result.SetPixel(x, y, Color.FromArgb(alpha, color.R, color.G, color.B));
            }
        }

        return result;
    }

    private static void ValidatePlaqueSet(
        IReadOnlyDictionary<string, Bitmap> plaques,
        IReadOnlyList<byte> canonicalAlpha,
        Rectangle canonicalBounds,
        int canonicalBaseline)
    {
        foreach (var name in PlaqueNames)
        {
            var plaque = plaques[name];
            if (plaque.Width != PlaqueWidth || plaque.Height != PlaqueHeight)
            {
                throw new InvalidOperationException(
                    $"Plaque {name} must be {PlaqueWidth}x{PlaqueHeight}, found {plaque.Width}x{plaque.Height}.");
            }

            var bounds = GetAlphaBounds(plaque, 0);
            if (bounds != canonicalBounds)
            {
                throw new InvalidOperationException(
                    $"Plaque {name} alpha bounds {bounds} differ from canonical SKILL bounds {canonicalBounds}.");
            }

            var alpha = ReadAlpha(plaque);
            if (!alpha.SequenceEqual(canonicalAlpha))
            {
                throw new InvalidOperationException($"Plaque {name} alpha silhouette differs from canonical SKILL.");
            }

            var label = DetectLabelGeometry(plaque, name);
            if (label.Baseline != canonicalBaseline || Math.Abs(label.CenterX - 150.5) > 2.0)
            {
                throw new InvalidOperationException(
                    $"Plaque {name} label is not aligned: baseline={label.Baseline}, center={label.CenterX:0.0}; " +
                    $"expected baseline={canonicalBaseline}, center~=150.5.");
            }
        }
    }

    private static void ReplaceTypePlaque(Bitmap typeFrame, Bitmap plaque)
    {
        ClearRectangle(typeFrame, OldPlaqueCrownRemovalBounds);
        ClearRectangle(typeFrame, OldPlaqueRemovalBounds);

        // SetPixel avoids GDI+'s premultiplied-alpha color conversion. Exact alpha/RGB is
        // important because all six silhouettes are compared byte-for-byte.
        for (var y = 0; y < PlaqueHeight; y++)
        {
            for (var x = 0; x < PlaqueWidth; x++)
            {
                typeFrame.SetPixel(PlaqueBounds.Left + x, PlaqueBounds.Top + y, plaque.GetPixel(x, y));
            }
        }
    }

    private static void ClearRectangle(Bitmap bitmap, Rectangle rectangle)
    {
        for (var y = rectangle.Top; y < rectangle.Bottom; y++)
        {
            for (var x = rectangle.Left; x < rectangle.Right; x++)
            {
                bitmap.SetPixel(x, y, TransparentPixel);
            }
        }
    }

    private static void ValidateCompositedPlaque(Bitmap typeFrame, PlaqueSet plaques, AtlasSlot slot)
    {
        for (var y = 0; y < PlaqueHeight; y++)
        {
            for (var x = 0; x < PlaqueWidth; x++)
            {
                var actual = typeFrame.GetPixel(PlaqueBounds.Left + x, PlaqueBounds.Top + y).A;
                var expected = plaques.CanonicalAlpha[y * PlaqueWidth + x];
                if (actual != expected)
                {
                    throw new InvalidOperationException(
                        $"Composited plaque silhouette for {slot.Description} differs at {x},{y}: " +
                        $"alpha {actual}, expected {expected}.");
                }
            }
        }

        var actualBounds = GetAlphaBoundsInRegion(typeFrame, PlaqueBounds, 0);
        var expectedBounds = new Rectangle(
            PlaqueBounds.Left + plaques.CanonicalAlphaBounds.Left,
            PlaqueBounds.Top + plaques.CanonicalAlphaBounds.Top,
            plaques.CanonicalAlphaBounds.Width,
            plaques.CanonicalAlphaBounds.Height);
        if (actualBounds != expectedBounds)
        {
            throw new InvalidOperationException(
                $"Composited plaque bounds for {slot.Description} are {actualBounds}; expected {expectedBounds}.");
        }
    }

    private static Rectangle GetAlphaBoundsInRegion(Bitmap bitmap, Rectangle region, byte alphaThreshold)
    {
        var left = region.Right;
        var top = region.Bottom;
        var right = -1;
        var bottom = -1;
        for (var y = region.Top; y < region.Bottom; y++)
        {
            for (var x = region.Left; x < region.Right; x++)
            {
                if (bitmap.GetPixel(x, y).A <= alphaThreshold)
                {
                    continue;
                }

                left = Math.Min(left, x);
                top = Math.Min(top, y);
                right = Math.Max(right, x);
                bottom = Math.Max(bottom, y);
            }
        }

        return right < left ? Rectangle.Empty : Rectangle.FromLTRB(left, top, right + 1, bottom + 1);
    }

    private sealed record BlankRun(int Start, int End)
    {
        public int Center => (Start + End) / 2;
        public int Length => End - Start + 1;
    }

    private static int[] FindRowBoundaries(Bitmap source)
    {
        var maximumNoisePixels = Math.Max(8, source.Width / 160);
        var minimumGapHeight = Math.Max(5, source.Height / 250);
        var runs = new List<BlankRun>();
        int? runStart = null;

        for (var y = 0; y < source.Height; y++)
        {
            var opaque = 0;
            for (var x = 0; x < source.Width; x++)
            {
                if (source.GetPixel(x, y).A > 12)
                {
                    opaque++;
                }
            }

            if (opaque <= maximumNoisePixels)
            {
                runStart ??= y;
                continue;
            }

            if (runStart is not null)
            {
                var run = new BlankRun(runStart.Value, y - 1);
                if (run.Start > 0 && run.Length >= minimumGapHeight)
                {
                    runs.Add(run);
                }

                runStart = null;
            }
        }

        var targets = new[] { 0.34, 0.60, 0.785 };
        var selected = new List<BlankRun>();
        foreach (var target in targets)
        {
            var expectedY = source.Height * target;
            var candidate = runs
                .Where(run => selected.All(chosen => chosen != run))
                .OrderBy(run => Math.Abs(run.Center - expectedY))
                .ThenByDescending(run => run.Length)
                .FirstOrDefault();
            if (candidate is null || Math.Abs(candidate.Center - expectedY) > source.Height * 0.11)
            {
                var found = string.Join(", ", runs.Select(run => $"{run.Start}-{run.End}"));
                throw new InvalidOperationException(
                    $"Could not find the atlas row gutter near y={expectedY:0}. Blank runs: [{found}]. " +
                    "Keep a continuous cyan gutter between all four atlas rows.");
            }

            selected.Add(candidate);
        }

        selected.Sort((left, right) => left.Center.CompareTo(right.Center));
        if (selected.Zip(selected.Skip(1), (left, right) => left.End < right.Start).Any(valid => !valid))
        {
            throw new InvalidOperationException("Detected atlas row gutters overlap or are out of order.");
        }

        return
        [
            0,
            (selected[0].Start + selected[0].End) / 2,
            (selected[1].Start + selected[1].End) / 2,
            (selected[2].Start + selected[2].End) / 2,
            source.Height,
        ];
    }

    private static int[] FindColumnBoundaries(Bitmap source)
    {
        var maximumNoisePixels = Math.Max(8, source.Height / 160);
        var minimumGapWidth = Math.Max(5, source.Width / 250);
        var runs = new List<BlankRun>();
        int? runStart = null;

        for (var x = 0; x < source.Width; x++)
        {
            var opaque = 0;
            for (var y = 0; y < source.Height; y++)
            {
                if (source.GetPixel(x, y).A > 12)
                {
                    opaque++;
                }
            }

            if (opaque <= maximumNoisePixels)
            {
                runStart ??= x;
                continue;
            }

            if (runStart is not null)
            {
                var run = new BlankRun(runStart.Value, x - 1);
                if (run.Start > 0 && run.Length >= minimumGapWidth)
                {
                    runs.Add(run);
                }

                runStart = null;
            }
        }

        var targets = new[] { 0.20, 0.40, 0.60, 0.80 };
        var selected = new List<BlankRun>();
        foreach (var target in targets)
        {
            var expectedX = source.Width * target;
            var candidate = runs
                .Where(run => selected.All(chosen => chosen != run))
                .OrderBy(run => Math.Abs(run.Center - expectedX))
                .ThenByDescending(run => run.Length)
                .FirstOrDefault();
            if (candidate is null || Math.Abs(candidate.Center - expectedX) > source.Width * 0.08)
            {
                var found = string.Join(", ", runs.Select(run => $"{run.Start}-{run.End}"));
                throw new InvalidOperationException(
                    $"Could not find the atlas column gutter near x={expectedX:0}. Blank runs: [{found}]. " +
                    "Keep a continuous cyan gutter between all five atlas columns.");
            }

            selected.Add(candidate);
        }

        selected.Sort((left, right) => left.Center.CompareTo(right.Center));
        return
        [
            0,
            selected[0].Center,
            selected[1].Center,
            selected[2].Center,
            selected[3].Center,
            source.Width,
        ];
    }

    private static Bitmap CropAtlasCell(
        Bitmap source,
        int row,
        int column,
        IReadOnlyList<int> rowBoundaries,
        IReadOnlyList<int> columnBoundaries)
    {
        var left = columnBoundaries[column];
        var right = columnBoundaries[column + 1];
        var top = rowBoundaries[row];
        var bottom = rowBoundaries[row + 1];
        return CropRectangle(source, new Rectangle(left, top, right - left, bottom - top));
    }

    private static Bitmap CropStableContent(Bitmap cell, AtlasSlot slot)
    {
        var components = FindOpaqueComponents(cell, alphaThreshold: 12)
            .Where(component => component.Count >= MinimumComponentPixels)
            .OrderByDescending(component => component.Count)
            .ToList();
        if (components.Count == 0)
        {
            throw new InvalidOperationException($"Atlas cell {slot.Row + 1},{slot.Column + 1} ({slot.Description}) is empty.");
        }

        var cellEdgeComponents = components
            .Where(component => component.Left == 0 || component.Right == cell.Width - 1)
            .ToArray();
        if (cellEdgeComponents.Length > 0)
        {
            var details = string.Join(", ", cellEdgeComponents.Select(component =>
                $"x={component.Left}..{component.Right}, pixels={component.Count}"));
            throw new InvalidOperationException(
                $"Atlas cell {slot.Row + 1},{slot.Column + 1} ({slot.Description}) contains a component " +
                $"touching a vertical cell edge ({details}). Keep each sprite inside its fixed column gutter.");
        }

        var largest = components[0];
        var proximity = Math.Max(8, Math.Min(cell.Width, cell.Height) / 24);
        var included = components
            .Where(component =>
                ReferenceEquals(component, largest) ||
                largest.Contains(component.CenterX, component.CenterY, proximity))
            .ToList();

        var left = included.Min(component => component.Left);
        var top = included.Min(component => component.Top);
        var right = included.Max(component => component.Right);
        var bottom = included.Max(component => component.Bottom);
        const int padding = 3;
        var rectangle = Rectangle.FromLTRB(
            Math.Max(0, left - padding),
            Math.Max(0, top - padding),
            Math.Min(cell.Width, right + padding + 1),
            Math.Min(cell.Height, bottom + padding + 1));

        if (rectangle.Width < 16 || rectangle.Height < 16)
        {
            throw new InvalidOperationException(
                $"Atlas cell {slot.Row + 1},{slot.Column + 1} ({slot.Description}) has no usable asset bounds.");
        }

        return CropRectangle(cell, rectangle);
    }

    private static void RemoveIsolatedNoise(Bitmap bitmap, Color matte)
    {
        var components = FindOpaqueComponents(bitmap, alphaThreshold: 10);
        foreach (var component in components.Where(component => component.Count <= TinyNoisePixels))
        {
            var allNoiseColored = true;
            for (var y = component.Top; y <= component.Bottom && allNoiseColored; y++)
            {
                for (var x = component.Left; x <= component.Right; x++)
                {
                    var color = bitmap.GetPixel(x, y);
                    if (color.A <= 10)
                    {
                        continue;
                    }

                    var nearWhite = color.R >= 232 && color.G >= 232 && color.B >= 232 &&
                                    Math.Max(color.R, Math.Max(color.G, color.B)) -
                                    Math.Min(color.R, Math.Min(color.G, color.B)) <= 20;
                    var nearChroma = ColorDistance(color, matte) <= 190 ||
                                     ComputeChromaKeyAmount(color, matte) >= 0.20;
                    if (!nearWhite && !nearChroma)
                    {
                        allNoiseColored = false;
                        break;
                    }
                }
            }

            if (!allNoiseColored)
            {
                continue;
            }

            for (var y = component.Top; y <= component.Bottom; y++)
            {
                for (var x = component.Left; x <= component.Right; x++)
                {
                    if (bitmap.GetPixel(x, y).A > 10)
                    {
                        bitmap.SetPixel(x, y, TransparentPixel);
                    }
                }
            }
        }
    }

    private static void RemoveSmallOpaqueComponents(Bitmap bitmap, params Rectangle[] protectedRegions)
    {
        var width = bitmap.Width;
        var components = FindOpaqueComponents(bitmap, alphaThreshold: 0);
        foreach (var component in components.Where(component => component.Count < 20))
        {
            if (protectedRegions.Any(region =>
                    region.Contains((int)Math.Round(component.CenterX), (int)Math.Round(component.CenterY))))
            {
                continue;
            }

            foreach (var pixel in component.Pixels)
            {
                bitmap.SetPixel(pixel % width, pixel / width, TransparentPixel);
            }
        }
    }

    private static void ValidateNoSmallOpaqueComponents(
        Bitmap bitmap,
        AtlasSlot slot,
        params Rectangle[] protectedRegions)
    {
        var suspicious = FindOpaqueComponents(bitmap, alphaThreshold: 0)
            .Where(component => component.Count < 20)
            .Where(component => !protectedRegions.Any(region =>
                region.Contains((int)Math.Round(component.CenterX), (int)Math.Round(component.CenterY))))
            .ToArray();
        if (suspicious.Length == 0)
        {
            return;
        }

        var first = suspicious[0];
        throw new InvalidOperationException(
            $"Output {slot.Description} contains {suspicious.Length} isolated opaque components under 20 pixels " +
            $"(first bounds {first.Left},{first.Top}..{first.Right},{first.Bottom}, pixels={first.Count}).");
    }

    private static List<Component> FindOpaqueComponents(Bitmap bitmap, byte alphaThreshold)
    {
        var width = bitmap.Width;
        var height = bitmap.Height;
        var visited = new bool[width * height];
        var queue = new Queue<int>();
        var components = new List<Component>();
        var directions = new[]
        {
            (-1, -1), (0, -1), (1, -1),
            (-1, 0),           (1, 0),
            (-1, 1),  (0, 1),  (1, 1),
        };

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var start = y * width + x;
                if (visited[start])
                {
                    continue;
                }

                visited[start] = true;
                if (bitmap.GetPixel(x, y).A <= alphaThreshold)
                {
                    continue;
                }

                var component = new Component();
                queue.Enqueue(start);
                while (queue.Count > 0)
                {
                    var current = queue.Dequeue();
                    var currentX = current % width;
                    var currentY = current / width;
                    component.Left = Math.Min(component.Left, currentX);
                    component.Top = Math.Min(component.Top, currentY);
                    component.Right = Math.Max(component.Right, currentX);
                    component.Bottom = Math.Max(component.Bottom, currentY);
                    component.Count++;
                    component.Pixels.Add(current);

                    foreach (var (dx, dy) in directions)
                    {
                        var nextX = currentX + dx;
                        var nextY = currentY + dy;
                        if (nextX < 0 || nextY < 0 || nextX >= width || nextY >= height)
                        {
                            continue;
                        }

                        var next = nextY * width + nextX;
                        if (visited[next])
                        {
                            continue;
                        }

                        visited[next] = true;
                        if (bitmap.GetPixel(nextX, nextY).A > alphaThreshold)
                        {
                            queue.Enqueue(next);
                        }
                    }
                }

                components.Add(component);
            }
        }

        return components;
    }

    private static Bitmap CropRectangle(Bitmap source, Rectangle rectangle)
    {
        var destination = new Bitmap(rectangle.Width, rectangle.Height, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(destination);
        graphics.CompositingMode = CompositingMode.SourceCopy;
        graphics.DrawImage(source,
            new Rectangle(0, 0, destination.Width, destination.Height),
            rectangle,
            GraphicsUnit.Pixel);
        return destination;
    }

    private static Bitmap ResizeExact(Bitmap source, int width, int height)
    {
        var destination = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(destination);
        ConfigurePixelGraphics(graphics);
        graphics.DrawImage(source,
            new Rectangle(0, 0, width, height),
            new Rectangle(0, 0, source.Width, source.Height),
            GraphicsUnit.Pixel);
        return destination;
    }

    private static Bitmap NormalizeAffiliationFrame(Bitmap source, int width, int height)
    {
        var destination = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        // Generated frames put the rules-panel divider at roughly 72% of their tight crop.
        // The runtime requires it at y=500 so the panel gains a fifth readable text line.
        var sourceDivider = Math.Clamp((int)Math.Round(source.Height * 0.72), 1, source.Height - 1);
        const int destinationDivider = 500;
        using var graphics = Graphics.FromImage(destination);
        ConfigurePixelGraphics(graphics);
        graphics.DrawImage(source,
            new Rectangle(0, 0, width, destinationDivider),
            new Rectangle(0, 0, source.Width, sourceDivider),
            GraphicsUnit.Pixel);
        graphics.DrawImage(source,
            new Rectangle(0, destinationDivider, width, height - destinationDivider),
            new Rectangle(0, sourceDivider, source.Width, source.Height - sourceDivider),
            GraphicsUnit.Pixel);
        return destination;
    }

    private static Bitmap RemoveLegacyArtworkFrameAndAddOpaqueBacking(
        Bitmap frame,
        AtlasSlot slot,
        out bool[] cleanupMask,
        out Rectangle cleanupBounds,
        out int[] expectedBackingArgb,
        out int replacedLegacyPixels)
    {
        var aperture = FindLargestEnclosedTransparentComponent(frame);
        var apertureBounds = Rectangle.FromLTRB(
            aperture.Left,
            aperture.Top,
            aperture.Right + 1,
            aperture.Bottom + 1);

        if (aperture.Count < 100_000 ||
            apertureBounds.Left < 30 || apertureBounds.Top < 100 ||
            apertureBounds.Right > 530 || apertureBounds.Bottom > RulesPanelTop)
        {
            throw new InvalidOperationException(
                $"Artwork aperture for {slot.Description} is unsafe: bounds={apertureBounds}, " +
                $"transparentPixels={aperture.Count}.");
        }

        cleanupMask = DilateComponentMask(
            aperture,
            frame.Width,
            frame.Height,
            LegacyArtworkFrameCleanupHorizontalDilation,
            LegacyArtworkFrameCleanupUpwardDilation,
            LegacyArtworkFrameCleanupDownwardDilation,
            RulesPanelTop);
        cleanupBounds = GetMaskBounds(cleanupMask, frame.Width, frame.Height);
        if (cleanupBounds.Left < 30 || cleanupBounds.Top < ProtectedTitleBandBottom ||
            cleanupBounds.Right > 530 || cleanupBounds.Bottom > RulesPanelTop)
        {
            throw new InvalidOperationException(
                $"Legacy artwork-frame cleanup for {slot.Description} is unsafe: " +
                $"bounds={cleanupBounds}, dilation=" +
                $"{LegacyArtworkFrameCleanupHorizontalDilation}px-horizontal/" +
                $"{LegacyArtworkFrameCleanupUpwardDilation}px-up/" +
                $"{LegacyArtworkFrameCleanupDownwardDilation}px-down.");
        }
        ValidateArtworkBackingSample(frame, slot);

        var destination = frame.Clone(
            new Rectangle(0, 0, frame.Width, frame.Height),
            PixelFormat.Format32bppArgb);
        expectedBackingArgb = new int[cleanupMask.Length];
        replacedLegacyPixels = 0;
        for (var pixel = 0; pixel < cleanupMask.Length; pixel++)
        {
            if (!cleanupMask[pixel])
            {
                continue;
            }

            var x = pixel % frame.Width;
            var y = pixel / frame.Width;
            replacedLegacyPixels += frame.GetPixel(x, y).A > 4 ? 1 : 0;
            var backing = SampleAffiliationArtworkBacking(frame, x, y);
            expectedBackingArgb[pixel] = backing.ToArgb();
            destination.SetPixel(x, y, backing);
        }

        // A non-trivial number of opaque pixels proves that this is actually removing the
        // generated legacy rim, rather than merely filling the already-transparent aperture.
        if (replacedLegacyPixels < 3_000)
        {
            destination.Dispose();
            throw new InvalidOperationException(
                $"Legacy artwork-frame cleanup for {slot.Description} replaced only " +
                $"{replacedLegacyPixels} opaque pixels; expected the detected inner rim.");
        }

        return destination;
    }

    private static Color SampleAffiliationArtworkBacking(Bitmap frame, int x, int y)
    {
        var sampleX = ArtworkBackingSampleBounds.Left + MirrorCoordinate(
            x - ArtworkBackingSampleBounds.Left,
            ArtworkBackingSampleBounds.Width);
        // Begin with the bottom of the title texture and reflect upward/downward. Mirrored
        // repetition preserves the real affiliation material while avoiding hard tile seams.
        var sampleY = ArtworkBackingSampleBounds.Bottom - 1 - MirrorCoordinate(
            Math.Max(0, y - ProtectedTitleBandBottom),
            ArtworkBackingSampleBounds.Height);
        var sampled = frame.GetPixel(sampleX, sampleY);
        return Color.FromArgb(byte.MaxValue, sampled.R, sampled.G, sampled.B);
    }

    private static void ValidateArtworkBackingSample(Bitmap frame, AtlasSlot slot)
    {
        if (ArtworkBackingSampleBounds.Left < 0 || ArtworkBackingSampleBounds.Top < 0 ||
            ArtworkBackingSampleBounds.Right > frame.Width ||
            ArtworkBackingSampleBounds.Bottom > ProtectedTitleBandBottom)
        {
            throw new InvalidOperationException(
                $"Artwork-backing sample bounds {ArtworkBackingSampleBounds} are outside the protected title band.");
        }

        var colours = new HashSet<int>();
        for (var y = ArtworkBackingSampleBounds.Top; y < ArtworkBackingSampleBounds.Bottom; y++)
        {
            for (var x = ArtworkBackingSampleBounds.Left; x < ArtworkBackingSampleBounds.Right; x++)
            {
                var sampled = frame.GetPixel(x, y);
                if (sampled.A != byte.MaxValue)
                {
                    throw new InvalidOperationException(
                        $"Title-band texture for {slot.Description} is not fully opaque at " +
                        $"{x},{y} (alpha={sampled.A}).");
                }

                colours.Add(sampled.ToArgb());
            }
        }

        if (colours.Count < 64)
        {
            throw new InvalidOperationException(
                $"Title-band texture for {slot.Description} contains only {colours.Count} colours.");
        }
    }

    private static int MirrorCoordinate(int value, int size)
    {
        if (size <= 1)
        {
            return 0;
        }

        var period = size * 2 - 2;
        var position = value % period;
        if (position < 0)
        {
            position += period;
        }

        return position < size ? position : period - position;
    }

    private static bool[] DilateComponentMask(
        Component component,
        int width,
        int height,
        int horizontalRadius,
        int upwardRadius,
        int downwardRadius,
        int bottomExclusive)
    {
        var seed = new bool[width * height];
        foreach (var pixel in component.Pixels)
        {
            seed[pixel] = true;
        }

        // A summed-area table performs the rectangular morphological dilation in
        // O(width*height). It follows the detected rounded/angled aperture instead of
        // erasing one coarse fixed rectangle.
        var integralWidth = width + 1;
        var integral = new int[integralWidth * (height + 1)];
        for (var y = 0; y < height; y++)
        {
            var rowSum = 0;
            for (var x = 0; x < width; x++)
            {
                rowSum += seed[y * width + x] ? 1 : 0;
                integral[(y + 1) * integralWidth + x + 1] =
                    integral[y * integralWidth + x + 1] + rowSum;
            }
        }

        var mask = new bool[width * height];
        var cleanupBottom = Math.Min(height, bottomExclusive);
        for (var y = 0; y < cleanupBottom; y++)
        {
            // A source aperture pixel below the destination grows the mask upward; a
            // source pixel above it grows downward. The asymmetric vertical window is
            // deliberate and preserves the unrelated side/bottom affiliation ornaments.
            var top = Math.Max(0, y - downwardRadius);
            var bottom = Math.Min(height - 1, y + upwardRadius);
            for (var x = 0; x < width; x++)
            {
                var left = Math.Max(0, x - horizontalRadius);
                var right = Math.Min(width - 1, x + horizontalRadius);
                var count = integral[(bottom + 1) * integralWidth + right + 1] -
                            integral[top * integralWidth + right + 1] -
                            integral[(bottom + 1) * integralWidth + left] +
                            integral[top * integralWidth + left];
                mask[y * width + x] = count > 0;
            }
        }

        return mask;
    }

    private static Rectangle GetMaskBounds(bool[] mask, int width, int height)
    {
        var left = width;
        var top = height;
        var right = -1;
        var bottom = -1;
        for (var pixel = 0; pixel < mask.Length; pixel++)
        {
            if (!mask[pixel])
            {
                continue;
            }

            var x = pixel % width;
            var y = pixel / width;
            left = Math.Min(left, x);
            top = Math.Min(top, y);
            right = Math.Max(right, x);
            bottom = Math.Max(bottom, y);
        }

        if (right < left || bottom < top)
        {
            throw new InvalidOperationException("Artwork cleanup mask is empty.");
        }

        return Rectangle.FromLTRB(left, top, right + 1, bottom + 1);
    }

    private static Bitmap PlaceTypeFrame(Bitmap source, int width, int height)
    {
        var destination = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        // Keep the first opaque type-frame pixel below the runtime title band. The tight
        // crop has a small transparent inset, yielding alpha top ~=132 and bottom <=495.
        const int targetWidth = 500;
        const int targetHeight = 375;
        const int targetX = 30;
        const int targetY = 125;
        using var graphics = Graphics.FromImage(destination);
        ConfigurePixelGraphics(graphics);
        graphics.DrawImage(source,
            new Rectangle(targetX, targetY, targetWidth, targetHeight),
            new Rectangle(0, 0, source.Width, source.Height),
            GraphicsUnit.Pixel);
        return destination;
    }

    private static Bitmap NormalizeCostBase(Bitmap source, int size)
    {
        var destination = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        const int margin = 3;
        var available = size - margin * 2;
        var scale = Math.Min(available / (double)source.Width, available / (double)source.Height);
        var targetWidth = Math.Max(1, (int)Math.Round(source.Width * scale));
        var targetHeight = Math.Max(1, (int)Math.Round(source.Height * scale));
        var targetX = (size - targetWidth) / 2;
        var targetY = (size - targetHeight) / 2;

        using var graphics = Graphics.FromImage(destination);
        ConfigurePixelGraphics(graphics);
        graphics.DrawImage(source,
            new Rectangle(targetX, targetY, targetWidth, targetHeight),
            new Rectangle(0, 0, source.Width, source.Height),
            GraphicsUnit.Pixel);
        return destination;
    }

    private static void ConfigurePixelGraphics(Graphics graphics)
    {
        graphics.Clear(TransparentPixel);
        graphics.CompositingMode = CompositingMode.SourceCopy;
        graphics.CompositingQuality = CompositingQuality.HighSpeed;
        graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
        graphics.PixelOffsetMode = PixelOffsetMode.Half;
        graphics.SmoothingMode = SmoothingMode.None;
    }

    private static Component FindLargestEnclosedTransparentComponent(Bitmap frame)
    {
        var width = frame.Width;
        var height = frame.Height;
        var visited = new bool[width * height];
        var queue = new Queue<int>();
        Component? largest = null;
        var directions = new[] { (-1, 0), (1, 0), (0, -1), (0, 1) };

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var start = y * width + x;
                if (visited[start])
                {
                    continue;
                }

                visited[start] = true;
                if (frame.GetPixel(x, y).A > 4)
                {
                    continue;
                }

                var component = new Component();
                var touchesBoundary = false;
                queue.Enqueue(start);
                while (queue.Count > 0)
                {
                    var current = queue.Dequeue();
                    var currentX = current % width;
                    var currentY = current / width;
                    component.Left = Math.Min(component.Left, currentX);
                    component.Top = Math.Min(component.Top, currentY);
                    component.Right = Math.Max(component.Right, currentX);
                    component.Bottom = Math.Max(component.Bottom, currentY);
                    component.Count++;
                    component.Pixels.Add(current);
                    touchesBoundary |= currentX == 0 || currentY == 0 ||
                                       currentX == width - 1 || currentY == height - 1;

                    foreach (var (dx, dy) in directions)
                    {
                        var nextX = currentX + dx;
                        var nextY = currentY + dy;
                        if (nextX < 0 || nextY < 0 || nextX >= width || nextY >= height)
                        {
                            continue;
                        }

                        var next = nextY * width + nextX;
                        if (visited[next])
                        {
                            continue;
                        }

                        visited[next] = true;
                        if (frame.GetPixel(nextX, nextY).A <= 4)
                        {
                            queue.Enqueue(next);
                        }
                    }
                }

                if (!touchesBoundary && (largest is null || component.Count > largest.Count))
                {
                    largest = component;
                }
            }
        }

        if (largest is null)
        {
            throw new InvalidOperationException(
                "No enclosed transparent illustration aperture was found. " +
                "The frame must close around its artwork opening.");
        }

        return largest;
    }

    private static Bitmap CreateLargestEnclosedTransparencyMask(Bitmap frame)
    {
        var largest = FindLargestEnclosedTransparentComponent(frame);
        var mask = new Bitmap(frame.Width, frame.Height, PixelFormat.Format32bppArgb);

        foreach (var pixel in largest.Pixels)
        {
            mask.SetPixel(pixel % frame.Width, pixel / frame.Width, Color.White);
        }

        // Every type shares this guaranteed art-safe rectangle. It may extend beneath an
        // opaque corner ornament; the type-frame layer is painted above the artwork.
        using (var graphics = Graphics.FromImage(mask))
        {
            graphics.CompositingMode = CompositingMode.SourceCopy;
            using var brush = new SolidBrush(Color.White);
            graphics.FillRectangle(brush, Rectangle.FromLTRB(120, 155, 445, 405));
        }

        return mask;
    }

    private static void ValidateEmptyCell(Bitmap cell, AtlasSlot slot)
    {
        var opaque = CountOpaquePixels(cell, 12);
        var allowance = Math.Max(24, cell.Width * cell.Height / 250);
        if (opaque > allowance)
        {
            throw new InvalidOperationException(
                $"Reserved cell {slot.Row + 1},{slot.Column + 1} contains {opaque} opaque pixels " +
                $"(allowance {allowance}). Keep the last two atlas cells empty.");
        }
    }

    private static void ValidateOutput(
        Bitmap bitmap,
        AtlasSlot slot,
        Color matte,
        int minimumOpaquePixels)
    {
        var opaque = CountOpaquePixels(bitmap, 12);
        if (opaque < minimumOpaquePixels)
        {
            throw new InvalidOperationException(
                $"Output {slot.Description} contains only {opaque} visible pixels; expected at least {minimumOpaquePixels}.");
        }

        var chromaFringe = 0;
        var residualMatteSpill = 0;
        var opaqueChroma = 0;
        string? firstResidualMatteSpill = null;
        for (var y = 0; y < bitmap.Height; y++)
        {
            for (var x = 0; x < bitmap.Width; x++)
            {
                var color = bitmap.GetPixel(x, y);
                if (color.A == 0)
                {
                    continue;
                }

                if (color.A < 250 &&
                    (ColorDistance(color, matte) < 72 || ComputeChromaKeyAmount(color, matte) > 0.72))
                {
                    chromaFringe++;
                }

                if (color.A >= 250 && ColorDistance(color, matte) < 72)
                {
                    opaqueChroma++;
                }


                var residualGreen = color.G >= Math.Max(color.R, color.B) + 18;
                var residualCyan = Math.Min(color.G, color.B) >= color.R + 22;
                if (color.A <= 200 && (residualGreen || residualCyan))
                {
                    residualMatteSpill++;
                    firstResidualMatteSpill ??=
                        $"{x},{y} A={color.A} RGB={color.R},{color.G},{color.B}";
                }
            }
        }

        if (chromaFringe > 0)
        {
            throw new InvalidOperationException(
                $"Output {slot.Description} retains {chromaFringe} semi-transparent chroma fringe pixels.");
        }

        if (residualMatteSpill > 0)
        {
            throw new InvalidOperationException(
                $"Output {slot.Description} retains {residualMatteSpill} low-alpha green/cyan matte-spill pixels " +
                $"(first {firstResidualMatteSpill}).");
        }

        if (opaqueChroma > 0)
        {
            throw new InvalidOperationException(
                $"Output {slot.Description} retains {opaqueChroma} opaque pixels matching the chroma matte.");
        }

        ValidateNoSmallOpaqueComponents(
            bitmap,
            slot,
            slot.Kind == AssetKind.Type ? PlaqueBounds : Rectangle.Empty);
    }

    private static void ValidateMask(Bitmap mask, AtlasSlot slot)
    {
        var aperturePixels = CountOpaquePixels(mask, 0);
        if (aperturePixels < 5_000)
        {
            throw new InvalidOperationException(
                $"Illustration mask for {slot.Description} is too small ({aperturePixels} pixels).");
        }

        var requiredOpening = Rectangle.FromLTRB(120, 155, 445, 405);
        for (var y = requiredOpening.Top; y < requiredOpening.Bottom; y++)
        {
            for (var x = requiredOpening.Left; x < requiredOpening.Right; x++)
            {
                if (mask.GetPixel(x, y).A == 0)
                {
                    throw new InvalidOperationException(
                        $"Illustration mask for {slot.Description} does not contain the shared safe opening " +
                        $"x=120..444, y=155..404 (first missing pixel {x},{y}).");
                }
            }
        }

        for (var x = 0; x < mask.Width; x++)
        {
            if (mask.GetPixel(x, 0).A > 0 || mask.GetPixel(x, mask.Height - 1).A > 0)
            {
                throw new InvalidOperationException($"Illustration mask for {slot.Description} touches the canvas edge.");
            }
        }

        for (var y = 0; y < mask.Height; y++)
        {
            if (mask.GetPixel(0, y).A > 0 || mask.GetPixel(mask.Width - 1, y).A > 0)
            {
                throw new InvalidOperationException($"Illustration mask for {slot.Description} touches the canvas edge.");
            }
        }
    }

    private static int CountOpaquePixels(Bitmap bitmap, byte alphaThreshold)
    {
        var count = 0;
        for (var y = 0; y < bitmap.Height; y++)
        {
            for (var x = 0; x < bitmap.Width; x++)
            {
                if (bitmap.GetPixel(x, y).A > alphaThreshold)
                {
                    count++;
                }
            }
        }

        return count;
    }

    private static MaskDifferenceSummary ValidateTypeMaskDistinctness(
        string maskDirectory,
        bool enforceMinimum)
    {
        var comparisons = new List<(string Pair, double Ratio)>();
        for (var leftIndex = 0; leftIndex < PlaqueNames.Length; leftIndex++)
        {
            using var left = new Bitmap(Path.Combine(maskDirectory, PlaqueNames[leftIndex] + ".png"));
            for (var rightIndex = leftIndex + 1; rightIndex < PlaqueNames.Length; rightIndex++)
            {
                using var right = new Bitmap(Path.Combine(maskDirectory, PlaqueNames[rightIndex] + ".png"));
                if (left.Size != right.Size)
                {
                    throw new InvalidOperationException(
                        $"Type masks {PlaqueNames[leftIndex]} and {PlaqueNames[rightIndex]} have different sizes.");
                }

                var unionPixels = 0;
                var differentPixels = 0;
                for (var y = 0; y < left.Height; y++)
                {
                    for (var x = 0; x < left.Width; x++)
                    {
                        var leftVisible = left.GetPixel(x, y).A > 0;
                        var rightVisible = right.GetPixel(x, y).A > 0;
                        if (leftVisible || rightVisible)
                        {
                            unionPixels++;
                        }

                        if (leftVisible != rightVisible)
                        {
                            differentPixels++;
                        }
                    }
                }

                if (unionPixels == 0)
                {
                    throw new InvalidOperationException(
                        $"Type masks {PlaqueNames[leftIndex]} and {PlaqueNames[rightIndex]} are empty.");
                }

                comparisons.Add((
                    $"{PlaqueNames[leftIndex]}/{PlaqueNames[rightIndex]}",
                    differentPixels / (double)unionPixels));
            }
        }

        var insufficient = comparisons
            .Where(comparison => comparison.Ratio < MinimumMaskPairDifferenceRatio)
            .OrderBy(comparison => comparison.Ratio)
            .ToArray();
        if (enforceMinimum && insufficient.Length > 0)
        {
            var details = string.Join(
                ", ",
                insufficient.Select(comparison => $"{comparison.Pair}={comparison.Ratio:P2}"));
            throw new InvalidOperationException(
                $"Type-mask silhouettes are not distinct enough ({details}); " +
                $"every pair must differ by at least {MinimumMaskPairDifferenceRatio:P0} of their union.");
        }

        var minimum = comparisons.MinBy(comparison => comparison.Ratio);
        var maximum = comparisons.MaxBy(comparison => comparison.Ratio);
        return new MaskDifferenceSummary(
            minimum.Pair,
            minimum.Ratio,
            maximum.Pair,
            maximum.Ratio);
    }

    private static AffiliationCleanupSummary ValidateAffiliationArtworkCleanup(
        string affiliationDirectory,
        IReadOnlyCollection<AffiliationCleanupContract> contracts)
    {
        var expectedNames = Slots
            .Where(slot => slot.Kind == AssetKind.Affiliation)
            .Select(slot => slot.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
        var actualNames = contracts
            .Select(contract => contract.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
        if (!expectedNames.SequenceEqual(actualNames, StringComparer.Ordinal))
        {
            throw new InvalidOperationException(
                "Affiliation cleanup contracts do not match the six staged affiliations.");
        }

        var cleanupPixels = 0;
        var replacedLegacyPixels = 0;
        var minimumBackingColorCount = int.MaxValue;
        var maximumBoundaryDelta = 0.0;
        var maximumBoundaryLocation = "none";
        var legacyBackingArgb = LegacyArtworkBackingColor.ToArgb();
        foreach (var contract in contracts)
        {
            using var staged = new Bitmap(Path.Combine(
                affiliationDirectory,
                contract.Name + ".png"));
            if (staged.Width != contract.Width || staged.Height != contract.Height)
            {
                throw new InvalidOperationException(
                    $"Affiliation frame {contract.Name} is {staged.Width}x{staged.Height}; " +
                    $"cleanup contract requires {contract.Width}x{contract.Height}.");
            }

            if (contract.CleanupMask.Length != contract.Width * contract.Height ||
                contract.NormalizedInputArgb.Length != contract.CleanupMask.Length ||
                contract.ExpectedBackingArgb.Length != contract.CleanupMask.Length)
            {
                throw new InvalidOperationException(
                    $"Affiliation cleanup contract for {contract.Name} has inconsistent buffers.");
            }

            var contractPixels = 0;
            var backingColors = new HashSet<int>();
            for (var pixel = 0; pixel < contract.CleanupMask.Length; pixel++)
            {
                var x = pixel % contract.Width;
                var y = pixel / contract.Width;
                var actualArgb = staged.GetPixel(x, y).ToArgb();
                if (contract.CleanupMask[pixel])
                {
                    contractPixels++;
                    if (y >= RulesPanelTop)
                    {
                        throw new InvalidOperationException(
                            $"Affiliation cleanup mask for {contract.Name} enters the rules panel at {x},{y}.");
                    }

                    var expectedBackingArgb = contract.ExpectedBackingArgb[pixel];
                    if (actualArgb != expectedBackingArgb || Color.FromArgb(actualArgb).A != byte.MaxValue)
                    {
                        throw new InvalidOperationException(
                            $"Affiliation backing for {contract.Name} differs from its deterministic " +
                            $"title-band texture at {x},{y}.");
                    }

                    if (actualArgb == legacyBackingArgb)
                    {
                        throw new InvalidOperationException(
                            $"Affiliation backing for {contract.Name} retains the legacy black fill at {x},{y}.");
                    }

                    backingColors.Add(actualArgb);
                }
                else if (actualArgb != contract.NormalizedInputArgb[pixel])
                {
                    throw new InvalidOperationException(
                        $"Affiliation cleanup for {contract.Name} altered normalized input outside " +
                        $"its shape-derived mask at {x},{y}.");
                }
            }

            if (contractPixels == 0 || contract.CleanupBounds.IsEmpty)
            {
                throw new InvalidOperationException(
                    $"Affiliation cleanup mask for {contract.Name} is empty.");
            }

            if (backingColors.Count < 64)
            {
                throw new InvalidOperationException(
                    $"Affiliation backing for {contract.Name} contains only {backingColors.Count} colours; " +
                    "expected the title-band material texture rather than a flat fill.");
            }

            var actualBounds = GetMaskBounds(
                contract.CleanupMask,
                contract.Width,
                contract.Height);
            if (actualBounds != contract.CleanupBounds)
            {
                throw new InvalidOperationException(
                    $"Affiliation cleanup bounds for {contract.Name} changed between generation and commit: " +
                    $"expected={contract.CleanupBounds}, actual={actualBounds}.");
            }

            // The outer canvas must remain transparent. Combined with the outside-mask
            // pixel equality above, this preserves the existing edge contract exactly.
            for (var x = 0; x < staged.Width; x++)
            {
                if (staged.GetPixel(x, 0).A > 0 || staged.GetPixel(x, staged.Height - 1).A > 0)
                {
                    throw new InvalidOperationException(
                        $"Affiliation cleanup for {contract.Name} reaches a horizontal canvas edge.");
                }
            }

            for (var y = 0; y < staged.Height; y++)
            {
                if (staged.GetPixel(0, y).A > 0 || staged.GetPixel(staged.Width - 1, y).A > 0)
                {
                    throw new InvalidOperationException(
                        $"Affiliation cleanup for {contract.Name} reaches a vertical canvas edge.");
                }
            }

            var (boundaryDelta, boundaryRow) = MeasureMaximumMeanPixelRgbRowDelta(
                staged,
                ArtworkBoundaryInspectionBounds);
            if (boundaryDelta > MaximumArtworkBoundaryMeanPixelRgbDelta)
            {
                throw new InvalidOperationException(
                    $"Affiliation backing for {contract.Name} has a visible horizontal seam at " +
                    $"y={boundaryRow}: mean per-pixel RGB delta={boundaryDelta:F2}, " +
                    $"maximum={MaximumArtworkBoundaryMeanPixelRgbDelta:F2}.");
            }
            if (boundaryDelta > maximumBoundaryDelta)
            {
                maximumBoundaryDelta = boundaryDelta;
                maximumBoundaryLocation = $"{contract.Name}:y{boundaryRow}";
            }

            cleanupPixels += contractPixels;
            replacedLegacyPixels += contract.ReplacedLegacyPixels;
            minimumBackingColorCount = Math.Min(minimumBackingColorCount, backingColors.Count);
        }

        return new AffiliationCleanupSummary(
            cleanupPixels,
            replacedLegacyPixels,
            minimumBackingColorCount,
            maximumBoundaryDelta,
            maximumBoundaryLocation);
    }

    private static (double Delta, int Row) MeasureMaximumMeanPixelRgbRowDelta(
        Bitmap bitmap,
        Rectangle bounds)
    {
        if (bounds.Left < 0 || bounds.Top < 0 ||
            bounds.Right > bitmap.Width || bounds.Bottom > bitmap.Height ||
            bounds.Width <= 0 || bounds.Height <= 1)
        {
            throw new InvalidOperationException(
                $"Artwork-boundary inspection bounds {bounds} are invalid for " +
                $"{bitmap.Width}x{bitmap.Height}.");
        }

        var maximumDelta = 0.0;
        var maximumRow = bounds.Top + 1;
        for (var y = bounds.Top + 1; y < bounds.Bottom; y++)
        {
            var totalDelta = 0.0;
            for (var x = bounds.Left; x < bounds.Right; x++)
            {
                var above = bitmap.GetPixel(x, y - 1);
                var current = bitmap.GetPixel(x, y);
                var red = current.R - above.R;
                var green = current.G - above.G;
                var blue = current.B - above.B;
                totalDelta += Math.Sqrt(red * red + green * green + blue * blue);
            }

            var meanDelta = totalDelta / bounds.Width;
            if (meanDelta > maximumDelta)
            {
                maximumDelta = meanDelta;
                maximumRow = y;
            }
        }

        return (maximumDelta, maximumRow);
    }

    private static int ValidateAffiliationArtworkBackingCoverage(
        string affiliationDirectory,
        string maskDirectory)
    {
        bool[]? apertureUnion = null;
        var width = 0;
        var height = 0;
        foreach (var typeName in PlaqueNames)
        {
            using var mask = new Bitmap(Path.Combine(maskDirectory, typeName + ".png"));
            if (apertureUnion is null)
            {
                width = mask.Width;
                height = mask.Height;
                apertureUnion = new bool[width * height];
            }
            else if (mask.Width != width || mask.Height != height)
            {
                throw new InvalidOperationException(
                    $"Type mask {typeName} is {mask.Width}x{mask.Height}; expected {width}x{height}.");
            }

            var alpha = ReadAlpha(mask);
            for (var pixel = 0; pixel < alpha.Length; pixel++)
            {
                apertureUnion[pixel] |= alpha[pixel] > 0;
            }
        }

        if (apertureUnion is null)
        {
            throw new InvalidOperationException("No type masks were available for artwork-backing validation.");
        }

        var coveragePixels = apertureUnion.Count(visible => visible);
        if (coveragePixels == 0)
        {
            throw new InvalidOperationException("The union of all type-mask apertures is empty.");
        }

        foreach (var affiliationName in Slots
                     .Where(slot => slot.Kind == AssetKind.Affiliation)
                     .Select(slot => slot.Name))
        {
            using var frame = new Bitmap(Path.Combine(affiliationDirectory, affiliationName + ".png"));
            if (frame.Width != width || frame.Height != height)
            {
                throw new InvalidOperationException(
                    $"Affiliation frame {affiliationName} is {frame.Width}x{frame.Height}; " +
                    $"expected {width}x{height} to cover the type-mask union.");
            }

            var alpha = ReadAlpha(frame);
            for (var pixel = 0; pixel < apertureUnion.Length; pixel++)
            {
                if (!apertureUnion[pixel] || alpha[pixel] == byte.MaxValue)
                {
                    continue;
                }

                var x = pixel % width;
                var y = pixel / width;
                throw new InvalidOperationException(
                    $"Affiliation frame {affiliationName} does not fully cover the generated type-mask union " +
                    $"at {x},{y} (alpha={alpha[pixel]}). Refusing to commit staged runtime assets.");
            }
        }

        return coveragePixels;
    }

    private static void ValidateWrittenOutputs(IReadOnlyCollection<string> paths)
    {
        const int expected = 24;
        if (paths.Count != expected)
        {
            throw new InvalidOperationException($"Expected {expected} output PNGs, but wrote {paths.Count}.");
        }

        var missing = paths.Where(path => !File.Exists(path)).ToArray();
        if (missing.Length > 0)
        {
            throw new InvalidOperationException("Missing output files: " + string.Join(", ", missing));
        }
    }

    private static void CommitStagedOutputs(
        IReadOnlyCollection<string> stagedPaths,
        string stagingRoot,
        string finalOutputRoot)
    {
        foreach (var stagedPath in stagedPaths)
        {
            var absoluteStagedPath = Path.GetFullPath(stagedPath);
            EnsureContainedPath(stagingRoot, absoluteStagedPath, "staged output");
            var relativePath = Path.GetRelativePath(stagingRoot, absoluteStagedPath);
            var finalPath = Path.GetFullPath(Path.Combine(finalOutputRoot, relativePath));
            EnsureContainedPath(finalOutputRoot, finalPath, "final output");
            Directory.CreateDirectory(Path.GetDirectoryName(finalPath) ?? finalOutputRoot);
            File.Move(absoluteStagedPath, finalPath, overwrite: true);
        }
    }

    private static void DeleteStagingDirectory(string stagingRoot, string finalOutputRoot)
    {
        if (!Directory.Exists(stagingRoot))
        {
            return;
        }

        EnsureContainedPath(finalOutputRoot, stagingRoot, "staging cleanup target");
        var directoryName = Path.GetFileName(stagingRoot);
        if (!directoryName.StartsWith(".card-frame-stage-", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Refusing to recursively delete unexpected staging directory: {stagingRoot}");
        }

        Directory.Delete(stagingRoot, recursive: true);
    }

    private static void EnsureContainedPath(string root, string path, string description)
    {
        var absoluteRoot = Path.GetFullPath(root);
        var absolutePath = Path.GetFullPath(path);
        var relative = Path.GetRelativePath(absoluteRoot, absolutePath);
        if (Path.IsPathRooted(relative) ||
            relative.Equals("..", StringComparison.Ordinal) ||
            relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) ||
            relative.StartsWith(".." + Path.AltDirectorySeparatorChar, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Refusing {description} outside output root. root={absoluteRoot}; path={absolutePath}");
        }
    }

    private sealed class StringTupleComparer : IEqualityComparer<(AssetKind Kind, string Name)>
    {
        public static StringTupleComparer OrdinalIgnoreCase { get; } = new();

        public bool Equals((AssetKind Kind, string Name) x, (AssetKind Kind, string Name) y) =>
            x.Kind == y.Kind && string.Equals(x.Name, y.Name, StringComparison.OrdinalIgnoreCase);

        public int GetHashCode((AssetKind Kind, string Name) obj) =>
            HashCode.Combine(obj.Kind, StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Name));
    }
}
