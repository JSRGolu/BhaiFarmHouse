using System;
using System.Collections.Generic;
using UnityEngine;

namespace BeaconIsles
{
    internal sealed class IslandTileMap
    {
        static readonly Vector2Int[] Neighbors =
        {
            Vector2Int.right, Vector2Int.up, Vector2Int.left, Vector2Int.down
        };

        readonly bool[,] land;
        readonly bool[,] obstacles;
        readonly HashSet<Vector2Int> trees = new HashSet<Vector2Int>();
        readonly List<Vector2Int> walkable;

        public int Width => land.GetLength(0);
        public int Height => land.GetLength(1);
        public Vector2Int Dock { get; }
        public Vector2Int[] Beacons { get; }

        public IslandTileMap(Vector2[] outline, int resolution, int seed = 0, float obstacleDensity = 0.12f)
        {
            Vector2 min = outline[0], max = outline[0];
            foreach (Vector2 point in outline)
            {
                min = Vector2.Min(min, point);
                max = Vector2.Max(max, point);
            }

            Vector2 span = max - min;
            float scale = resolution / Mathf.Max(span.x, span.y);
            land = new bool[Mathf.CeilToInt(span.x * scale) + 2, Mathf.CeilToInt(span.y * scale) + 2];
            for (int y = 1; y < Height - 1; y++)
            {
                for (int x = 1; x < Width - 1; x++)
                {
                    Vector2 sample = min + new Vector2(x - 0.5f, y - 0.5f) / scale;
                    land[x, y] = Inside(outline, sample);
                }
            }

            obstacles = new bool[Width, Height];

            // Narrow peninsulas can disconnect when converted to tiles.
            // Use the largest connected landmass so every goal is reachable.
            walkable = LargestLandmass();
            if (walkable.Count < 4)
                throw new InvalidOperationException("This outline produced fewer than four connected land tiles.");

            var connected = new HashSet<Vector2Int>(walkable);
            for (int y = 0; y < Height; y++)
                for (int x = 0; x < Width; x++)
                    land[x, y] = connected.Contains(new Vector2Int(x, y));

            // Choose a corner before placing objectives or buildings. The closest
            // connected land tile provides a valid spawn even on irregular coastlines.
            var spawnRandom = new System.Random(unchecked(seed ^ 0x5F3759DF));
            int corner = spawnRandom.Next(4);
            var cornerTile = new Vector2Int(
                (corner & 1) == 0 ? 0 : Width - 1,
                (corner & 2) == 0 ? 0 : Height - 1);
            Dock = walkable[0];
            int nearestCornerDistance = int.MaxValue;
            foreach (Vector2Int tile in walkable)
            {
                int dx = tile.x - cornerTile.x;
                int dy = tile.y - cornerTile.y;
                int distance = dx * dx + dy * dy;
                if (distance < nearestCornerDistance)
                {
                    nearestCornerDistance = distance;
                    Dock = tile;
                }
            }

            // Spread the beacons by maximizing distance along walkable tiles.
            Beacons = new Vector2Int[3];
            int[,] nearest = DistancesFrom(Dock);
            for (int i = 0; i < Beacons.Length; i++)
            {
                Vector2Int farthest = Dock;
                foreach (Vector2Int tile in walkable)
                    if (nearest[tile.x, tile.y] > nearest[farthest.x, farthest.y])
                        farthest = tile;

                Beacons[i] = farthest;
                if (i + 1 == Beacons.Length)
                    break;
                int[,] distance = DistancesFrom(farthest);
                foreach (Vector2Int tile in walkable)
                    nearest[tile.x, tile.y] = Math.Min(nearest[tile.x, tile.y], distance[tile.x, tile.y]);
            }
            PlaceObstacles(seed, obstacleDensity);
        }

        public bool HasTree(Vector2Int tile) => trees.Contains(tile);

        public bool IsStreet(Vector2Int tile) => IsLand(tile) && (tile.x % 4 == 0 || tile.y % 4 == 0);

        public bool IsObstacle(Vector2Int tile) => IsLand(tile) && obstacles[tile.x, tile.y];

        public bool IsWalkable(Vector2Int tile) => IsLand(tile) && !obstacles[tile.x, tile.y];

