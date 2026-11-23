param(
    [string]$OutputRoot = "artifacts/raster-audit/normalized"
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

if (-not ('RcgRasterNormalizer' -as [type])) {
    Add-Type -ReferencedAssemblies System.Drawing -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;

public static class RcgRasterNormalizer
{
    private sealed class Component
    {
        public readonly List<Point> Pixels = new List<Point>();
        public Rectangle Bounds;
        public bool TouchesEdge;
    }

    public static Bitmap RemoveConnectedLightBackdrop(Bitmap source)
    {
        var result = new Bitmap(source.Width, source.Height, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(result)) g.DrawImageUnscaled(source, 0, 0);

        var visited = new bool[result.Width * result.Height];
        var queue = new Queue<Point>();
        Action<int, int> enqueue = (x, y) => {
            if (x < 0 || y < 0 || x >= result.Width || y >= result.Height) return;
            var index = y * result.Width + x;
            if (visited[index]) return;
            var color = result.GetPixel(x, y);
            var max = Math.Max(color.R, Math.Max(color.G, color.B));
            var min = Math.Min(color.R, Math.Min(color.G, color.B));
            if (min < 222 || max - min > 24) return;
            visited[index] = true;
            queue.Enqueue(new Point(x, y));
        };

        for (var x = 0; x < result.Width; x++) { enqueue(x, 0); enqueue(x, result.Height - 1); }
        for (var y = 0; y < result.Height; y++) { enqueue(0, y); enqueue(result.Width - 1, y); }

        while (queue.Count > 0)
        {
            var point = queue.Dequeue();
            result.SetPixel(point.X, point.Y, Color.Transparent);
            enqueue(point.X - 1, point.Y);
            enqueue(point.X + 1, point.Y);
            enqueue(point.X, point.Y - 1);
            enqueue(point.X, point.Y + 1);
        }

        return result;
    }

    public static Bitmap RemoveConnectedChromaBackdrop(Bitmap source)
    {
        var result = new Bitmap(source.Width, source.Height, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(result)) g.DrawImageUnscaled(source, 0, 0);

        var visited = new bool[result.Width * result.Height];
        var queue = new Queue<Point>();
        Action<int, int> enqueue = (x, y) => {
            if (x < 0 || y < 0 || x >= result.Width || y >= result.Height) return;
            var index = y * result.Width + x;
            if (visited[index]) return;
            var color = result.GetPixel(x, y);
            // The generated sheet uses a flat #00ff00 stage. Keep this close
            // enough to absorb its antialiased fringe, but narrow enough that
            // the Tactician's darker green magic remains intact.
            if (color.G < 220 || color.R > 82 || color.B > 82
                || color.G - color.R < 125 || color.G - color.B < 125) return;
            visited[index] = true;
            queue.Enqueue(new Point(x, y));
        };

        for (var x = 0; x < result.Width; x++) { enqueue(x, 0); enqueue(x, result.Height - 1); }
        for (var y = 0; y < result.Height; y++) { enqueue(0, y); enqueue(result.Width - 1, y); }
        while (queue.Count > 0)
        {
            var point = queue.Dequeue();
            result.SetPixel(point.X, point.Y, Color.Transparent);
            enqueue(point.X - 1, point.Y);
            enqueue(point.X + 1, point.Y);
            enqueue(point.X, point.Y - 1);
            enqueue(point.X, point.Y + 1);
        }

        // Image generators blend the solid stage colour into the outermost
        // antialias pixels. Once the connected backdrop is gone those pixels
        // become a thin neon-green halo. Peel only green-dominant boundary
        // pixels in three shallow passes; interior class-green art remains.
        for (var pass = 0; pass < 3; pass++)
        {
            var remove = new List<Point>();
            for (var y = 1; y < result.Height - 1; y++)
            for (var x = 1; x < result.Width - 1; x++)
            {
                var color = result.GetPixel(x, y);
                if (color.A <= 8) continue;
                var chromaEdge = color.G >= 72
                    && color.G - color.R >= 38
                    && color.G - color.B >= 38
                    && color.G >= Math.Max(color.R, color.B) * 1.42;
                if (!chromaEdge) continue;
                var touchesTransparency = false;
                for (var ny = y - 1; ny <= y + 1 && !touchesTransparency; ny++)
                for (var nx = x - 1; nx <= x + 1; nx++)
                    if (result.GetPixel(nx, ny).A <= 8) { touchesTransparency = true; break; }
                if (touchesTransparency) remove.Add(new Point(x, y));
            }
            foreach (var point in remove) result.SetPixel(point.X, point.Y, Color.Transparent);
        }
        return result;
    }

    public static void RemoveChromaDominantRows(Bitmap source, int rows, int[] rowIndexes)
    {
        var selected = new HashSet<int>(rowIndexes);
        for (var y = 0; y < source.Height; y++)
        {
            var row = Math.Min(rows - 1, (int)Math.Floor(y * rows / (double)source.Height));
            if (!selected.Contains(row)) continue;
            for (var x = 0; x < source.Width; x++)
            {
                var color = source.GetPixel(x, y);
                if (color.A <= 8) continue;
                if (color.G >= 62
                    && color.G - color.R >= 30
                    && color.G - color.B >= 30
                    && color.G >= Math.Max(color.R, color.B) * 1.34)
                    source.SetPixel(x, y, Color.Transparent);
            }
        }
    }

    public static Bitmap ExtractControlPlate(
        Bitmap atlas,
        int left,
        int top,
        int width,
        int height,
        int targetWidth,
        int targetHeight,
        int backgroundStep)
    {
        using (var crop = new Bitmap(width, height, PixelFormat.Format32bppArgb))
        {
            using (var g = Graphics.FromImage(crop))
            {
                g.CompositingMode = CompositingMode.SourceCopy;
                g.DrawImage(atlas, new Rectangle(0, 0, width, height),
                    new Rectangle(left, top, width, height), GraphicsUnit.Pixel);
            }
            using (var isolated = RemoveSmoothConnectedBackdrop(crop, backgroundStep))
            {
                var bounds = AlphaBounds(isolated);
                var output = new Bitmap(targetWidth, targetHeight, PixelFormat.Format32bppArgb);
                using (var g = Graphics.FromImage(output))
                {
                    g.CompositingMode = CompositingMode.SourceCopy;
                    g.Clear(Color.Transparent);
                    g.InterpolationMode = InterpolationMode.NearestNeighbor;
                    g.PixelOffsetMode = PixelOffsetMode.Half;
                    g.DrawImage(isolated,
                        new Rectangle(1, 1, targetWidth - 2, targetHeight - 2),
                        bounds,
                        GraphicsUnit.Pixel);
                }
                return output;
            }
        }
    }

    private static Bitmap RemoveSmoothConnectedBackdrop(Bitmap source, int maxStep)
    {
        var output = new Bitmap(source.Width, source.Height, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(output)) g.DrawImageUnscaled(source, 0, 0);
        var visited = new bool[source.Width * source.Height];
        var queue = new Queue<Point>();
        Action<int, int> seed = (x, y) => {
            var index = y * source.Width + x;
            if (visited[index]) return;
            visited[index] = true;
            queue.Enqueue(new Point(x, y));
        };
        for (var x = 0; x < source.Width; x++) { seed(x, 0); seed(x, source.Height - 1); }
        for (var y = 0; y < source.Height; y++) { seed(0, y); seed(source.Width - 1, y); }
        var directions = new[] {
            new Point(0,-1), new Point(-1,0), new Point(1,0), new Point(0,1)
        };
        while (queue.Count > 0)
        {
            var point = queue.Dequeue();
            var current = source.GetPixel(point.X, point.Y);
            output.SetPixel(point.X, point.Y, Color.Transparent);
            foreach (var direction in directions)
            {
                var nx = point.X + direction.X; var ny = point.Y + direction.Y;
                if (nx < 0 || ny < 0 || nx >= source.Width || ny >= source.Height) continue;
                var index = ny * source.Width + nx;
                if (visited[index]) continue;
                var next = source.GetPixel(nx, ny);
                var step = Math.Max(Math.Abs(current.R - next.R),
                    Math.Max(Math.Abs(current.G - next.G), Math.Abs(current.B - next.B)));
                if (step > maxStep) continue;
                visited[index] = true;
                queue.Enqueue(new Point(nx, ny));
            }
        }
        return output;
    }

    public static Bitmap ExtractNormalizedCell(
        Bitmap atlas,
        int columns,
        int rows,
        int column,
        int row,
        int targetWidth,
        int targetHeight,
        int padding)
    {
        var left = (int)Math.Floor(column * atlas.Width / (double)columns);
        var right = (int)Math.Floor((column + 1) * atlas.Width / (double)columns);
        var top = (int)Math.Floor(row * atlas.Height / (double)rows);
        var bottom = (int)Math.Floor((row + 1) * atlas.Height / (double)rows);
        var cell = new Bitmap(right - left, bottom - top, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(cell))
        {
            g.CompositingMode = CompositingMode.SourceCopy;
            g.DrawImage(atlas, new Rectangle(0, 0, cell.Width, cell.Height),
                new Rectangle(left, top, cell.Width, cell.Height), GraphicsUnit.Pixel);
        }

        using (cell)
        using (var isolated = KeepCentralComponents(cell))
            return Normalize(isolated, targetWidth, targetHeight, padding);
    }

    public static Bitmap ExtractNormalizedCellWithOverflow(
        Bitmap atlas,
        int columns,
        int rows,
        int column,
        int row,
        int targetWidth,
        int targetHeight,
        int padding)
    {
        var nominalLeft = (int)Math.Floor(column * atlas.Width / (double)columns);
        var nominalRight = (int)Math.Floor((column + 1) * atlas.Width / (double)columns);
        var nominalTop = (int)Math.Floor(row * atlas.Height / (double)rows);
        var nominalBottom = (int)Math.Floor((row + 1) * atlas.Height / (double)rows);
        var nominalWidth = nominalRight - nominalLeft;
        var nominalHeight = nominalBottom - nominalTop;

        // Generated contact sheets rarely respect their conceptual grid exactly:
        // hats, horns, staves and hair often cross 15-20% into the previous row.
        // Crop a deliberately overlapping region first, then identify the actor by
        // how many of its pixels live in the nominal cell. This recovers the crossed
        // silhouette without importing the neighbouring actor.
        var marginX = Math.Max(12, (int)Math.Round(nominalWidth * 0.24));
        var marginY = Math.Max(16, (int)Math.Round(nominalHeight * 0.30));
        var cropLeft = Math.Max(0, nominalLeft - marginX);
        var cropRight = Math.Min(atlas.Width, nominalRight + marginX);
        var cropTop = Math.Max(0, nominalTop - marginY);
        var cropBottom = Math.Min(atlas.Height, nominalBottom + marginY);

        using (var cell = new Bitmap(cropRight - cropLeft, cropBottom - cropTop,
            PixelFormat.Format32bppArgb))
        {
            using (var g = Graphics.FromImage(cell))
            {
                g.CompositingMode = CompositingMode.SourceCopy;
                g.DrawImage(atlas, new Rectangle(0, 0, cell.Width, cell.Height),
                    new Rectangle(cropLeft, cropTop, cell.Width, cell.Height), GraphicsUnit.Pixel);
            }

            var nominalInCell = Rectangle.FromLTRB(
                nominalLeft - cropLeft,
                nominalTop - cropTop,
                nominalRight - cropLeft,
                nominalBottom - cropTop);
            using (var isolated = KeepAnchoredComponents(cell, nominalInCell))
                return Normalize(isolated, targetWidth, targetHeight, padding);
        }
    }

    public static Bitmap ExtractNormalizedVerticalOverflow(
        Bitmap atlas,
        int sourceLeft,
        int sourceRight,
        int nominalTop,
        int nominalBottom,
        int targetWidth,
        int targetHeight,
        int padding)
    {
        var nominalHeight = nominalBottom - nominalTop;
        var marginY = Math.Max(16, (int)Math.Round(nominalHeight * 0.30));
        var cropTop = Math.Max(0, nominalTop - marginY);
        var cropBottom = Math.Min(atlas.Height, nominalBottom + marginY);
        using (var cell = new Bitmap(sourceRight - sourceLeft, cropBottom - cropTop,
            PixelFormat.Format32bppArgb))
        {
            using (var g = Graphics.FromImage(cell))
            {
                g.CompositingMode = CompositingMode.SourceCopy;
                g.DrawImage(atlas, new Rectangle(0, 0, cell.Width, cell.Height),
                    new Rectangle(sourceLeft, cropTop, cell.Width, cell.Height), GraphicsUnit.Pixel);
            }
            var nominalInCell = Rectangle.FromLTRB(
                0, nominalTop - cropTop, cell.Width, nominalBottom - cropTop);
            using (var isolated = KeepAnchoredComponents(cell, nominalInCell))
                return Normalize(isolated, targetWidth, targetHeight, padding);
        }
    }

    public static Bitmap ExtractPreservingScaleCell(
        Bitmap atlas,
        int columns,
        int column,
        int targetWidth,
        int targetHeight,
        int padding)
    {
        var left = (int)Math.Floor(column * atlas.Width / (double)columns);
        var right = (int)Math.Floor((column + 1) * atlas.Width / (double)columns);
        using (var cell = new Bitmap(right - left, atlas.Height, PixelFormat.Format32bppArgb))
        {
            using (var g = Graphics.FromImage(cell))
            {
                g.CompositingMode = CompositingMode.SourceCopy;
                g.DrawImage(atlas, new Rectangle(0, 0, cell.Width, cell.Height),
                    new Rectangle(left, 0, cell.Width, atlas.Height), GraphicsUnit.Pixel);
            }
            using (var isolated = KeepCentralComponents(cell))
            {
                var bounds = AlphaBounds(isolated);
                var scale = Math.Min(1.0, Math.Min(
                    (targetWidth - padding * 2) / (double)Math.Max(1, bounds.Width),
                    (targetHeight - padding * 2) / (double)Math.Max(1, bounds.Height)));
                return NormalizeAtScale(isolated, bounds, targetWidth, targetHeight, padding, scale);
            }
        }
    }

    public static Bitmap[] ExtractUniformHeroRow(
        Bitmap atlas,
        int row,
        int targetWidth,
        int targetHeight,
        int padding)
    {
        var isolated = new Bitmap[4];
        var bounds = new Rectangle[4];
        try
        {
            for (var column = 0; column < 4; column++)
            {
                var left = (int)Math.Floor(column * atlas.Width / 4.0);
                var right = (int)Math.Floor((column + 1) * atlas.Width / 4.0);
                var top = (int)Math.Floor(row * atlas.Height / 4.0);
                var bottom = (int)Math.Floor((row + 1) * atlas.Height / 4.0);
                using (var cell = new Bitmap(right - left, bottom - top, PixelFormat.Format32bppArgb))
                {
                    using (var g = Graphics.FromImage(cell))
                    {
                        g.CompositingMode = CompositingMode.SourceCopy;
                        g.DrawImage(atlas, new Rectangle(0, 0, cell.Width, cell.Height),
                            new Rectangle(left, top, cell.Width, cell.Height), GraphicsUnit.Pixel);
                    }
                    isolated[column] = KeepCentralComponents(cell);
                    bounds[column] = AlphaBounds(isolated[column]);
                }
            }

            var maxWidth = bounds.Max(b => b.Width);
            var maxHeight = bounds.Max(b => b.Height);
            var scale = Math.Min(
                (targetWidth - padding * 2) / (double)Math.Max(1, maxWidth),
                (targetHeight - padding * 2) / (double)Math.Max(1, maxHeight));
            var result = new Bitmap[4];
            for (var i = 0; i < 4; i++)
                result[i] = NormalizeAtScale(isolated[i], bounds[i], targetWidth, targetHeight, padding, scale);
            return result;
        }
        finally
        {
            foreach (var image in isolated) if (image != null) image.Dispose();
        }
    }

    public static Bitmap[] ExtractUniformHeroRowWithOverflow(
        Bitmap atlas,
        int row,
        int targetWidth,
        int targetHeight,
        int padding)
    {
        var isolated = new Bitmap[4];
        var bounds = new Rectangle[4];
        try
        {
            for (var column = 0; column < 4; column++)
            {
                var nominalLeft = (int)Math.Floor(column * atlas.Width / 4.0);
                var nominalRight = (int)Math.Floor((column + 1) * atlas.Width / 4.0);
                var nominalTop = (int)Math.Floor(row * atlas.Height / 4.0);
                var nominalBottom = (int)Math.Floor((row + 1) * atlas.Height / 4.0);
                var marginX = Math.Max(12, (int)Math.Round((nominalRight - nominalLeft) * 0.18));
                var marginY = Math.Max(16, (int)Math.Round((nominalBottom - nominalTop) * 0.20));
                var cropLeft = Math.Max(0, nominalLeft - marginX);
                var cropRight = Math.Min(atlas.Width, nominalRight + marginX);
                var cropTop = Math.Max(0, nominalTop - marginY);
                var cropBottom = Math.Min(atlas.Height, nominalBottom + marginY);
                using (var cell = new Bitmap(cropRight - cropLeft, cropBottom - cropTop,
                    PixelFormat.Format32bppArgb))
                {
                    using (var g = Graphics.FromImage(cell))
                    {
                        g.CompositingMode = CompositingMode.SourceCopy;
                        g.DrawImage(atlas, new Rectangle(0, 0, cell.Width, cell.Height),
                            new Rectangle(cropLeft, cropTop, cell.Width, cell.Height), GraphicsUnit.Pixel);
                    }
                    var nominalInCell = Rectangle.FromLTRB(
                        nominalLeft - cropLeft, nominalTop - cropTop,
                        nominalRight - cropLeft, nominalBottom - cropTop);
                    isolated[column] = KeepAnchoredComponents(cell, nominalInCell);
                    bounds[column] = AlphaBounds(isolated[column]);
                }
            }

            var maxWidth = bounds.Max(bound => bound.Width);
            var maxHeight = bounds.Max(bound => bound.Height);
            var scale = Math.Min(
                (targetWidth - padding * 2) / (double)Math.Max(1, maxWidth),
                (targetHeight - padding * 2) / (double)Math.Max(1, maxHeight));
            var result = new Bitmap[4];
            for (var i = 0; i < 4; i++)
                result[i] = NormalizeAtScale(isolated[i], bounds[i], targetWidth, targetHeight, padding, scale);
            return result;
        }
        finally
        {
            foreach (var image in isolated) if (image != null) image.Dispose();
        }
    }

    public static Bitmap TransformFrame(
        Bitmap source,
        double scaleX,
        double scaleY,
        int offsetX,
        int offsetY)
    {
        var bounds = AlphaBounds(source);
        var output = new Bitmap(source.Width, source.Height, PixelFormat.Format32bppArgb);
        var drawWidth = Math.Max(1, (int)Math.Round(bounds.Width * scaleX));
        var drawHeight = Math.Max(1, (int)Math.Round(bounds.Height * scaleY));
        var drawX = (source.Width - drawWidth) / 2 + offsetX;
        var drawY = bounds.Bottom - drawHeight + offsetY;
        using (var g = Graphics.FromImage(output))
        {
            g.CompositingMode = CompositingMode.SourceCopy;
            g.Clear(Color.Transparent);
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            g.DrawImage(source, new Rectangle(drawX, drawY, drawWidth, drawHeight), bounds, GraphicsUnit.Pixel);
        }
        return output;
    }

    public static void SaveStrip(string path, Bitmap[] sourceFrames, int[] sequence)
    {
        if (sequence.Length == 0) throw new ArgumentException("Sequence cannot be empty.");
        var width = sourceFrames[0].Width;
        var height = sourceFrames[0].Height;
        using (var strip = new Bitmap(width * sequence.Length, height, PixelFormat.Format32bppArgb))
        using (var g = Graphics.FromImage(strip))
        {
            g.CompositingMode = CompositingMode.SourceCopy;
            g.Clear(Color.Transparent);
            for (var i = 0; i < sequence.Length; i++)
                g.DrawImageUnscaled(sourceFrames[sequence[i]], i * width, 0);
            SavePng(strip, path);
        }
    }

    public static void SavePng(Bitmap image, string path)
    {
        var directory = Path.GetDirectoryName(path);
        if (!String.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        image.Save(path, ImageFormat.Png);
    }

    private static Bitmap KeepCentralComponents(Bitmap source)
    {
        var width = source.Width;
        var height = source.Height;
        var visited = new bool[width * height];
        var components = new List<Component>();
        var edgeGuard = Math.Max(2, Math.Min(width, height) / 16);
        var directions = new[] {
            new Point(-1,-1), new Point(0,-1), new Point(1,-1), new Point(-1,0),
            new Point(1,0), new Point(-1,1), new Point(0,1), new Point(1,1)
        };

        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            var index = y * width + x;
            if (visited[index] || source.GetPixel(x, y).A <= 8) continue;
            var component = new Component();
            var queue = new Queue<Point>();
            queue.Enqueue(new Point(x, y));
            visited[index] = true;
            var minX = x; var maxX = x; var minY = y; var maxY = y;
            while (queue.Count > 0)
            {
                var point = queue.Dequeue();
                component.Pixels.Add(point);
                minX = Math.Min(minX, point.X); maxX = Math.Max(maxX, point.X);
                minY = Math.Min(minY, point.Y); maxY = Math.Max(maxY, point.Y);
                if (point.X < edgeGuard || point.Y < edgeGuard
                    || point.X >= width - edgeGuard || point.Y >= height - edgeGuard)
                    component.TouchesEdge = true;
                foreach (var direction in directions)
                {
                    var nx = point.X + direction.X; var ny = point.Y + direction.Y;
                    if (nx < 0 || ny < 0 || nx >= width || ny >= height) continue;
                    var nextIndex = ny * width + nx;
                    if (visited[nextIndex] || source.GetPixel(nx, ny).A <= 8) continue;
                    visited[nextIndex] = true;
                    queue.Enqueue(new Point(nx, ny));
                }
            }
            component.Bounds = Rectangle.FromLTRB(minX, minY, maxX + 1, maxY + 1);
            components.Add(component);
        }

        var output = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        if (components.Count == 0) return output;
        var center = new PointF(width / 2f, height / 2f);
        var largest = components
            .OrderByDescending(c => c.Pixels.Count)
            .ThenBy(c => DistanceTo(center, c.Bounds))
            .First();
        var threshold = Math.Max(4, largest.Pixels.Count / 600);
        var expanded = Rectangle.Inflate(largest.Bounds, Math.Max(14, width / 9), Math.Max(14, height / 9));
        var keep = components.Where(component =>
            ReferenceEquals(component, largest)
            || (!component.TouchesEdge
                && component.Pixels.Count >= threshold
                && (expanded.IntersectsWith(component.Bounds)
                    || DistanceBetween(expanded, component.Bounds) <= Math.Max(10, width / 12))));

        foreach (var component in keep)
        foreach (var point in component.Pixels)
            output.SetPixel(point.X, point.Y, source.GetPixel(point.X, point.Y));
        return output;
    }

    private static Bitmap KeepAnchoredComponents(Bitmap source, Rectangle anchor)
    {
        var width = source.Width;
        var height = source.Height;
        var visited = new bool[width * height];
        var components = new List<Component>();
        var edgeGuard = Math.Max(2, Math.Min(width, height) / 24);
        var directions = new[] {
            new Point(-1,-1), new Point(0,-1), new Point(1,-1), new Point(-1,0),
            new Point(1,0), new Point(-1,1), new Point(0,1), new Point(1,1)
        };

        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            var index = y * width + x;
            if (visited[index] || source.GetPixel(x, y).A <= 8) continue;
            var component = new Component();
            var queue = new Queue<Point>();
            queue.Enqueue(new Point(x, y));
            visited[index] = true;
            var minX = x; var maxX = x; var minY = y; var maxY = y;
            while (queue.Count > 0)
            {
                var point = queue.Dequeue();
                component.Pixels.Add(point);
                minX = Math.Min(minX, point.X); maxX = Math.Max(maxX, point.X);
                minY = Math.Min(minY, point.Y); maxY = Math.Max(maxY, point.Y);
                if (point.X < edgeGuard || point.Y < edgeGuard
                    || point.X >= width - edgeGuard || point.Y >= height - edgeGuard)
                    component.TouchesEdge = true;
                foreach (var direction in directions)
                {
                    var nx = point.X + direction.X; var ny = point.Y + direction.Y;
                    if (nx < 0 || ny < 0 || nx >= width || ny >= height) continue;
                    var nextIndex = ny * width + nx;
                    if (visited[nextIndex] || source.GetPixel(nx, ny).A <= 8) continue;
                    visited[nextIndex] = true;
                    queue.Enqueue(new Point(nx, ny));
                }
            }
            component.Bounds = Rectangle.FromLTRB(minX, minY, maxX + 1, maxY + 1);
            components.Add(component);
        }

        var output = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        if (components.Count == 0) return output;

        Func<Component, int> pixelsInsideAnchor = component =>
            component.Pixels.Count(point => anchor.Contains(point));
        var center = new PointF(anchor.Left + anchor.Width / 2f, anchor.Top + anchor.Height / 2f);
        var main = components
            .OrderByDescending(pixelsInsideAnchor)
            .ThenByDescending(component => component.Pixels.Count)
            .ThenBy(component => DistanceTo(center, component.Bounds))
            .First();
        var threshold = Math.Max(3, main.Pixels.Count / 700);
        var expandedMain = Rectangle.Inflate(main.Bounds,
            Math.Max(12, anchor.Width / 8), Math.Max(12, anchor.Height / 8));
        var expandedAnchor = Rectangle.Inflate(anchor,
            Math.Max(6, anchor.Width / 12), Math.Max(8, anchor.Height / 12));

        var keep = components.Where(component =>
            ReferenceEquals(component, main)
            || (!component.TouchesEdge
                && component.Pixels.Count >= threshold
                && pixelsInsideAnchor(component) > 0
                && (expandedMain.IntersectsWith(component.Bounds)
                    || expandedAnchor.Contains(new Point(
                        component.Bounds.Left + component.Bounds.Width / 2,
                        component.Bounds.Top + component.Bounds.Height / 2)))));

        foreach (var component in keep)
        foreach (var point in component.Pixels)
            output.SetPixel(point.X, point.Y, source.GetPixel(point.X, point.Y));
        return output;
    }

    private static Bitmap Normalize(Bitmap source, int targetWidth, int targetHeight, int padding)
    {
        var bounds = AlphaBounds(source);
        var scale = Math.Min(
            (targetWidth - padding * 2) / (double)Math.Max(1, bounds.Width),
            (targetHeight - padding * 2) / (double)Math.Max(1, bounds.Height));
        return NormalizeAtScale(source, bounds, targetWidth, targetHeight, padding, scale);
    }

    private static Bitmap NormalizeAtScale(
        Bitmap source,
        Rectangle bounds,
        int targetWidth,
        int targetHeight,
        int padding,
        double scale)
    {
        var output = new Bitmap(targetWidth, targetHeight, PixelFormat.Format32bppArgb);
        var drawWidth = Math.Max(1, (int)Math.Round(bounds.Width * scale));
        var drawHeight = Math.Max(1, (int)Math.Round(bounds.Height * scale));
        var drawX = (targetWidth - drawWidth) / 2;
        var drawY = targetHeight - padding - drawHeight;
        using (var g = Graphics.FromImage(output))
        {
            g.CompositingMode = CompositingMode.SourceCopy;
            g.Clear(Color.Transparent);
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            g.DrawImage(source, new Rectangle(drawX, drawY, drawWidth, drawHeight), bounds, GraphicsUnit.Pixel);
        }
        return output;
    }

    private static Rectangle AlphaBounds(Bitmap source)
    {
        var minX = source.Width; var minY = source.Height; var maxX = -1; var maxY = -1;
        for (var y = 0; y < source.Height; y++)
        for (var x = 0; x < source.Width; x++)
        {
            if (source.GetPixel(x, y).A <= 8) continue;
            minX = Math.Min(minX, x); minY = Math.Min(minY, y);
            maxX = Math.Max(maxX, x); maxY = Math.Max(maxY, y);
        }
        return maxX < minX
            ? new Rectangle(0, 0, 1, 1)
            : Rectangle.FromLTRB(minX, minY, maxX + 1, maxY + 1);
    }

    private static double DistanceTo(PointF point, Rectangle rectangle)
    {
        var cx = rectangle.Left + rectangle.Width / 2.0;
        var cy = rectangle.Top + rectangle.Height / 2.0;
        var dx = point.X - cx; var dy = point.Y - cy;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    private static int DistanceBetween(Rectangle a, Rectangle b)
    {
        var dx = Math.Max(0, Math.Max(a.Left - b.Right, b.Left - a.Right));
        var dy = Math.Max(0, Math.Max(a.Top - b.Bottom, b.Top - a.Bottom));
        return Math.Max(dx, dy);
    }
}
'@
}

$workspace = Split-Path -Parent $PSScriptRoot
$output = Join-Path $workspace $OutputRoot
New-Item -ItemType Directory -Force -Path $output | Out-Null

function Assert-RasterSource {
    param(
        [Parameter(Mandatory = $true)][string]$RelativePath,
        [Parameter(Mandatory = $true)][int]$ExpectedWidth,
        [Parameter(Mandatory = $true)][int]$ExpectedHeight,
        [Parameter(Mandatory = $true)][string]$ExpectedSha256
    )

    $path = Join-Path $workspace $RelativePath
    if (-not (Test-Path -LiteralPath $path)) {
        throw "Required raster source is missing: $RelativePath"
    }

    $actualHash = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actualHash -ne $ExpectedSha256.ToLowerInvariant()) {
        throw "Raster source changed: $RelativePath (expected SHA-256 $ExpectedSha256, actual $actualHash). Re-audit its extraction constants before updating this fingerprint."
    }

    $bitmap = [System.Drawing.Bitmap]::new($path)
    try {
        if ($bitmap.Width -ne $ExpectedWidth -or $bitmap.Height -ne $ExpectedHeight) {
            throw "Raster source dimensions changed: $RelativePath (expected ${ExpectedWidth}x${ExpectedHeight}, actual $($bitmap.Width)x$($bitmap.Height))."
        }
    }
    finally {
        $bitmap.Dispose()
    }

    return $path
}

