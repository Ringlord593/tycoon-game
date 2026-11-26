using Godot;
using System.Collections.Generic;

[Tool]
public partial class MapEditorPlugin : EditorPlugin
{
    /// The currently selected FactoryMap being edited.
    private FactoryMap _currentMap;

    /// Root node containing all editor-only preview objects.
    /// This node is never saved with the scene.
    private Node3D _editorRoot;

    /// Indicates whether map editing is currently enabled.
    /// Editing is only active while a FactoryMap is selected.
    private bool _isEditingMap;

    /// Pool of reusable ghost tiles used to preview brush placement.
    /// Additional ghost tiles are created when the brush grows and
    /// hidden when they are no longer required.
    private readonly List<FactoryTile> _ghostTiles = new();

    /// Tile scene used for both placement and preview generation.
    private PackedScene _tileScene;

    /// Distance from the center of a hex to the midpoint of a flat edge.
    private const float HEX_APOTHEM = 1f;

    /// Distance from the center of a hex to a corner.
    private const float HEX_RADIUS = HEX_APOTHEM / 0.8660254f;

    /// Width of a hex measured across opposing flat edges.
    private const float HEX_WIDTH = HEX_APOTHEM * 2f;

    /// Distance between adjacent hex rows.
    private const float HEX_ROW_SPACING = HEX_RADIUS * 1.5f;


    /// Processes mouse input from the 3D viewport.
    /// Mouse movement updates the ghost preview.
    /// Left-click applies the currently selected edit operation.
    public override int _Forward3DGuiInput(Camera3D viewportCamera, InputEvent @event)
	{
		if (!_isEditingMap)
		{
			return (int)AfterGuiInput.Pass;
		}
		if (@event is InputEventMouseMotion mouseMotion)
		{
			UpdateGhost(viewportCamera, mouseMotion.Position); 
		}

		if (@event is InputEventMouseButton mouseButton)
		{
			if (mouseButton.Pressed && mouseButton.ButtonIndex == MouseButton.Left)
			{
				SelectTiles(viewportCamera, mouseButton.Position);
			}
		}

		return (int)EditorPlugin.AfterGuiInput.Stop;
	}


    /// Casts a ray from the editor camera through the mouse position
    /// and returns the world-space hit point.
    /// Returns null if nothing is hit.
    private Vector3? GetMouseWorldPosition(Camera3D camera, Vector2 mousePosition)
	{
		var from = camera.ProjectRayOrigin(mousePosition);

		var to = from + camera.ProjectRayNormal(mousePosition) * 1000f;

		var query = PhysicsRayQueryParameters3D.Create(from, to);

		var result = camera.GetWorld3D().DirectSpaceState.IntersectRay(query);

		if (result.Count == 0)
			return null;

		return (Vector3)result["position"];
	}

    /// Converts axial hex coordinates into world-space coordinates.
    private Vector3 HexToWorld(int q, int r)
	{
		float x =
			HEX_WIDTH * (q + r * 0.5f);

		float z =
			HEX_ROW_SPACING * r;

		return new Vector3(x, 0, z);
	}

    // Rounds fractional axial coordinates to the nearest valid hex cell.
    private Vector2I RoundHex(Vector2 axial)
	{
		float q = axial.X;
		float r = axial.Y;

		float x = q;
		float z = r;
		float y = -x - z;

		int rx = Mathf.RoundToInt(x);
		int ry = Mathf.RoundToInt(y);
		int rz = Mathf.RoundToInt(z);

		float dx = Mathf.Abs(rx - x);
		float dy = Mathf.Abs(ry - y);
		float dz = Mathf.Abs(rz - z);

		if (dx > dy && dx > dz)
			rx = -ry - rz;
		else if (dy > dz)
			ry = -rx - rz;
		else
			rz = -rx - ry;

		return new Vector2I(rx, rz);
	}

    /// Converts a world-space position into fractional axial coordinates.
    /// The resulting coordinates should be passed through RoundHex before use.
    private Vector2 HexWorldToAxial(Vector3 pos)
	{
		float q = ((Mathf.Sqrt(3f) / 3f) * pos.X -
				(1f / 3f) * pos.Z) / HEX_RADIUS;

		float r = ((2f / 3f) * pos.Z) / HEX_RADIUS;

		return new Vector2(q, r);
	}

    /// Updates the ghost tile preview to correct brush size
    /// and positions them beneath the mouse cursor.
    private void UpdateGhost(Camera3D camera, Vector2 mousePosition)
	{
		var hit = GetMouseWorldPosition(camera, mousePosition);

		if (hit == null)
		{
			HideGhosts();
			return;
		}

		var axial = HexWorldToAxial(hit.Value);
		var center = RoundHex(axial);

		var cells = GetHexBrush(
			center,
			_currentMap.BrushSize - 1);

		EnsureGhostCount(cells.Count);

		for (int i = 0; i < cells.Count; i++)
		{
			_ghostTiles[i].Visible = true;
			_ghostTiles[i].Position =
				HexToWorld(cells[i].X, cells[i].Y);
		}

		for (int i = cells.Count; i < _ghostTiles.Count; i++)
		{
			_ghostTiles[i].Visible = false;
		}
	}

    /// Adds ghost tiles as they are needed
    /// Ghost tiles are pooled and reused rather than recreated.
    private void EnsureGhostCount(int count)
	{
		while (_ghostTiles.Count < count)
		{
			var ghost = _tileScene.Instantiate<FactoryTile>();

            ghost.Name = $"_GhostTile_{_ghostTiles.Count}";

            MakeGhost(ghost);

            _editorRoot.AddChild(ghost, false, Node.InternalMode.Front);

            _ghostTiles.Add(ghost);
		}
	}

