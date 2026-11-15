using System;
using System.Collections.Generic;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace BeaconIsles
{
    public sealed class BeaconIslesGame : MonoBehaviour
    {
        const int TilePixels = 8;
        const float StepInterval = 0.095f;

        Vector2[] startingOutline =
        {
            new Vector2(0, 0), new Vector2(10, 0), new Vector2(10, 4),
            new Vector2(8, 4), new Vector2(8, 7), new Vector2(6, 7),
            new Vector2(6, 4), new Vector2(4, 4), new Vector2(4, 6),
            new Vector2(2, 6), new Vector2(2, 4), new Vector2(0, 4)
        };
        
        [SerializeField, Range(0, 0.35f)] float obstacleDensity = 0.12f;
        [SerializeField, Range(20, 80)] int mapResolution = 34;
        [SerializeField, Range(1, 12)] int ruleCorners = 6;
        [SerializeField] Camera gameCamera;
        [SerializeField] Material spriteMaterial;
        [SerializeField] Font titleFont;
        [SerializeField] Font commandsFont;
        [SerializeField] Font bodyFont;

        [Header("Music and Sound Effects")]
        [SerializeField] AudioClip backgroundMusic;
        [SerializeField] AudioClip beggarChaseSfx;
        [SerializeField] AudioClip beggarCatchSfx;
        [SerializeField] AudioClip gameOverSfx;
        [SerializeField] AudioClip bishnoiChaseSfx;
        [SerializeField] AudioClip bishnoiCatchSfx;
        [SerializeField] AudioClip levelCompleteSfx;
        [SerializeField] AudioClip guestPickupSfx;
        [SerializeField, Range(0f, 1f)] float musicVolume = 0.2f;
        [SerializeField, Range(0f, 1f)] float sfxVolume = 0.8f;
        [Header("Credits")]
        [SerializeField, TextArea(6, 20)] string creditsText = "Add your team's names and roles here.";
        bool settingsOpen;
        bool creditsOpen;
        float settingsOpenedAt;
        Vector2 creditsScroll;
        GUIStyle settingsHeading;
        GUIStyle settingsButton;
        GUIStyle creditsStyle;
        Texture2D settingsIcon;
        AudioSource musicSource;
        AudioSource effectsSource;
        AudioSource gameOverSource;
        AudioListener createdListener;

        [SerializeField, Range(0, 12)] int roamingHazardCount = 4;
        [SerializeField, Min(1)]
        [Tooltip("One beggar is allowed per this many eligible road tiles. The result is rounded down.")]
        int roadTilesPerBeggar = 5;
        [SerializeField, Range(0f, 1f)]
        [Tooltip("Chance to spawn one Bishnoi on each new level after unlocking. 0.33 means 33%.")]
        float bishnoiSpawnChance = 0.33f;
        readonly List<RoamingHazard> hazards = new List<RoamingHazard>();
        readonly System.Random hazardRandom = new System.Random();
        static readonly Vector2Int[] HazardSteps =
        {
            Vector2Int.right, Vector2Int.up, Vector2Int.left, Vector2Int.down
        };

        sealed class RoamingHazard
        {
            public float LastMoveAt = float.NegativeInfinity;
            public int Steps;
            public bool IsBishnoi;
            public bool IsChasing;
            public Vector2Int Tile;
            public SpriteRenderer Renderer;
            public float NextMove;
        }

        IslandGraphGrammar grammar;
        IslandTileMap map;
        int nextSeed;
        bool generationFailed;
        bool pendingAdvance;
        List<Vector2Int> plannedBeggars = new List<Vector2Int>();
        Vector2Int? plannedBishnoi;
        int collected;
        bool won;
        float nextLevelAt;
        int lives;
        int currentSeed;
        readonly int bishnoiUnlockAfter = new System.Random(Guid.NewGuid().GetHashCode()).Next(4, 7);
        bool spawnBishnoiThisLevel;
        Sprite bishnoiSprite;
        Texture2D bishnoiTexture;
        int levelNumber = 1;
        Sprite playerSprite;
        Texture2D playerTexture;
        float nextStep;
        float lastPlayerMoveAt;
        int playerSteps;
        bool playerHasMoved;
        readonly Dictionary<Sprite, Sprite[]> walkingFrames = new Dictionary<Sprite, Sprite[]>();
        readonly List<Texture2D> walkingTextures = new List<Texture2D>();
        float playerIdleBlend;
        Vector2Int playerTile;
        Transform stage;
        SpriteRenderer terrain;
        SpriteRenderer player;
        SpriteRenderer dock;
        readonly SpriteRenderer[] beacons = new SpriteRenderer[3];
        Sprite beggarSprite;
        Texture2D beggarTexture;
        readonly List<Sprite> objectiveSprites = new List<Sprite>();
        readonly List<Texture2D> objectiveTextures = new List<Texture2D>();
        Sprite marker;
        Texture2D markerTexture;
        Sprite terrainSprite;
        Texture2D terrainTexture;
        Camera createdCamera;
        Material createdMaterial;
        float framedAspect;
        string notice;
        string status;
        Texture2D heartTexture;
        GUIStyle objectiveStyle;
        GUIStyle badgeStyle;
        GUIStyle titleStyle;
        GUIStyle hudStyle;
        GUIStyle commandsStyle;

        void Start()
        {
            try
            {
                grammar = new IslandGraphGrammar(startingOutline, ruleCorners);
            }
            catch (ArgumentException error)
            {
                notice = error.Message;
                Debug.LogError(error.Message, this);
                return;
            }

            if (gameCamera == null)
                gameCamera = Camera.main;
            if (gameCamera == null)
            {
                createdCamera = new GameObject("Beacon Isles Camera").AddComponent<Camera>();
                gameCamera = createdCamera;
            }
            InitializeAudio();
            gameCamera.orthographic = true;
            gameCamera.clearFlags = CameraClearFlags.SolidColor;
            gameCamera.backgroundColor = new Color32(27, 15, 62, 255);

            if (spriteMaterial == null)
            {
                Shader shader = Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default");
                if (shader == null || !shader.isSupported)
                    shader = Shader.Find("Sprites/Default");
                if (shader != null && shader.isSupported)
                    createdMaterial = new Material(shader);
            }

            stage = new GameObject("Beacon Isles Map").transform;
            terrain = AddSprite("Island", null, Color.white, 0, 1);
            markerTexture = MakeMarkerTexture();
            marker = Sprite.Create(markerTexture, new Rect(0, 0, 16, 16),
                new Vector2(0.5f, 0.5f), 16, 0, SpriteMeshType.FullRect);
            dock = AddSprite("Farmhouse entrance", MakeObjectiveSprite(-1), Color.white, 1, 1.9f);
            for (int i = 0; i < beacons.Length; i++)
                beacons[i] = AddSprite($"Guest {i + 1}", MakeObjectiveSprite(i), Color.white, 2, 1.25f);
            playerTexture = MakePlayerTexture();
            playerSprite = Sprite.Create(playerTexture, new Rect(0, 0, 16, 16),
                new Vector2(0.5f, 0.5f), 16, 0, SpriteMeshType.FullRect);
            player = AddSprite("Player", playerSprite, Color.white, 3, 1.35f);
            LoadIsland(new System.Random(Guid.NewGuid().GetHashCode()).Next());
        }

        void LoadIsland(int requestedSeed, bool advanceLevel = false)
        {
            IslandTileMap candidate = null;
            int candidateSeed = requestedSeed;
            int targetLevel = advanceLevel ? levelNumber + 1 : levelNumber;
            bool wantsBishnoi = advanceLevel
                ? targetLevel > bishnoiUnlockAfter && hazardRandom.NextDouble() < Mathf.Clamp01(bishnoiSpawnChance)
                : spawnBishnoiThisLevel;
            string failure = "No valid map found.";
            List<Vector2Int> beggarPlan = null;
            Vector2Int? bishnoiPlan = null;
            for (int attempt = 0; attempt < 20; attempt++, candidateSeed = unchecked(candidateSeed + 1))
            {
                try
                {
                    candidate = new IslandTileMap(grammar.Generate(candidateSeed), Mathf.Clamp(mapResolution, 20, 80), candidateSeed, obstacleDensity);
                    if (VerifyLevel(candidate, targetLevel, wantsBishnoi,
                        out beggarPlan, out bishnoiPlan, out failure))
                        break;
                    candidate = null;
                }
                catch (InvalidOperationException error)
                {
                    failure = error.Message;
                    candidate = null;
                }
            }

            if (candidate == null)
            {
                notice = "Level generation failed after 20 attempts. Press Space to retry. " + failure;
                generationFailed = true;
                pendingAdvance = advanceLevel;
                nextSeed = candidateSeed;
                return;
            }

            notice = null;
            generationFailed = false;
            gameOverSource.Stop();
            if (backgroundMusic != null && !musicSource.isPlaying)
            {
                musicSource.clip = backgroundMusic;
                musicSource.volume = musicVolume;
                musicSource.Play();
            }
            plannedBeggars = beggarPlan;
            plannedBishnoi = bishnoiPlan;
            nextSeed = unchecked(candidateSeed + 1);
            if (advanceLevel)
                levelNumber++;
            spawnBishnoiThisLevel = wantsBishnoi;
            currentSeed = candidateSeed;
            lives = 3;
            map = candidate;
            DrawTerrain();
            playerTile = map.Dock;
            collected = 0;
            won = false;
            nextStep = 0;
            lastPlayerMoveAt = Time.unscaledTime;
            playerSteps = 0;
            playerHasMoved = false;
            player.sprite = playerSprite;
            playerIdleBlend = 0;
            player.transform.localScale = Vector3.one * 1.35f;
            player.transform.localPosition = At(playerTile);
            dock.transform.localPosition = At(map.Dock);
            dock.color = Color.white;
            for (int i = 0; i < beacons.Length; i++)
            {
                beacons[i].transform.localPosition = At(map.Beacons[i]);
                beacons[i].enabled = true;
            }
            if (beggarSprite == null)
            {
                beggarTexture = MakeBeggarTexture();
                beggarSprite = Sprite.Create(beggarTexture, new Rect(0, 0, 16, 16),
                    new Vector2(0.5f, 0.5f), 16, 0, SpriteMeshType.FullRect);
            }
            SpawnHazards();
            SpawnBishnoi();
            UpdateStatus();
            FrameIsland();
        }

        void Update()
        {
            if (grammar == null || settingsOpen)
                return;
            ReadKeyboard(out Vector2Int direction, out bool restartLevel, out bool skipLevel);
            if (generationFailed)
            {
                if (restartLevel || skipLevel)
                    LoadIsland(nextSeed, pendingAdvance);
                return;
            }
            if (skipLevel)
            {
                LoadIsland(nextSeed, map != null);
                return;
            }
            if (restartLevel)
            {
                LoadIsland(map != null ? currentSeed : nextSeed);
                return;
            }
            if (won)
            {
                if (Time.unscaledTime >= nextLevelAt)
                {
                    nextLevelAt = Time.unscaledTime + 1f;
                    LoadIsland(nextSeed, true);
                }
                return;
            }
            if (map == null || lives <= 0)
                return;
            MoveHazards();
            if (HitHazard())
                return;
            if (direction == Vector2Int.zero)
            {
                nextStep = 0;
                return;
            }
            if (Time.unscaledTime < nextStep)
                return;

            nextStep = Time.unscaledTime + StepInterval;
            Vector2Int destination = playerTile + direction;
            if (!map.IsWalkable(destination))
                return;

            playerTile = destination;
            playerSteps++;
            playerHasMoved = true;
            lastPlayerMoveAt = Time.unscaledTime;
            player.transform.localPosition = At(playerTile);
            if (HitHazard())
                return;
            for (int i = 0; i < beacons.Length; i++)
            {
                if (!beacons[i].enabled || playerTile != map.Beacons[i])
                    continue;
                beacons[i].enabled = false;
                collected++;
                PlayEffect(guestPickupSfx);
                if (collected == beacons.Length)
                    dock.color = new Color32(101, 234, 201, 255);
                UpdateStatus();
            }

            if (collected == beacons.Length && playerTile == map.Dock)
            {
                won = true;
                PlayEffect(levelCompleteSfx);
                nextLevelAt = Time.unscaledTime + Mathf.Max(1f,
                    levelCompleteSfx != null ? levelCompleteSfx.length : 0f);
                UpdateStatus();
            }
        }

        bool SafeZone(Vector2Int tile)
        {
            if (Math.Abs(tile.x - map.Dock.x) + Math.Abs(tile.y - map.Dock.y) <= 2)
                return true;
            foreach (Vector2Int beacon in map.Beacons)
                if (Math.Abs(tile.x - beacon.x) + Math.Abs(tile.y - beacon.y) <= 1)
                    return true;
            return false;
        }

        bool CanBeggarWalk(Vector2Int tile)
        {
            return map.IsStreet(tile) && map.IsWalkable(tile) && !SafeZone(tile);
        }

        bool CanHazardWalk(RoamingHazard hazard, Vector2Int tile)
        {
            return hazard.IsBishnoi
                ? map.IsWalkable(tile) && !SafeZone(tile)
                : CanBeggarWalk(tile);
        }

        bool OccupiedByHazard(Vector2Int tile)
        {
            foreach (RoamingHazard hazard in hazards)
                if (hazard.Tile == tile)
                    return true;
            return false;
        }

        bool VerifyLevel(IslandTileMap candidate, int targetLevel, bool wantsBishnoi,
            out List<Vector2Int> beggars, out Vector2Int? bishnoi, out string failure)
        {
            beggars = new List<Vector2Int>();
            bishnoi = null;
            failure = "";
            if (!candidate.IsWalkable(candidate.Dock))
            {
                failure = "The gate is blocked.";
                return false;
            }
            var reached = new HashSet<Vector2Int> { candidate.Dock };
            var queue = new Queue<Vector2Int>();
            queue.Enqueue(candidate.Dock);
            while (queue.Count > 0)
            {
                Vector2Int tile = queue.Dequeue();
                foreach (Vector2Int step in HazardSteps)
                {
                    Vector2Int next = tile + step;
                    if (candidate.IsWalkable(next) && reached.Add(next))
                        queue.Enqueue(next);
                }
            }
            var objectives = new HashSet<Vector2Int> { candidate.Dock };
            if (candidate.Beacons.Length != 3 || reached.Count < 4)
            {
                failure = "The gate has insufficient reachable space.";
                return false;
            }
            foreach (Vector2Int guest in candidate.Beacons)
            {
                if (!reached.Contains(guest) || !objectives.Add(guest))
                {
                    failure = "A guest is unreachable or overlaps another objective.";
                    return false;
                }
            }

            bool Protected(Vector2Int tile)
            {
                if (Math.Abs(tile.x - candidate.Dock.x) + Math.Abs(tile.y - candidate.Dock.y) <= 2)
                    return true;
                foreach (Vector2Int guest in candidate.Beacons)
                    if (Math.Abs(tile.x - guest.x) + Math.Abs(tile.y - guest.y) <= 1)
                        return true;
                return false;
            }
            bool Allowed(Vector2Int tile, bool roadOnly)
            {
                return candidate.IsWalkable(tile) && !Protected(tile)
                    && (!roadOnly || candidate.IsStreet(tile));
            }

            var roads = new List<Vector2Int>();
            var allLand = new List<Vector2Int>();
            for (int y = 0; y < candidate.Height; y++)
                for (int x = 0; x < candidate.Width; x++)
                {
                    var tile = new Vector2Int(x, y);
                    if (!Allowed(tile, false))
                        continue;
                    allLand.Add(tile);
                    if (candidate.IsStreet(tile))
                        roads.Add(tile);
                }
            var occupied = new HashSet<Vector2Int>();
            // Reserve an empty exit per enemy so later spawns cannot trap it.
            var reservedExits = new HashSet<Vector2Int>();
            bool Place(List<Vector2Int> choices, bool roadOnly, out Vector2Int spawn)
            {
                while (choices.Count > 0)
                {
                    int index = hazardRandom.Next(choices.Count);
                    Vector2Int tile = choices[index];
                    choices.RemoveAt(index);
                    if (occupied.Contains(tile) || reservedExits.Contains(tile))
                        continue;
                    foreach (Vector2Int direction in HazardSteps)
                    {
                        Vector2Int exit = tile + direction;
                        if (!Allowed(exit, roadOnly) || occupied.Contains(exit))
                            continue;
                        occupied.Add(tile);
                        reservedExits.Add(exit);
                        spawn = tile;
                        return true;
                    }
                }
                spawn = default;
                return false;
            }
            int limit = roads.Count / Math.Max(1, roadTilesPerBeggar);
            int count = (int)Math.Min(limit,
                Mathf.Clamp(roamingHazardCount, 0, 12) + 2L * (targetLevel - 1));
            if (wantsBishnoi)
            {
                if (!Place(allLand, false, out Vector2Int spawn))
                {
                    failure = "No safe mobile spawn for Bishnoi.";
                    return false;
                }
                bishnoi = spawn;
            }
            for (int i = 0; i < count; i++)
            {
                if (!Place(roads, true, out Vector2Int spawn))
                {
                    failure = "Insufficient road space for mobile beggars.";
                    return false;
                }
                beggars.Add(spawn);
            }
            return true;
        }

        void SpawnHazards()
        {
            foreach (RoamingHazard hazard in hazards)
            {
                hazard.Renderer.enabled = false;
                Destroy(hazard.Renderer.gameObject);
            }
            hazards.Clear();
            foreach (Vector2Int tile in plannedBeggars)
            {
                SpriteRenderer renderer = AddSprite("Roaming beggar", beggarSprite, Color.white, 2, 0.95f);
                renderer.transform.localPosition = At(tile);

                hazards.Add(new RoamingHazard
                {
                    Tile = tile,
                    Renderer = renderer,
                    NextMove = Time.unscaledTime + 1f + (float)hazardRandom.NextDouble()
                });
            }
        }

        void SpawnBishnoi()
        {
            if (!plannedBishnoi.HasValue)
                return;
            if (bishnoiSprite == null)
            {
                bishnoiTexture = MakeBishnoiTexture();
                bishnoiSprite = Sprite.Create(bishnoiTexture, new Rect(0, 0, 16, 16),
                    new Vector2(0.5f, 0.5f), 16, 0, SpriteMeshType.FullRect);
            }
            Vector2Int spawn = plannedBishnoi.Value;
            SpriteRenderer renderer = AddSprite("Bishnoi", bishnoiSprite, Color.white, 2, 1.15f);
            renderer.transform.localPosition = At(spawn);
            hazards.Add(new RoamingHazard
            {
                IsBishnoi = true,
                Tile = spawn,
                Renderer = renderer,
                NextMove = Time.unscaledTime + 1f
            });
        }

        void MoveHazards()
        {
            foreach (RoamingHazard hazard in hazards)
            {
                if (Time.unscaledTime < hazard.NextMove)
                    continue;
                hazard.NextMove = Time.unscaledTime + 0.3f + (float)hazardRandom.NextDouble() * 0.8f;
                bool seesPlayer = CanSeePlayer(hazard);
                if (seesPlayer && !hazard.IsChasing)
                    PlayEffect(hazard.IsBishnoi ? bishnoiChaseSfx : beggarChaseSfx);
                hazard.IsChasing = seesPlayer;
                if (seesPlayer)
                {
                    hazard.NextMove = Time.unscaledTime + 0.12f;
                    Vector2Int chase = ChaseStep(hazard);
                    if (chase != hazard.Tile)
                    {
                        hazard.LastMoveAt = Time.unscaledTime;
                        hazard.Steps++;
                    }
                    hazard.Tile = chase;
                    hazard.Renderer.transform.localPosition = At(chase);
                    continue;
                }
                // A fresh choice each step creates pauses, reversals and varied routes.
                if (hazardRandom.Next(5) == 0)
                    continue;
                int start = hazardRandom.Next(HazardSteps.Length);
                for (int i = 0; i < HazardSteps.Length; i++)
                {
                    Vector2Int next = hazard.Tile + HazardSteps[(start + i) % HazardSteps.Length];
                    if (!CanHazardWalk(hazard, next) || OccupiedByHazard(next))
                        continue;
                    hazard.LastMoveAt = Time.unscaledTime;
                    hazard.Steps++;
                    hazard.Tile = next;
                    hazard.Renderer.transform.localPosition = At(next);
                    break;
                }
            }
        }

        bool CanSeePlayer(RoamingHazard hazard)
        {
            Vector2Int from = hazard.Tile;
            int dx = playerTile.x - from.x, dy = playerTile.y - from.y;
            if (dx * dx + dy * dy > 9 || !CanHazardWalk(hazard, playerTile))
                return false;
            // Sample the centre-to-centre sight line, checking both cells at
            // diagonal boundaries so houses also block diagonal vision.
            int steps = Math.Max(Math.Abs(dx), Math.Abs(dy)) * 8;
            for (int i = 1; i < steps; i++)
            {
                double x = from.x + 0.5 + dx * i / (double)steps;
                double y = from.y + 0.5 + dy * i / (double)steps;
                for (int offset = -1; offset <= 1; offset += 2)
                {
                    var tile = new Vector2Int((int)Math.Floor(x + offset * 0.00001),
                        (int)Math.Floor(y + offset * 0.00001));
                    if (!map.IsWalkable(tile))
                        return false;
                }
            }
            return true;
        }

        Vector2Int ChaseStep(RoamingHazard hazard)
        {
            Vector2Int start = hazard.Tile;
            var queue = new Queue<Vector2Int>();
            var firstStep = new Dictionary<Vector2Int, Vector2Int>();
            firstStep.Add(start, start);
            queue.Enqueue(start);
            while (queue.Count > 0)
            {
                Vector2Int tile = queue.Dequeue();
                foreach (Vector2Int direction in HazardSteps)
                {
                    Vector2Int next = tile + direction;
                    if (!CanHazardWalk(hazard, next) || OccupiedByHazard(next) ||
                        firstStep.ContainsKey(next))
                        continue;
                    Vector2Int first = tile == start ? next : firstStep[tile];
                    if (next == playerTile)
                        return first;
                    firstStep.Add(next, first);
                    queue.Enqueue(next);
                }
            }
            return start;
        }

        bool HitHazard()
        {
            RoamingHazard catcher = hazards.Find(hazard => hazard.Tile == playerTile);
            if (catcher == null)
                return false;
            lives--;
            playerHasMoved = false;
            foreach (RoamingHazard hazard in hazards)
                hazard.IsChasing = false;
            PlayEffect(catcher.IsBishnoi ? bishnoiCatchSfx : beggarCatchSfx);
            if (lives <= 0)
            {
                musicSource.Stop();
                if (gameOverSfx != null)
                {
                    gameOverSource.clip = gameOverSfx;
                    gameOverSource.volume = sfxVolume;
                    gameOverSource.Play();
                }
            }
            playerTile = map.Dock;
            player.transform.localPosition = At(playerTile);
            lastPlayerMoveAt = Time.unscaledTime;
            nextStep = Time.unscaledTime + 0.75f;
            UpdateStatus();
            return true;
        }

        static void ReadKeyboard(out Vector2Int direction, out bool restartLevel, out bool skipLevel)
        {
            bool left = false, right = false, up = false, down = false;
            restartLevel = false;
            skipLevel = false;
#if ENABLE_INPUT_SYSTEM
            Keyboard keys = Keyboard.current;
            if (keys != null)
            {
                left = keys.aKey.isPressed || keys.leftArrowKey.isPressed;
                right = keys.dKey.isPressed || keys.rightArrowKey.isPressed;
                up = keys.wKey.isPressed || keys.upArrowKey.isPressed;
                down = keys.sKey.isPressed || keys.downArrowKey.isPressed;
                restartLevel = keys.spaceKey.wasPressedThisFrame;
                skipLevel = keys.nKey.wasPressedThisFrame;
            }
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
            left |= Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow);
            right |= Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow);
            up |= Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow);
            down |= Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow);
            restartLevel |= Input.GetKeyDown(KeyCode.Space);
            skipLevel |= Input.GetKeyDown(KeyCode.N);