$heroSourcePath = Assert-RasterSource `
    'artifacts/imagegen/hero-contact-sheet.png' 1254 1254 `
    '7377d71da3508dd66a08a6ab71ea2ee5b058a354e2c3ee509f9160f360245b67'
$generatedHeroSourcePath = Assert-RasterSource `
    'artifacts/imagegen/hero-inbetweens-v2.png' 1254 1254 `
    'ac98a3aee0292ce287023b89a74351a1d0db64cafb70ffd139d488adeb7e9b13'
$controlSourcePath = Assert-RasterSource `
    'artifacts/imagegen/battle-controls-v2.png' 1536 1024 `
    'df9a387fe4695af3aca071a27ec0acddf011dcce5d9798615a4d7aa5bb27e9ef'
$enemyASourcePath = Assert-RasterSource `
    'artifacts/imagegen/enemy-contact-sheet-a.png' 1254 1254 `
    '536d2f62ee5dfa11a762d5eb8bab9c504700766af0db4bfb1be3753cacba631f'
$enemyBSourcePath = Assert-RasterSource `
    'artifacts/imagegen/enemy-contact-sheet-b.png' 1254 1254 `
    'cfdb491dadbd6b752f672b772b17817a428411ae70ccf8d4d1e5411a05cd05ab'

