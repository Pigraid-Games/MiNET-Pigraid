﻿using MiNET.Items;
using MiNET.Particles;
using MiNET.Utils.Vectors;
using MiNET.Worlds;
using System.Linq;
using System.Numerics;
using MiNET.Items.Tools;
using MiNET.Sounds;

namespace MiNET.Blocks;

public partial class Composter : Block
{
	private static int[] compostableIds = { 458, 464, 31, 335, 18, 362, 361, 6, -130, 477, 295, 81, -139, 360, 338, 175, 106, 260, 457, 391, 111, 103, 39, 40, 99, 392, 86, -156, 296, 393, 297, 357, 170, 354, 400 };

	public Composter() : base(468)
	{
		BlastResistance = 3;
		Hardness = 0.5f;
	}

	public override bool IsBestTool(Item item)
	{
		return item is ItemAxe ? true : false;
	}

	public override bool Interact(Level level, Player player, BlockCoordinates blockCoordinates, BlockFace face, Vector3 faceCoord)
	{
		//todo: level add possibility
		if (!compostableIds.Contains(player.Inventory.GetItemInHand().Id)) return true;
		DoInteract(level, blockCoordinates);
		return true;
	}

	public void DoInteract(Level level, BlockCoordinates blockCoordinates)
	{
		if (ComposterFillLevel < 8)
		{
			ComposterFillLevel += 1;
			level.BroadcastSound(new BlockComposterFillSound(blockCoordinates));
		}
		else
		{
			level.DropItem(blockCoordinates, new Item(351, 15));
			ComposterFillLevel = 0;
			level.BroadcastSound(new BlockComposterReadySound(blockCoordinates));
		}
		level.SetBlock(this);
		LegacyParticle particle = new VillagerHappy(level) { Position = new Vector3(Coordinates.X + 0.5f, Coordinates.Y + 1, Coordinates.Z + 0.5f) };
		particle.Spawn();
	}
}