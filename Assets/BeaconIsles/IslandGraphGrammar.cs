using System;
using System.Collections.Generic;
using UnityEngine;

namespace BeaconIsles
{
    /// <summary>A bounded graph grammar for a single closed, straight-edged 2D outline.</summary>
    public sealed class IslandGraphGrammar
    {
        const double Epsilon = 0.000001;
        const int MaxPaths = 12000;
        const int MaxEdges = 96;

        readonly List<Vector2> directions = new List<Vector2>();
        readonly List<List<double>> sampleLengths = new List<List<double>>();
        readonly Dictionary<(int, int), int> turns = new Dictionary<(int, int), int>();
        readonly Dictionary<(int, int, int), List<int[]>> rules =
            new Dictionary<(int, int, int), List<int[]>>();
        readonly Vector2[] source;
        readonly int[] startEdges;
        readonly double[] startLengths;
        readonly double minLength;
        readonly double maxLength;
        readonly int ruleCorners;

        public IslandGraphGrammar(IReadOnlyList<Vector2> outline, int ruleCorners = 6)
        {
            this.ruleCorners = Mathf.Clamp(ruleCorners, 1, 12);
            source = Normalize(outline);
            startEdges = new int[source.Length];
            startLengths = new double[source.Length];
            double shortest = double.PositiveInfinity;
            double longest = 0;
            for (int i = 0; i < source.Length; i++)
            {
                Vector2 edge = source[(i + 1) % source.Length] - source[i];
                double length = edge.magnitude;
                Vector2 direction = edge / (float)length;
                int label = directions.FindIndex(d => (d - direction).sqrMagnitude <= Epsilon * Epsilon);
                if (label < 0)
                {
                    label = directions.Count;
                    directions.Add(direction);
                    sampleLengths.Add(new List<double>());
                }
                startEdges[i] = label;
                startLengths[i] = length;
                sampleLengths[label].Add(length);
                shortest = Math.Min(shortest, length);
                longest = Math.Max(longest, length);
            }
            minLength = shortest / 5;
            maxLength = longest * 5;
            LearnRules();
        }

        void LearnRules()
        {
            var successors = new List<int>[directions.Count];
            var angles = new double[directions.Count];
            var frontier = new List<(int[] edges, int winding)>();
            for (int i = 0; i < directions.Count; i++)
            {
                successors[i] = new List<int>();
                angles[i] = Math.Atan2(directions[i].y, directions[i].x);
                frontier.Add((new[] { i }, 0));
            }
            for (int i = 0; i < startEdges.Length; i++)
            {
                int a = startEdges[i], b = startEdges[(i + 1) % startEdges.Length];
                if (turns.ContainsKey((a, b)))
                    continue;
                successors[a].Add(b);
                double delta = angles[b] - angles[a];
                turns.Add((a, b), delta < -Math.PI ? 1 : delta > Math.PI ? -1 : 0);
            }

            // A primitive is one observed corner. Walk matching half-edges to
            // build paths and group them by connectors and signed full turns.
            int pathCount = 0;
            for (int depth = 0; depth < ruleCorners && frontier.Count > 0; depth++)
            {
                var children = new List<(int[], int)>();
                foreach (var path in frontier)
                {
                    int last = path.edges[path.edges.Length - 1];
                    foreach (int next in successors[last])
                    {
                        int[] edges = new int[path.edges.Length + 1];
                        Array.Copy(path.edges, edges, path.edges.Length);
                        edges[edges.Length - 1] = next;
                        int winding = path.winding + turns[(last, next)];
                        var boundary = (edges[0], next, winding);
                        if (!rules.TryGetValue(boundary, out var replacements))
                        {
                            replacements = new List<int[]>();
                            rules.Add(boundary, replacements);
                        }
                        replacements.Add(edges);
                        children.Add((edges, winding));
                        if (++pathCount >= MaxPaths)
                            return;
                    }
                }
                frontier = children;
            }
        }

        /// <summary>Generate an outline in local coordinates from a repeatable seed.</summary>
        public Vector2[] Generate(int seed, int rewrites = 4)
        {
            var random = new System.Random(seed);
            int[] edges = startEdges;
            double[] lengths = startLengths;
            Vector2[] best = null;
            int remaining = Mathf.Clamp(rewrites, 1, 16);
            for (int attempt = 0; attempt < 180; attempt++)
            {
                if (attempt % 20 == 0)
                {
                    edges = startEdges;
                    lengths = startLengths;
                }
                if (!Propose(random, edges, lengths, out var proposal, out var guess))
                    continue;
                for (int sample = 0; sample < 5; sample++)
                {
                    if (!Draw(random, proposal, guess, out var fitted, out var polygon))
                        continue;
                    edges = proposal;
                    lengths = fitted;
                    if (!SameCycle(startEdges, edges))
                    {
                        best = polygon;
                        if (--remaining == 0)
                            return best;
                    }
                    break;
                }
            }

            if (best != null)
                return best;
            // Shapes like a rectangle can still vary through their proportions.
            for (int attempt = 0; attempt < 24; attempt++)
                if (Draw(random, startEdges, startLengths, out _, out var polygon))
                    return polygon;
            return (Vector2[])source.Clone();
        }

