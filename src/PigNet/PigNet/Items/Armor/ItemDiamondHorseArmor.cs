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
// All portions of the code written by Niclas Olofsson are Copyright (c) 2014-2020 Niclas Olofsson.
// All Rights Reserved.

#endregion

namespace PigNet.Items.Armor;

public class ItemLeatherHorseArmor : Item
{
	public ItemLeatherHorseArmor() : base("minecraft:leather_horse_armor", 416)
	{
		MaxStackSize = 1;
		ItemType = ItemType.Chestplate;
		ItemMaterial = ItemMaterial.Leather;
	}
}

public class ItemIronHorseArmor : Item
{
	public ItemIronHorseArmor() : base("minecraft:iron_horse_armor", 417)
	{
		MaxStackSize = 1;
		ItemType = ItemType.Chestplate;
		ItemMaterial = ItemMaterial.Iron;
	}
}

public class ItemGoldenHorseArmor : Item
{
	public ItemGoldenHorseArmor() : base("minecraft:golden_horse_armor", 418)
	{
		MaxStackSize = 1;
		ItemType = ItemType.Chestplate;
		ItemMaterial = ItemMaterial.Gold;
	}
}

public class ItemDiamondHorseArmor : Item
{
	public ItemDiamondHorseArmor() : base("minecraft:diamond_horse_armor", 419)
	{
		MaxStackSize = 1;
		ItemType = ItemType.Chestplate;
		ItemMaterial = ItemMaterial.Diamond;
	}
}