$heroIds = @('default', 'guardian_mireille', 'tactician_yuno', 'enhancer_lyra')
$heroSource = [System.Drawing.Bitmap]::new($heroSourcePath)
$generatedHeroSource = [System.Drawing.Bitmap]::new($generatedHeroSourcePath)
$cleanGeneratedHeroSource = [RcgRasterNormalizer]::RemoveConnectedChromaBackdrop($generatedHeroSource)
# This source-specific matte repair is intentionally destructive. The source
# fingerprint above prevents it from silently running against replacement art
# whose costume or effects may legitimately contain a brighter green.
[RcgRasterNormalizer]::RemoveChromaDominantRows($cleanGeneratedHeroSource, 4, [int[]]@(0, 1, 3))
try {
    [RcgRasterNormalizer]::SavePng(
        $cleanGeneratedHeroSource,
        (Join-Path $output 'sources/hero-inbetweens-clean.png'))

    $idlePlan = @(
        @{ Set='original'; Index=0; ScaleX=1.000; ScaleY=1.000; X=0;  Y=0  },
        @{ Set='extra';    Index=0; ScaleX=1.000; ScaleY=1.000; X=0;  Y=-1 },
        @{ Set='extra';    Index=0; ScaleX=1.004; ScaleY=1.008; X=0;  Y=-2 },
        @{ Set='original'; Index=1; ScaleX=1.000; ScaleY=1.010; X=0;  Y=-2 },
        @{ Set='original'; Index=1; ScaleX=1.005; ScaleY=1.014; X=0;  Y=-3 },
        @{ Set='extra';    Index=0; ScaleX=1.006; ScaleY=1.012; X=0;  Y=-2 },
        @{ Set='extra';    Index=0; ScaleX=1.004; ScaleY=1.008; X=0;  Y=-1 },
        @{ Set='original'; Index=1; ScaleX=1.002; ScaleY=1.004; X=0;  Y=0  },
        @{ Set='extra';    Index=0; ScaleX=0.999; ScaleY=0.998; X=0;  Y=1  },
        @{ Set='extra';    Index=0; ScaleX=0.997; ScaleY=0.995; X=0;  Y=1  },
        @{ Set='original'; Index=0; ScaleX=0.999; ScaleY=0.998; X=0;  Y=0  },
        @{ Set='original'; Index=0; ScaleX=1.000; ScaleY=1.000; X=0;  Y=0  }
    )
    $attackPlan = @(
        @{ Set='original'; Index=0; ScaleX=1.000; ScaleY=1.000; X=0;  Y=0  },
        @{ Set='extra';    Index=1; ScaleX=1.000; ScaleY=1.000; X=-2; Y=0  },
        @{ Set='extra';    Index=1; ScaleX=0.995; ScaleY=0.990; X=-3; Y=1  },
        @{ Set='original'; Index=1; ScaleX=1.000; ScaleY=1.000; X=-2; Y=0  },
        @{ Set='original'; Index=2; ScaleX=1.000; ScaleY=1.000; X=1;  Y=0  },
        @{ Set='original'; Index=2; ScaleX=1.008; ScaleY=1.006; X=2;  Y=0  },
        @{ Set='extra';    Index=2; ScaleX=1.000; ScaleY=1.000; X=2;  Y=0  },
        @{ Set='extra';    Index=2; ScaleX=1.010; ScaleY=1.006; X=1;  Y=0  },
        @{ Set='extra';    Index=3; ScaleX=1.000; ScaleY=1.000; X=4;  Y=1  },
        @{ Set='extra';    Index=3; ScaleX=0.998; ScaleY=0.995; X=2;  Y=1  },
        @{ Set='original'; Index=0; ScaleX=1.000; ScaleY=1.000; X=1;  Y=0  },
        @{ Set='original'; Index=0; ScaleX=1.000; ScaleY=1.000; X=0;  Y=0  }
    )
    $hurtPlan = @(
        @{ Set='original'; Index=0; ScaleX=1.000; ScaleY=1.000; X=0;  Y=0 },
        @{ Set='original'; Index=3; ScaleX=1.000; ScaleY=0.990; X=-2; Y=1 },
        @{ Set='original'; Index=3; ScaleX=0.995; ScaleY=0.970; X=-5; Y=2 },
        @{ Set='extra';    Index=3; ScaleX=0.990; ScaleY=0.960; X=-7; Y=2 },
        @{ Set='extra';    Index=3; ScaleX=0.995; ScaleY=0.970; X=-5; Y=2 },
        @{ Set='original'; Index=3; ScaleX=0.998; ScaleY=0.985; X=-3; Y=1 },
        @{ Set='original'; Index=0; ScaleX=1.000; ScaleY=1.000; X=-1; Y=0 },
        @{ Set='original'; Index=0; ScaleX=1.000; ScaleY=1.000; X=0;  Y=0 }
    )

    for ($heroRow = 0; $heroRow -lt $heroIds.Count; $heroRow++) {
        $heroId = $heroIds[$heroRow]
        # Match the generated in-between poses to the recovered source actor's
        # body scale. The contact sheets use different per-row framing.
        $extraScale = @(0.83, 0.84, 1.00, 0.79)[$heroRow]
        $heroDir = Join-Path $output 'characters'
        $originalFrames = [RcgRasterNormalizer]::ExtractUniformHeroRowWithOverflow(
            $heroSource, $heroRow, 192, 256, 10)
        $extraFrames = [RcgRasterNormalizer]::ExtractUniformHeroRow(
            $cleanGeneratedHeroSource, $heroRow, 192, 256, 10)
        try {
            [RcgRasterNormalizer]::SavePng(
                $originalFrames[0],
                (Join-Path $heroDir "${heroId}_stand.png"))

            foreach ($motionPlan in @(
                @{ Name='idle'; Plan=$idlePlan },
                @{ Name='attack'; Plan=$attackPlan },
                @{ Name='hurt'; Plan=$hurtPlan }
            )) {
                $plan = $motionPlan.Plan
                $frames = New-Object 'System.Drawing.Bitmap[]' $plan.Count
                try {
                    for ($frameIndex = 0; $frameIndex -lt $plan.Count; $frameIndex++) {
                        $entry = $plan[$frameIndex]
                        $sourceFrame = if ($entry.Set -eq 'extra') {
                            $extraFrames[$entry.Index]
                        } else {
                            $originalFrames[$entry.Index]
                        }
                        $sourceScale = if ($entry.Set -eq 'extra') { $extraScale } else { 1.0 }
                        $frames[$frameIndex] = [RcgRasterNormalizer]::TransformFrame(
                            $sourceFrame,
                            ([double]$entry.ScaleX * $sourceScale),
                            ([double]$entry.ScaleY * $sourceScale),
                            [int]$entry.X,
                            [int]$entry.Y)
                    }
                    [RcgRasterNormalizer]::SaveStrip(
                        (Join-Path $heroDir "${heroId}_$($motionPlan.Name).png"),
                        $frames,
                        [int[]](0..($frames.Count - 1)))
                }
                finally {
                    foreach ($frameImage in $frames) {
                        if ($null -ne $frameImage) { $frameImage.Dispose() }
                    }
                }
            }
        }
        finally {
            foreach ($frameImage in $originalFrames) { if ($null -ne $frameImage) { $frameImage.Dispose() } }
            foreach ($frameImage in $extraFrames) { if ($null -ne $frameImage) { $frameImage.Dispose() } }
        }
    }
}
finally {
    $cleanGeneratedHeroSource.Dispose()
    $generatedHeroSource.Dispose()
    $heroSource.Dispose()
}