        bool Propose(System.Random random, int[] edges, double[] lengths,
            out int[] proposal, out double[] guess)
        {
            int count = edges.Length;
            int first = random.Next(count);
            for (int offset = 0; offset < count; offset++)
            {
                int start = (first + offset) % count;
                int winding = 0;
                int maxSpan = Math.Min(ruleCorners + 1, count);
                var matches = new List<(int span, List<int[]> paths)>();
                for (int span = 2; span <= maxSpan; span++)
                {
                    int a = edges[(start + span - 2) % count];
                    int b = edges[(start + span - 1) % count];
                    winding += turns[(a, b)];
                    if (rules.TryGetValue((edges[start], b, winding), out var paths) && paths.Count > 1)
                        matches.Add((span, paths));
                }

                if (matches.Count == 0)
                    continue;
                int firstMatch = random.Next(matches.Count);
                for (int m = 0; m < matches.Count; m++)
                {
                    var match = matches[(firstMatch + m) % matches.Count];
                    int firstPath = random.Next(match.paths.Count);
                    for (int i = 0; i < match.paths.Count; i++)
                    {
                        int[] replacement = match.paths[(firstPath + i) % match.paths.Count];
                        int size = count - match.span + replacement.Length;
                        if (size < 3 || size > MaxEdges || MatchesPath(edges, start, match.span, replacement))
                            continue;

                        proposal = new int[size];
                        guess = new double[size];
                        Array.Copy(replacement, proposal, replacement.Length);
                        guess[0] = lengths[start];
                        for (int j = 1; j < replacement.Length - 1; j++)
                        {
                            var samples = sampleLengths[replacement[j]];
                            guess[j] = samples[random.Next(samples.Count)];
                        }
                        guess[replacement.Length - 1] = lengths[(start + match.span - 1) % count];
                        for (int j = match.span; j < count; j++)
                        {
                            int destination = replacement.Length + j - match.span;
                            proposal[destination] = edges[(start + j) % count];
                            guess[destination] = lengths[(start + j) % count];
                        }
                        return true;
                    }
                }
            }
            proposal = null;
            guess = null;
            return false;
        }

        bool Draw(System.Random random, int[] edges, double[] guess,
            out double[] fitted, out Vector2[] polygon)
        {
            fitted = new double[edges.Length];
            polygon = null;
            double xx = 0, xy = 0, yy = 0, dx = 0, dy = 0;
            for (int i = 0; i < edges.Length; i++)
            {
                var direction = directions[edges[i]];
                double x = direction.x, y = direction.y;
                fitted[i] = guess[i] * (0.45 + 1.10 * random.NextDouble());
                xx += x * x;
                xy += x * y;
                yy += y * y;
                dx += x * fitted[i];
                dy += y * fitted[i];
            }
            // Project the sampled lengths onto the two closure equations.
            double determinant = xx * yy - xy * xy;
            if (determinant <= Epsilon * Epsilon)
                return false;
            double horizontal = (yy * dx - xy * dy) / determinant;
            double vertical = (xx * dy - xy * dx) / determinant;
            var points = new Vector2[edges.Length];
            double px = 0, py = 0;
            for (int i = 0; i < edges.Length; i++)
            {
                var direction = directions[edges[i]];
                double length = fitted[i] - horizontal * direction.x - vertical * direction.y;
                if (length < minLength || length > maxLength)
                    return false;
                fitted[i] = length;
                points[i] = new Vector2((float)px, (float)py);
                px += direction.x * length;
                py += direction.y * length;
            }
            if (Math.Abs(px) > Epsilon || Math.Abs(py) > Epsilon || !IsSimple(points))
                return false;
            polygon = points;
            return true;
        }

        static bool MatchesPath(int[] cycle, int start, int span, int[] path)
        {
            if (span != path.Length)
                return false;
            for (int i = 0; i < span; i++)
                if (cycle[(start + i) % cycle.Length] != path[i])
                    return false;
            return true;
        }

