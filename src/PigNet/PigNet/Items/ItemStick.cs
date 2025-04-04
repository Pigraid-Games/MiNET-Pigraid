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

using System;
using System.Numerics;
using System.Threading.Tasks;
using PigNet.Net;
using PigNet.Items.Armor;
using PigNet.Net.Packets.Mcpe;
using PigNet.Utils.Vectors;
using PigNet.Worlds;

namespace PigNet.Items;

public class ItemStick : Item
{
	public ItemStick() : base("minecraft:stick", 280, canInteract: false)
	{
		FuelEfficiency = 5;
	}

	public override void UseItem(Level world, Player player, BlockCoordinates blockCoordinates)
	{
		if (player.IsGliding)
		{
			double currentSpeed = player.CurrentSpeed / 20f;
			if (currentSpeed > 35f / 20f)
				//player.SendMessage($"Speed already over max {player.CurrentSpeed:F2}m/s", MessageType.Raw);
				return;

			Vector3 velocity = Vector3.Normalize(player.KnownPosition.GetHeadDirection()) * (float) currentSpeed;
			float factor = (float) (1 + (1 / (1 + (currentSpeed * 2))));
			velocity *= factor;

			if (currentSpeed < 7f / 20f) velocity = Vector3.Normalize(velocity) * 1.2f;

			McpeSetActorMotion motions = McpeSetActorMotion.CreateObject();
			motions.runtimeActorId = EntityManager.EntityIdSelf;
			motions.velocity = velocity;

			player.SendPacket(motions);
		}
		else if (player.Inventory.ArmorInventory.GetChestItem() is ItemElytra)
		{
			McpeSetActorMotion motions = McpeSetActorMotion.CreateObject();
			motions.runtimeActorId = EntityManager.EntityIdSelf;
			var velocity = new Vector3(0, 2, 0);
			motions.velocity = velocity;
			player.SendPacket(motions);

			_ = SendWithDelay(200, () =>
			{
				player.IsGliding = true;
				player.Height = 0.6;
				player.BroadcastSetEntityData();
			});
		}
	}

	private static async Task SendWithDelay(int delay, Action action)
	{
		await Task.Delay(delay);
		action();
	}
}