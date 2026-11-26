using Godot;
using Godot.Collections;
using System;

[Tool]
public partial class FactoryMap : Node3D
{
    [Export]
    public string TileScenePath = "res://scenes/tiles/base_tile.tscn";
    
    [Export]
    public EditMode Mode { get; set; } = EditMode.Paint;


    [Export(PropertyHint.Range, "1,10")]
    public int BrushSize = 1;


    public Dictionary<Vector2I, FactoryTile> Tiles = new();

    // Called when the node enters the scene tree for the first time.
    public override void _Ready()
    {
        RebuildTileCache();
    }

    public void RebuildTileCache()
    {
        Tiles.Clear();

        foreach (Node child in GetChildren())
        {
            if (child is FactoryTile tile)
            {
                Tiles[tile.Coord] = tile;
            }
        }
    }

    public bool HasTile(Vector2I coord)
    {
        return Tiles.ContainsKey(coord);
    }

    public FactoryTile GetTile(Vector2I coord)
    {
        Tiles.TryGetValue(coord, out var tile);
        return tile;
    }

    public void RegisterTile(FactoryTile tile)
    {
        Tiles[tile.Coord] = tile;
    }

    public void RemoveTile(Vector2I coord)
    {
        Tiles.Remove(coord);
    }

    public enum EditMode
    {
        Paint,
        Delete
    }

}