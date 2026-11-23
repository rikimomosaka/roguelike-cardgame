param(
    [string]$OutputRoot = "src/Client/public"
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

if (-not ('RcgPresentationAssetProcessor' -as [type])) {
    Add-Type -ReferencedAssemblies System.Drawing -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

public static class RcgPresentationAssetProcessor
{
    private const int StandardFrameWidth = 192;
    private const int FrameHeight = 256;
    private const int SideGutter = 8;
    private const int TopGutter = 10;
    private const int BottomGutter = 10;
    private const int ReferenceActorHeight = 218;

    public sealed class MotionReport
    {
        public string Name;
        public int FrameCount;
        public double ScaleX;
        public double ScaleY;
        public int MinimumWidth;
        public int MaximumWidth;
        public int MinimumHeight;
        public int MaximumHeight;
    }

    private static Bitmap ToArgb(Bitmap source)
    {
        var output = new Bitmap(source.Width, source.Height, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(output))
        {
            graphics.CompositingMode = CompositingMode.SourceCopy;
            graphics.Clear(Color.Transparent);
            graphics.DrawImageUnscaled(source, 0, 0);
        }
        return output;
    }

    private static bool IsLightBackdrop(byte b, byte g, byte r, byte a)
    {
        if (a <= 8) return true;
        var minimum = Math.Min(r, Math.Min(g, b));
        var maximum = Math.Max(r, Math.Max(g, b));
        return minimum >= 216 && maximum - minimum <= 30;
    }

    private static bool IsGreenBackdrop(byte b, byte g, byte r, byte a)
    {
        if (a <= 8) return true;
        var other = Math.Max(r, b);
        return g >= 72 && g - other >= 22 && g >= other * 1.14;
    }

    private static bool IsMagentaBackdrop(byte b, byte g, byte r, byte a)
    {
        if (a <= 8) return true;
        var chromaFloor = Math.Min(r, b);
        return r >= 84 && b >= 84 && chromaFloor - g >= 28 && Math.Abs(r - b) <= 104;
    }

    private static bool IsResidualGreenKey(byte b, byte g, byte r, byte a)
    {
        if (a <= 8) return false;
        var other = Math.Max(r, b);
        return g >= 24 && g - other >= 14 && g >= other * 1.12;
    }

    private static bool IsResidualMagentaKey(byte b, byte g, byte r, byte a)
    {
        if (a <= 8) return false;
        var chromaFloor = Math.Min(r, b);
        return chromaFloor >= 24 && chromaFloor - g >= 14 && Math.Abs(r - b) <= 68;
    }

    // 0 = rendered light checker, 1 = green key, 2 = magenta key.
    private static int DetectBackdropMode(byte[] bytes, int width, int height, int stride)
    {
        var green = 0;
        var magenta = 0;
        var light = 0;
        var samples = 0;
        Action<int, int> sample = (x, y) => {
            var offset = y * stride + x * 4;
            var b = bytes[offset];
            var g = bytes[offset + 1];
            var r = bytes[offset + 2];
            var a = bytes[offset + 3];
            if (IsGreenBackdrop(b, g, r, a)) green++;
            if (IsMagentaBackdrop(b, g, r, a)) magenta++;
            if (IsLightBackdrop(b, g, r, a)) light++;
            samples++;
        };
        var stepX = Math.Max(1, width / 128);
        var stepY = Math.Max(1, height / 128);
        for (var x = 0; x < width; x += stepX)
        {
            sample(x, 0);
            sample(x, height - 1);
        }
        for (var y = stepY; y < height - 1; y += stepY)
        {
            sample(0, y);
            sample(width - 1, y);
        }
        if (green > samples * 0.35 && green > light * 2 && green > magenta) return 1;
        if (magenta > samples * 0.35 && magenta > light * 2) return 2;
        return 0;
    }

    private static byte ClampByte(double value)
    {
        return (byte)Math.Max(0, Math.Min(255, Math.Round(value)));
    }

    private static void SanitizeTransparentRgb(byte[] bytes, int width, int height, int stride)
    {
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            var offset = y * stride + x * 4;
            if (bytes[offset + 3] > 8) continue;
            bytes[offset] = bytes[offset + 1] = bytes[offset + 2] = bytes[offset + 3] = 0;
        }
    }

    private static void SanitizeTransparentRgb(Bitmap bitmap)
    {
        var rectangle = new Rectangle(0, 0, bitmap.Width, bitmap.Height);
        var data = bitmap.LockBits(rectangle, ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
        try
        {
            var stride = Math.Abs(data.Stride);
            var bytes = new byte[stride * bitmap.Height];
            Marshal.Copy(data.Scan0, bytes, 0, bytes.Length);
            SanitizeTransparentRgb(bytes, bitmap.Width, bitmap.Height, stride);
            Marshal.Copy(bytes, 0, data.Scan0, bytes.Length);
        }
        finally { bitmap.UnlockBits(data); }
    }

    // Recover foreground colour and coverage from the generated matte in the
    // shallow 3px silhouette boundary. Merely deleting white pixels leaves the
    // remaining antialias RGB contaminated by the checkerboard; it becomes a
    // bright outline on the battle's dark background. Solving C=aF+(1-a)B
    // removes that matte while retaining genuinely white hair and armour: when
    // the nearby foreground is also white the solve is intentionally skipped.
    private static void DecontaminateBoundary(
        byte[] bytes, int width, int height, int stride, bool[] background)
    {
        const int matteDepth = 3;
        const int extendedNeutralDepth = matteDepth;
        const int searchRadius = 7;
        var ring = Enumerable.Repeat(-1, width * height).ToArray();
        for (var pixel = 0; pixel < background.Length; pixel++)
            if (background[pixel]) ring[pixel] = 0;

        for (var distance = 1; distance <= extendedNeutralDepth + 1; distance++)
        {
            var discovered = new List<int>();
            for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
            {
                var pixel = y * width + x;
                if (ring[pixel] >= 0) continue;
                if ((x > 0 && ring[pixel - 1] == distance - 1) ||
                    (x + 1 < width && ring[pixel + 1] == distance - 1) ||
                    (y > 0 && ring[pixel - width] == distance - 1) ||
                    (y + 1 < height && ring[pixel + width] == distance - 1))
                    discovered.Add(pixel);
            }
            foreach (var pixel in discovered) ring[pixel] = distance;
        }

        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            var pixel = y * width + x;
            if (ring[pixel] < 1 || ring[pixel] > extendedNeutralDepth) continue;

            var bestBackground = -1;
            var bestForeground = -1;
            var bestBackgroundDistance = int.MaxValue;
            var bestForegroundDistance = int.MaxValue;
            for (var dy = -searchRadius; dy <= searchRadius; dy++)
            for (var dx = -searchRadius; dx <= searchRadius; dx++)
            {
                var nx = x + dx;
                var ny = y + dy;
                if (nx < 0 || ny < 0 || nx >= width || ny >= height) continue;
                var squared = dx * dx + dy * dy;
                if (squared == 0 || squared > searchRadius * searchRadius) continue;
                var neighbour = ny * width + nx;
                if (ring[neighbour] == 0 && squared < bestBackgroundDistance)
                {
                    bestBackground = neighbour;
                    bestBackgroundDistance = squared;
                }
                // Use a pixel beyond the corrected matte as the local true
                // foreground. Prefer the nearest one to preserve small details.
                if ((ring[neighbour] < 0 || ring[neighbour] > extendedNeutralDepth) &&
                    bytes[ny * stride + nx * 4 + 3] > 8 &&
                    squared < bestForegroundDistance)
                {
                    bestForeground = neighbour;
                    bestForegroundDistance = squared;
                }
            }
            if (bestBackground < 0 || bestForeground < 0) continue;

            var offset = y * stride + x * 4;
            var backgroundX = bestBackground % width;
            var backgroundY = bestBackground / width;
            var backgroundOffset = backgroundY * stride + backgroundX * 4;
            var foregroundX = bestForeground % width;
            var foregroundY = bestForeground / width;
            var foregroundOffset = foregroundY * stride + foregroundX * 4;

            var cb = (double)bytes[offset];
            var cg = (double)bytes[offset + 1];
            var cr = (double)bytes[offset + 2];
            var bb = (double)bytes[backgroundOffset];
            var bg = (double)bytes[backgroundOffset + 1];
            var br = (double)bytes[backgroundOffset + 2];
            var fb = (double)bytes[foregroundOffset];
            var fg = (double)bytes[foregroundOffset + 1];
            var fr = (double)bytes[foregroundOffset + 2];
            var db = fb - bb;
            var dg = fg - bg;
            var dr = fr - br;
            var denominator = db * db + dg * dg + dr * dr;

            // Beyond the normal 3px antialias boundary, only neutral light
            // pixels against a materially darker local foreground qualify.
            // This targets checker-shadow dashes around dark hair while
            // preserving silver armour and white hair whose interior is also
            // pale. Coloured fabric/skin is never touched in this wider band.
            if (ring[pixel] > matteDepth)
            {
                var currentMaximum = Math.Max(cr, Math.Max(cg, cb));
                var currentMinimum = Math.Min(cr, Math.Min(cg, cb));
                var currentLuminance = (cr * 54 + cg * 183 + cb * 19) / 256;
                var foregroundLuminance = (fr * 54 + fg * 183 + fb * 19) / 256;
                if (currentMaximum - currentMinimum > 24 ||
                    currentLuminance < 70 ||
                    currentLuminance < foregroundLuminance + 28)
                    continue;
            }

            // A white/green foreground genuinely close to the matte cannot be
            // separated reliably. Preserve it rather than eating pale hair,
            // armour highlights, or the tactician's actual green clothing.
            if (denominator < 1100) continue;

            var alpha = ((cb - bb) * db + (cg - bg) * dg + (cr - br) * dr) / denominator;
            alpha = Math.Max(0, Math.Min(1, alpha));
            if (alpha >= 0.985) continue;
            if (alpha <= 0.025)
            {
                bytes[offset] = bytes[offset + 1] = bytes[offset + 2] = bytes[offset + 3] = 0;
                continue;
            }

            var recoveredB = (cb - (1 - alpha) * bb) / alpha;
            var recoveredG = (cg - (1 - alpha) * bg) / alpha;
            var recoveredR = (cr - (1 - alpha) * br) / alpha;
            // Low coverage is numerically noisy. Blend toward the nearest
            // interior colour so a dark-background composite stays clean.
            var interiorWeight = alpha < 0.45 ? 0.58 : 0.24;
            bytes[offset] = ClampByte(recoveredB * (1 - interiorWeight) + fb * interiorWeight);
            bytes[offset + 1] = ClampByte(recoveredG * (1 - interiorWeight) + fg * interiorWeight);
            bytes[offset + 2] = ClampByte(recoveredR * (1 - interiorWeight) + fr * interiorWeight);
            bytes[offset + 3] = ClampByte(bytes[offset + 3] * alpha);
        }
    }

    // Generated sources can contain either a rendered white/grey checkerboard
    // or a deliberate chroma-green key. Only the matte connected to the outer
    // edges is removed, then its RGB contribution is reconstructed in the
    // foreground boundary. This keeps white armour/hair and internal green
    // costume regions intact.
    public static Bitmap CleanGeneratedBackdrop(Bitmap source)
    {
        var output = ToArgb(source);
        var rectangle = new Rectangle(0, 0, output.Width, output.Height);
        var data = output.LockBits(rectangle, ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
        try
        {
            var stride = Math.Abs(data.Stride);
            var bytes = new byte[stride * output.Height];
            Marshal.Copy(data.Scan0, bytes, 0, bytes.Length);
            var backdropMode = DetectBackdropMode(bytes, output.Width, output.Height, stride);
            var visited = new bool[output.Width * output.Height];
            var queue = new Queue<int>();

            Action<int, int> enqueue = (x, y) => {
                if (x < 0 || y < 0 || x >= output.Width || y >= output.Height) return;
                var pixel = y * output.Width + x;
                if (visited[pixel]) return;
                var offset = y * stride + x * 4;
                var isBackdrop = backdropMode == 1
                    ? IsGreenBackdrop(bytes[offset], bytes[offset + 1], bytes[offset + 2], bytes[offset + 3])
                    : backdropMode == 2
                        ? IsMagentaBackdrop(bytes[offset], bytes[offset + 1], bytes[offset + 2], bytes[offset + 3])
                        : IsLightBackdrop(bytes[offset], bytes[offset + 1], bytes[offset + 2], bytes[offset + 3]);
                if (!isBackdrop) return;
                visited[pixel] = true;
                queue.Enqueue(pixel);
            };

            for (var x = 0; x < output.Width; x++)
            {
                enqueue(x, 0);
                enqueue(x, output.Height - 1);
            }
            for (var y = 0; y < output.Height; y++)
            {
                enqueue(0, y);
                enqueue(output.Width - 1, y);
            }

            while (queue.Count > 0)
            {
                var pixel = queue.Dequeue();
                var x = pixel % output.Width;
                var y = pixel / output.Width;
                enqueue(x - 1, y);
                enqueue(x + 1, y);
                enqueue(x, y - 1);
                enqueue(x, y + 1);
            }

            // Hair loops, bows and weapons can enclose islands of key colour
            // that are intentionally disconnected from every outer edge. Once
            // the edge samples establish a chroma-key source, classify those
            // exact key-colour islands across the whole image as backdrop too.
            // This is safe for the supplied set: the green-costumed tactician
            // deliberately uses magenta, while the other three use green.
            if (backdropMode != 0)
            {
                for (var y = 0; y < output.Height; y++)
                for (var x = 0; x < output.Width; x++)
                {
                    var pixel = y * output.Width + x;
                    var offset = y * stride + x * 4;
                    if (backdropMode == 1
                        ? IsGreenBackdrop(bytes[offset], bytes[offset + 1], bytes[offset + 2], bytes[offset + 3])
                        : IsMagentaBackdrop(bytes[offset], bytes[offset + 1], bytes[offset + 2], bytes[offset + 3]))
                        visited[pixel] = true;
                }
            }

            DecontaminateBoundary(bytes, output.Width, output.Height, stride, visited);
            for (var pixel = 0; pixel < visited.Length; pixel++)
            {
                if (!visited[pixel]) continue;
                var x = pixel % output.Width;
                var y = pixel / output.Width;
                var offset = y * stride + x * 4;
                bytes[offset] = bytes[offset + 1] = bytes[offset + 2] = bytes[offset + 3] = 0;
            }
            // Remove isolated subpixel key specks left after colour recovery.
            // They are too small to represent intentional art, but become vivid
            // green/magenta pinholes after scaling into a cut-in.
            if (backdropMode != 0)
            {
                for (var y = 0; y < output.Height; y++)
                for (var x = 0; x < output.Width; x++)
                {
                    var offset = y * stride + x * 4;
                    var residual = backdropMode == 1
                        ? IsResidualGreenKey(bytes[offset], bytes[offset + 1], bytes[offset + 2], bytes[offset + 3])
                        : IsResidualMagentaKey(bytes[offset], bytes[offset + 1], bytes[offset + 2], bytes[offset + 3]);
                    if (residual)
                        bytes[offset] = bytes[offset + 1] = bytes[offset + 2] = bytes[offset + 3] = 0;
                }
            }
            SanitizeTransparentRgb(bytes, output.Width, output.Height, stride);

            Marshal.Copy(bytes, 0, data.Scan0, bytes.Length);
        }
        finally
        {
            output.UnlockBits(data);
        }
        return output;
    }

    private static Bitmap ExtractCell(Bitmap source, int columns, int rows, int index, int separatorDivisor)
    {
        var column = index % columns;
        var row = index / columns;
        var left = (int)Math.Floor(column * source.Width / (double)columns);
        var right = (int)Math.Floor((column + 1) * source.Width / (double)columns);
        var top = (int)Math.Floor(row * source.Height / (double)rows);
        var bottom = (int)Math.Floor((row + 1) * source.Height / (double)rows);
        var output = new Bitmap(right - left, bottom - top, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(output))
        {
            graphics.CompositingMode = CompositingMode.SourceCopy;
            graphics.Clear(Color.Transparent);
            graphics.DrawImage(source,
                new Rectangle(0, 0, output.Width, output.Height),
                new Rectangle(left, top, output.Width, output.Height),
                GraphicsUnit.Pixel);
        }
        // The conceptual grid is deliberately spacious. Reserve its outer six
        // percent as a hard separator so a long slash generated in the previous
        // cell cannot reappear as a stray spark in the recovery frame.
        RemoveEdgeContamination(output, Math.Max(3, Math.Min(output.Width, output.Height) / separatorDivisor));
        RemoveTinyIslands(output, 5);
        return output;
    }

    private static void RemoveEdgeContamination(Bitmap bitmap, int border)
    {
        var rectangle = new Rectangle(0, 0, bitmap.Width, bitmap.Height);
        var data = bitmap.LockBits(rectangle, ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
        try
        {
            var stride = Math.Abs(data.Stride);
            var bytes = new byte[stride * bitmap.Height];
            Marshal.Copy(data.Scan0, bytes, 0, bytes.Length);
            for (var y = 0; y < bitmap.Height; y++)
            for (var x = 0; x < bitmap.Width; x++)
            {
                if (x >= border && y >= border && x < bitmap.Width - border && y < bitmap.Height - border) continue;
                var offset = y * stride + x * 4;
                bytes[offset] = bytes[offset + 1] = bytes[offset + 2] = bytes[offset + 3] = 0;
            }
            Marshal.Copy(bytes, 0, data.Scan0, bytes.Length);
        }
        finally { bitmap.UnlockBits(data); }
    }

    private static void RemoveTinyIslands(Bitmap bitmap, int minimumPixels)
    {
        var rectangle = new Rectangle(0, 0, bitmap.Width, bitmap.Height);
        var data = bitmap.LockBits(rectangle, ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
        try
        {
            var stride = Math.Abs(data.Stride);
            var bytes = new byte[stride * bitmap.Height];
            Marshal.Copy(data.Scan0, bytes, 0, bytes.Length);
            var visited = new bool[bitmap.Width * bitmap.Height];
            var directions = new[] { -1, 0, 1 };
            for (var y = 0; y < bitmap.Height; y++)
            for (var x = 0; x < bitmap.Width; x++)
            {
                var start = y * bitmap.Width + x;
                if (visited[start] || bytes[y * stride + x * 4 + 3] <= 8) continue;
                var component = new List<int>();
                var queue = new Queue<int>();
                visited[start] = true;
                queue.Enqueue(start);
                while (queue.Count > 0)
                {
                    var pixel = queue.Dequeue();
                    component.Add(pixel);
                    var px = pixel % bitmap.Width;
                    var py = pixel / bitmap.Width;
                    foreach (var dy in directions)
                    foreach (var dx in directions)
                    {
                        if (dx == 0 && dy == 0) continue;
                        var nx = px + dx;
                        var ny = py + dy;
                        if (nx < 0 || ny < 0 || nx >= bitmap.Width || ny >= bitmap.Height) continue;
                        var next = ny * bitmap.Width + nx;
                        if (visited[next] || bytes[ny * stride + nx * 4 + 3] <= 8) continue;
                        visited[next] = true;
                        queue.Enqueue(next);
                    }
                }
                if (component.Count >= minimumPixels) continue;
                foreach (var pixel in component)
                {
                    var px = pixel % bitmap.Width;
                    var py = pixel / bitmap.Width;
                    var offset = py * stride + px * 4;
                    bytes[offset] = bytes[offset + 1] = bytes[offset + 2] = bytes[offset + 3] = 0;
                }
            }
            Marshal.Copy(bytes, 0, data.Scan0, bytes.Length);
        }
        finally { bitmap.UnlockBits(data); }
    }

    public static Rectangle AlphaBounds(Bitmap bitmap)
    {
        var minimumX = bitmap.Width;
        var minimumY = bitmap.Height;
        var maximumX = -1;
        var maximumY = -1;
        for (var y = 0; y < bitmap.Height; y++)
        for (var x = 0; x < bitmap.Width; x++)
        {
            if (bitmap.GetPixel(x, y).A <= 8) continue;
            minimumX = Math.Min(minimumX, x);
            minimumY = Math.Min(minimumY, y);
            maximumX = Math.Max(maximumX, x);
            maximumY = Math.Max(maximumY, y);
        }
        if (maximumX < minimumX) throw new InvalidDataException("Generated cell is fully transparent.");
        return Rectangle.FromLTRB(minimumX, minimumY, maximumX + 1, maximumY + 1);
    }

    private static int OpaqueMedianX(Bitmap bitmap, Rectangle bounds)
    {
        var columns = new int[bounds.Width];
        var total = 0;
        for (var y = bounds.Top; y < bounds.Bottom; y++)
        for (var x = bounds.Left; x < bounds.Right; x++)
        {
            if (bitmap.GetPixel(x, y).A <= 8) continue;
            columns[x - bounds.Left]++;
            total++;
        }
        var halfway = Math.Max(1, total / 2);
        var running = 0;
        for (var x = 0; x < columns.Length; x++)
        {
            running += columns[x];
            if (running >= halfway) return bounds.Left + x;
        }
        return bounds.Left + bounds.Width / 2;
    }

    private static void FadeHorizontalSafeEdges(Bitmap bitmap)
    {
        var rectangle = new Rectangle(0, 0, bitmap.Width, bitmap.Height);
        var data = bitmap.LockBits(rectangle, ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
        try
        {
            var stride = Math.Abs(data.Stride);
            var bytes = new byte[stride * bitmap.Height];
            Marshal.Copy(data.Scan0, bytes, 0, bytes.Length);
            const int fadeWidth = 6;
            for (var y = 0; y < bitmap.Height; y++)
            for (var x = SideGutter; x < SideGutter + fadeWidth; x++)
            {
                var leftFactor = (x - SideGutter) / (double)fadeWidth;
                var rightX = bitmap.Width - 1 - x;
                var leftOffset = y * stride + x * 4;
                var rightOffset = y * stride + rightX * 4;
                bytes[leftOffset + 3] = (byte)Math.Round(bytes[leftOffset + 3] * leftFactor);
                bytes[rightOffset + 3] = (byte)Math.Round(bytes[rightOffset + 3] * leftFactor);
            }
            Marshal.Copy(bytes, 0, data.Scan0, bytes.Length);
        }
        finally { bitmap.UnlockBits(data); }
    }

    private static Bitmap RenderFrame(Bitmap source, Rectangle bounds, double scale, int frameWidth)
    {
        var output = new Bitmap(frameWidth, FrameHeight, PixelFormat.Format32bppArgb);
        var drawWidth = Math.Max(1, (int)Math.Round(bounds.Width * scale));
        var drawHeight = Math.Max(1, (int)Math.Round(bounds.Height * scale));
        // Thin slashes and projectiles can make a generated frame much wider
        // than its actor. Anchor on the median occupied pixel (the dense body),
        // keep one uniform scale, and safely clip only the long VFX tail.
        var actorAnchor = OpaqueMedianX(source, bounds);
        var drawX = frameWidth / 2 - (int)Math.Round((actorAnchor - bounds.Left) * scale);
        var drawY = FrameHeight - BottomGutter - drawHeight;
        using (var graphics = Graphics.FromImage(output))
        {
            graphics.CompositingMode = CompositingMode.SourceCopy;
            graphics.Clear(Color.Transparent);
            graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
            graphics.PixelOffsetMode = PixelOffsetMode.Half;
            graphics.SetClip(new Rectangle(
                SideGutter, TopGutter,
                frameWidth - SideGutter * 2,
                FrameHeight - TopGutter - BottomGutter));
            graphics.DrawImage(source, new Rectangle(drawX, drawY, drawWidth, drawHeight), bounds, GraphicsUnit.Pixel);
        }
        FadeHorizontalSafeEdges(output);
        SanitizeTransparentRgb(output);
        return output;
    }

    // One checker renderer also baked a wider neutral drop-shadow around the
    // tactician's dark hair. It is not ordinary 1-3px antialiasing, so recover
    // only mid-neutral boundary pixels whose deeper local foreground is much
    // darker. Bright bolts and pale costume pieces are deliberately excluded.
    private static void RemoveNeutralMatteShadow(Bitmap bitmap)
    {
        const int depth = 7;
        const int searchRadius = 12;
        var rectangle = new Rectangle(0, 0, bitmap.Width, bitmap.Height);
        var data = bitmap.LockBits(rectangle, ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
        try
        {
            var stride = Math.Abs(data.Stride);
            var bytes = new byte[stride * bitmap.Height];
            Marshal.Copy(data.Scan0, bytes, 0, bytes.Length);
            var ring = Enumerable.Repeat(-1, bitmap.Width * bitmap.Height).ToArray();
            for (var y = 0; y < bitmap.Height; y++)
            for (var x = 0; x < bitmap.Width; x++)
                if (bytes[y * stride + x * 4 + 3] <= 8) ring[y * bitmap.Width + x] = 0;

            for (var distance = 1; distance <= depth + 1; distance++)
            {
                var discovered = new List<int>();
                for (var y = 0; y < bitmap.Height; y++)
                for (var x = 0; x < bitmap.Width; x++)
                {
                    var pixel = y * bitmap.Width + x;
                    if (ring[pixel] >= 0) continue;
                    if ((x > 0 && ring[pixel - 1] == distance - 1) ||
                        (x + 1 < bitmap.Width && ring[pixel + 1] == distance - 1) ||
                        (y > 0 && ring[pixel - bitmap.Width] == distance - 1) ||
                        (y + 1 < bitmap.Height && ring[pixel + bitmap.Width] == distance - 1))
                        discovered.Add(pixel);
                }
                foreach (var pixel in discovered) ring[pixel] = distance;
            }

            for (var y = 0; y < bitmap.Height; y++)
            for (var x = 0; x < bitmap.Width; x++)
            {
                var pixel = y * bitmap.Width + x;
                if (ring[pixel] < 1 || ring[pixel] > depth) continue;
                var offset = y * stride + x * 4;
                var cb = (double)bytes[offset];
                var cg = (double)bytes[offset + 1];
                var cr = (double)bytes[offset + 2];
                var maximum = Math.Max(cr, Math.Max(cg, cb));
                var minimum = Math.Min(cr, Math.Min(cg, cb));
                var luminance = (cr * 54 + cg * 183 + cb * 19) / 256;
                if (maximum - minimum > 24 || luminance < 60 || luminance > 205) continue;

                var bestForeground = -1;
                var bestDistance = int.MaxValue;
                for (var dy = -searchRadius; dy <= searchRadius; dy++)
                for (var dx = -searchRadius; dx <= searchRadius; dx++)
                {
                    var nx = x + dx;
                    var ny = y + dy;
                    var squared = dx * dx + dy * dy;
                    if (squared == 0 || squared > searchRadius * searchRadius || squared >= bestDistance ||
                        nx < 0 || ny < 0 || nx >= bitmap.Width || ny >= bitmap.Height)
                        continue;
                    var neighbour = ny * bitmap.Width + nx;
                    var neighbourOffset = ny * stride + nx * 4;
                    if ((ring[neighbour] < 0 || ring[neighbour] > depth) && bytes[neighbourOffset + 3] > 180)
                    {
                        bestForeground = neighbourOffset;
                        bestDistance = squared;
                    }
                }
                if (bestForeground < 0) continue;
                var fb = (double)bytes[bestForeground];
                var fg = (double)bytes[bestForeground + 1];
                var fr = (double)bytes[bestForeground + 2];
                var foregroundLuminance = (fr * 54 + fg * 183 + fb * 19) / 256;
                if (luminance < foregroundLuminance + 28) continue;

                const double backdrop = 246;
                var db = fb - backdrop;
                var dg = fg - backdrop;
                var dr = fr - backdrop;
                var denominator = db * db + dg * dg + dr * dr;
                if (denominator < 1100) continue;
                var alpha = ((cb - backdrop) * db + (cg - backdrop) * dg + (cr - backdrop) * dr) / denominator;
                alpha = Math.Max(0, Math.Min(1, alpha));
                if (alpha >= 0.985) continue;
                if (alpha <= 0.025)
                {
                    bytes[offset] = bytes[offset + 1] = bytes[offset + 2] = bytes[offset + 3] = 0;
                    continue;
                }
                var recoveredB = (cb - (1 - alpha) * backdrop) / alpha;
                var recoveredG = (cg - (1 - alpha) * backdrop) / alpha;
                var recoveredR = (cr - (1 - alpha) * backdrop) / alpha;
                const double interiorWeight = 0.45;
                bytes[offset] = ClampByte(recoveredB * (1 - interiorWeight) + fb * interiorWeight);
                bytes[offset + 1] = ClampByte(recoveredG * (1 - interiorWeight) + fg * interiorWeight);
                bytes[offset + 2] = ClampByte(recoveredR * (1 - interiorWeight) + fr * interiorWeight);
                bytes[offset + 3] = ClampByte(bytes[offset + 3] * alpha);
            }
            SanitizeTransparentRgb(bytes, bitmap.Width, bitmap.Height, stride);
            Marshal.Copy(bytes, 0, data.Scan0, bytes.Length);
        }
        finally { bitmap.UnlockBits(data); }
    }

    private static void RemoveSmallNeutralBoundaryIslands(Bitmap bitmap)
    {
        const int maximumIslandPixels = 20;
        var rectangle = new Rectangle(0, 0, bitmap.Width, bitmap.Height);
        var data = bitmap.LockBits(rectangle, ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
        try
        {
            var stride = Math.Abs(data.Stride);
            var bytes = new byte[stride * bitmap.Height];
            Marshal.Copy(data.Scan0, bytes, 0, bytes.Length);
            var candidates = new bool[bitmap.Width * bitmap.Height];
            for (var y = 0; y < bitmap.Height; y++)
            for (var x = 0; x < bitmap.Width; x++)
            {
                var offset = y * stride + x * 4;
                if (bytes[offset + 3] <= 8) continue;
                var b = bytes[offset];
                var g = bytes[offset + 1];
                var r = bytes[offset + 2];
                var maximum = Math.Max(r, Math.Max(g, b));
                var minimum = Math.Min(r, Math.Min(g, b));
                var luminance = (r * 54 + g * 183 + b * 19) / 256;
                if (maximum - minimum > 16 || luminance < 35 || luminance > 205) continue;
                var touchesTransparent = false;
                for (var dy = -1; dy <= 1 && !touchesTransparent; dy++)
                for (var dx = -1; dx <= 1; dx++)
                {
                    if (dx == 0 && dy == 0) continue;
                    var nx = x + dx;
                    var ny = y + dy;
                    if (nx < 0 || ny < 0 || nx >= bitmap.Width || ny >= bitmap.Height ||
                        bytes[ny * stride + nx * 4 + 3] <= 8)
                    {
                        touchesTransparent = true;
                        break;
                    }
                }
                candidates[y * bitmap.Width + x] = touchesTransparent;
            }

            var visited = new bool[candidates.Length];
            for (var start = 0; start < candidates.Length; start++)
            {
                if (!candidates[start] || visited[start]) continue;
                var component = new List<int>();
                var queue = new Queue<int>();
                visited[start] = true;
                queue.Enqueue(start);
                while (queue.Count > 0)
                {
                    var pixel = queue.Dequeue();
                    component.Add(pixel);
                    var x = pixel % bitmap.Width;
                    var y = pixel / bitmap.Width;
                    for (var dy = -1; dy <= 1; dy++)
                    for (var dx = -1; dx <= 1; dx++)
                    {
                        if (dx == 0 && dy == 0) continue;
                        var nx = x + dx;
                        var ny = y + dy;
                        if (nx < 0 || ny < 0 || nx >= bitmap.Width || ny >= bitmap.Height) continue;
                        var next = ny * bitmap.Width + nx;
                        if (!candidates[next] || visited[next]) continue;
                        visited[next] = true;
                        queue.Enqueue(next);
                    }
                }
                if (component.Count > maximumIslandPixels) continue;
                foreach (var pixel in component)
                {
                    var x = pixel % bitmap.Width;
                    var y = pixel / bitmap.Width;
                    var offset = y * stride + x * 4;
                    bytes[offset] = bytes[offset + 1] = bytes[offset + 2] = bytes[offset + 3] = 0;
                }
            }
            SanitizeTransparentRgb(bytes, bitmap.Width, bitmap.Height, stride);
            Marshal.Copy(bytes, 0, data.Scan0, bytes.Length);
        }
        finally { bitmap.UnlockBits(data); }
    }

    public static MotionReport BuildMotion(
        string sourcePath, int columns, int rows, int frameWidth, string outputPath,
        bool removeNeutralMatteShadow)
    {
        using (var loaded = new Bitmap(sourcePath))
        using (var clean = CleanGeneratedBackdrop(loaded))
        {
            var frameCount = columns * rows;
            var cells = new Bitmap[frameCount];
            var bounds = new Rectangle[frameCount];
            var rendered = new Bitmap[frameCount];
            try
            {
                for (var index = 0; index < frameCount; index++)
                {
                    // Attack sheets receive a larger conceptual gutter because
                    // generated trails commonly trespass into the next 2x2 cell.
                    // Idle/hurt poses keep the gentler separator to preserve hair.
                    var separatorDivisor = frameWidth > StandardFrameWidth ? 6 : 18;
                    cells[index] = ExtractCell(clean, columns, rows, index, separatorDivisor);
                    bounds[index] = AlphaBounds(cells[index]);
                }

                var referenceHeight = Math.Max(1, bounds[0].Height);
                var maximumHeight = bounds.Max(item => item.Height);
                var scale = Math.Min(
                    ReferenceActorHeight / (double)referenceHeight,
                    (FrameHeight - TopGutter - BottomGutter) / (double)maximumHeight);

                for (var index = 0; index < frameCount; index++)
                {
                    rendered[index] = RenderFrame(cells[index], bounds[index], scale, frameWidth);
                    if (removeNeutralMatteShadow)
                    {
                        RemoveNeutralMatteShadow(rendered[index]);
                        RemoveSmallNeutralBoundaryIslands(rendered[index]);
                    }
                }

                Directory.CreateDirectory(Path.GetDirectoryName(outputPath));
                using (var strip = new Bitmap(frameWidth * frameCount, FrameHeight, PixelFormat.Format32bppArgb))
                {
                    using (var graphics = Graphics.FromImage(strip))
                    {
                        graphics.CompositingMode = CompositingMode.SourceCopy;
                        graphics.Clear(Color.Transparent);
                        for (var index = 0; index < frameCount; index++)
                            graphics.DrawImageUnscaled(rendered[index], index * frameWidth, 0);
                    }
                    SanitizeTransparentRgb(strip);
                    strip.Save(outputPath, ImageFormat.Png);
                }

                var renderedBounds = rendered.Select(AlphaBounds).ToArray();
                return new MotionReport {
                    Name = Path.GetFileName(outputPath),
                    FrameCount = frameCount,
                    ScaleX = scale,
                    ScaleY = scale,
                    MinimumWidth = renderedBounds.Min(item => item.Width),
                    MaximumWidth = renderedBounds.Max(item => item.Width),
                    MinimumHeight = renderedBounds.Min(item => item.Height),
                    MaximumHeight = renderedBounds.Max(item => item.Height),
                };
            }
            finally
            {
                foreach (var bitmap in rendered) if (bitmap != null) bitmap.Dispose();
                foreach (var bitmap in cells) if (bitmap != null) bitmap.Dispose();
            }
        }
    }

    public static void CopyFirstFrame(string stripPath, string outputPath)
    {
        using (var strip = new Bitmap(stripPath))
        using (var output = new Bitmap(StandardFrameWidth, FrameHeight, PixelFormat.Format32bppArgb))
        {
            using (var graphics = Graphics.FromImage(output))
            {
                graphics.CompositingMode = CompositingMode.SourceCopy;
                graphics.Clear(Color.Transparent);
                graphics.DrawImage(strip, new Rectangle(0, 0, StandardFrameWidth, FrameHeight),
                    new Rectangle(0, 0, StandardFrameWidth, FrameHeight), GraphicsUnit.Pixel);
            }
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath));
            SanitizeTransparentRgb(output);
            output.Save(outputPath, ImageFormat.Png);
        }
    }

    public static void BuildPortrait(string sourcePath, string outputPath)
    {
        using (var loaded = new Bitmap(sourcePath))
        using (var clean = CleanGeneratedBackdrop(loaded))
        {
            RemoveTinyIslands(clean, 6);
            var bounds = AlphaBounds(clean);
            const int width = 384;
            const int height = 512;
            const int padding = 14;
            var scale = Math.Min(
                (width - padding * 2) / (double)bounds.Width,
                (height - padding * 2) / (double)bounds.Height);
            var drawWidth = Math.Max(1, (int)Math.Round(bounds.Width * scale));
            var drawHeight = Math.Max(1, (int)Math.Round(bounds.Height * scale));
            var drawX = (width - drawWidth) / 2;
            var drawY = height - padding - drawHeight;
            using (var output = new Bitmap(width, height, PixelFormat.Format32bppArgb))
            {
                using (var graphics = Graphics.FromImage(output))
                {
                    graphics.CompositingMode = CompositingMode.SourceCopy;
                    graphics.Clear(Color.Transparent);
                    graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    graphics.DrawImage(clean, new Rectangle(drawX, drawY, drawWidth, drawHeight), bounds, GraphicsUnit.Pixel);
                }
                SanitizeTransparentRgb(output);
                Directory.CreateDirectory(Path.GetDirectoryName(outputPath));
                output.Save(outputPath, ImageFormat.Png);
            }
        }
    }

    public static bool SanitizeFile(string path)
    {
        Bitmap output;
        using (var loaded = new Bitmap(path)) output = ToArgb(loaded);
        var changed = false;
        var rectangle = new Rectangle(0, 0, output.Width, output.Height);
        var data = output.LockBits(rectangle, ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
        try
        {
            var stride = Math.Abs(data.Stride);
            var bytes = new byte[stride * output.Height];
            Marshal.Copy(data.Scan0, bytes, 0, bytes.Length);
            for (var y = 0; y < output.Height; y++)
            for (var x = 0; x < output.Width; x++)
            {
                var offset = y * stride + x * 4;
                if (bytes[offset + 3] > 8 ||
                    (bytes[offset] == 0 && bytes[offset + 1] == 0 && bytes[offset + 2] == 0 && bytes[offset + 3] == 0))
                    continue;
                bytes[offset] = bytes[offset + 1] = bytes[offset + 2] = bytes[offset + 3] = 0;
                changed = true;
            }
            if (changed) Marshal.Copy(bytes, 0, data.Scan0, bytes.Length);
        }
        finally { output.UnlockBits(data); }

        try
        {
            if (!changed) return false;
            var temporaryPath = path + ".alpha-sanitize.tmp.png";
            output.Save(temporaryPath, ImageFormat.Png);
            File.Copy(temporaryPath, path, true);
            File.Delete(temporaryPath);
            return true;
        }
        finally { output.Dispose(); }
    }

    public static void BuildComboPlate(string sourcePath, string outputPath)
    {
        using (var loaded = new Bitmap(sourcePath))
        using (var clean = CleanGeneratedBackdrop(loaded))
        {
            RemoveTinyIslands(clean, 8);
            var bounds = AlphaBounds(clean);
            using (var output = new Bitmap(220, 104, PixelFormat.Format32bppArgb))
            {
                using (var graphics = Graphics.FromImage(output))
                {
                    graphics.CompositingMode = CompositingMode.SourceCopy;
                    graphics.Clear(Color.Transparent);
                    graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    graphics.DrawImage(clean, new Rectangle(2, 2, 216, 100), bounds, GraphicsUnit.Pixel);
                }
                if (output.GetPixel(110, 52).A <= 220)
                    throw new InvalidDataException("Combo plate centre must be opaque.");
                SanitizeTransparentRgb(output);
                Directory.CreateDirectory(Path.GetDirectoryName(outputPath));
                output.Save(outputPath, ImageFormat.Png);
            }
        }
    }
}
'@
}

$workspace = Split-Path -Parent $PSScriptRoot
$output = Join-Path $workspace $OutputRoot
$sourceRoot = Join-Path $workspace 'artifacts/imagegen/motions-v3'
$portraitSourceRoot = Join-Path $workspace 'artifacts/imagegen/portraits-v4'

$expectedSources = @{
    'combo-source.png' = '6ca8cb58b87a384120a4ea6bf8776d840fe680893301793590dff49bc4ce761a'
    'default-attack-source.png' = '14b1d9a669b96583bde932ecb6cf88276d7234899748a0facf53c803da5e0231'
    'default-hurt-source.png' = 'f8132ee49ed7a2c6354cbf87c5a44e09e2b544e19c06bfe708c7c346737cbcd1'
    'default-idle-source.png' = '222994d1ec0bda78ed00197c574c02e83e64ce98d858dd2cbd9fb72a639daa35'
    'enhancer_lyra-attack-source.png' = 'a71d6becfaffaa1535f3f5d63b5f5f17aff6da94a64604b95c6bf120c704e3fc'
    'enhancer_lyra-hurt-source.png' = '07824f26dd8113d7bf947a45a3136804ba262cf1abf3af8da4c1fdd8afee76d4'
    'enhancer_lyra-idle-source.png' = '3e792b228ff6d0721bc6ac5ed0f5e4ddbf0c1ec9162317e1fa920787610a343c'
    'guardian_mireille-attack-source.png' = '4e9710ea3e03801fbd00ce39a98abb145e6fad74f4e546dd0c8b35802e0a27c9'
    'guardian_mireille-hurt-source.png' = 'f53906cd383949bad618f1c483992491559d7d150e7f0227fe6cc10e09e54ab2'
    'guardian_mireille-idle-source.png' = '1b230c37d94c60b81ea65464479b2b68889ed51122a6cbab41285df7e9d301e7'
    'tactician_yuno-attack-source.png' = '86648316c9838f0b8f918a61e39836ab5be626401f1c8a6ccba90099f750abf2'
    'tactician_yuno-hurt-source.png' = '350ed92d690c58ec1faba5c2572c0cf81b3863e04e21845c61fda85ca873766d'
    'tactician_yuno-idle-source.png' = '16f5bc2c685ec59767f615bfa80267230e3611e44fd818c431bcb359ae4c9af1'
}

$expectedPortraitSources = @{
    'default-portrait-source.png' = 'a7e1899e64a656db2389c6c87c7a6ff3af502797c742f5fe556f1b9c6c53c721'
    'enhancer_lyra-portrait-source.png' = 'e7eb8488430d7c90152b5dce275cf7871d1db7fb9e26bd2601d0e6047cab223d'
    'guardian_mireille-portrait-source.png' = '5ef1131422b0a542a58b10deb9a1c66f827719e7417b9b87892d67d5e229b744'
    'tactician_yuno-portrait-source.png' = '9324fc7643626a2edd55459fe108230bb0319ad4edba5a06190905b293fe4c9f'
}

foreach ($entry in $expectedSources.GetEnumerator()) {
    $path = Join-Path $sourceRoot $entry.Key
    if (-not (Test-Path -LiteralPath $path)) { throw "Missing generated source: $($entry.Key)" }
    $actual = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actual -ne $entry.Value) {
        throw "Generated source changed: $($entry.Key) (expected $($entry.Value), actual $actual). Re-audit before accepting it."
    }
}

foreach ($entry in $expectedPortraitSources.GetEnumerator()) {
    $path = Join-Path $portraitSourceRoot $entry.Key
    if (-not (Test-Path -LiteralPath $path)) { throw "Missing generated portrait source: $($entry.Key)" }
    $actual = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actual -ne $entry.Value) {
        throw "Generated portrait source changed: $($entry.Key) (expected $($entry.Value), actual $actual). Re-audit before accepting it."
    }
}

$heroIds = @('default', 'guardian_mireille', 'tactician_yuno', 'enhancer_lyra')
$reports = @()
foreach ($heroId in $heroIds) {
    foreach ($motion in @(
        @{ Name='idle'; Columns=4; Rows=2; FrameWidth=192 },
        @{ Name='attack'; Columns=2; Rows=2; FrameWidth=384 },
        @{ Name='hurt'; Columns=2; Rows=2; FrameWidth=192 }
    )) {
        $sourcePath = Join-Path $sourceRoot "${heroId}-$($motion.Name)-source.png"
        $outputPath = Join-Path $output "characters/${heroId}_$($motion.Name).png"
        $reports += [RcgPresentationAssetProcessor]::BuildMotion(
            $sourcePath, $motion.Columns, $motion.Rows, $motion.FrameWidth, $outputPath,
            ($heroId -eq 'tactician_yuno'))
    }

    [RcgPresentationAssetProcessor]::CopyFirstFrame(
        (Join-Path $output "characters/${heroId}_idle.png"),
        (Join-Path $output "characters/${heroId}_stand.png"))
    [RcgPresentationAssetProcessor]::BuildPortrait(
        (Join-Path $portraitSourceRoot "${heroId}-portrait-source.png"),
        (Join-Path $output "characters/portraits/${heroId}.png"))
}

[RcgPresentationAssetProcessor]::BuildComboPlate(
    (Join-Path $sourceRoot 'combo-source.png'),
    (Join-Path $output 'ui/battle-control-combo.png'))

$sanitizedFiles = 0
foreach ($assetRoot in @(
    (Join-Path $output 'characters'),
    (Join-Path $output 'icons/potions')
)) {
    if (-not (Test-Path -LiteralPath $assetRoot)) { continue }
    foreach ($asset in Get-ChildItem -LiteralPath $assetRoot -Filter '*.png' -File -Recurse) {
        if ([RcgPresentationAssetProcessor]::SanitizeFile($asset.FullName)) { $sanitizedFiles++ }
    }
}

$reports | ForEach-Object {
    Write-Host ("{0}: frames={1}, scale={2:N3}/{3:N3}, bounds={4}-{5}x{6}-{7}" -f `
        $_.Name, $_.FrameCount, $_.ScaleX, $_.ScaleY,
        $_.MinimumWidth, $_.MaximumWidth, $_.MinimumHeight, $_.MaximumHeight)
}
Write-Host "Character presentation assets written to $output"
Write-Host "Transparent RGB sanitized in $sanitizedFiles production PNG(s)."
