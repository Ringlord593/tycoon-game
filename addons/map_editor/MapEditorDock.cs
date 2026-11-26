using Godot;
using System;

[Tool]
public partial class MapEditorDock : VBoxContainer
{

    [Export]
    public string TileScenePath = "res://scenes/tiles/base_tile.tscn";

    [Export]
    public string MapRootName = "Map";

    


    // Called when the node enters the scene tree for the first time.
    public override void _Ready()
    {
        var tilePath = GetNode<LineEdit>("TileScenePath");
        tilePath.Text = TileScenePath;

        var mode = GetNode<OptionButton>("ModeDropdown");

        mode.AddItem("Paint");
        mode.AddItem("Erase");
    }

    // Called every frame. 'delta' is the elapsed time since the previous frame.
    public override void _Process(double delta)
	{
	}
}
