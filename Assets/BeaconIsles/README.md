# Beacon Isles

A small top-down Unity game. You play a courier who must recover three gold signal beacons from a generated island. Walk back to the dock once it turns teal to finish the island.

## Run it in Unity

1. Open or create a 2D Unity project with an empty scene.
2. Copy the three C# files into a folder such as `Assets/BeaconIsles`.
3. Create an empty GameObject named **Beacon Isles** and attach **Beacon Isles Game** to it.
4. Press Play and click the Game view. Use **WASD** or **arrow keys** to move. Press **R** to generate the next island.

The component creates the terrain, dock, player, and beacons at runtime. It uses the assigned Game Camera, then the scene's Main Camera, or creates a camera if neither exists. It positions and configures the camera for this scene. Keyboard input uses the Input System when enabled and otherwise uses the legacy Input Manager.

For a standalone build, assign an unlit sprite material to **Sprite Material** in the Inspector so the build has a direct reference to its shader. Use **Sprite-Unlit-Default** in a URP 2D project or a material using **Sprites/Default** in a Built-In Render Pipeline project.

## Use your own starting shape

In the component's Inspector, edit **Starting Outline**. Enter the X and Y coordinates in the order you would trace the coastline. Unity closes the outline from the last point back to the first. A simple outline like a rectangle uses `(0, 0)`, `(4, 0)`, `(4, 2)`, and `(0, 2)`. The default stepped outline gives the grammar more corner combinations to work with.

- **Seed** chooses the initial island. The same settings and seed give the same result within a runtime.
- **Map Resolution** controls how many tiles fit across the island's longer dimension. Increase it if a narrow part of the outline disappears during conversion to tiles.
- **Rule Corners** caps the length of the paths the grammar considers for swapping.

## Code

- `IslandGraphGrammar.cs` learns reusable corner patterns and swaps paths with the same connector directions and net turn. It fits the edge lengths to close the outline and rejects intersections.
- `IslandTileMap.cs` converts the outline to land and water cells. It keeps the largest connected landmass and uses walking distance to spread out the beacons.
- `BeaconIslesGame.cs` runs the keyboard controls, drawing, objective, and next-island flow.

This port uses a bounded search for a single, straight-edged 2D outline. Shapes with few matching paths can produce variations in proportions alone. Extremely thin outlines can yield too few connected tiles to play.
