using Godot;
using System;

[Tool]
public partial class FactoryTile : Node3D
{
    [Export]
    public Vector2I Coord { get; set; }

    // Called when the node enters the scene tree for the first time.
    public override void _Ready()
	{
	}

	// Called every frame. 'delta' is the elapsed time since the previous frame.
	public override void _Process(double delta)
	{
	}
}
