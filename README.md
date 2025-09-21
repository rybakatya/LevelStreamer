# OpenWorldToolkit.LevelStreaming

A Unity **grid-based level streaming system**
It bakes your scene into cell prefabs and streams them in/out at runtime using **Unity Addressables** as the player moves.

This README matches the provided API:

- **Editor**
  - `LevelStreamerTab` (custom EditorWindow tab)
  - `LevelStreamerSettingsAsset` (ScriptableObject with `cellSize` and a list of `StreamableAssetReference`)
  - Editor utilities for baking the scene and marking Addressable groups
- **Runtime**
  - `LevelStreamerManager` (MonoBehaviour for live streaming)
  - `StreamableAssetReference` (ISpatialItem storing prefab position + AssetReference)

---

## ✨ Requirements
- Unity 2020 or newer
- **Unity Addressables** package
- [SpatialHash](https://github.com/rybakatya/SpatialHash)
- [WindowTabSystem](https://github.com/rybakatya/WindowTabSystem)  

---

## 📦 Installation (Unity Package Manager)

Install with **Package Manager (UPM)**.

### Option A — Git URL
1. Open **Window → Package Manager**.
2. Click **+** → **Add package from git URL…**.
3. Paste:
   ```
   https://github.com/<org-or-user>/OpenWorldToolkit.LevelStreaming.git
   ```
---

## 🚀 Quick Start

### 1️⃣ Prepare the Scene

1. **Create a LevelStreamer Settings asset**  
   - Open the **Level Streaming** editor window (via your WindowTabSystem host with the `"Streaming"` tab).  
   - Click **New** to create a `LevelStreamerSettingsAsset`.  
   - Set `Cell Size` to your desired world-unit size (e.g., 64).

2. **Bake the scene into cells**  
   - Assign a **World Root** transform containing the objects to stream.
   - Click **Bake** and confirm.  
   - The baker will:
     - Partition all direct children of `World Root` into a SpatialHash grid.
     - Create a `CellsRoot` container.
     - Save each cell group as a prefab in `SceneFolder/Chunks/`.
     - Mark each prefab as an **Addressable** asset and add it to the `assets` list of your settings asset.
---

### 2️⃣ Add the Runtime Manager

Attach `LevelStreamerManager` to an empty GameObject in your scene:
- Assign the baked `LevelStreamerSettingsAsset` to **Streamer Settings** in the inspector.

At runtime (or in Play Mode with `ExecuteAlways`):

- `LevelStreamerManager` uses the main camera transform as the **probe**.
- It continuously computes a **3×3 cell neighborhood** around the probe.
- Cells entering the neighborhood are loaded asynchronously through Addressables.
- Cells leaving the neighborhood are unloaded and released.

---

## 🧩 How It Works

### Editor Workflow (`LevelStreamerTab`)
- Appears as a tab (via `EditorWindowTab(1, "Streaming")`) inside your WindowTabSystem host window.
- Provides two sub-tabs:
  - **Settings**: choose or create the `LevelStreamerSettingsAsset` and set `cellSize`.
  - **Baker**: select a `World Root` and bake the scene.

**BakeScene()**:
- Groups top-level children of `World Root` into cells of size `cellSize`.
- Creates a prefab for each cell in a `Chunks` folder alongside the scene.
- Marks each prefab as Addressable and stores `StreamableAssetReference` (position + AssetReference GUID) in the settings asset.

### Runtime Streaming (`LevelStreamerManager`)
- Maintains:
  - `Dictionary<SpatialCell, AssetReference> cells` from settings.
  - `_instances`: loaded GameObjects per cell.
  - `_loading`: in-flight Addressables operations.
  - `_desired`: target 3×3 neighborhood around the probe.

**Process**:
1. Compute the current center cell from the probe’s position.
2. Build the 3×3 set of desired cells.
3. **Unload**:
   - Any loaded or loading cells no longer desired.
4. **Load**:
   - For each desired cell not loaded or loading:
     - Start `Addressables.InstantiateAsync` at cell center.
     - Track with generation token to avoid stale completions.
5. Generation tokens ensure late-completing loads are cancelled if the probe has moved on.

---

## 🛠 Recommended Practices

- Choose a `cellSize` that balances:
  - **Too small**: many tiny prefabs, more Addressables overhead.
  - **Too large**: heavy prefabs, slower unload/load times.
- Back up your scene before baking (baker moves objects into new parents).
- Prefabs are stored under `SceneFolder/Chunks/` by default; keep them version-controlled.
- Use **Unity Addressables Build** workflow to bundle and distribute cell prefabs for your target platform.

---

## ⚠️ Troubleshooting

- **Cells not loading**  
  - Ensure `LevelStreamerSettingsAsset` is assigned to `LevelStreamerManager`.
  - Verify Addressables build includes the Chunks group and is initialized.

- **Bake fails or objects disappear**  
  - Always back up the scene before baking.
  - Confirm `World Root` is set and contains the intended objects.

- **Performance issues**  
  - Adjust `cellSize` to reduce load frequency.
  - Profile Addressables settings (bundle size, compression, etc.).

---

## 🚧 Limitations

- Requires Unity Addressables package.
- Streaming is 2D (XZ plane) using `SpatialCell` for grid indexing.
- Designed for single-probe (camera) driven worlds; extend for multiplayer if needed.

---

## 📜 License
See the repository’s `LICENSE` for details.
