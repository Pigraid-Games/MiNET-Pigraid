﻿using System;
using System.Collections.Generic;
using System.Numerics;
using PigNet.Net;
using PigNet.Utils.Vectors;
using PigNet.Net.Packets.Mcpe;
using PigNet.Worlds;

namespace PigNet.Entities.Projectiles
{
	public class WindCharge : Projectile
	{
		public WindCharge(Player shooter, Level level) : base(shooter, EntityType.WindCharge, level, 0)
		{
			Width = 0.25;
			Length = 0.25;
			Height = 0.25;

			Gravity = 0;
			Drag = 0;
			Ttl = 5000;

			HealthManager.IsInvulnerable = true;
			DespawnOnImpact = true;
			BroadcastMovement = true;
			IsDispawningOnShooterImpact = false;
		}

		public override void DespawnEntity()
		{
			base.DespawnEntity();

			McpeLevelEvent particleEvent = McpeLevelEvent.CreateObject();
			particleEvent.eventId = LevelEventType.WindExplosion;
			particleEvent.position = KnownPosition;
			particleEvent.data = Data;
			Level.RelayBroadcast(Level.GetAllPlayers(), particleEvent);
			Level.BroadcastSound(KnownPosition, LevelSoundEventType.WindChargeBurst);
			ApplyEffect();
		}

		protected override void OnHitEntity(Entity entityCollided)
		{
			base.OnHitEntity(entityCollided);
			entityCollided.HealthManager.TakeHit(Shooter, 1, DamageCause.EntityExplosion);

			Vector3 explosionPoint = KnownPosition;
			Vector3 direction = entityCollided.KnownPosition - explosionPoint;
			direction = direction.Normalize() * (float)1.5;

			entityCollided.Knockback(direction);
		}

		private void ApplyEffect()
		{
			var playersInArea = new List<Player>();
			foreach (var player in Level.Players.Values)
			{
				float distanceSquared = Vector3.DistanceSquared(player.KnownPosition, KnownPosition);
				if (distanceSquared <= 16)
				{
					playersInArea.Add(player);
				}
			}

			foreach (var player in playersInArea)
			{
				Vector3 explosionPoint = KnownPosition;
				Vector3 direction = player.KnownPosition - explosionPoint;
				direction = direction.Normalize() * (float) 1.5;
				player.Knockback(direction);
			}
		}
	}
}
