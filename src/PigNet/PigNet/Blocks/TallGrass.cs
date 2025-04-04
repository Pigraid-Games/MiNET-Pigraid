﻿#region LICENSE

// The contents of this file are subject to the Common Public Attribution
// License Version 1.0. (the "License"); you may not use this file except in
// compliance with the License. You may obtain a copy of the License at
// https://github.com/NiclasOlofsson/PigNet/blob/master/LICENSE. 
// The License is based on the Mozilla Public License Version 1.1, but Sections 14 
// and 15 have been added to cover use of software over a computer network and 
// provide for limited attribution for the Original Developer. In addition, Exhibit A has 
// been modified to be consistent with Exhibit B.
// 
// Software distributed under the License is distributed on an "AS IS" basis,
// WITHOUT WARRANTY OF ANY KIND, either express or implied. See the License for
// the specific language governing rights and limitations under the License.
// 
// The Original Code is PigNet.
// 
// The Original Developer is the Initial Developer.  The Initial Developer of
// the Original Code is Niclas Olofsson.
// 
// All portions of the code written by Niclas Olofsson are Copyright (c) 2014-2018 Niclas Olofsson. 
// All Rights Reserved.

#endregion

using System;
using PigNet.Items;
using PigNet.Utils.Vectors;
using PigNet.Worlds;

namespace PigNet.Blocks;

public partial class Tallgrass : Block
{
	public enum TallGrassTypes
	{
		DeadShrub = 0,
		TallGrass = 1,
		Fern = 2
	}

	public Tallgrass() : base(31)
	{
		BlastResistance = 3;
		Hardness = 0.6f;

		IsSolid = false;
		IsReplaceable = true;
		IsTransparent = true;
	}

	public override void OnTick(Level level, bool isRandom)
	{
		base.OnTick(level, isRandom);

		if (isRandom)
		{
		}
	}

	public override void BlockUpdate(Level level, BlockCoordinates blockCoordinates)
	{
		if (Coordinates.BlockDown() == blockCoordinates)
		{
			level.SetAir(Coordinates);
			UpdateBlocks(level);
		}
	}

	public override Item[] GetDrops(Item tool)
	{
		// 50% chance to drop seeds.
		var rnd = new Random();
		if (rnd.NextDouble() > 0.5) return new[] { ItemFactory.GetItem(295) };

		return new Item[0];
	}
}