    /// Applies a translucent material to a tile instance so it can be
    /// used as a placement preview without being mistaken for a real tile.
    private void MakeGhost(Node3D ghost)
	{
		foreach (Node child in ghost.GetChildren())
		{
			if (child is MeshInstance3D mesh)
			{
				var material =
					mesh.GetActiveMaterial(0)?.Duplicate()
					as StandardMaterial3D;

				if (material != null)
				{
					material.Transparency =
						BaseMaterial3D.TransparencyEnum.Alpha;

					material.AlbedoColor =
						new Color(1, 1, 1, 0.4f);

					mesh.MaterialOverride =
						material;
				}
			}
		}
	}

    /// Sets the visiblity to false for all the ghost tiles
    private void HideGhosts()
	{
		foreach (var ghost in _ghostTiles)
		{
			ghost.Visible = false;
		}
	}

    /// Determines which cells are affected by the current brush and
    /// executes the appropriate operation.
    private void SelectTiles(Camera3D camera, Vector2 mousePosition)
	{
		var hit = GetMouseWorldPosition(camera, mousePosition);

		if (hit == null)
			return;

		var axial = HexWorldToAxial(hit.Value);

		var coord = RoundHex(axial);

		var cells = GetHexBrush(coord, _currentMap.BrushSize - 1);
;
        if (_currentMap.Mode == FactoryMap.EditMode.Paint)
		{
			PlaceTiles(cells);
		}
		else if (_currentMap.Mode == FactoryMap.EditMode.Delete)
		{
			DeleteTiles(cells);
		}
	}

    /// Creates tile for every cell in the brush.
    /// Existing tiles are ignored.
    private void PlaceTiles(List<Vector2I> cells)
    {
        var undoRedo = GetUndoRedo();

        undoRedo.CreateAction("Paint Tiles");

        foreach (var cell in cells)
        {
            if (_currentMap.Tiles.ContainsKey(cell))
                continue;

            var tile = _tileScene.Instantiate<FactoryTile>();

            tile.Position = HexToWorld(cell.X, cell.Y);
            tile.Coord = cell;

            undoRedo.AddDoMethod(this, nameof(AddTileNode), tile);

            undoRedo.AddUndoMethod(this, nameof(RemoveTileNode), tile);
        }

        undoRedo.CommitAction();
    }

    /// Removes any existing tiles in the brush.
    private void DeleteTiles(List<Vector2I> cells)
    {
        var undoRedo = GetUndoRedo();

        undoRedo.CreateAction("Delete Tiles");

        foreach (var cell in cells)
        {
            if (!_currentMap.Tiles.TryGetValue(cell, out var tile))
                continue;

            undoRedo.AddDoMethod(this, nameof(RemoveTileNode), tile);

            undoRedo.AddUndoMethod(this, nameof(AddTileNode), tile);
        }

        undoRedo.CommitAction();
    }


	/// Adds the specified tile as a child node of the map 
    private void AddTileNode(FactoryTile tile)
    {
        _currentMap.AddChild(tile);

        tile.Owner = _currentMap;

        _currentMap.Tiles[tile.Coord] = tile;
    }

    /// Removes the specified tile from the map
    private void RemoveTileNode(FactoryTile tile)
    {
        _currentMap.Tiles.Remove(tile.Coord);

        if (tile.GetParent() != null)
        {
            tile.GetParent().RemoveChild(tile);
        }
    }

    /// Generates all axial coordinates contained within a hexagonal brush.
    /// Radius 0 returns a single cell.
    /// Radius 1 returns 7 cells.
    /// Radius 2 returns 19 cells.
    private List<Vector2I> GetHexBrush(Vector2I center, int radius)
	{
		var result = new List<Vector2I>();

		for (int q = -radius; q <= radius; q++)
		{
			int r1 = Mathf.Max(-radius, -q - radius);
			int r2 = Mathf.Min(radius, -q + radius);

			for (int r = r1; r <= r2; r++)
			{
				result.Add(new Vector2I(center.X + q, center.Y + r));
			}
		}

		return result;
	}

    /// Determines whether this plugin should handle the selected object.
    /// Only FactoryMap nodes are editable.
    public override bool _Handles(GodotObject obj)
	{
		return obj is FactoryMap;
	}

    /// Called when the editor selection changes.
    /// Rebuilds tile caches and loads map-specific settings.
    public override void _Edit(GodotObject obj)
	{
        HideGhosts();

        _currentMap = obj as FactoryMap;

		if( _currentMap != null)
		{
			_currentMap.RebuildTileCache();
            _tileScene = GD.Load<PackedScene>(_currentMap.TileScenePath);
        }

		SetEditingEnabled(_currentMap != null);
	}

	private void SetEditingEnabled(bool enabled)
	{
		_isEditingMap = enabled;
		if (!enabled)
			HideGhosts();
	}


    /// Creates the editor-only preview root and removes any orphaned
    /// preview roots left behind by script recompilation.
    public override void _EnterTree()
    {
        foreach (Node child in GetTree().Root.GetChildren())
        {
            if (child.Name == "_MapEditorPreview")
            {
                child.QueueFree();
            }
        }

        _editorRoot = new Node3D();
        _editorRoot.Name = "_MapEditorPreview";

        GetTree().Root.AddChild(_editorRoot);

        _tileScene = GD.Load<PackedScene>(_currentMap?.TileScenePath
            ?? "res://scenes/tiles/base_tile.tscn");
    }


    /// Cleans up editor-only preview objects when the plugin unloads.
    public override void _ExitTree()
	{
		_editorRoot?.QueueFree(); 
	}
}