        static bool SameCycle(int[] a, int[] b)
        {
            if (a.Length != b.Length)
                return false;
            for (int start = 0; start < a.Length; start++)
                if (MatchesPath(a, start, a.Length, b))
                    return true;
            return false;
        }

        static Vector2[] Normalize(IReadOnlyList<Vector2> outline)
        {
            if (outline == null || outline.Count < 3)
                throw new ArgumentException("Provide at least three outline points.");
            int count = outline.Count;
            if (outline[0].Equals(outline[count - 1]))
                count--;

            Vector2 minimum = outline[0], maximum = outline[0];
            for (int i = 0; i < count; i++)
            {
                Vector2 point = outline[i];
                if (float.IsNaN(point.x) || float.IsNaN(point.y) ||
                    float.IsInfinity(point.x) || float.IsInfinity(point.y))
                    throw new ArgumentException("All outline coordinates must be finite.");
                minimum = Vector2.Min(minimum, point);
                maximum = Vector2.Max(maximum, point);
            }
            Vector2 span = maximum - minimum;
            float scale = Mathf.Max(span.x, span.y);
            if (scale <= 0 || float.IsInfinity(scale))
                throw new ArgumentException("The outline needs a finite, positive width or height.");

            var points = new Vector2[count];
            for (int i = 0; i < count; i++)
                points[i] = (outline[i] - minimum) / scale;

            var corners = new List<Vector2>();
            for (int i = 0; i < count; i++)
            {
                Vector2 a = points[(i + count - 1) % count];
                Vector2 b = points[i];
                Vector2 c = points[(i + 1) % count];
                Vector2 incoming = b - a, outgoing = c - b;
                double lengths = incoming.magnitude * (double)outgoing.magnitude;
                if (incoming.sqrMagnitude <= Epsilon * Epsilon)
                    throw new ArgumentException("Consecutive points must be distinct.");
                if (Math.Abs(Cross(a, b, c)) > Epsilon * lengths)
                    corners.Add(b);
                else if (Vector2.Dot(incoming, outgoing) < 0)
                    throw new ArgumentException("The outline doubles back along an edge.");
            }
            if (corners.Count < 3 || corners.Count > MaxEdges)
                throw new ArgumentException($"Use an outline with 3 to {MaxEdges} corners.");
            if (TwiceArea(corners) < 0)
                corners.Reverse();
            if (!IsSimple(corners))
                throw new ArgumentException("The outline must enclose an area without crossing or touching itself.");
            return corners.ToArray();
        }

        static double Cross(Vector2 a, Vector2 b, Vector2 c)
        {
            return ((double)b.x - a.x) * ((double)c.y - a.y)
                - ((double)b.y - a.y) * ((double)c.x - a.x);
        }

        static double TwiceArea(IReadOnlyList<Vector2> points)
        {
            double area = 0;
            for (int i = 1; i < points.Count - 1; i++)
                area += Cross(points[0], points[i], points[i + 1]);
            return area;
        }

        static bool IsSimple(IReadOnlyList<Vector2> points)
        {
            if (TwiceArea(points) <= Epsilon * Epsilon)
                return false;
            int count = points.Count;
            for (int i = 0; i < count; i++)
            {
                for (int j = i + 2; j < count; j++)
                {
                    if (i == 0 && j == count - 1)
                        continue;
                    if (Intersects(points[i], points[(i + 1) % count], points[j], points[(j + 1) % count]))
                        return false;
                }
            }
            return true;
        }

        static bool Intersects(Vector2 a, Vector2 b, Vector2 c, Vector2 d)
        {
            int abC = Side(a, b, c), abD = Side(a, b, d);
            int cdA = Side(c, d, a), cdB = Side(c, d, b);
            return (abC * abD < 0 && cdA * cdB < 0)
                || (abC == 0 && Between(a, b, c))
                || (abD == 0 && Between(a, b, d))
                || (cdA == 0 && Between(c, d, a))
                || (cdB == 0 && Between(c, d, b));
        }

        static int Side(Vector2 a, Vector2 b, Vector2 point)
        {
            double cross = Cross(a, b, point);
            double tolerance = Epsilon * (b - a).magnitude;
            return cross > tolerance ? 1 : cross < -tolerance ? -1 : 0;
        }

        static bool Between(Vector2 a, Vector2 b, Vector2 point)
        {
            return point.x >= Math.Min(a.x, b.x) - Epsilon && point.x <= Math.Max(a.x, b.x) + Epsilon
                && point.y >= Math.Min(a.y, b.y) - Epsilon && point.y <= Math.Max(a.y, b.y) + Epsilon;
        }
    }
}