if (Test-Path -LiteralPath $controlSourcePath) {
    $controlAtlas = [System.Drawing.Bitmap]::new($controlSourcePath)
    try {
        $controlSpecs = @(
            @{ Name='battle-control-tactical'; X=84;  Y=112; W=600; H=330; TargetW=168; TargetH=104 },
            @{ Name='battle-control-ultimate'; X=824; Y=104; W=640; H=340; TargetW=168; TargetH=104 },
            @{ Name='battle-control-combo';    X=18;  Y=570; W=740; H=360; TargetW=220; TargetH=104 },
            @{ Name='battle-control-ready';    X=752; Y=570; W=770; H=350; TargetW=304; TargetH=128 }
        )
        foreach ($spec in $controlSpecs) {
            $plate = [RcgRasterNormalizer]::ExtractControlPlate(
                $controlAtlas,
                $spec.X, $spec.Y, $spec.W, $spec.H,
                $spec.TargetW, $spec.TargetH,
                18)
            try {
                [RcgRasterNormalizer]::SavePng(
                    $plate,
                    (Join-Path $output "ui/$($spec.Name).png"))
            }
            finally { $plate.Dispose() }
        }
    }
    finally { $controlAtlas.Dispose() }
}

$enemyAIds = @(
    @('louse_red','slime_green_s','slime_spike_s','slime_big','slime_king'),
    @('cave_bat_a','cave_bat_b','jaw_worm','wolf_dire','bandit'),
    @('goblin_a','goblin_b','goblin_c','hobgoblin','cultist_dark'),
    @('mushroom_a','mushroom_b','orc_red','orc_blue','ogre'),
    @('iron_golem_a','guardian_golem','iron_golem_b','iron_golem_c','six_ghost')
)
$enemyA = [System.Drawing.Bitmap]::new($enemyASourcePath)
for ($row = 0; $row -lt 5; $row++) {
    for ($column = 0; $column -lt 5; $column++) {
        if ($row -eq 4 -and ($column -eq 2 -or $column -eq 3)) {
            # These two generated actors straddle the nominal x=752 grid line.
            # Alpha occupancy has a clear valley at source x=720; split there
            # while still recovering their heads from the previous row.
            $nominalTop = [int][math]::Floor($row * $enemyA.Height / 5.0)
            $nominalBottom = [int][math]::Floor(($row + 1) * $enemyA.Height / 5.0)
            $sourceLeft = if ($column -eq 2) { [int][math]::Floor(2 * $enemyA.Width / 5.0) } else { 720 }
            $sourceRight = if ($column -eq 2) { 720 } else { [int][math]::Floor(4 * $enemyA.Width / 5.0) }
            $cell = [RcgRasterNormalizer]::ExtractNormalizedVerticalOverflow(
                $enemyA, $sourceLeft, $sourceRight, $nominalTop, $nominalBottom, 192, 256, 10)
        }
        else {
            $cell = [RcgRasterNormalizer]::ExtractNormalizedCellWithOverflow($enemyA, 5, 5, $column, $row, 192, 256, 10)
        }
        try {
            [RcgRasterNormalizer]::SavePng($cell, (Join-Path $output "characters/enemies/$($enemyAIds[$row][$column]).png"))
        }
        finally { $cell.Dispose() }
    }
}
$enemyA.Dispose()