#endif
            int x = (right ? 1 : 0) - (left ? 1 : 0);
            int y = (up ? 1 : 0) - (down ? 1 : 0);
            direction = x != 0 ? new Vector2Int(x, 0) : new Vector2Int(0, y);
        }

        void LateUpdate()
        {
            if (map == null || settingsOpen)
                return;
            if (framedAspect != gameCamera.aspect)
                FrameIsland();

            bool idle = Time.unscaledTime - lastPlayerMoveAt > StepInterval * 1.5f;
            bool playing = lives > 0 && !won;
            playerIdleBlend = Mathf.MoveTowards(playerIdleBlend,
                idle && playing ? 1f : 0f, Time.unscaledDeltaTime * 8f);
            if (playing && playerHasMoved && !idle)
                AnimateWalk(player, playerSprite, playerTile, 1.35f,
                    lastPlayerMoveAt, playerSteps, StepInterval * 1.5f);
            else
            {
                player.sprite = playerSprite;
                AnimateIdle(player, playerTile, 1.35f, 0f, playerIdleBlend);
            }

            foreach (RoamingHazard hazard in hazards)
            {
                Sprite standing = hazard.IsBishnoi ? bishnoiSprite : beggarSprite;
                float size = hazard.IsBishnoi ? 1.15f : 0.95f;
                float duration = hazard.IsChasing ? 0.12f : 0.24f;
                if (playing && Time.unscaledTime - hazard.LastMoveAt < duration)
                    AnimateWalk(hazard.Renderer, standing, hazard.Tile, size,
                        hazard.LastMoveAt, hazard.Steps, duration);
                else
                {
                    hazard.Renderer.sprite = standing;
                    hazard.Renderer.transform.localPosition = At(hazard.Tile);
                    hazard.Renderer.transform.localScale = Vector3.one * size;
                }
            }

            for (int i = 0; i < beacons.Length; i++)
            {
                if (beacons[i].enabled)
                    AnimateIdle(beacons[i], map.Beacons[i], 1.25f,
                        1.7f + i * 2.1f, playing ? 1f : 0f);
            }
        }

        void AnimateWalk(SpriteRenderer renderer, Sprite standing, Vector2Int tile,
            float size, float startedAt, int steps, float duration)
        {
            if (!walkingFrames.TryGetValue(standing, out Sprite[] frames))
            {
                frames = new Sprite[2];
                Color32[] original = standing.texture.GetPixels32();
                for (int frame = 0; frame < frames.Length; frame++)
                {
                    var pixels = (Color32[])original.Clone();
                    // Alternate the two legs by lifting one foot a pixel.
                    for (int y = 0; y < 4; y++)
                        for (int x = 0; x < 16; x++)
                            pixels[y * 16 + x] = new Color32(0, 0, 0, 0);
                    for (int y = 0; y < 4; y++)
                        for (int x = 0; x < 16; x++)
                        {
                            int lift = (x < 8) == (frame == 0) ? 1 : 0;
                            pixels[(y + lift) * 16 + x] = original[y * 16 + x];
                        }
                    Texture2D texture = MakeTexture(16, 16, pixels);
                    frames[frame] = Sprite.Create(texture, new Rect(0, 0, 16, 16),
                        new Vector2(0.5f, 0.5f), 16, 0, SpriteMeshType.FullRect);
                    walkingTextures.Add(texture);
                }
                walkingFrames.Add(standing, frames);
            }
            renderer.sprite = frames[steps % 2];
            float progress = Mathf.Clamp01((Time.unscaledTime - startedAt) / duration);
            float bounce = Mathf.Sin(progress * Mathf.PI);
            Vector3 position = At(tile);
            position.y += bounce * 0.055f;
            renderer.transform.localPosition = position;
            renderer.transform.localScale = new Vector3(size * (1f - bounce * 0.025f),
                size * (1f + bounce * 0.025f), size);
        }

        static void AnimateIdle(SpriteRenderer character, Vector2Int tile,
            float baseScale, float phase, float intensity)
        {
            float breath = Mathf.Sin(Time.unscaledTime * 2.8f + phase) * intensity;
            float stretch = 0.018f * breath;
            character.transform.localScale = new Vector3(
                baseScale * (1f - stretch * 0.4f),
                baseScale * (1f + stretch), baseScale);

            // Rebuild from the tile centre each frame to prevent position drift.
            // The vertical correction keeps the feet near their original height.
            Vector3 position = At(tile);
            position.y += baseScale * stretch * 0.5f + 0.012f * breath;
            character.transform.localPosition = position;
        }

        void FrameIsland()
        {
            // Reserve screen space above the map for the HUD and objective.
            gameCamera.rect = new Rect(0, 0.08f, 1, 0.68f);
            float padding = 2;
            float height = map.Height + padding * 2;
            float width = map.Width + padding * 2;
            framedAspect = gameCamera.aspect;
            float viewHeight = Mathf.Max(height, width / Mathf.Max(0.01f, framedAspect));
            gameCamera.transform.SetPositionAndRotation(
                new Vector3(map.Width / 2f, map.Height / 2f, -10),
                Quaternion.identity
            );
            gameCamera.orthographicSize = viewHeight * 0.5f;
        }

        static Vector3 At(Vector2Int tile) => new Vector3(tile.x + 0.5f, tile.y + 0.5f, 0);

        SpriteRenderer AddSprite(string label, Sprite sprite, Color color, int layer, float scale)
        {
            SpriteRenderer renderer = new GameObject(label).AddComponent<SpriteRenderer>();
            renderer.transform.SetParent(stage, false);
            renderer.transform.localScale = Vector3.one * scale;
            renderer.sprite = sprite;
            renderer.color = color;
            renderer.sortingOrder = layer;
            Material material = spriteMaterial != null ? spriteMaterial : createdMaterial;
            if (material != null)
                renderer.sharedMaterial = material;
            return renderer;
        }

        void DrawTerrain()
        {
            if (terrainSprite != null)
                Destroy(terrainSprite);
            if (terrainTexture != null)
                Destroy(terrainTexture);

            int width = map.Width * TilePixels, height = map.Height * TilePixels;
            var pixels = new Color32[width * height];
            for (int y = 0; y < map.Height; y++)
            {
                for (int x = 0; x < map.Width; x++)
                {
                    var tile = new Vector2Int(x, y);
                    bool land = map.IsLand(tile);
                    bool coast = land && map.IsCoast(tile);
                    bool alternate = ((x * 17 + y * 31) % 7) < 3;
                    Color32 color = land
                        ? coast ? new Color32(242, 156, 104, 255)
                                : alternate ? new Color32(65, 137, 150, 255) : new Color32(77, 151, 156, 255)
                        : new Color32(27, 15, 62, 255);
                    if (map.IsStreet(tile))
                        color = new Color32(125, 105, 166, 255);
                    for (int py = 0; py < TilePixels; py++)
                    {
                        for (int px = 0; px < TilePixels; px++)
                        {
                            if (map.IsObstacle(tile))
                            {
                                int facingY = tile.y % 4 == 1 ? py : 7 - py;
                                Color32 houseColor = color;
                                if (px >= 1 && px <= 5 && facingY >= 1 && facingY <= 6)
                                {
                                    houseColor = facingY >= 4
                                        ? new Color32(173, 30, 106, 255) : new Color32(246, 107, 159, 255);
                                    if (px == 3 && facingY <= 2)
                                        houseColor = new Color32(65, 22, 83, 255);
                                    // Alternate painted facades, glowing windows and balcony trim.
                                    if (((x + y) & 1) == 0 && facingY <= 3)
                                        houseColor = new Color32(113, 80, 197, 255);
                                    if ((px == 1 || px == 5) && facingY == 3)
                                        houseColor = new Color32(255, 221, 104, 255);
                                    if (facingY == 4)
                                        houseColor = new Color32(255, 174, 82, 255);
                                    if (facingY == 5 && px >= 2 && px <= 4)
                                        houseColor = new Color32(255, 62, 163, 255);
                                    if (px == 3 && facingY <= 2)
                                        houseColor = new Color32(65, 22, 83, 255);
                                }
                                if (map.HasTree(tile) && px >= 5 && facingY >= 3 && facingY <= 6)
                                    houseColor = facingY == 3 && px == 6
                                        ? new Color32(90, 61, 39, 255) : new Color32(27, 183, 129, 255);
                                pixels[(y * TilePixels + py) * width + x * TilePixels + px] = houseColor;
                                continue;
                            }
                            if (map.IsStreet(tile) && (x % 4 == 0 && y % 4 == 0)
                                && py == 1 && px >= 2 && px <= 5)
                            {
                                pixels[(y * TilePixels + py) * width + x * TilePixels + px] =
                                    new Color32(255, 189, 111, 255);
                                continue;
                            }
                            bool fleck = alternate && py == 3 && px >= 2 && px <= 3;
                            pixels[(y * TilePixels + py) * width + x * TilePixels + px] =
                                !land && fleck ? new Color32(76, 43, 121, 255) : color;
                        }
                    }
                }
            }

            terrainTexture = MakeTexture(width, height, pixels);
            terrainSprite = Sprite.Create(terrainTexture, new Rect(0, 0, width, height),
                Vector2.zero, TilePixels, 0, SpriteMeshType.FullRect);
            terrain.sprite = terrainSprite;
        }

        static Texture2D MakePlayerTexture()
        {
            // Dark swept hair, sunglasses, broad shoulders, black shirt,
            // blue jeans and a turquoise wrist bracelet.
            string[] rows =
            {
                ".....HHHHHH.....",
                "....HHHHHHHH....",
                "....HSSSSSSH....",
                "....SKKSSKKH....",
                ".....SSSSSS.....",
                "......SMM.......",
                "...BBBBBBBBBB...",
                "..SSBBBBBBBBSS..",
                "..SSBBBBBBBBSS..",
                "..TSBBBBBBBBSS..",
                "...SBBBBBBBBS...",
                ".....JJJJJJ.....",
                ".....JJ..JJ.....",
                ".....JJ..JJ.....",
                "....KKK..KKK....",
                "................"
            };
            var pixels = new Color32[256];
            for (int y = 0; y < 16; y++)
                for (int x = 0; x < 16; x++)
                {
                    Color32 color;
                    switch (rows[y][x])
                    {
                        case 'H': color = new Color32(35, 27, 24, 255); break;
                        case 'S': color = new Color32(206, 155, 116, 255); break;
                        case 'M': color = new Color32(121, 73, 59, 255); break;
                        case 'B': color = new Color32(45, 48, 56, 255); break;
                        case 'J': color = new Color32(62, 100, 153, 255); break;
                        case 'K': color = new Color32(20, 23, 29, 255); break;
                        case 'T': color = new Color32(56, 214, 208, 255); break;
                        default: color = new Color32(0, 0, 0, 0); break;
                    }
                    pixels[(15 - y) * 16 + x] = color;
                }
            return MakeTexture(16, 16, pixels);
        }

        Sprite MakeObjectiveSprite(int outfit)
        {
            string[] rows;
            if (outfit < 0)
            {
                rows = new[]
                {
                    "................",
                    ".RR..........RR.",
                    "RSSR........RSSR",
                    ".SS..........SS.",
                    ".SS..GGGGGG..SS.",
                    ".SS..G....G..SS.",
                    ".SS..G....G..SS.",
                    ".SS..GGGGGG..SS.",
                    ".SS..G....G..SS.",
                    ".SS..G....G..SS.",
                    ".SS..G....G..SS.",
                    ".SS..GGGGGG..SS.",
                    ".SS..........SS.",
                    ".SS..........SS.",
                    "PPPPPPPPPPPPPPPP",
                    "PPPPPPPPPPPPPPPP"
                };
            }
            else
            {
                rows = new[]
                {
                    ".....HHHHHH.....",
                    "....HHHHHHHH....",
                    "....HSSSSSSH....",
                    "....HSKSSKSH....",
                    "....HSSSSSSH....",
                    "....HHSSSSHH....",
                    ".....DDDDDD.....",
                    "....SDDDDDDS....",
                    "....SDDDDDDS....",
                    "....SDDDDDDS....",
                    ".....DDADDD.....",
                    "....DDDADDDD....",
                    "....DDDADDDD....",
                    "...DDDDADDDDD...",
                    ".....SS..SS.....",
                    ".....KK..KK....."
                };
                // Separate silhouettes for a blue jumpsuit and a gold jacket.
                if (outfit == 1)
                {
                    rows[11] = ".....DD..DD.....";
                    rows[12] = ".....DD..DD.....";
                    rows[13] = ".....DD..DD.....";
                }
                if (outfit == 2)
                {
                    rows[7] = "....SDDAADDS....";
                    rows[8] = "....SDDAADDS....";
                    rows[11] = ".....KKKKKK.....";
                    rows[12] = ".....KK..KK.....";
                    rows[13] = ".....KK..KK.....";
                }
            }
            Color32[] outfits =
            {
                new Color32(247, 54, 151, 255),
                new Color32(38, 205, 224, 255),
                new Color32(255, 192, 57, 255)
            };
            var pixels = new Color32[256];
            for (int y = 0; y < 16; y++)
                for (int x = 0; x < 16; x++)
                {
                    Color32 color;
                    switch (rows[y][x])
                    {
                        case 'H': color = new Color32(50, 32, 29, 255); break;
                        case 'S': color = outfit < 0 ? new Color32(250, 196, 139, 255)
                            : new Color32(211, 160, 121, 255); break;
                        case 'D': color = outfits[outfit]; break;
                        case 'A': color = new Color32(250, 221, 154, 255); break;
                        case 'K': color = new Color32(31, 34, 42, 255); break;
                        case 'R': color = new Color32(219, 49, 134, 255); break;
                        case 'G': color = new Color32(255, 204, 100, 255); break;
                        case 'P': color = new Color32(151, 116, 180, 255); break;
                        default: color = new Color32(0, 0, 0, 0); break;
                    }
                    pixels[(15 - y) * 16 + x] = color;
                }
            Texture2D texture = MakeTexture(16, 16, pixels);
            Sprite sprite = Sprite.Create(texture, new Rect(0, 0, 16, 16),
                new Vector2(0.5f, 0.5f), 16, 0, SpriteMeshType.FullRect);
            objectiveTextures.Add(texture);
            objectiveSprites.Add(sprite);
            return sprite;
        }

        static Texture2D MakeBishnoiTexture()
        {
            // Simplified portrait with dark hair, beard and a pale shirt.
            string[] rows =
            {
                ".....HHHHHH.....",
                "....HHHHHHHH....",
                "....HSSSSSSH....",
                "....SKSSSSKS....",
                "....SSSSSSSS....",
                ".....HSSSSH.....",
                ".....HHHHHH.....",
                "...WWSSSSWWWW...",
                "..SSWWWWWWWWSS..",
                "..SSWWWWWWWWSS..",
                "...SWWWWWWWWS...",
                ".....JJJJJJ.....",
                ".....JJ..JJ.....",
                ".....JJ..JJ.....",
                "....KKK..KKK....",
                "................"
            };
            var pixels = new Color32[256];
            for (int y = 0; y < 16; y++)
                for (int x = 0; x < 16; x++)
                {
                    Color32 color;
                    switch (rows[y][x])
                    {
                        case 'H': color = new Color32(32, 26, 23, 255); break;
                        case 'S': color = new Color32(187, 139, 104, 255); break;
                        case 'W': color = new Color32(229, 219, 194, 255); break;
                        case 'J': color = new Color32(66, 72, 82, 255); break;
                        case 'K': color = new Color32(20, 22, 26, 255); break;
                        default: color = new Color32(0, 0, 0, 0); break;
                    }
                    pixels[(15 - y) * 16 + x] = color;
                }
            return MakeTexture(16, 16, pixels);
        }

        static Texture2D MakeBeggarTexture()
        {
            var pixels = new Color32[16 * 16];
            for (int y = 0; y < 16; y++)
                for (int x = 0; x < 16; x++)
                {
                    Color32 color = new Color32(0, 0, 0, 0);
                    if (y >= 10 && y <= 13 && x >= 6 && x <= 9)
                        color = new Color32(181, 131, 95, 255);
                    if (y >= 4 && y <= 9 && x >= 4 && x <= 10)
                        color = new Color32(122, 104, 80, 255);
                    if (y >= 1 && y <= 3 && (x == 5 || x == 9))
                        color = new Color32(57, 48, 43, 255);
                    if (y >= 5 && y <= 6 && x >= 11 && x <= 13)
                        color = new Color32(182, 177, 151, 255);
                    pixels[y * 16 + x] = color;
                }
            return MakeTexture(16, 16, pixels);
        }

        static Texture2D MakeMarkerTexture()
        {
            var pixels = new Color32[16 * 16];
            for (int y = 0; y < 16; y++)
            {
                for (int x = 0; x < 16; x++)
                {
                    float radius = Mathf.Max(Mathf.Abs(x - 7.5f), Mathf.Abs(y - 7.5f));
                    if (radius <= 6)
                        pixels[y * 16 + x] = radius <= 4
                            ? new Color32(255, 255, 255, 255)
                            : new Color32(34, 54, 64, 255);
                }
            }
            return MakeTexture(16, 16, pixels);
        }

        static Texture2D MakeTexture(int width, int height, Color32[] pixels)
        {
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            texture.filterMode = FilterMode.Point;
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.SetPixels32(pixels);
            texture.Apply(false, false);
            return texture;
        }

        void UpdateStatus()
        {
            if (lives <= 0)
                status = "Out of lives — press Space to try again";
            else if (won)
                status = "Everyone is home! Next level...";
            else if (collected == beacons.Length)
                status = "Return to the farmhouse gate";
            else
                status = "Find all three guests, then return to the farmhouse gate";
        }

        static Texture2D MakeHeartTexture()
        {
            string[] rows =
            {
                ".XX.XX.",
                "XXXXXXX",
                "XXXXXXX",
                ".XXXXX.",
                "..XXX..",
                "...X..."
            };
            var pixels = new Color32[42];
            for (int y = 0; y < rows.Length; y++)
                for (int x = 0; x < 7; x++)
                    pixels[(5 - y) * 7 + x] = rows[y][x] == 'X'
                        ? new Color32(255, 74, 147, 255) : new Color32(0, 0, 0, 0);
            return MakeTexture(7, 6, pixels);
        }

        void OnGUI()
        {
            if (titleStyle == null)
            {
                titleStyle = new GUIStyle(GUI.skin.label)
                {
                    font = titleFont, fontSize = 27, fontStyle = FontStyle.Normal,
                    alignment = TextAnchor.MiddleLeft
                };
                hudStyle = new GUIStyle(GUI.skin.label)
                {
                    font = bodyFont, fontSize = 15, alignment = TextAnchor.MiddleCenter
                };
                commandsStyle = new GUIStyle(hudStyle) { font = commandsFont };
                badgeStyle = new GUIStyle(hudStyle) { fontStyle = FontStyle.Bold };
                objectiveStyle = new GUIStyle(hudStyle)
                {
                    fontSize = 20, wordWrap = true, fontStyle = FontStyle.Bold
                };
                titleStyle.normal.textColor = new Color32(168, 12, 240, 255);
                hudStyle.normal.textColor = new Color32(231, 224, 255, 255);
                commandsStyle.normal.textColor = hudStyle.normal.textColor;
                badgeStyle.normal.textColor = new Color32(75, 235, 215, 255);
                objectiveStyle.normal.textColor = new Color32(255, 230, 188, 255);
                settingsHeading = new GUIStyle(titleStyle) { alignment = TextAnchor.MiddleCenter, fontSize = 34 };
                settingsButton = new GUIStyle(GUI.skin.button) { font = bodyFont, fontSize = 18 };
                creditsStyle = new GUIStyle(hudStyle) { alignment = TextAnchor.UpperLeft, wordWrap = true, fontSize = 19 };
                heartTexture = MakeHeartTexture();
                settingsIcon = MakeSettingsIcon();
            }

            Matrix4x4 previousMatrix = GUI.matrix;
            Color previousColor = GUI.color;
            float scale = Mathf.Min(Screen.width / 960f, Screen.height / 540f);
            if (scale <= 0)
                return;
            float width = Screen.width / scale;
            float height = Screen.height / scale;
            GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, Vector3.one * scale);
            try
            {
                if (settingsOpen)
                {
                    DrawSettings(width, height);
                    return;
                }
                GUI.color = new Color(0.16f, 0.045f, 0.25f, 0.98f);
                GUI.DrawTexture(new Rect(0, 0, width, height * 0.24f), Texture2D.whiteTexture);
                GUI.DrawTexture(new Rect(0, height * 0.92f, width, height * 0.08f), Texture2D.whiteTexture);
                GUI.color = Color.white;
                GUI.Label(new Rect(24, 12, 400, 40), "Bhai's Farmhouse", titleStyle);

                GUI.Box(new Rect(width - 354, 16, 105, 36), GUIContent.none);
                GUI.Label(new Rect(width - 354, 16, 105, 36), $"LEVEL {levelNumber}", badgeStyle);
                GUI.Box(new Rect(width - 235, 16, 102, 36), GUIContent.none);
                GUI.Label(new Rect(width - 235, 16, 102, 36), $"Guests {collected}/3", badgeStyle);
                for (int i = 0; i < lives; i++)
                    GUI.DrawTexture(new Rect(width - 116 + i * 32, 24, 28, 24), heartTexture);

                GUI.Label(new Rect(40, 62, width - 80, height * 0.24f - 66),
                    notice ?? status ?? "Preparing the farmhouse...", objectiveStyle);
                GUI.Label(new Rect(78, height * 0.92f, width - 98, height * 0.08f),
                    "WASD / Arrow keys   Move       •       Space   Restart level", commandsStyle);

                if (GUI.Button(new Rect(16, height - 42, 40, 36),
                    new GUIContent(settingsIcon, "Settings")))
                    OpenSettings();

                if (lives <= 0 && map != null)
                {
                    GUI.color = new Color(0.20f, 0.05f, 0.29f, 0.97f);
                    GUI.DrawTexture(new Rect(width / 2 - 220, height / 2 - 46, 440, 92), Texture2D.whiteTexture);
                    GUI.color = Color.white;
                    GUI.Label(new Rect(width / 2 - 210, height / 2 - 36, 420, 36),
                        "Out of lives", objectiveStyle);
                    GUI.Label(new Rect(width / 2 - 210, height / 2 + 4, 420, 32),
                        "Press Space to restart", commandsStyle);
                }
            }
            finally
            {
                GUI.matrix = previousMatrix;
                GUI.color = previousColor;
            }
        }

        static Texture2D MakeSettingsIcon()
        {
            var pixels = new Color32[256];
            for (int y = 0; y < 16; y++)
                for (int x = 0; x < 16; x++)
                {
                    float dx = x - 7.5f, dy = y - 7.5f;
                    float radius = dx * dx + dy * dy;
                    bool ring = radius >= 10 && radius <= 36;
                    bool tooth = radius > 36 && radius <= 56 &&
                        (Math.Abs(dx) < 2 || Math.Abs(dy) < 2 || Math.Abs(Math.Abs(dx) - Math.Abs(dy)) < 1.2f);
                    if (ring || tooth)
                        pixels[y * 16 + x] = new Color32(255, 207, 111, 255);
                }
            return MakeTexture(16, 16, pixels);
        }

        void OpenSettings()
        {
            settingsOpen = true;
            settingsOpenedAt = Time.unscaledTime;
            creditsOpen = false;
            GUI.FocusControl(null);
        }

        void CloseSettings()
        {
            float pausedDuration = Time.unscaledTime - settingsOpenedAt;
            nextStep += pausedDuration;
            nextLevelAt += pausedDuration;
            lastPlayerMoveAt += pausedDuration;
            foreach (RoamingHazard hazard in hazards)
            {
                hazard.NextMove += pausedDuration;
                hazard.LastMoveAt += pausedDuration;
            }
            settingsOpen = false;
            creditsOpen = false;
            GUI.FocusControl(null);
        }

        void DrawSettings(float width, float height)
        {
            GUI.color = new Color(0.10f, 0.025f, 0.17f, 1f);
            GUI.DrawTexture(new Rect(0, 0, width, height), Texture2D.whiteTexture);
            GUI.color = Color.white;
            float left = width / 2 - 260;
            float top = height / 2 - 210;
            GUI.Label(new Rect(left, top, 520, 60), "Settings", settingsHeading);

            if (creditsOpen)
            {
                DrawCredits(width, height);
                return;
            }

            GUI.Label(new Rect(left, top + 90, 360, 32), "Background music", hudStyle);
            GUI.Label(new Rect(left + 390, top + 90, 130, 32),
                Mathf.RoundToInt(musicVolume * 100) + "%", badgeStyle);
            musicVolume = GUI.HorizontalSlider(new Rect(left + 20, top + 132, 480, 24), musicVolume, 0f, 1f);
            GUI.Label(new Rect(left, top + 175, 360, 32), "Sound effects", hudStyle);
            GUI.Label(new Rect(left + 390, top + 175, 130, 32),
                Mathf.RoundToInt(sfxVolume * 100) + "%", badgeStyle);
            sfxVolume = GUI.HorizontalSlider(new Rect(left + 20, top + 217, 480, 24), sfxVolume, 0f, 1f);
            if (musicSource != null)
                musicSource.volume = musicVolume;
            if (effectsSource != null)
                effectsSource.volume = sfxVolume;
            if (gameOverSource != null)
                gameOverSource.volume = sfxVolume;

            if (GUI.Button(new Rect(left + 80, top + 280, 360, 48), "Credits", settingsButton))
            {
                creditsOpen = true;
                creditsScroll = Vector2.zero;
                GUI.FocusControl(null);
            }
            if (GUI.Button(new Rect(left + 80, top + 346, 360, 48), "Back to game", settingsButton))
                CloseSettings();
        }

        void DrawCredits(float width, float height)
        {
            float left = width / 2 - 300;
            float top = height / 2 - 205;
            GUI.color = new Color(0.20f, 0.05f, 0.29f, 1f);
            GUI.DrawTexture(new Rect(left, top, 600, 420), Texture2D.whiteTexture);
            GUI.color = Color.white;
            GUI.Label(new Rect(left + 20, top + 12, 560, 54), "Credits", settingsHeading);
            string text = creditsText ?? "";
            float contentHeight = Mathf.Max(246, creditsStyle.CalcHeight(new GUIContent(text), 520));
            creditsScroll = GUI.BeginScrollView(new Rect(left + 28, top + 80, 544, 250),
                creditsScroll, new Rect(0, 0, 520, contentHeight));
            GUI.Label(new Rect(0, 0, 520, contentHeight), text, creditsStyle);
            GUI.EndScrollView();
            if (GUI.Button(new Rect(left + 160, top + 352, 280, 44), "Back to settings", settingsButton))
            {
                creditsOpen = false;
                GUI.FocusControl(null);
            }
        }

        void InitializeAudio()
        {
            musicSource = gameObject.AddComponent<AudioSource>();
            effectsSource = gameObject.AddComponent<AudioSource>();
            gameOverSource = gameObject.AddComponent<AudioSource>();
            foreach (AudioSource source in new[] { musicSource, effectsSource, gameOverSource })
            {
                source.playOnAwake = false;
                source.spatialBlend = 0f;
            }
            musicSource.loop = true;
            if (UnityEngine.Object.FindFirstObjectByType<AudioListener>() == null)
                createdListener = gameCamera.gameObject.AddComponent<AudioListener>();
        }

        void PlayEffect(AudioClip clip)
        {
            if (clip != null && effectsSource != null)
            {
                effectsSource.volume = sfxVolume;
                effectsSource.PlayOneShot(clip);
            }
        }

        void OnDestroy()
        {
            if (musicSource != null)
            {
                musicSource.Stop();
                Destroy(musicSource);
            }
            if (effectsSource != null)
            {
                effectsSource.Stop();
                Destroy(effectsSource);
            }
            if (gameOverSource != null)
            {
                gameOverSource.Stop();
                Destroy(gameOverSource);
            }
            if (createdListener != null)
                Destroy(createdListener);
            if (createdCamera != null)
                Destroy(createdCamera.gameObject);
            if (stage != null)
                Destroy(stage.gameObject);
            Destroy(terrainSprite);
            Destroy(terrainTexture);
            Destroy(bishnoiSprite);
            Destroy(bishnoiTexture);
            Destroy(beggarSprite);
            Destroy(beggarTexture);
            Destroy(playerSprite);
            Destroy(playerTexture);
            foreach (Sprite sprite in objectiveSprites)
                Destroy(sprite);
            foreach (Texture2D texture in objectiveTextures)
                Destroy(texture);
            foreach (Sprite[] frames in walkingFrames.Values)
                foreach (Sprite frame in frames)
                    Destroy(frame);
            foreach (Texture2D texture in walkingTextures)
                Destroy(texture);
            Destroy(settingsIcon);
            Destroy(heartTexture);
            Destroy(marker);
            Destroy(markerTexture);
            Destroy(createdMaterial);
        }
    }
}
