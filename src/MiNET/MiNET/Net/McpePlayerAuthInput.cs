﻿using System.Collections.Generic;
using System.Numerics;
using MiNET.Utils;
using MiNET.Utils.Vectors;

namespace MiNET.Net;

public partial class McpePlayerAuthInput : Packet<McpePlayerAuthInput>
{
	public PlayerLocation Position;
	public Vector2 MoveVector;
	public AuthInputFlags InputFlags;
	public PlayerInputMode InputMode;
	public PlayerPlayMode PlayMode;
	public PlayerInteractionModel InteractionModel;
	public long Tick;
	public Vector2 InteractRotation;
	public Vector3 Delta;
	public PlayerBlockActions Actions;
	public ItemStackRequests ItemStack;
	public Vector2 AnalogMoveVector;
	public Vector3 CameraOrientation;

	partial void AfterDecode()
	{
		var Rot = ReadVector2();
		var Pos = ReadVector3();
		MoveVector = ReadVector2();
		var HeadYaw = ReadFloat();
		Position = new PlayerLocation(Pos.X, Pos.Y, Pos.Z, HeadYaw, Rot.Y, Rot.X);
		InputFlags = (AuthInputFlags)ReadUnsignedVarLong();
		InputMode = (PlayerInputMode)ReadUnsignedVarInt();
		PlayMode = (PlayerPlayMode)ReadUnsignedVarInt();
		InteractionModel = (PlayerInteractionModel) ReadUnsignedVarInt();
		InteractRotation = ReadVector2();
		Tick = ReadUnsignedVarLong();
		Delta = ReadVector3();

		if ((InputFlags & AuthInputFlags.PerformItemStackRequest) != 0)
		{
			ItemStack = ReadItemStackRequests(true);
		}

		if ((InputFlags & AuthInputFlags.PerformBlockActions) != 0)
		{
			Actions = ReadPlayerBlockActions();
		}

		AnalogMoveVector = ReadVector2();
		CameraOrientation = ReadVector3();
		ReadVector2(); // motion todo
	}

	partial void AfterEncode()
	{
		WriteUnsignedVarLong((long)InputFlags);
		WriteUnsignedVarInt((uint)InputMode);
		WriteUnsignedVarInt((uint)PlayMode);
		WriteUnsignedVarInt((uint)InteractionModel);
		Write(InteractRotation);
		WriteUnsignedVarLong(Tick);
		Write(Delta);
		Write(AnalogMoveVector);
		Write(CameraOrientation);
	}

	public override void Reset()
	{
		base.Reset();
		Position = default(PlayerLocation);
		MoveVector = default(Vector2);
		InputFlags = 0;
		InputMode = PlayerInputMode.Mouse;
		PlayMode = PlayerPlayMode.Normal;
		InteractionModel = PlayerInteractionModel.Touch;
		InteractRotation = default(Vector2);
		Tick = 0;
		Delta = Vector3.Zero;
		AnalogMoveVector = Vector2.Zero;
		Actions = default(PlayerBlockActions);
		ItemStack = default(ItemStackRequests);
	}

	public enum PlayerPlayMode
	{
		Normal = 0,
		Teaser = 1,
		Screen = 2,
		Viewer = 3,
		VR = 4,
		Placement = 5,
		LivingRoom = 6,
		ExitLevel = 7,
		ExitLevelLivingRoom = 8
	}
	
	public enum PlayerInputMode
	{
		Mouse = 1,
		Touch = 2,
		GamePad = 3,
		MotionController = 4
	}

	public enum PlayerInteractionModel
	{
		Touch = 0,
		Crosshair = 1,
		Classic = 2
	}

	public class PlayerBlockActions
	{
		public List<PlayerBlockActionData> PlayerBlockAction = new List<PlayerBlockActionData>();
	}

	public class PlayerBlockActionData
	{
		public PlayerAction PlayerActionType;
		public BlockCoordinates BlockCoordinates;
		public int Facing;
	}
}