        void PlaceObstacles(int seed, float density)
        {
            var random = new System.Random(seed);
            var candidates = new List<Vector2Int>(walkable);
            for (int i = candidates.Count - 1; i > 0; i--)
            {
                int j = random.Next(i + 1);
                Vector2Int swap = candidates[i];
                candidates[i] = candidates[j];
                candidates[j] = swap;
            }

            int target = (int)(walkable.Count * Math.Max(0, Math.Min(0.35, density)));
            int placed = 0;
            foreach (Vector2Int tile in candidates)
            {
                if (placed >= target)
                    break;
                // Two facing rows per block, with a shared garden strip and streets.
                if (IsCoast(tile) || tile.x % 4 == 0 || tile.x % 4 == 3 ||
                    tile.y % 4 == 0 || tile.y % 4 == 2)
                    continue;
                Vector2Int entrance = tile + (tile.y % 4 == 1 ? Vector2Int.down : Vector2Int.up);
                if (!IsLand(entrance))
                    continue;
                if (Near(tile, Dock) || Array.Exists(Beacons, beacon => Near(tile, beacon)))
                    continue;
                obstacles[tile.x, tile.y] = true;
                int[,] reachable = DistancesFrom(Dock);
                bool connected = true;
                foreach (Vector2Int landTile in walkable)
                {
                    if (IsWalkable(landTile) && reachable[landTile.x, landTile.y] == 0)
                    {
                        connected = false;
                        break;
                    }
                }
                if (connected)
                {
                    placed++;
                    // Independent 40% chance per accepted house. Tree art occupies
                    // its garden edge within the same blocked tile.
                    if (random.NextDouble() < 0.4)
                        trees.Add(tile);
                }
                else
                    obstacles[tile.x, tile.y] = false;
            }
        }

        static bool Near(Vector2Int a, Vector2Int b)
        {
            return Math.Abs(a.x - b.x) + Math.Abs(a.y - b.y) <= 1;
        }

        public bool IsLand(Vector2Int tile)
        {
            return tile.x >= 0 && tile.y >= 0 && tile.x < Width && tile.y < Height && land[tile.x, tile.y];
        }

        public bool IsCoast(Vector2Int tile)
        {
            foreach (Vector2Int step in Neighbors)
                if (!IsLand(tile + step))
                    return true;
            return false;
        }

        List<Vector2Int> LargestLandmass()
        {
            var largest = new List<Vector2Int>();
            var seen = new bool[Width, Height];
            var queue = new Queue<Vector2Int>();
            for (int y = 1; y < Height - 1; y++)
            {
                for (int x = 1; x < Width - 1; x++)
                {
                    if (!land[x, y] || seen[x, y])
                        continue;
                    var component = new List<Vector2Int>();
                    queue.Enqueue(new Vector2Int(x, y));
                    seen[x, y] = true;
                    while (queue.Count > 0)
                    {
                        Vector2Int tile = queue.Dequeue();
                        component.Add(tile);
                        foreach (Vector2Int step in Neighbors)
                        {
                            Vector2Int next = tile + step;
                            if (!IsLand(next) || seen[next.x, next.y])
                                continue;
                            seen[next.x, next.y] = true;
                            queue.Enqueue(next);
                        }
                    }
                    if (component.Count > largest.Count)
                        largest = component;
                }
            }
            return largest;
        }

        int[,] DistancesFrom(Vector2Int start)
        {
            var distance = new int[Width, Height];
            var queue = new Queue<Vector2Int>();
            queue.Enqueue(start);
            distance[start.x, start.y] = 1;
            while (queue.Count > 0)
            {
                Vector2Int tile = queue.Dequeue();
                foreach (Vector2Int step in Neighbors)
                {
                    Vector2Int next = tile + step;
                    if (!IsWalkable(next) || distance[next.x, next.y] != 0)
                        continue;
                    distance[next.x, next.y] = distance[tile.x, tile.y] + 1;
                    queue.Enqueue(next);
                }
            }
            return distance;
        }

        static bool Inside(Vector2[] polygon, Vector2 point)
        {
            bool inside = false;
            for (int i = 0, j = polygon.Length - 1; i < polygon.Length; j = i++)
            {
                Vector2 a = polygon[i], b = polygon[j];
                if ((a.y > point.y) != (b.y > point.y)
                    && point.x < a.x + (b.x - a.x) * (point.y - a.y) / (b.y - a.y))
                    inside = !inside;
            }
            return inside;
        }
    }
}