# The late-game source only occupies the upper half: five columns by two rows.
$enemyBIds = @(
    @('dragon_sleep','act2_grunt','act2_brute','act2_elite','act2_boss'),
    @('act3_grunt','act3_brute','act3_elite','act3_boss','wisp')
)
$enemyB = [System.Drawing.Bitmap]::new($enemyBSourcePath)
for ($row = 0; $row -lt 2; $row++) {
    for ($column = 0; $column -lt 5; $column++) {
        $cell = [RcgRasterNormalizer]::ExtractNormalizedCellWithOverflow($enemyB, 5, 4, $column, $row, 192, 256, 10)
        try {
            $relative = if ($enemyBIds[$row][$column] -eq 'wisp') {
                'characters/units/wisp.png'
            } else {
                "characters/enemies/$($enemyBIds[$row][$column]).png"
            }
            [RcgRasterNormalizer]::SavePng($cell, (Join-Path $output $relative))
        }
        finally { $cell.Dispose() }
    }
}
$enemyB.Dispose()

$potionIds = @(
    @('barrier_potion','regeneration_potion','resistance_potion','reflection_potion'),
    @('frenzy_potion','insight_potion','mastery_potion','revival_potion')
)
$potionAtlas = [System.Drawing.Bitmap]::new((Join-Path $workspace 'src/Client/public/icons/atlases/relic_potion_atlas_01.png'))
for ($row = 0; $row -lt 2; $row++) {
    for ($column = 0; $column -lt 4; $column++) {
        $cell = [RcgRasterNormalizer]::ExtractNormalizedCell($potionAtlas, 4, 4, $column, ($row + 2), 180, 180, 20)
        try { [RcgRasterNormalizer]::SavePng($cell, (Join-Path $output "icons/potions/$($potionIds[$row][$column]).png")) }
        finally { $cell.Dispose() }
    }
}
$potionAtlas.Dispose()

# The v3 character motion/portrait sources are intentionally one character and
# one motion per image. Run their dedicated processor last so the legacy contact
# sheet path cannot overwrite the 8/4/4 production strips or the opaque combo
# control plate.
& (Join-Path $PSScriptRoot 'BuildCharacterPresentationAssets.ps1') -OutputRoot $OutputRoot

Write-Host "Normalized raster assets written to $output"
