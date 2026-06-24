// ReSharper disable MemberCanBePrivate.Global

using System;
using System.Collections.Generic;
using Godot;
using Newtonsoft.Json;

namespace RealHackerEvolution.Scripts;

public partial class PlayerMovementScript : CharacterBody3D
{
	private readonly Dictionary<string, Key> _inputMap = new()
	{
		{ "move_left", Key.A },
		{ "move_right", Key.D },
		{ "move_forward", Key.W },
		{ "move_backward", Key.S },
		{ "move_jump", Key.Space },
		{ "ui_cancel", Key.Escape },
		{ "tac_sprint", Key.Shift },
		{ "tac_sprint_alt", Key.Q },
	};

	public float Speed => BaseSpeed * (_isTacSprint || _isTacSprintAlt ? TacSprintMultiplier : 1.0f);
	public float BaseSpeed { get; set; } = 5.0f;
	public float TacSprintMultiplier { get; set; } = 3.0f;
	public float JumpVelocity { get; set; } = 4.5f;
	public float MouseSensitivity { get; set; } = 0.002f;
	public float PushForce { get; set; } = 80.0f;
	public bool IsTyping { get; set; }

	private float _gravity;
	private Camera3D _camera;
	private bool _isTacSprint;
	private bool _isTacSprintAlt;
	private bool _holdsSpace;
	private bool _isFlying;
	private int _jumpCount;
	private DateTime _lastJumpTime;

	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
		_gravity = ProjectSettings.GetSetting("physics/3d/default_gravity").AsSingle();
		_camera = GetNode<Camera3D>("Camera3D");
		Input.MouseMode = Input.MouseModeEnum.Captured;

		SetupInputMap();
		SetProcessInput(true);
	}

	private void SetupInputMap()
	{
		foreach (var (input, key) in _inputMap)
		{
			if (InputMap.HasAction(input))
				continue;

			InputMap.AddAction(input);
			InputMap.ActionAddEvent(input, new InputEventKey { PhysicalKeycode = key });
		}
	}

	public override void _UnhandledInput(InputEvent @event)
	{
		if (IsTyping) return;

		if (@event is InputEventMouseMotion ieem && Input.MouseMode == Input.MouseModeEnum.Captured)
		{
			RotateY(-ieem.Relative.X * MouseSensitivity);
			_camera.RotateX(-ieem.Relative.Y * MouseSensitivity);
			Vector3 cameraRot = _camera.Rotation;
			cameraRot.X = Mathf.Clamp(cameraRot.X, -Mathf.Pi / 2f, Mathf.Pi / 2f);
			_camera.Rotation = cameraRot;
		}

		if (!@event.IsActionPressed("ui_cancel"))
			return;

		Input.MouseMode = Input.MouseMode == Input.MouseModeEnum.Captured
			? Input.MouseModeEnum.Visible
			: Input.MouseModeEnum.Captured;
	}

	public override void _Input(InputEvent @event)
	{
		if (@event.IsActionPressed("tac_sprint")) _isTacSprint = true;
		if (@event.IsActionReleased("tac_sprint")) _isTacSprint = false;

		if (@event.IsActionPressed("tac_sprint_alt")) _isTacSprintAlt = true;
		if (@event.IsActionReleased("tac_sprint_alt")) _isTacSprintAlt = false;

		if (@event.IsActionPressed("move_jump")) _holdsSpace = true;
		if (@event.IsActionReleased("move_jump")) _holdsSpace = false;

		base._Input(@event);
	}


	// // Add the gravity.
	// if (!IsOnFloor() && !_isFlying)
	// velocity.Y -= _gravity * (float)delta;
	// 	else if (IsOnFloor() && !_isFlying)
	// _jumpCount = 0;
	//
	// var pressedJump = Input.IsActionJustPressed("move_jump");
	// var currentJumpTime = DateTime.Now;
	//
	// 	if (pressedJump)
	// {
	// 	if ((currentJumpTime - _lastJumpTime) <= TimeSpan.FromMilliseconds(500))
	// 	{
	// 		_isFlying = !_isFlying;
	// 		if (_isFlying) velocity.Y = 0; // Prevent vertical drifting when entering flight
	// 	}
	// 	else if (IsOnFloor())
	// 	{
	// 		velocity.Y = JumpVelocity;
	// 		_jumpCount++;
	// 	}
	// 	_lastJumpTime = currentJumpTime;
	// }

	// Vertical movement during flight
	// if (_isFlying)
	// {
	// 	velocity.Y = 0;
	// 	if (Input.IsActionPressed("move_jump"))
	// 		velocity.Y += Speed;
	// 	if (Input.IsActionPressed("tac_sprint"))
	// 		velocity.Y -= Speed;
	// }

	public override void _PhysicsProcess(double delta)
	{
		if (IsTyping)
		{
			var vel = Velocity;
			if (!IsOnFloor()) vel.Y -= _gravity * (float)delta;
			vel.X = Mathf.MoveToward(vel.X, 0, Speed);
			vel.Z = Mathf.MoveToward(vel.Z, 0, Speed);
			Velocity = vel;
			MoveAndSlide();
			return;
		}

		var velocity = Velocity;

		// Add the gravity.
		if (!IsOnFloor() && !_isFlying)
			velocity.Y -= _gravity * (float)delta;
		else if (IsOnFloor() && !_isFlying)
			_jumpCount = 0;

		var pressedJump = Input.IsActionJustPressed("move_jump");
		var isOnTheFloor = IsOnFloor();
		var currentJumpTime = DateTime.Now;

		if (_isFlying)
			velocity.Y = _isTacSprint ? -Speed : 0;

		switch (pressedJump, isOnTheFloor)
		{
			case (true, _) when (currentJumpTime - _lastJumpTime) <= TimeSpan.FromMilliseconds(200):
				_isFlying = !_isFlying;
				break;
			case (true, _) when _jumpCount <= 2 && !_isFlying:
				velocity.Y = JumpVelocity;
				_jumpCount++;
				break;
			case (_, _) when _isFlying && _holdsSpace:
				velocity.Y = Speed;
				break;
			default:
				break;
		}

		if (pressedJump)
			_lastJumpTime = currentJumpTime;

		// Get the input direction and handle the movement/deceleration.
		var inputDir = Input.GetVector(
			"move_left",
			"move_right",
			"move_forward",
            "move_backward"
		);

		var direction = GetDirection(inputDir);

		Velocity = direction == Vector3.Zero
			? NeutralizeVelocity(ref velocity)
			: ApplySpeed(ref velocity, ref direction);

		MoveAndSlide();

		// Handle pushing rigidbodies
		for (var i = 0; i < GetSlideCollisionCount(); i++)
		{
			var kinematicCollision = GetSlideCollision(i);
			if (kinematicCollision.GetCollider() is not RigidBody3D rigidBody)
				continue;

			rigidBody.ApplyCentralImpulse(-kinematicCollision.GetNormal() * PushForce * (float)delta);
		}
	}

	private Vector3 GetDirection(Vector2 inputDir) =>
		(Transform.Basis * new Vector3(inputDir.X, 0, inputDir.Y)).Normalized();

	private Vector3 ApplySpeed(ref Vector3 velocity, ref Vector3 direction)
	{
		velocity.X = direction.X * Speed;
		velocity.Z = direction.Z * Speed;
		return velocity;
	}

	private Vector3 NeutralizeVelocity(ref Vector3 velocity)
	{
		velocity.X = Mathf.MoveToward(Velocity.X, 0, Speed);
		velocity.Z = Mathf.MoveToward(Velocity.Z, 0, Speed);
		return velocity;
	}
}
