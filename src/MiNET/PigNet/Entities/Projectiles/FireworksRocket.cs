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
using fNbt;
using log4net;
using PigNet.Net;
using PigNet.Sounds;
using PigNet.Utils;
using PigNet.Utils.Vectors;
using PigNet.Items;
using PigNet.Net.EnumerationsTable;
using PigNet.Net.Packets.Mcpe;
using PigNet.Utils.Metadata;
using PigNet.Worlds;

namespace PigNet.Entities.Projectiles
{
	public class FireworksRocket : Projectile
	{
		private static readonly ILog Log = LogManager.GetLogger(typeof(FireworksRocket));

		public Item Fireworks { get; set; }
		public int Lifetime { get; set; }

		public FireworksRocket(Player shooter, Level level, Item fireworks, Random random = null) : base(shooter, EntityType.FireworksRocket, level, 0)
		{
			random = random ?? new Random();

			Fireworks = fireworks;
			Width = 0.25;
			Length = 0.25;
			Height = 0.25;

			Gravity = 0.0;
			Drag = 0.01;

			HealthManager.IsInvulnerable = true;

			HasCollision = true;
			IsAffectedByGravity = true;

			int flyTime = 1;
			try
			{
				if (Fireworks.ExtraData["Fireworks"]["Flight"] is NbtByte flight)
				{
					flyTime = flight.ByteValue;
				}
			}
			catch (Exception e)
			{
				Log.Debug(e);
			}

			Lifetime = 20 * flyTime + random.Next(5) + random.Next(7);
		}

		public override MetadataDictionary GetMetadata()
		{
			var metadata = base.GetMetadata();
			//metadata[(int) MetadataFlags.FireworksType] = new MetadataSlot(Fireworks);
			return metadata;
		}

		public override void SpawnEntity()
		{
			Velocity = Force = KnownPosition.GetDirection().Normalize() * 0.06055374f;
			KnownPosition.Yaw = (float) Velocity.GetYaw();
			KnownPosition.Pitch = (float) Velocity.GetPitch();

			Level.BroadcastSound(KnownPosition.ToVector3(), LevelSoundEventType.Launch);

			base.SpawnEntity();
		}

		public override void DespawnEntity()
		{
			McpeActorEvent actorEvent = McpeActorEvent.CreateObject();
			actorEvent.runtimeEntityId = EntityId;
			actorEvent.eventId = ActorEvent.FireworksExplode;
			actorEvent.data = 0;
			Level.RelayBroadcast(actorEvent);

			Level.BroadcastSound(KnownPosition.ToVector3(), LevelSoundEventType.Blast);

			base.DespawnEntity();
		}

		public override void OnTick(Entity[] entities)
		{
			if (Lifetime-- < 0)
			{
				DespawnEntity();
			}
			else
			{
				base.OnTick(entities);
			}
		}
	}
}